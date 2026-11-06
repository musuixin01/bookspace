using System;
using System.Net;
using System.Text.RegularExpressions;

namespace 图书馆理模块.Services
{
    public sealed class CirculationException : Exception
    {
        public HttpStatusCode Status { get; private set; }
        public CirculationException(HttpStatusCode status, string message) : base(message) { Status = status; }
    }

    public static class CirculationRules
    {
        public const int LoanDays = 14;
        public const int MaxActiveLoans = 5;

        public static string NormalizeCode(string code)
        {
            var normalized = (code ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(normalized, @"^[A-Z0-9-]{3,32}$"))
                throw new CirculationException(HttpStatusCode.BadRequest,
                    "编号须为 3～32 位英文字母、数字或连字符。");
            return normalized;
        }

        public static string NormalizeName(string name)
        {
            var normalized = (name ?? "").Trim();
            if (normalized.Length == 0 || normalized.Length > 50)
                throw new CirculationException(HttpStatusCode.BadRequest, "读者姓名须为 1～50 个字符。");
            return normalized;
        }

        public static DateTime DueAtUtc(DateTime borrowedAtUtc)
        {
            return borrowedAtUtc.AddDays(LoanDays);
        }

        public static void EnsureCanBorrow(bool copyAvailable, bool readerActive, int activeLoanCount)
        {
            if (!readerActive)
                throw new CirculationException(HttpStatusCode.Conflict, "读者不存在或已停用。");
            if (!copyAvailable)
                throw new CirculationException(HttpStatusCode.Conflict, "这册图书已经借出，无法重复借阅。");
            if (activeLoanCount >= MaxActiveLoans)
                throw new CirculationException(HttpStatusCode.Conflict, "该读者同时借阅不得超过 5 册。");
        }
    }
}