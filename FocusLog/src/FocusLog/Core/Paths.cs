using System;
using System.IO;

namespace FocusLog.Core;

public static class Paths
{
    public static string DataDir { get; } = ResolveDataDir();
    public static string DbFile => Path.Combine(DataDir, "focuslog.db");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "error.log");

    // Portable mode: create an empty "portable.txt" next to FocusLog.exe and data is kept in .\Data
    private static string ResolveDataDir()
    {
        string? exeDir = null;
        try { exeDir = Path.GetDirectoryName(Environment.ProcessPath); } catch { }
        if (exeDir != null && File.Exists(Path.Combine(exeDir, "portable.txt")))
            return Path.Combine(exeDir, "Data");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusLog");
    }
}
