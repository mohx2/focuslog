using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FocusLog.Core;
using Xunit;

namespace FocusLog.Tests;

internal sealed class FakeClock : IClock
{
    public long Timestamp { get; private set; } = 1_000_000;
    public long Frequency => 1000; // 1 tick = 1 ms
    public DateTime UtcNow { get; private set; }
    public TimeZoneInfo Zone => TimeZoneInfo.Utc;
    public FakeClock(DateTime utc) { UtcNow = DateTime.SpecifyKind(utc, DateTimeKind.Utc); }
    public void Advance(TimeSpan t) { Timestamp += (long)t.TotalMilliseconds; UtcNow += t; }
}

internal sealed class FakeProbe : IForegroundProbe
{
    public AppInfo? Current { get; set; }
    public AppInfo? Read() => Current;
}

internal sealed class MemoryStore : ISessionStore
{
    public sealed class Row { public long Id; public AppInfo App = null!; public string Date = ""; public bool Cont; public long Dur; public bool Open; }
    public List<Row> Rows { get; } = new();
    public long Insert(AppInfo app, long startUtcMs, string localDate, bool continuation, long durMs, bool open)
    {
        var r = new Row { Id = Rows.Count + 1, App = app, Date = localDate, Cont = continuation, Dur = durMs, Open = open };
        Rows.Add(r);
        return r.Id;
    }
    public void Update(long id, long durMs, bool open) { var r = Rows.First(x => x.Id == id); r.Dur = durMs; r.Open = open; }
}

public class EngineTests
{
    private static readonly AppInfo Claude = new("claude.exe", "Claude", "claude.exe", "");
    private static readonly AppInfo Chrome = new("chrome.exe", "Google Chrome", "chrome.exe", "");
    private static readonly AppInfo Insta = new("site:instagram", "Instagram", "chrome.exe", "");

    private static (TrackingEngine e, FakeClock c, FakeProbe p, MemoryStore s) Make(string start = "2026-11-11T10:00:00")
    {
        var c = new FakeClock(DateTime.Parse(start));
        var p = new FakeProbe();
        var s = new MemoryStore();
        return (new TrackingEngine(c, s, p), c, p, s);
    }

    private static void Run(TrackingEngine e, FakeClock c, int seconds)
    {
        for (int i = 0; i < seconds / 5; i++) { c.Advance(TimeSpan.FromSeconds(5)); e.Tick(); }
    }

    [Fact]
    public void Switching_apps_moves_time_to_the_new_app_only()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 600);
        p.Current = Chrome; e.Evaluate();
        Run(e, c, 300);
        p.Current = Claude; e.Evaluate();
        Run(e, c, 120);
        e.Shutdown();

        Assert.Equal(600_000, s.Rows.Where(r => r.App.Key == "claude.exe").First().Dur);
        Assert.Equal(300_000, s.Rows.Single(r => r.App.Key == "chrome.exe").Dur);
        Assert.Equal(720_000, s.Rows.Where(r => r.App.Key == "claude.exe").Sum(r => r.Dur));
        Assert.All(s.Rows, r => Assert.False(r.Open));
    }

    [Fact]
    public void Minimising_the_app_stops_its_timer()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 60);
        p.Current = null; e.Evaluate();     // probe: minimised / nothing trackable
        Run(e, c, 300);
        e.Shutdown();
        Assert.Equal(60_000, s.Rows.Sum(r => r.Dur));
        Assert.Equal(TrackingState.Idle, e.State);
    }

    [Fact]
    public void Lock_and_sleep_stop_tracking_and_unlock_resumes_with_the_foreground_app()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 60);
        e.SetBlock(Block.Locked, true);
        Assert.Equal(TrackingState.Locked, e.State);
        c.Advance(TimeSpan.FromMinutes(30));          // nothing counted while locked
        e.SetBlock(Block.Locked, false);
        Assert.Equal(TrackingState.Tracking, e.State);
        Run(e, c, 60);
        e.Shutdown();
        Assert.Equal(120_000, s.Rows.Sum(r => r.Dur));
    }

    [Fact]
    public void Pause_blocks_all_time()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 30);
        e.SetBlock(Block.User, true);
        c.Advance(TimeSpan.FromMinutes(10));
        e.SetBlock(Block.User, false);
        Run(e, c, 30);
        e.Shutdown();
        Assert.Equal(60_000, s.Rows.Sum(r => r.Dur));
    }

    [Fact]
    public void Only_one_app_owns_time_at_any_moment()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 100);
        p.Current = Insta; e.Evaluate();
        Run(e, c, 100);
        p.Current = Chrome; e.Evaluate();
        Run(e, c, 100);
        e.Shutdown();
        Assert.Equal(300_000, s.Rows.Sum(r => r.Dur)); // wall time elapsed == sum of sessions
    }

    [Fact]
    public void Flicker_shorter_than_minimum_is_discarded()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 60);
        p.Current = Chrome; e.Evaluate();
        c.Advance(TimeSpan.FromMilliseconds(800));
        p.Current = Claude; e.Evaluate();
        Run(e, c, 60);
        e.Shutdown();
        Assert.DoesNotContain(s.Rows, r => r.App.Key == "chrome.exe");
    }

    [Fact]
    public void Session_crossing_midnight_is_split_and_continuation_is_flagged()
    {
        var (e, c, p, s) = Make("2026-11-11T23:55:00");
        p.Current = Claude; e.Evaluate();
        Run(e, c, 600);
        e.Shutdown();
        Assert.Equal(2, s.Rows.Count);
        Assert.Equal("2026-11-11", s.Rows[0].Date);
        Assert.Equal(300_000, s.Rows[0].Dur);
        Assert.False(s.Rows[0].Cont);
        Assert.Equal("2026-11-12", s.Rows[1].Date);
        Assert.Equal(300_000, s.Rows[1].Dur);
        Assert.True(s.Rows[1].Cont);
    }

    [Fact]
    public void A_long_gap_between_heartbeats_is_treated_as_sleep_and_not_counted()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 60);
        c.Advance(TimeSpan.FromHours(2));   // laptop slept without a notification
        e.Tick();
        Run(e, c, 60);
        e.Shutdown();
        long total = s.Rows.Sum(r => r.Dur);
        Assert.InRange(total, 119_000, 126_000);
    }

    [Fact]
    public void Checkpoint_marks_session_open_and_close_clears_it()
    {
        var (e, c, p, s) = Make();
        p.Current = Claude; e.Evaluate();
        Run(e, c, 60);                      // 12 ticks => at least one checkpoint
        Assert.Single(s.Rows);
        Assert.True(s.Rows[0].Open);
        e.Shutdown();
        Assert.False(s.Rows[0].Open);
    }

    [Fact]
    public void Site_classifier_recognises_common_titles()
    {
        var cl = new SiteClassifier(SiteRule.Defaults());
        Assert.Equal("site:youtube", cl.Match("Lofi beats - YouTube - Google Chrome")?.Key);
        Assert.Equal("site:youtube", cl.Match("(3) YouTube - Google Chrome")?.Key);
        Assert.Equal("site:instagram", cl.Match("Instagram - Google Chrome")?.Key);
        Assert.Equal("site:whatsapp-web", cl.Match("WhatsApp - Microsoft Edge")?.Key);
        Assert.Null(cl.Match("Stack Overflow - Where developers learn - Google Chrome"));
        Assert.Null(cl.Match("How to use the YouTube API in C# - Stack Overflow"));
    }
}

public class DatabaseTests
{
    [Fact]
    public void Open_sessions_are_recovered_after_a_crash_and_reports_work()
    {
        string path = Path.Combine(Path.GetTempPath(), "focuslog-test-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            long id;
            var claude = new AppInfo("claude.exe", "Claude", "claude.exe", "");
            using (var db = new Database(path))
            {
                db.SetStudyKeys(new[] { "claude.exe" });
                id = db.Insert(claude, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"), false, 90_000, true);
                db.Update(id, 120_000, true);          // last checkpoint before the "crash"
            }
            using (var db2 = new Database(path))        // restart
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var rows = db2.GetSessions(today, today);
                Assert.Single(rows);
                Assert.Equal(120_000, rows[0].DurMs);
                Assert.True(rows[0].IsStudy);
                var totals = db2.GetDayTotals(today, today);
                Assert.Equal(120_000, totals[0].StudyMs);

                string csv = Path.ChangeExtension(path, ".csv");
                db2.ExportCsv(csv, today, today);
                Assert.Contains("Claude", File.ReadAllText(csv));
                File.Delete(csv);
            }
        }
        finally
        {
            foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
