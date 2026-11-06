using System;
using System.ComponentModel.DataAnnotations;

namespace 图书馆理模块.Models
{
    public sealed class LibraryCopyDto
    {
        public int CopyId { get; set; }
        public int BookNum { get; set; }
        public string BookName { get; set; }
        public string Barcode { get; set; }
        public string ShelfLocation { get; set; }
        public bool IsBorrowed { get; set; }
    }
    public sealed class LibraryReaderDto
    {
        public int ReaderId { get; set; }
        public string CardNumber { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public bool IsActive { get; set; }
    }
    public sealed class LibraryLoanDto
    {
        public int LoanId { get; set; }
        public int CopyId { get; set; }
        public string Barcode { get; set; }
        public string BookName { get; set; }
        public int ReaderId { get; set; }
        public string ReaderName { get; set; }
        public string CardNumber { get; set; }
        public DateTime BorrowedAtUtc { get; set; }
        public DateTime DueAtUtc { get; set; }
        public DateTime? ReturnedAtUtc { get; set; }
        public byte RenewCount { get; set; }
    }
    public sealed class NewCopyRequest
    {
        [Range(1, int.MaxValue, ErrorMessage = "请选择已有图书。")]
        public int BookNum { get; set; }
        [Required, StringLength(32, MinimumLength = 3)]
        public string Barcode { get; set; }
        [StringLength(80)]
        public string ShelfLocation { get; set; }
    }
    public sealed class NewReaderRequest
    {
        [Required, StringLength(32, MinimumLength = 3)]
        public string CardNumber { get; set; }
        [Required, StringLength(50)]
        public string FullName { get; set; }
        [StringLength(30)]
        public string Phone { get; set; }
    }
    public sealed class BorrowRequest
    {
        [Range(1, int.MaxValue, ErrorMessage = "请选择图书副本。")]
        public int CopyId { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "请选择读者。")]
        public int ReaderId { get; set; }
    }
}