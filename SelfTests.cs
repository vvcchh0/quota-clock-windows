using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace QuotaClock
{
    public static class SelfTests
    {
        static int passed;
        static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
        static void Near(double a, double b, string message) { Check(Math.Abs(a - b) < .0001, message); }
        public static int Run()
        {
            string report;
            try
            {
                double[] a = QuotaMath.Angles(73.4m); Near(a[0], 95.76, "hour 73.4"); Near(a[1], 237.6, "minute 73.4"); Near(a[2], 216, "second 73.4");
                a = QuotaMath.Angles(100); Near(a[0], 0, "full starts at twelve"); Near(a[1], 0, "full minute"); Near(a[2], 0, "full second");
                a = QuotaMath.Angles(0); Near(a[0], 360, "empty completes twelve-hour cycle"); Near(a[1], 0, "empty minute");
                a = QuotaMath.Angles(99.96m); Near(a[0], 0, "rounding carry to full"); Near(a[1], 0, "rounding carry minute"); Near(a[2], 0, "rounding carry second");
                Near(QuotaMath.Angles(90)[0],36,"ten percent used maps to 1.2 hours");
                Check(QuotaMath.Angles(80)[0]>QuotaMath.Angles(90)[0],"more consumption advances clockwise");
                Check(QuotaMath.Display(-3) == 0 && QuotaMath.Display(103) == 100, "clamp bounds");
                Check(QuotaMath.Percent(73.4m, true) == "73.4%", "fraction format");
                Check(QuotaMath.Percent(1, false) == "1%", "no invented decimal for integer source");
                var now = DateTimeOffset.FromUnixTimeSeconds(1000);
                var snap = QuotaParser.Parse("{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":26.6,\"windowDurationMins\":300,\"resetsAt\":2000}}}}", now);
                Check(snap.Windows.Count == 1, "legacy bucket");
                Check(snap.Windows[0].Remaining == 73.4m && snap.Windows[0].Fractional, "fraction preserved");
                Check(snap.Windows[0].Minutes == 300, "window duration");
                Check(snap.Windows[0].Reset == 2000, "reset timestamp");
                snap = QuotaParser.Parse("{\"rateLimitsByLimitId\":{\"codex_other\":{\"primary\":{\"usedPercent\":50}},\"codex\":{\"primary\":{\"usedPercent\":99},\"secondary\":null}},\"rateLimits\":{\"primary\":{\"usedPercent\":0}}}", now);
                Check(snap.Windows.Count == 2, "prefer multi-bucket without legacy double count");
                Check(snap.Windows[0].Id == "codex:primary", "codex first");
                Check(snap.Windows[0].Remaining == 1 && !snap.Windows[0].Fractional, "integer source");
                snap = QuotaParser.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":null},\"secondary\":{\"usedPercent\":\"25\"}}}", now);
                Check(snap.Windows.Count == 0, "missing or string values never appear as full quota");
                snap = QuotaParser.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":101},\"secondary\":{\"usedPercent\":-1}}}", now);
                Check(snap.Windows.Count == 0, "invalid quota rejected");
                snap = QuotaParser.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":100},\"secondary\":{\"usedPercent\":0}}}", now);
                Check(snap.Windows[0].Remaining == 0 && snap.Windows[1].Remaining == 100, "exact zero and full");
                Check(!QuotaMath.Expired(new QuotaWindow { Reset = null }, now), "unknown reset");
                Check(QuotaMath.Expired(new QuotaWindow { Reset = 1000 }, now), "expires at boundary");
                Check(!QuotaMath.Expired(new QuotaWindow { Reset = 1001 }, now), "future still valid");
                Check(QuotaMath.ResetText(new QuotaWindow { Reset = 1000 + 90000 }, now).Contains("1天"), "daily countdown");
                Check(QuotaMath.ResetText(new QuotaWindow { Reset = null }, now).Contains("未提供"), "no fabricated reset");
                Check(TaskbarPlacement.Left(0, 1800, 168, 0) == 1632, "taskbar left of tray");
                Check(TaskbarPlacement.Left(0, 1800, 168, 90) == 1542, "taskbar offset moves left");
                Check(TaskbarPlacement.Left(0, 100, 168, 600) == 8, "taskbar screen bound");
                report = "PASS: " + passed + " assertions. Mapping, boundaries, precision, bucket precedence, missing data and expiry.";
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results.txt"), report);
                Console.WriteLine(report); return 0;
            }
            catch (Exception ex)
            {
                report = "FAIL after " + passed + ": " + ex.Message;
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results.txt"), report); Console.WriteLine(report); return 1;
            }
        }
    }
    public static class Probe
    {
        public static int Run()
        {
            try
            {
                using (var c = new CodexClient())
                {
                    Snapshot s = c.ReadAsync().GetAwaiter().GetResult();
                    var windows = new List<object>();
                    foreach (var w in s.Windows)
                        windows.Add(new { id = w.Id, usedPercent = w.Used, remainingPercent = w.Remaining, fractional = w.Fractional, durationMinutes = w.Minutes, resetsAt = w.Reset });
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connection-check.json"),
                        new JavaScriptSerializer().Serialize(new { ok = true, fetchedAt = s.Fetched.ToString("o"), windows = windows }));
                }
                return 0;
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connection-check.json"),
                    new JavaScriptSerializer().Serialize(new { ok = false, error = e is InvalidOperationException || e is TimeoutException ? e.Message : e.GetType().Name }));
                return 1;
            }
        }
    }
}


