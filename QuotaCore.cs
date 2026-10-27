using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace QuotaClock
{
    public sealed class QuotaWindow
    {
        public string Id, Name;
        public decimal Used;
        public int Minutes;
        public long? Reset;
        public bool Fractional;
        public decimal Remaining { get { return Math.Max(0, Math.Min(100, 100 - Used)); } }
        public string Label
        {
            get
            {
                string period = Minutes == 10080 ? "周额度" : Minutes == 300 ? "5 小时额度" :
                    Minutes % 1440 == 0 && Minutes > 0 ? (Minutes / 1440) + " 天额度" :
                    Minutes % 60 == 0 && Minutes > 0 ? (Minutes / 60) + " 小时额度" : Minutes > 0 ? Minutes + " 分钟额度" : "额度窗口";
                return Name + " · " + period;
            }
        }
    }

    public sealed class Snapshot
    {
        public List<QuotaWindow> Windows = new List<QuotaWindow>();
        public DateTimeOffset Fetched;
    }

    public static class QuotaMath
    {
        // Clockwise consumption dial: 0-100% consumed maps to a full 12-hour rotation.
        // Long hand maps the units decade to 60 minute marks; fine hand maps tenths to 60 second marks.
        // These are quota hands, not a civil-time conversion.
        public static decimal Display(decimal remaining) { return Math.Round(Math.Max(0, Math.Min(100, remaining)), 1, MidpointRounding.AwayFromZero); }
        public static double[] Angles(decimal remaining)
        {
            decimal p = 100 - Display(remaining);
            int tenths = (int)(p * 10) % 10;
            return new[] { (double)p * 3.6, (double)(p % 10) * 36, tenths * 36.0 };
        }
        public static string Percent(decimal remaining, bool fractional)
        {
            return Display(remaining).ToString(fractional ? "0.0" : "0", CultureInfo.InvariantCulture) + "%";
        }
        public static bool Expired(QuotaWindow w, DateTimeOffset now)
        { return w.Reset.HasValue && w.Reset.Value <= now.ToUnixTimeSeconds(); }
        public static string ResetText(QuotaWindow w, DateTimeOffset now)
        {
            if (!w.Reset.HasValue) return "服务端未提供重置时间";
            long left = w.Reset.Value - now.ToUnixTimeSeconds();
            if (left <= 0) return "窗口已到期 · 等待重新读取";
            var t = TimeSpan.FromSeconds(left);
            string duration = t.TotalDays >= 1 ? ((int)t.TotalDays) + "天 " + t.Hours + "小时" :
                t.TotalHours >= 1 ? ((int)t.TotalHours) + "小时 " + t.Minutes + "分" : Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)) + "分钟";
            return duration + "后重置";
        }
    }

    public static class QuotaParser
    {
        static Dictionary<string, object> Dict(object o) { return o as Dictionary<string, object>; }
        static object Get(Dictionary<string, object> d, string key) { object o; return d != null && d.TryGetValue(key, out o) ? o : null; }
        static string Text(object o) { return o as string; }
        static bool Number(object o, out decimal value)
        {
            value = 0;
            return o != null && !(o is string) && !(o is bool) &&
                decimal.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
        public static Snapshot Parse(string json, DateTimeOffset now)
        {
            var root = Dict(new JavaScriptSerializer().DeserializeObject(json));
            var result = Dict(Get(root, "result")) ?? root;
            var snap = new Snapshot { Fetched = now };
            var buckets = Dict(Get(result, "rateLimitsByLimitId"));
            if (buckets != null && buckets.Count > 0)
            {
                foreach (var kv in buckets.OrderBy(k => k.Key == "codex" ? "" : k.Key, StringComparer.Ordinal))
                    AddBucket(snap, kv.Key, Dict(kv.Value));
            }
            else AddBucket(snap, "codex", Dict(Get(result, "rateLimits")));
            return snap;
        }
        static void AddBucket(Snapshot snap, string id, Dictionary<string, object> b)
        {
            if (b == null) return;
            string label = Text(Get(b, "limitName"));
            if (String.IsNullOrWhiteSpace(label)) label = id == "codex" ? "Codex" : id;
            foreach (string kind in new[] { "primary", "secondary" })
            {
                var w = Dict(Get(b, kind));
                decimal used;
                if (w == null || !Number(Get(w, "usedPercent"), out used) || used < 0 || used > 100) continue;
                decimal mins, reset;
                int minutes = Number(Get(w, "windowDurationMins"), out mins) && mins > 0 && mins <= Int32.MaxValue ? (int)mins : 0;
                long? resets = Number(Get(w, "resetsAt"), out reset) && reset >= 0 && reset <= 253402300799L ? (long?)reset : null;
                snap.Windows.Add(new QuotaWindow { Id = id + ":" + kind, Name = label, Used = used, Minutes = minutes, Reset = resets,
                    Fractional = used != decimal.Truncate(used) });
            }
        }
    }

}
