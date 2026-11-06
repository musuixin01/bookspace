using System;
using System.Net;
using 图书馆理模块.Services;

internal static class CirculationRulesSmoke
{
    private static int passed;

    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL " + label);
        Console.WriteLine("PASS " + label);
        passed++;
    }

    private static void ExpectError(Action action, HttpStatusCode status, string label)
    {
        try { action(); }
        catch (CirculationException e)
        {
            Assert(e.Status == status, label);
            return;
        }
        throw new Exception("FAIL " + label + " (no exception)");
    }

    private static int Main()
    {
        Assert(CirculationRules.NormalizeCode(" bk-0001 ") == "BK-0001",
            "barcode trim and uppercase");
        ExpectError(() => CirculationRules.NormalizeCode("bad code"),
            HttpStatusCode.BadRequest, "invalid barcode rejected");
        ExpectError(() => CirculationRules.NormalizeCode("12"),
            HttpStatusCode.BadRequest, "short barcode rejected");
        Assert(CirculationRules.NormalizeName(" 测试读者 ") == "测试读者",
            "reader name trimmed");
        ExpectError(() => CirculationRules.NormalizeName("  "),
            HttpStatusCode.BadRequest, "blank reader name rejected");
        var borrowed = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        Assert(CirculationRules.DueAtUtc(borrowed) == borrowed.AddDays(14),
            "14-day fixed loan period");
        CirculationRules.EnsureCanBorrow(true, true, 0);
        Assert(true, "available copy accepted");
        CirculationRules.EnsureCanBorrow(true, true, 4);
        Assert(true, "fifth active loan accepted");
        ExpectError(() => CirculationRules.EnsureCanBorrow(false, true, 0),
            HttpStatusCode.Conflict, "duplicate checkout rejected");
        ExpectError(() => CirculationRules.EnsureCanBorrow(true, false, 0),
            HttpStatusCode.Conflict, "inactive reader rejected");
        ExpectError(() => CirculationRules.EnsureCanBorrow(true, true, 5),
            HttpStatusCode.Conflict, "sixth active loan rejected");

        var writeUrl = new Uri("http://localhost:5123/api/circulation/loans");
        var samePage = new Uri("http://localhost:5123/Circulation");
        var otherHost = new Uri("https://example.com/");
        Assert(LocalAccess.IsSameOriginWrite(writeUrl, "http://localhost:5123", null),
            "same-origin Origin accepted");
        Assert(LocalAccess.IsSameOriginWrite(writeUrl, null, samePage),
            "same-origin Referer accepted");
        Assert(LocalAccess.IsSameOriginWrite(writeUrl, "http://localhost:5123", samePage),
            "both matching headers accepted");
        Assert(!LocalAccess.IsSameOriginWrite(writeUrl, null, null),
            "missing Origin and Referer rejected");
        Assert(!LocalAccess.IsSameOriginWrite(writeUrl, "null", null),
            "opaque Origin rejected");
        Assert(!LocalAccess.IsSameOriginWrite(writeUrl, "https://example.com", null),
            "external Origin rejected");
        Assert(!LocalAccess.IsSameOriginWrite(writeUrl, "http://localhost:5124", null),
            "different port rejected");
        Assert(!LocalAccess.IsSameOriginWrite(writeUrl, "https://localhost:5123", null),
            "different scheme rejected");
        Assert(!LocalAccess.IsSameOriginWrite(writeUrl, "http://localhost:5123/private", null),
            "Origin with path rejected");
        Assert(!LocalAccess.IsSameOriginWrite(writeUrl, "http://localhost:5123", otherHost),
            "conflicting Referer rejected");
        Assert(!LocalAccess.IsSameOriginWrite(null, "http://localhost:5123", null),
            "missing request URL rejected");

        Console.WriteLine("RESULT " + passed + " checks passed");
        CirculationAdvancedRulesSmoke.Run();
        return 0;
    }
}