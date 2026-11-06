using System;
using System.Net;

namespace 图书馆理模块.Services
{
    /// <summary>Explicit rules for V2 circulation, independently testable without SQL Server.</summary>
    public static class CirculationAdvancedRules
    {
        public const int MaxRenewals = 1;

        public static void EnsureCanDeactivate(int activeLoanCount, int pendingReservationCount)
        {
            if (activeLoanCount != 0 || pendingReservationCount != 0)
                throw new CirculationException(HttpStatusCode.Conflict,
                    "该读者仍有借阅中图书或未完成预约，不能停用。请先归还或取消预约。");
        }

        public static void EnsureCanReserve(bool readerActive, bool alreadyBorrowingTitle,
            bool availableCopyExists, bool pendingReservationsExist)
        {
            if (!readerActive)
                throw new CirculationException(HttpStatusCode.Conflict, "读者不存在或已停用。");
            if (alreadyBorrowingTitle)
                throw new CirculationException(HttpStatusCode.Conflict, "读者已经借阅这本书，不能重复预约。");
            if (availableCopyExists && !pendingReservationsExist)
                throw new CirculationException(HttpStatusCode.Conflict, "目前有在馆副本，请直接办理借阅。");
        }

        public static void EnsureReservationPriority(int firstPendingReaderId, int borrowerId)
        {
            if (firstPendingReaderId != 0 && firstPendingReaderId != borrowerId)
                throw new CirculationException(HttpStatusCode.Conflict,
                    "此书已有其他读者在前排队，请优先满足预约。");
        }

        public static void EnsureCanRenew(DateTime dueAtUtc, DateTime nowUtc,
            bool returned, int renewCount, bool otherReaderWaiting)
        {
            if (returned)
                throw new CirculationException(HttpStatusCode.Conflict, "已归还的书不能续借。");
            if (dueAtUtc < nowUtc)
                throw new CirculationException(HttpStatusCode.Conflict, "已逾期，请先办理归还。");
            if (renewCount >= MaxRenewals)
                throw new CirculationException(HttpStatusCode.Conflict, "每笔借阅最多续借一次。");
            if (otherReaderWaiting)
                throw new CirculationException(HttpStatusCode.Conflict, "已有其他读者预约此书，暂不能续借。");
        }

        public static DateTime RenewedDueAtUtc(DateTime originalDueAtUtc)
        {
            return originalDueAtUtc.AddDays(CirculationRules.LoanDays);
        }
    }
}