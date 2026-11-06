using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using 图书馆理模块.Models;

namespace 图书馆理模块.Services
{
    /// <summary>
    /// Database access for physical copies, readers and loan history.
    /// All mutations are parameterized; checkout/check-in use SERIALIZABLE transactions.
    /// </summary>
    public sealed partial class CirculationStore    {
        private static SqlConnection Connect()
        {
            // Reuse the same configured database as BooksDataContext.
            // Separate overrides could silently split catalog and circulation data.
            var settings = ConfigurationManager.ConnectionStrings["BooksConnectionString"];
            var value = settings == null ? null : settings.ConnectionString;
            if (string.IsNullOrWhiteSpace(value))
                throw new CirculationException(HttpStatusCode.ServiceUnavailable,
                    "未配置 BooksConnectionString。");
            var connection = new SqlConnection(value);
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql)
        {
            var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = 15;
            if (transaction != null) cmd.Transaction = transaction;
            return cmd;
        }

        private static void Add(SqlCommand cmd, string key, SqlDbType type, int size, object value)
        {
            var param = size > 0 ? cmd.Parameters.Add(key, type, size) : cmd.Parameters.Add(key, type);
            param.Value = value ?? DBNull.Value;
        }

        public IList<LibraryCopyDto> Copies()
        {
            var result = new List<LibraryCopyDto>();
            using (var db = Connect())
            using (var cmd = Command(db, null, @"
                SELECT c.CopyId,c.BookNum,b.BookName,c.Barcode,c.ShelfLocation,c.Status
                FROM dbo.LibraryCopy c JOIN dbo.BookInfo b ON b.BookNum=c.BookNum
                ORDER BY c.CopyId DESC"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(new LibraryCopyDto
                    {
                        CopyId = reader.GetInt32(0),
                        BookNum = reader.GetInt32(1),
                        BookName = reader.GetString(2),
                        Barcode = reader.GetString(3),
                        ShelfLocation = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsBorrowed = reader.GetByte(5) == 1
                    });
            }
            return result;
        }

        public IList<LibraryReaderDto> Readers()
        {
            var result = new List<LibraryReaderDto>();
            using (var db = Connect())
            using (var cmd = Command(db, null, @"
                SELECT ReaderId,CardNumber,FullName,Phone,IsActive
                FROM dbo.LibraryReader ORDER BY ReaderId DESC"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(new LibraryReaderDto
                    {
                        ReaderId = reader.GetInt32(0),
                        CardNumber = reader.GetString(1),
                        FullName = reader.GetString(2),
                        Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                        IsActive = reader.GetBoolean(4)
                    });
            }
            return result;
        }

        public IList<LibraryLoanDto> Loans(bool overdueOnly = false)
        {
            var result = new List<LibraryLoanDto>();
            var query = @"
                SELECT l.LoanId,l.CopyId,c.Barcode,b.BookName,
                    l.ReaderId,r.FullName,r.CardNumber,
                    l.BorrowedAtUtc,l.DueAtUtc,l.ReturnedAtUtc,l.RenewCount
                FROM dbo.LibraryLoan l
                JOIN dbo.LibraryCopy c ON c.CopyId=l.CopyId
                JOIN dbo.BookInfo b ON b.BookNum=c.BookNum
                JOIN dbo.LibraryReader r ON r.ReaderId=l.ReaderId "
                + (overdueOnly ? " WHERE l.ReturnedAtUtc IS NULL AND l.DueAtUtc < SYSUTCDATETIME() " : "")
                + @" ORDER BY CASE WHEN l.ReturnedAtUtc IS NULL THEN 0 ELSE 1 END,
                    l.BorrowedAtUtc DESC";
            using (var db = Connect())
            using (var cmd = Command(db, null, query))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(new LibraryLoanDto
                    {
                        LoanId = reader.GetInt32(0),
                        CopyId = reader.GetInt32(1),
                        Barcode = reader.GetString(2),
                        BookName = reader.GetString(3),
                        ReaderId = reader.GetInt32(4),
                        ReaderName = reader.GetString(5),
                        CardNumber = reader.GetString(6),
                        BorrowedAtUtc = reader.GetDateTime(7),
                        DueAtUtc = reader.GetDateTime(8),
                        ReturnedAtUtc = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
                        RenewCount = reader.GetByte(10)
                    });
            }
            return result;
        }

        public int RegisterCopy(NewCopyRequest input)
        {
            if (input == null || input.BookNum <= 0)
                throw new CirculationException(HttpStatusCode.BadRequest, "请选择已有图书。");
            var barcode = CirculationRules.NormalizeCode(input.Barcode);
            var location = (input.ShelfLocation ?? "").Trim();
            if (location.Length > 80)
                throw new CirculationException(HttpStatusCode.BadRequest, "书架位置不能超过 80 个字符。");
            using (var db = Connect())
            using (var cmd = Command(db, null, @"
                INSERT dbo.LibraryCopy(BookNum,Barcode,ShelfLocation)
                OUTPUT INSERTED.CopyId
                VALUES (@book,@barcode,@location)"))
            {
                Add(cmd, "@book", SqlDbType.Int, 0, input.BookNum);
                Add(cmd, "@barcode", SqlDbType.NVarChar, 32, barcode);
                Add(cmd, "@location", SqlDbType.NVarChar, 80, location.Length == 0 ? null : location);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public int RegisterReader(NewReaderRequest input)
        {
            if (input == null)
                throw new CirculationException(HttpStatusCode.BadRequest, "请输入读者信息。");
            var card = CirculationRules.NormalizeCode(input.CardNumber);
            var name = CirculationRules.NormalizeName(input.FullName);
            var phone = (input.Phone ?? "").Trim();
            if (phone.Length > 30)
                throw new CirculationException(HttpStatusCode.BadRequest, "联系方式不能超过 30 个字符。");
            using (var db = Connect())
            using (var cmd = Command(db, null, @"
                INSERT dbo.LibraryReader(CardNumber,FullName,Phone)
                OUTPUT INSERTED.ReaderId
                VALUES (@card,@name,@phone)"))
            {
                Add(cmd, "@card", SqlDbType.NVarChar, 32, card);
                Add(cmd, "@name", SqlDbType.NVarChar, 50, name);
                Add(cmd, "@phone", SqlDbType.NVarChar, 30, phone.Length == 0 ? null : phone);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public int Borrow(BorrowRequest input)
        {
            if (input == null || input.CopyId <= 0 || input.ReaderId <= 0)
                throw new CirculationException(HttpStatusCode.BadRequest, "请选择读者和可借书籍。");

            using (var db = Connect())
            using (var tx = db.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    // Lock the title before its copy so reservations and checkouts
                    // agree on a deterministic lock order.
                    int bookId;
                    using (var cmd = Command(db, tx,
                        "SELECT BookNum FROM dbo.LibraryCopy WHERE CopyId=@id"))
                    {
                        Add(cmd, "@id", SqlDbType.Int, 0, input.CopyId);
                        var found = cmd.ExecuteScalar();
                        if (found == null)
                            throw new CirculationException(HttpStatusCode.NotFound, "图书副本不存在。");
                        bookId = (int)found;
                    }
                    LockBook(db, tx, bookId);

                    int status;
                    using (var cmd = Command(db, tx,
                        "SELECT Status FROM dbo.LibraryCopy WITH (UPDLOCK,HOLDLOCK) WHERE CopyId=@id"))
                    {
                        Add(cmd, "@id", SqlDbType.Int, 0, input.CopyId);
                        var found = cmd.ExecuteScalar();
                        if (found == null)
                            throw new CirculationException(HttpStatusCode.NotFound, "图书副本不存在。");
                        status = Convert.ToInt32(found);
                    }

                    bool active;
                    using (var cmd = Command(db, tx,
                        "SELECT IsActive FROM dbo.LibraryReader WITH (UPDLOCK,HOLDLOCK) WHERE ReaderId=@id"))
                    {
                        Add(cmd, "@id", SqlDbType.Int, 0, input.ReaderId);
                        var found = cmd.ExecuteScalar();
                        if (found == null)
                            throw new CirculationException(HttpStatusCode.NotFound, "读者不存在。");
                        active = Convert.ToBoolean(found);
                    }

                    int loanCount;
                    using (var cmd = Command(db, tx,
                        "SELECT COUNT(*) FROM dbo.LibraryLoan WHERE ReaderId=@reader AND ReturnedAtUtc IS NULL"))
                    {
                        Add(cmd, "@reader", SqlDbType.Int, 0, input.ReaderId);
                        loanCount = (int)cmd.ExecuteScalar();
                    }
                    CirculationRules.EnsureCanBorrow(status == 0, active, loanCount);

                    int firstReservationId = 0, firstReaderId = 0;
                    using (var cmd = Command(db, tx, @"
                        SELECT TOP(1) ReservationId,ReaderId
                        FROM dbo.LibraryReservation WITH (UPDLOCK,HOLDLOCK)
                        WHERE BookNum=@book AND Status=0
                        ORDER BY RequestedAtUtc,ReservationId"))
                    {
                        Add(cmd, "@book", SqlDbType.Int, 0, bookId);
                        using (var reservation = cmd.ExecuteReader())
                        {
                            if (reservation.Read())
                            {
                                firstReservationId = reservation.GetInt32(0);
                                firstReaderId = reservation.GetInt32(1);
                            }
                        }
                    }
                    CirculationAdvancedRules.EnsureReservationPriority(firstReaderId, input.ReaderId);

                    using (var cmd = Command(db, tx,
                        "UPDATE dbo.LibraryCopy SET Status=1 WHERE CopyId=@id AND Status=0"))
                    {
                        Add(cmd, "@id", SqlDbType.Int, 0, input.CopyId);
                        if (cmd.ExecuteNonQuery() != 1)
                            throw new CirculationException(HttpStatusCode.Conflict, "借阅状态已发生变化，请刷新。");
                    }

                    var now = DateTime.UtcNow;
                    int loanId;
                    using (var cmd = Command(db, tx, @"
                        INSERT dbo.LibraryLoan(CopyId,ReaderId,BorrowedAtUtc,DueAtUtc)
                        OUTPUT INSERTED.LoanId
                        VALUES (@copy,@reader,@borrowed,@due)"))
                    {
                        Add(cmd, "@copy", SqlDbType.Int, 0, input.CopyId);
                        Add(cmd, "@reader", SqlDbType.Int, 0, input.ReaderId);
                        Add(cmd, "@borrowed", SqlDbType.DateTime2, 0, now);
                        Add(cmd, "@due", SqlDbType.DateTime2, 0, CirculationRules.DueAtUtc(now));
                        loanId = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                    if (firstReservationId != 0)
                    {
                        using (var cmd = Command(db, tx, @"
                            UPDATE dbo.LibraryReservation
                            SET Status=1,ClosedAtUtc=CASE
                              WHEN SYSUTCDATETIME()<RequestedAtUtc THEN RequestedAtUtc
                              ELSE SYSUTCDATETIME() END
                            WHERE ReservationId=@reservation AND Status=0"))
                        {
                            Add(cmd, "@reservation", SqlDbType.Int, 0, firstReservationId);
                            if (cmd.ExecuteNonQuery() != 1)
                                throw new CirculationException(HttpStatusCode.Conflict,
                                    "预约状态已变化，请刷新后重试借书。");
                        }
                    }
                    tx.Commit();
                    return loanId;
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        public void Return(int loanId)
        {
            if (loanId <= 0)
                throw new CirculationException(HttpStatusCode.BadRequest, "借阅编号无效。");
            using (var db = Connect())
            using (var tx = db.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    int copyId;
                    using (var cmd = Command(db, tx, @"
                        SELECT CopyId FROM dbo.LibraryLoan WITH (UPDLOCK,HOLDLOCK)
                        WHERE LoanId=@loan AND ReturnedAtUtc IS NULL"))
                    {
                        Add(cmd, "@loan", SqlDbType.Int, 0, loanId);
                        var found = cmd.ExecuteScalar();
                        if (found == null)
                            throw new CirculationException(HttpStatusCode.Conflict, "记录不存在或已经归还。");
                        copyId = Convert.ToInt32(found);
                    }

                    using (var cmd = Command(db, tx, @"
                        UPDATE dbo.LibraryLoan
                        SET ReturnedAtUtc=CASE WHEN SYSUTCDATETIME()<BorrowedAtUtc
                            THEN BorrowedAtUtc ELSE SYSUTCDATETIME() END
                        WHERE LoanId=@loan AND ReturnedAtUtc IS NULL"))
                    {
                        Add(cmd, "@loan", SqlDbType.Int, 0, loanId);
                        if (cmd.ExecuteNonQuery() != 1)
                            throw new CirculationException(HttpStatusCode.Conflict, "归还状态已变更，请刷新。");
                    }
                    using (var cmd = Command(db, tx,
                        "UPDATE dbo.LibraryCopy SET Status=0 WHERE CopyId=@copy AND Status=1"))
                    {
                        Add(cmd, "@copy", SqlDbType.Int, 0, copyId);
                        if (cmd.ExecuteNonQuery() != 1)
                            throw new CirculationException(HttpStatusCode.Conflict, "馆藏副本状态不一致，已回滚操作。");
                    }
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }
    }
}