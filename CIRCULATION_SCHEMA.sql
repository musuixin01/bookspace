/*
 BOOKSPACE circulation schema · MANUAL REVIEW REQUIRED.
 Backup your existing Books database and inspect dbo.BookInfo before execution.
 This script does not migrate/delete any existing book records.
*/
USE [Books];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.LibraryReader', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LibraryReader (
        ReaderId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LibraryReader PRIMARY KEY,
        CardNumber NVARCHAR(32) NOT NULL,
        FullName NVARCHAR(50) NOT NULL,
        Phone NVARCHAR(30) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_LibraryReader_Active DEFAULT (1),
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LibraryReader_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_LibraryReader_CardNumber UNIQUE (CardNumber)
    );
END;

IF OBJECT_ID(N'dbo.LibraryCopy', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LibraryCopy (
        CopyId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LibraryCopy PRIMARY KEY,
        BookNum INT NOT NULL,
        Barcode NVARCHAR(32) NOT NULL,
        ShelfLocation NVARCHAR(80) NULL,
        Status TINYINT NOT NULL CONSTRAINT DF_LibraryCopy_Status DEFAULT (0),
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LibraryCopy_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_LibraryCopy_Barcode UNIQUE (Barcode),
        CONSTRAINT CK_LibraryCopy_Status CHECK (Status IN (0, 1)),
        CONSTRAINT FK_LibraryCopy_Book FOREIGN KEY (BookNum)
            REFERENCES dbo.BookInfo(BookNum) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'dbo.LibraryLoan', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LibraryLoan (
        LoanId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LibraryLoan PRIMARY KEY,
        CopyId INT NOT NULL,
        ReaderId INT NOT NULL,
        BorrowedAtUtc DATETIME2(0) NOT NULL,
        DueAtUtc DATETIME2(0) NOT NULL,
        ReturnedAtUtc DATETIME2(0) NULL,
        CONSTRAINT FK_LibraryLoan_Copy FOREIGN KEY (CopyId)
            REFERENCES dbo.LibraryCopy(CopyId) ON DELETE NO ACTION,
        CONSTRAINT FK_LibraryLoan_Reader FOREIGN KEY (ReaderId)
            REFERENCES dbo.LibraryReader(ReaderId) ON DELETE NO ACTION,
        CONSTRAINT CK_LibraryLoan_Dates CHECK (
            DueAtUtc > BorrowedAtUtc AND
            (ReturnedAtUtc IS NULL OR ReturnedAtUtc >= BorrowedAtUtc)
        )
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id=OBJECT_ID(N'dbo.LibraryLoan') AND name=N'UX_LibraryLoan_ActiveCopy')
    CREATE UNIQUE INDEX UX_LibraryLoan_ActiveCopy
        ON dbo.LibraryLoan(CopyId) WHERE ReturnedAtUtc IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id=OBJECT_ID(N'dbo.LibraryLoan') AND name=N'IX_LibraryLoan_ReaderActive')
    CREATE INDEX IX_LibraryLoan_ReaderActive
        ON dbo.LibraryLoan(ReaderId, ReturnedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id=OBJECT_ID(N'dbo.LibraryCopy') AND name=N'IX_LibraryCopy_Book')
    CREATE INDEX IX_LibraryCopy_Book ON dbo.LibraryCopy(BookNum);

COMMIT TRANSACTION;
GO
-- Existing BookInfo rows are not automatically converted to physical copies.
-- Register each copy explicitly using its unique barcode.
-- If tables exist with incompatible columns, STOP and reconcile manually.