using System;

namespace FocusLog.Core;

public sealed record AppInfo(string Key, string Name, string Exe, string Path);

public sealed class SessionRecord
{
    public long Id { get; set; }
    public string AppKey { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public long DurMs { get; set; }
    public bool Continuation { get; set; }
    public bool IsStudy { get; set; }
}

public sealed class AppUsage
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsStudy { get; set; }
    public long TotalMs { get; set; }
    public int Sessions { get; set; }
    public DateTime First { get; set; }
    public DateTime Last { get; set; }
    public long LongestMs { get; set; }
}

public sealed class DayTotal
{
    public DateOnly Date { get; set; }
    public long TotalMs { get; set; }
    public long StudyMs { get; set; }
}

public sealed record KnownApp(string Key, string Name, string Exe, string Path, bool Study);

public enum TrackingState { Tracking, Idle, Paused, Locked, Asleep }

[Flags]
public enum Block
{
    None = 0,
    User = 1,
    Locked = 2,
    Sleep = 4,
    DisplayOff = 8
}
