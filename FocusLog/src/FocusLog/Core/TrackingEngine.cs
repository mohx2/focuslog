using System;
using System.Globalization;

namespace FocusLog.Core;

/// <summary>
/// Session state machine. Exactly one app can own time at any moment.
/// States: Tracking(app) | Idle (nothing trackable) | Paused | Locked | Asleep.
/// All durations are measured with a monotonic clock; wall-clock time is only used for labelling.
/// The engine has no UI/OS dependencies: foreground detection and storage are injected.
/// </summary>
public sealed class TrackingEngine
{
    private sealed class OpenSession
    {
        public AppInfo App = null!;
        public long StartTs;
        public DateTime StartUtc;
        public string LocalDate = "";
        public bool Continuation;
        public long Id;
    }

    private readonly IClock _clock;
    private readonly ISessionStore _store;
    private readonly IForegroundProbe _probe;
    private OpenSession? _cur;
    private Block _block;
    private bool _stopped;
    private long _lastTickTs;
    private DateTime _lastTickUtc;
    private int _tickCount;

    public TrackingEngine(IClock clock, ISessionStore store, IForegroundProbe probe)
    {
        _clock = clock;
        _store = store;
        _probe = probe;
    }

    /// <summary>Sessions shorter than this are discarded (Alt+Tab flicker).</summary>
    public int MinSessionMs { get; set; } = 2000;

    /// <summary>If two heartbeats are further apart than this, the machine was asleep/frozen: that gap is not counted.</summary>
    public int GapThresholdMs { get; set; } = 20000;

    public int CheckpointEveryTicks { get; set; } = 6;

    public event Action? Changed;

    public AppInfo? CurrentApp => _cur?.App;
    public bool HasOpenSession => _cur != null;
    public Block Blocks => _block;

    public TimeSpan CurrentElapsed =>
        _cur == null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Math.Max(0, MsBetween(_cur.StartTs, _clock.Timestamp)));

    public TrackingState State
    {
        get
        {
            if ((_block & Block.User) != 0) return TrackingState.Paused;
            if ((_block & Block.Locked) != 0) return TrackingState.Locked;
            if ((_block & (Block.Sleep | Block.DisplayOff)) != 0) return TrackingState.Asleep;
            return _cur != null ? TrackingState.Tracking : TrackingState.Idle;
        }
    }

    public void SetBlock(Block flag, bool on)
    {
        var before = _block;
        _block = on ? _block | flag : _block & ~flag;
        if (before != _block) Evaluate();
    }

    /// <summary>Re-reads the foreground and moves the state machine. atTs lets a debounced event
    /// back-date the switch to when the event really happened.</summary>
    public void Evaluate(long? atTs = null)
    {
        if (_stopped) return;
        long nowTs = _clock.Timestamp;
        long ts = atTs.HasValue ? Math.Min(atTs.Value, nowTs) : nowTs;
        SplitIfCrossedMidnight(nowTs);

        AppInfo? desired = _block == Block.None ? _probe.Read() : null;
        if (_cur != null && desired != null && _cur.App.Key == desired.Key) return;
        if (_cur == null && desired == null) return;

        long at = _cur != null ? Math.Max(ts, _cur.StartTs) : ts;
        if (_cur != null) CloseCurrent(at);
        if (desired != null) OpenNew(desired, at, false, null);
        Changed?.Invoke();
    }

    /// <summary>Heartbeat (only runs while a session is open): gap detection, midnight split,
    /// safety resync and periodic crash-safe checkpoint.</summary>
    public void Tick()
    {
        if (_stopped) return;
        long nowTs = _clock.Timestamp;
        DateTime nowUtc = _clock.UtcNow;

        if (_cur != null)
        {
            long mono = MsBetween(_lastTickTs, nowTs);
            long wall = (long)(nowUtc - _lastTickUtc).TotalMilliseconds;
            if (mono > GapThresholdMs || wall - mono > GapThresholdMs)
            {
                SplitIfCrossedMidnight(_lastTickTs);
                if (_cur != null) CloseCurrent(Math.Max(_lastTickTs, _cur.StartTs));
                _lastTickTs = nowTs;
                _lastTickUtc = nowUtc;
                Evaluate(nowTs);
                Changed?.Invoke();
                return;
            }
        }

        _lastTickTs = nowTs;
        _lastTickUtc = nowUtc;
        Evaluate(nowTs);
        if (++_tickCount % Math.Max(1, CheckpointEveryTicks) == 0) Checkpoint();
    }

    /// <summary>Writes the open session's current duration (flagged open) so a crash loses at most one interval.</summary>
    public void Checkpoint()
    {
        var c = _cur;
        if (c == null) return;
        long dur = Math.Max(0, MsBetween(c.StartTs, _clock.Timestamp));
        if (c.Id != 0) _store.Update(c.Id, dur, true);
        else if (dur >= MinSessionMs)
            c.Id = _store.Insert(c.App, ToUnixMs(c.StartUtc), c.LocalDate, c.Continuation, dur, true);
    }

    /// <summary>Forget the open session without saving (used after "clear all data").</summary>
    public void Discard()
    {
        _cur = null;
        Changed?.Invoke();
    }

    public void Shutdown()
    {
        if (_stopped) return;
        long now = _clock.Timestamp;
        SplitIfCrossedMidnight(now);
        if (_cur != null) CloseCurrent(now);
        _stopped = true;
    }

    // ---- internals -------------------------------------------------------------------------

    private void OpenNew(AppInfo app, long ts, bool continuation, DateTime? startUtc)
    {
        long nowTs = _clock.Timestamp;
        DateTime su = startUtc ?? _clock.UtcNow - TimeSpan.FromMilliseconds(MsBetween(ts, nowTs));
        su = DateTime.SpecifyKind(su, DateTimeKind.Utc);
        _cur = new OpenSession
        {
            App = app,
            StartTs = ts,
            StartUtc = su,
            LocalDate = LocalDate(su),
            Continuation = continuation,
        };
        _lastTickTs = nowTs;
        _lastTickUtc = _clock.UtcNow;
    }

    private void CloseCurrent(long ts)
    {
        var c = _cur;
        if (c == null) return;
        _cur = null;
        long dur = Math.Max(0, MsBetween(c.StartTs, ts));
        if (c.Id != 0) _store.Update(c.Id, dur, false);
        else if (dur >= MinSessionMs)
            _store.Insert(c.App, ToUnixMs(c.StartUtc), c.LocalDate, c.Continuation, dur, false);
    }

    /// <summary>A session that runs across local midnight is cut at midnight so daily totals stay correct.</summary>
    private void SplitIfCrossedMidnight(long ts)
    {
        for (int i = 0; i < 4 && _cur != null; i++)
        {
            var c = _cur;
            DateTime endUtc = c.StartUtc + TimeSpan.FromMilliseconds(MsBetween(c.StartTs, ts));
            if (LocalDate(endUtc) == c.LocalDate) return;

            DateTime boundaryUtc = NextLocalMidnightUtc(c.StartUtc);
            if (boundaryUtc <= c.StartUtc) return;
            long boundaryTs = c.StartTs +
                (long)((boundaryUtc - c.StartUtc).TotalMilliseconds * _clock.Frequency / 1000.0);

            var app = c.App;
            CloseCurrent(boundaryTs);
            OpenNew(app, boundaryTs, true, boundaryUtc);
        }
    }

    private long MsBetween(long a, long b) => (long)((b - a) * 1000.0 / _clock.Frequency);

    private static long ToUnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private string LocalDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _clock.Zone)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private DateTime NextLocalMidnightUtc(DateTime utc)
    {
        var zone = _clock.Zone;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var next = local.Date.AddDays(1);
        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), zone);
        }
        catch (ArgumentException) // midnight does not exist (DST gap)
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next.AddHours(1), DateTimeKind.Unspecified), zone);
        }
    }
}
