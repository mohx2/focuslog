using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FocusLog.Core;

public static class ReportMath
{
    public static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly WeekStart(DateOnly d)
    {
        int diff = ((int)d.DayOfWeek + 6) % 7; // Monday = 0
        return d.AddDays(-diff);
    }

    public static string Dur(long ms)
    {
        long s = Math.Max(0, ms) / 1000;
        if (s >= 3600)
        {
            long h = s / 3600, m = (s % 3600) / 60;
            return m == 0 ? $"{h}h" : $"{h}h {m}m";
        }
        if (s >= 60) return $"{s / 60}m";
        return s == 0 ? "0m" : $"{s}s";
    }

    public static string Clock(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>Longest continuous session; rows split at midnight are stitched back together.</summary>
    public static long LongestChain(IEnumerable<SessionRecord> rows)
    {
        var chain = new Dictionary<string, long>();
        long best = 0;
        foreach (var r in rows.OrderBy(x => x.Start))
        {
            long cur = r.DurMs;
            if (r.Continuation && chain.TryGetValue(r.AppKey, out var prev)) cur += prev;
            chain[r.AppKey] = cur;
            if (cur > best) best = cur;
        }
        return best;
    }

    public static List<AppUsage> Aggregate(IEnumerable<SessionRecord> rows)
    {
        var list = rows.OrderBy(r => r.Start).ToList();
        var map = new Dictionary<string, AppUsage>();
        var chain = new Dictionary<string, long>();
        foreach (var r in list)
        {
            if (!map.TryGetValue(r.AppKey, out var u))
            {
                u = new AppUsage { Key = r.AppKey, Name = r.AppName, Exe = r.Exe, Path = r.Path, IsStudy = r.IsStudy, First = r.Start, Last = r.End };
                map[r.AppKey] = u;
            }
            u.TotalMs += r.DurMs;
            if (!r.Continuation) u.Sessions++;
            if (r.Start < u.First) u.First = r.Start;
            if (r.End > u.Last) u.Last = r.End;
            long cur = r.DurMs;
            if (r.Continuation && chain.TryGetValue(r.AppKey, out var prev)) cur += prev;
            chain[r.AppKey] = cur;
            if (cur > u.LongestMs) u.LongestMs = cur;
        }
        return map.Values.OrderByDescending(u => u.TotalMs).ToList();
    }
}
