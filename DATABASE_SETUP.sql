-- 供人工评审后初始化全新 SQL Server 测试库，软件不会自动运行。
IF DB_ID(N'Books') IS NULL CREATE DATABASE Books;
GO
USE Books;
GO
IF OBJECT_ID(N'dbo.BookCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookCategory(
        CategoryId INT IDENTITY(1,1) PRIMARY KEY,
        CategoryName NVARCHAR(20) NOT NULL UNIQUE
    );
    INSERT INTO dbo.BookCategory(CategoryName) VALUES (N'文学'),(N'计算机'),(N'工程技术');
END;
GO
IF OBJECT_ID(N'dbo.BookInfo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookInfo(
        BookNum INT IDENTITY(1,1) PRIMARY KEY,
        BookName NVARCHAR(50) NOT NULL,
        BookPrice DECIMAL(6,2) NULL,
        Author NVARCHAR(30) NULL,
        Publisher NVARCHAR(100) NULL,
        CategoryId INT NOT NULL,
        CONSTRAINT FK_BookInfo_Category FOREIGN KEY (CategoryId)
            REFERENCES dbo.BookCategory(CategoryId)
    );
END;
GO
-- 不会迁移旧数据库；已有数据时先备份并核对真实结构。