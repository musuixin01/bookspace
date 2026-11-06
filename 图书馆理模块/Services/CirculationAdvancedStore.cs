using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using 图书馆理模块.Models;

namespace 图书馆理模块.Services
{
    public sealed partial class CirculationStore
    {
        private static int ReadCount(SqlConnection db, SqlTransaction tx,
            string sql, string parameter, int id)
        {
            using (var cmd = Command(db, tx, sql))
            {
                Add(cmd, parameter, SqlDbType.Int, 0, id);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static int LockBook(SqlConnection db, SqlTransaction tx, int bookNum)
        {
            using (var cmd = Command(db, tx,
                "SELECT BookNum FROM dbo.BookInfo WITH (UPDLOCK,HOLDLOCK) WHERE BookNum=@book"))
            {
                Add(cmd, "@book", SqlDbType.Int, 0, bookNum);
                if (cmd.ExecuteScalar() == null)
                    throw new CirculationException(HttpStatusCode.NotFound, "图书书目不存在。");
                return bookNum;
            }
        }

        private static bool LockReader(SqlConnection db, SqlTransaction tx, int readerId)
        {
            using (var cmd = Command(db, tx,
                "SELECT IsActive FROM dbo.LibraryReader WITH (UPDLOCK,HOLDLOCK) WHERE ReaderId=@reader"))
            {
                Add(cmd, "@reader", SqlDbType.Int, 0, readerId);
                var state = cmd.ExecuteScalar();
                if (state == null)
                    throw new CirculationException(HttpStatusCode.NotFound, "读者不存在。");
                return Convert.ToBoolean(state);
            }
        }

        public void UpdateReader(int readerId, UpdateReaderRequest input)
        {
            if (readerId <= 0 || input == null)
                throw new CirculationException(HttpStatusCode.BadRequest, "读者资料不完整。");
            var name = CirculationRules.NormalizeName(input.FullName);
            var phone = (input.Phone ?? "").Trim();
            if (phone.Length > 30)
                throw new CirculationException(HttpStatusCode.BadRequest, "联系方式不能超过 30 个字符。");

            using (var db = Connect())
            using (var cmd = Command(db, null,
                "UPDATE dbo.LibraryReader SET FullName=@name,Phone=@phone WHERE ReaderId=@reader"))
            {
                Add(cmd, "@name", SqlDbType.NVarChar, 50, name);
                Add(cmd, "@phone", SqlDbType.NVarChar, 30, phone.Length == 0 ? null : phone);
                Add(cmd, "@reader", SqlDbType.Int, 0, readerId);
                if (cmd.ExecuteNonQuery() != 1)
                    throw new CirculationException(HttpStatusCode.NotFound, "读者不存在。");
            }
        }

        public void SetReaderStatus(int readerId, bool enabled)
        {
            if (readerId <= 0)
                throw new CirculationException(HttpStatusCode.BadRequest, "读者编号无效。");
            using (var db = Connect())
            using (var tx = db.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    LockReader(db, tx, readerId);
                    if (!enabled)
                    {
                        var loanCount = ReadCount(db, tx,
                            "SELECT COUNT(*) FROM dbo.LibraryLoan WHERE ReaderId=@reader AND ReturnedAtUtc IS NULL",
                            "@reader", readerId);
                        var reservations = ReadCount(db, tx,
                            "SELECT COUNT(*) FROM dbo.LibraryReservation WHERE ReaderId=@reader AND Status=0",
                            "@reader", readerId);
                        CirculationAdvancedRules.EnsureCanDeactivate(loanCount, reservations);
                    }
                    using (var cmd = Command(db, tx,
                        "UPDATE dbo.LibraryReader SET IsActive=@active WHERE ReaderId=@reader"))
                    {
                        Add(cmd, "@active", SqlDbType.Bit, 0, enabled);
                        Add(cmd, "@reader", SqlDbType.Int, 0, readerId);
                        if (cmd.ExecuteNonQuery() != 1)
                            throw new CirculationException(HttpStatusCode.NotFound, "读者不存在。");
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

        public IList<LibraryReservationDto> Reservations()
        {
            var result = new List<LibraryReservationDto>();
            using (var db = Connect())
            using (var cmd = Command(db, null, @"
                SELECT h.ReservationId,h.BookNum,b.BookName,h.ReaderId,r.FullName,r.CardNumber,
                       h.Status,h.RequestedAtUtc,h.ClosedAtUtc
                FROM dbo.LibraryReservation h
                JOIN dbo.BookInfo b ON b.BookNum=h.BookNum
                JOIN dbo.LibraryReader r ON r.ReaderId=h.ReaderId
                ORDER BY CASE WHEN h.Status=0 THEN 0 ELSE 1 END,
                         h.RequestedAtUtc, h.ReservationId"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(new LibraryReservationDto
                    {
                        ReservationId = reader.GetInt32(0),
                        BookNum = reader.GetInt32(1),
                        BookName = reader.GetString(2),
                        ReaderId = reader.GetInt32(3),
                        ReaderName = reader.GetString(4),
                        CardNumber = reader.GetString(5),
                        Status = reader.GetByte(6),
                        RequestedAtUtc = reader.GetDateTime(7),
                        ClosedAtUtc = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8)
                    });
            }
            return result;
        }

        public int Reserve(NewReservationRequest input)
        {
            if (input == null || input.BookNum <= 0 || input.ReaderId <= 0)
                throw new CirculationException(HttpStatusCode.BadRequest, "请选择书目与有效读者。");

            using (var db = Connect())
            using (var tx = db.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    // Serialize reservations and checkouts per title by first locking BookInfo.
                    LockBook(db, tx, input.BookNum);

                    var availableCount = ReadCount(db, tx,
                        @"SELECT COUNT(*) FROM dbo.LibraryCopy
                          WHERE BookNum=@book AND Status=0", "@book", input.BookNum);
                    var pendingCount = ReadCount(db, tx,
                        @"SELECT COUNT(*) FROM dbo.LibraryReservation
                          WHERE BookNum=@book AND Status=0", "@book", input.BookNum);

                    bool active = LockReader(db, tx, input.ReaderId);
                    int borrowedCount;
                    using (var cmd = Command(db, tx, @"
                        SELECT COUNT(*) FROM dbo.LibraryLoan l
                        JOIN dbo.LibraryCopy c ON c.CopyId=l.CopyId
                        WHERE l.ReaderId=@reader AND c.BookNum=@book
                          AND l.ReturnedAtUtc IS NULL"))
                    {
                        Add(cmd, "@reader", SqlDbType.Int, 0, input.ReaderId);
                        Add(cmd, "@book", SqlDbType.Int, 0, input.BookNum);
                        borrowedCount = (int)cmd.ExecuteScalar();
                    }

                    CirculationAdvancedRules.EnsureCanReserve(
                        active, borrowedCount != 0, availableCount != 0, pendingCount != 0);

                    var duplicate = 0;
                    using (var cmd = Command(db, tx, @"
                        SELECT COUNT(*) FROM dbo.LibraryReservation
                        WHERE ReaderId=@reader AND BookNum=@book AND Status=0"))
                    {
                        Add(cmd, "@reader", SqlDbType.Int, 0, input.ReaderId);
                        Add(cmd, "@book", SqlDbType.Int, 0, input.BookNum);
                        duplicate = (int)cmd.ExecuteScalar();
                    }
                    if (duplicate != 0)
                        throw new CirculationException(HttpStatusCode.Conflict,
                            "该读者已经预约此书，不能重复排队。");

                    int id;
                    using (var cmd = Command(db, tx, @"
                        INSERT dbo.LibraryReservation(BookNum,ReaderId)
                        OUTPUT INSERTED.ReservationId VALUES(@book,@reader)"))
                    {
                        Add(cmd, "@book", SqlDbType.Int, 0, input.BookNum);
                        Add(cmd, "@reader", SqlDbType.Int, 0, input.ReaderId);
                        id = (int)cmd.ExecuteScalar();
                    }
                    tx.Commit();
                    return id;
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        public void CancelReservation(int reservationId)
        {
            if (reservationId <= 0)
                throw new CirculationException(HttpStatusCode.BadRequest, "预约编号无效。");
            using (var db = Connect())
            using (var tx = db.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    int bookId;
                    using (var cmd = Command(db, tx,
                        "SELECT BookNum FROM dbo.LibraryReservation WHERE ReservationId=@id"))
                    {
                        Add(cmd, "@id", SqlDbType.Int, 0, reservationId);
                        var found = cmd.ExecuteScalar();
                        if (found == null)
                            throw new CirculationException(HttpStatusCode.NotFound, "预约记录不存在。");
                        bookId = (int)found;
                    }
                    LockBook(db, tx, bookId);
                    using (var cmd = Command(db, tx, @"
                        UPDATE dbo.LibraryReservation
                        SET Status=2,ClosedAtUtc=CASE WHEN SYSUTCDATETIME()<RequestedAtUtc
                            THEN RequestedAtUtc ELSE SYSUTCDATETIME() END
                        WHERE ReservationId=@id AND Status=0"))
                    {
                        Add(cmd, "@id", SqlDbType.Int, 0, reservationId);
                        if (cmd.ExecuteNonQuery() != 1)
                            throw new CirculationException(HttpStatusCode.Conflict,
                                "预约已完成或取消，不能再次取消。");
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

        public DateTime Renew(int loanId)
        {
            if (loanId <= 0)
                throw new CirculationException(HttpStatusCode.BadRequest, "借阅编号无效。");
            using (var db = Connect())
            using (var tx = db.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    int bookId;
                    using (var cmd = Command(db, tx, @"
                        SELECT c.BookNum FROM dbo.LibraryLoan l
                        JOIN dbo.LibraryCopy c ON c.CopyId=l.CopyId
                        WHERE l.LoanId=@loan"))
                    {
                        Add(cmd, "@loan", SqlDbType.Int, 0, loanId);
                        var found = cmd.ExecuteScalar();
                        if (found == null)
                            throw new CirculationException(HttpStatusCode.NotFound, "借阅记录不存在。");
                        bookId = (int)found;
                    }
                    LockBook(db, tx, bookId);
                    DateTime dueAt;
                    bool returned;
                    int renewCount, readerId;
                    using (var cmd = Command(db, tx, @"
                        SELECT ReaderId,DueAtUtc,ReturnedAtUtc,RenewCount
                        FROM dbo.LibraryLoan WITH (UPDLOCK,HOLDLOCK)
                        WHERE LoanId=@loan"))
                    {
                        Add(cmd, "@loan", SqlDbType.Int, 0, loanId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new CirculationException(HttpStatusCode.NotFound, "借阅记录不存在。");
                            readerId = reader.GetInt32(0);
                            dueAt = reader.GetDateTime(1);
                            returned = !reader.IsDBNull(2);
                            renewCount = Convert.ToInt32(reader.GetByte(3));
                        }
                    }

                    int waiting;
                    using (var cmd = Command(db, tx, @"
                        SELECT COUNT(*) FROM dbo.LibraryReservation
                        WHERE BookNum=@book AND Status=0 AND ReaderId<>@reader"))
                    {
                        Add(cmd, "@book", SqlDbType.Int, 0, bookId);
                        Add(cmd, "@reader", SqlDbType.Int, 0, readerId);
                        waiting = (int)cmd.ExecuteScalar();
                    }
                    CirculationAdvancedRules.EnsureCanRenew(
                        dueAt, DateTime.UtcNow, returned, renewCount, waiting != 0);
                    var newDue = CirculationAdvancedRules.RenewedDueAtUtc(dueAt);
                    using (var cmd = Command(db, tx, @"
                        UPDATE dbo.LibraryLoan
                        SET DueAtUtc=@newDue,RenewCount=RenewCount+1
                        WHERE LoanId=@loan AND ReturnedAtUtc IS NULL AND RenewCount=0"))
                    {
                        Add(cmd, "@newDue", SqlDbType.DateTime2, 0, newDue);
                        Add(cmd, "@loan", SqlDbType.Int, 0, loanId);
                        if (cmd.ExecuteNonQuery() != 1)
                            throw new CirculationException(HttpStatusCode.Conflict, "续借状态已变化，请刷新。");
                    }
                    tx.Commit();
                    return newDue;
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