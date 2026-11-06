using System;
using System.ComponentModel.DataAnnotations;

namespace 图书馆理模块.Models
{
    public sealed class UpdateReaderRequest
    {
        [Required, StringLength(50)]
        public string FullName { get; set; }
        [StringLength(30)]
        public string Phone { get; set; }
    }

    public sealed class ReaderStatusRequest
    {
        public bool IsActive { get; set; }
    }

    public sealed class NewReservationRequest
    {
        [Range(1, int.MaxValue)]
        public int BookNum { get; set; }
        [Range(1, int.MaxValue)]
        public int ReaderId { get; set; }
    }

    public sealed class LibraryReservationDto
    {
        public int ReservationId { get; set; }
        public int BookNum { get; set; }
        public string BookName { get; set; }
        public int ReaderId { get; set; }
        public string ReaderName { get; set; }
        public string CardNumber { get; set; }
        public byte Status { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
    }
}