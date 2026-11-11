using System;
using System.Diagnostics;

namespace FocusLog.Core;

/// <summary>Monotonic + wall clock abstraction (lets the engine be unit-tested).</summary>
public interface IClock
{
    long Timestamp { get; }
    long Frequency { get; }
    DateTime UtcNow { get; }
    TimeZoneInfo Zone { get; }
}

public sealed class SystemClock : IClock
{
    public long Timestamp => Stopwatch.GetTimestamp();
    public long Frequency => Stopwatch.Frequency;
    public DateTime UtcNow => DateTime.UtcNow;
    public TimeZoneInfo Zone => TimeZoneInfo.Local;
}

/// <summary>Returns the app that currently owns the foreground, or null if nothing trackable
/// (desktop, minimized, shell UI, FocusLog itself, secure desktop...).</summary>
public interface IForegroundProbe
{
    AppInfo? Read();
}

public interface ISessionStore
{
    long Insert(AppInfo app, long startUtcMs, string localDate, bool continuation, long durMs, bool open);
    void Update(long id, long durMs, bool open);
}
