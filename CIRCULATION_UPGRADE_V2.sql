/*
 BOOKSPACE circulation v2 · additive SQL Server upgrade.
 Manual review and execution only. Requires V1 circulation schema.
 NO database actions are performed by running/building the application.
 Existing book / loan records are not deleted or overwritten.
 Schema-changing statements use separate dynamic batches to support
 newly added columns/tables in a single atomic TRY/CATCH transaction.
*/
USE [Books];
GO
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF DB_NAME() <> N'Books'
        THROW 51000, 'Unexpected database; stop.', 1;

    IF OBJECT_ID(N'dbo.BookInfo', N'U') IS NULL
       OR OBJECT_ID(N'dbo.LibraryCopy', N'U') IS NULL
       OR OBJECT_ID(N'dbo.LibraryReader', N'U') IS NULL
       OR OBJECT_ID(N'dbo.LibraryLoan', N'U') IS NULL
        THROW 51001, 'BOOKSPACE V1 tables are missing. Apply V1 only after review.', 1;

    IF COL_LENGTH(N'dbo.LibraryLoan', N'LoanId') IS NULL
       OR COL_LENGTH(N'dbo.LibraryLoan', N'DueAtUtc') IS NULL
       OR COL_LENGTH(N'dbo.LibraryLoan', N'ReturnedAtUtc') IS NULL
       OR COL_LENGTH(N'dbo.LibraryLoan', N'ReaderId') IS NULL
       OR COL_LENGTH(N'dbo.LibraryLoan', N'CopyId') IS NULL
       OR COL_LENGTH(N'dbo.LibraryReader', N'ReaderId') IS NULL
       OR COL_LENGTH(N'dbo.BookInfo', N'BookNum') IS NULL
        THROW 51002, 'V1 columns are missing or incompatible; stop.', 1;

    IF COL_LENGTH(N'dbo.LibraryLoan', N'RenewCount') IS NULL
        EXEC(N'ALTER TABLE dbo.LibraryLoan
          ADD RenewCount TINYINT NOT NULL
          CONSTRAINT DF_LibraryLoan_RenewCount DEFAULT(0) WITH VALUES;');

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.LibraryLoan')
          AND name = N'RenewCount'
          AND (system_type_id <> 48 OR is_nullable <> 0)
    )
        THROW 51003, 'Existing RenewCount type is incompatible; stop.', 1;

    DECLARE @invalidRenewals INT = 0;
    EXEC sys.sp_executesql
        N'SELECT @invalid = COUNT(*)
          FROM dbo.LibraryLoan WHERE RenewCount NOT IN (0,1);',
        N'@invalid INT OUTPUT', @invalid = @invalidRenewals OUTPUT;

    IF @invalidRenewals > 0
        THROW 51004, 'Existing RenewCount values violate policy; stop.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.LibraryLoan')
          AND name=N'CK_LibraryLoan_RenewCount'
    )
        EXEC(N'ALTER TABLE dbo.LibraryLoan
          WITH CHECK ADD CONSTRAINT CK_LibraryLoan_RenewCount
          CHECK (RenewCount IN (0,1));');

    IF OBJECT_ID(N'dbo.LibraryReservation', N'U') IS NULL
        EXEC(N'CREATE TABLE dbo.LibraryReservation (
            ReservationId INT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_LibraryReservation PRIMARY KEY,
            BookNum INT NOT NULL,
            ReaderId INT NOT NULL,
            Status TINYINT NOT NULL
                CONSTRAINT DF_LibraryReservation_Status DEFAULT(0),
            RequestedAtUtc DATETIME2(0) NOT NULL
                CONSTRAINT DF_LibraryReservation_Requested DEFAULT SYSUTCDATETIME(),
            ClosedAtUtc DATETIME2(0) NULL,
            CONSTRAINT FK_LibraryReservation_Book FOREIGN KEY (BookNum)
                REFERENCES dbo.BookInfo(BookNum) ON DELETE NO ACTION,
            CONSTRAINT FK_LibraryReservation_Reader FOREIGN KEY (ReaderId)
                REFERENCES dbo.LibraryReader(ReaderId) ON DELETE NO ACTION,
            CONSTRAINT CK_LibraryReservation_State CHECK (
                (Status=0 AND ClosedAtUtc IS NULL) OR
                (Status IN (1,2) AND ClosedAtUtc IS NOT NULL
                 AND ClosedAtUtc >= RequestedAtUtc)
            )
        );');

    IF COL_LENGTH(N'dbo.LibraryReservation', N'ReservationId') IS NULL
       OR COL_LENGTH(N'dbo.LibraryReservation', N'BookNum') IS NULL
       OR COL_LENGTH(N'dbo.LibraryReservation', N'ReaderId') IS NULL
       OR COL_LENGTH(N'dbo.LibraryReservation', N'Status') IS NULL
       OR COL_LENGTH(N'dbo.LibraryReservation', N'RequestedAtUtc') IS NULL
       OR COL_LENGTH(N'dbo.LibraryReservation', N'ClosedAtUtc') IS NULL
        THROW 51005, 'LibraryReservation fields are missing; stop.', 1;

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.LibraryReservation')
          AND name IN (N'ReservationId',N'BookNum',N'ReaderId',N'Status',
                       N'RequestedAtUtc',N'ClosedAtUtc')
          AND (
             (name IN (N'ReservationId',N'BookNum',N'ReaderId') AND
                 (system_type_id<>56 OR is_nullable<>0))
             OR (name=N'Status' AND
                 (system_type_id<>48 OR is_nullable<>0))
             OR (name=N'RequestedAtUtc' AND
                 (system_type_id<>42 OR is_nullable<>0))
             OR (name=N'ClosedAtUtc' AND system_type_id<>42)
          )
    )
        THROW 51006, 'LibraryReservation field types are incompatible; stop.', 1;

    DECLARE @invalidReservations BIT = 0;
    EXEC sys.sp_executesql N'
        IF EXISTS (
            SELECT 1 FROM dbo.LibraryReservation
            WHERE Status NOT IN (0,1,2)
               OR (Status=0 AND ClosedAtUtc IS NOT NULL)
               OR (Status IN (1,2) AND
                   (ClosedAtUtc IS NULL OR ClosedAtUtc<RequestedAtUtc))
        ) SET @invalid = 1;',
        N'@invalid BIT OUTPUT', @invalid = @invalidReservations OUTPUT;

    IF @invalidReservations = 1
        THROW 51007, 'Existing reservation rows violate state policy; stop.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.LibraryReservation')
          AND name=N'CK_LibraryReservation_State'
    )
        EXEC(N'ALTER TABLE dbo.LibraryReservation
          WITH CHECK ADD CONSTRAINT CK_LibraryReservation_State CHECK (
            (Status=0 AND ClosedAtUtc IS NULL) OR
            (Status IN (1,2) AND ClosedAtUtc IS NOT NULL
             AND ClosedAtUtc>=RequestedAtUtc)
          );');

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id=OBJECT_ID(N'dbo.LibraryReservation')
          AND name=N'FK_LibraryReservation_Book'
    )
        EXEC(N'ALTER TABLE dbo.LibraryReservation
          WITH CHECK ADD CONSTRAINT FK_LibraryReservation_Book
          FOREIGN KEY (BookNum) REFERENCES dbo.BookInfo(BookNum)
          ON DELETE NO ACTION;');

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id=OBJECT_ID(N'dbo.LibraryReservation')
          AND name=N'FK_LibraryReservation_Reader'
    )
        EXEC(N'ALTER TABLE dbo.LibraryReservation
          WITH CHECK ADD CONSTRAINT FK_LibraryReservation_Reader
          FOREIGN KEY (ReaderId) REFERENCES dbo.LibraryReader(ReaderId)
          ON DELETE NO ACTION;');

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.LibraryReservation')
          AND name=N'UX_LibraryReservation_ActiveReaderBook'
    )
        EXEC(N'CREATE UNIQUE INDEX UX_LibraryReservation_ActiveReaderBook
          ON dbo.LibraryReservation(ReaderId,BookNum) WHERE Status=0;');

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.LibraryReservation')
          AND name=N'IX_LibraryReservation_BookQueue'
    )
        EXEC(N'CREATE INDEX IX_LibraryReservation_BookQueue
          ON dbo.LibraryReservation(BookNum,Status,RequestedAtUtc,ReservationId)
          INCLUDE (ReaderId);');

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.LibraryLoan')
          AND name=N'IX_LibraryLoan_DueActive'
    )
        EXEC(N'CREATE INDEX IX_LibraryLoan_DueActive
          ON dbo.LibraryLoan(DueAtUtc)
          INCLUDE (ReaderId,CopyId) WHERE ReturnedAtUtc IS NULL;');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
-- Reservation Status: 0 pending, 1 fulfilled, 2 canceled.
-- Only the oldest pending reservation of a title has borrowing priority.
-- Deactivated readers cannot borrow/reserve; completed history is retained.
-- 0 pending, 1 fulfilled on a successful checkout, 2 canceled.
-- The earliest pending reservation of a title receives first borrowing priority.
-- Completed/canceled history is retained; no physical stock is fabricated.