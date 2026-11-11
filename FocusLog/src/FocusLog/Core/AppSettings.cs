using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FocusLog.Core;

public sealed class AppSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = false;
    public bool ClassifySitesByTitle { get; set; } = true;
    public int MinSessionSeconds { get; set; } = 2;
    public double WeeklyGoalHours { get; set; } = 15;
    public string StudyLabel { get; set; } = "Claude";
    public string Accent { get; set; } = "#7C8AF5";

    /// <summary>Executable names (lower-case) whose foreground time counts as study time.</summary>
    public List<string> StudyAppKeys { get; set; } = new() { "claude.exe" };

    public List<string> BrowserExes { get; set; } =
        new() { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe" };

    public List<SiteRule> SiteRules { get; set; } = SiteRule.Defaults();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, true);
        }
        catch { }
    }
}
