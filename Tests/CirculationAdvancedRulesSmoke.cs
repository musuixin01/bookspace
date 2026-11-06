using System;
using System.Net;
using 图书馆理模块.Services;

internal static class CirculationAdvancedRulesSmoke
{
    private static int count;
    private static void Assert(bool passed, string label)
    {
        if (!passed) throw new Exception("FAIL " + label);
        count++;
        Console.WriteLine("PASS " + label);
    }

    private static void Conflict(Action test, string label)
    {
        try { test(); }
        catch (CirculationException error)
        {
            Assert(error.Status == HttpStatusCode.Conflict, label);
            return;
        }
        throw new Exception("FAIL " + label + ": did not throw");
    }

    public static int Run()
    {
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        CirculationAdvancedRules.EnsureCanDeactivate(0, 0);
        Assert(true, "clean reader can be disabled");
        Conflict(() => CirculationAdvancedRules.EnsureCanDeactivate(1, 0),
            "reader with active loans cannot be disabled");
        Conflict(() => CirculationAdvancedRules.EnsureCanDeactivate(0, 1),
            "reader with pending reservations cannot be disabled");

        CirculationAdvancedRules.EnsureCanReserve(true, false, false, false);
        Assert(true, "borrowed-out title can be reserved");
        CirculationAdvancedRules.EnsureCanReserve(true, false, true, true);
        Assert(true, "queued title still accepts later reservations");
        Conflict(() => CirculationAdvancedRules.EnsureCanReserve(false, false, false, false),
            "inactive reader cannot reserve");
        Conflict(() => CirculationAdvancedRules.EnsureCanReserve(true, true, false, false),
            "reader already borrowing title cannot reserve");
        Conflict(() => CirculationAdvancedRules.EnsureCanReserve(true, false, true, false),
            "available title without queue should be directly borrowed");

        CirculationAdvancedRules.EnsureReservationPriority(0, 2);
        Assert(true, "no reservations means free checkout");
        CirculationAdvancedRules.EnsureReservationPriority(2, 2);
        Assert(true, "first queued reader can borrow");
        Conflict(() => CirculationAdvancedRules.EnsureReservationPriority(2, 3),
            "other reader cannot jump queue");

        CirculationAdvancedRules.EnsureCanRenew(now.AddDays(4), now, false, 0, false);
        Assert(true, "valid renewal accepted");
        Conflict(() => CirculationAdvancedRules.EnsureCanRenew(now.AddDays(4), now, true, 0, false),
            "returned loan cannot renew");
        Conflict(() => CirculationAdvancedRules.EnsureCanRenew(now.AddSeconds(-1), now, false, 0, false),
            "overdue loan cannot renew");
        Conflict(() => CirculationAdvancedRules.EnsureCanRenew(now.AddDays(4), now, false, 1, false),
            "second renewal rejected");
        Conflict(() => CirculationAdvancedRules.EnsureCanRenew(now.AddDays(4), now, false, 0, true),
            "waiting other reader blocks renewal");
        Assert(CirculationAdvancedRules.RenewedDueAtUtc(now.AddDays(14)) == now.AddDays(28),
            "renewal adds exactly 14 days to old due date");

        Console.WriteLine("RESULT V2 " + count + " checks passed");
        return count;
    }
}