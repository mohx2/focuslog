using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using FocusLog.Core;

namespace FocusLog.Native;

/// <summary>
/// Identifies the foreground app by executable identity (GetForegroundWindow -> PID -> image path).
/// Window titles are only inspected for browsers, only in memory, only to recognise YouTube/Instagram/etc.
/// </summary>
internal sealed class WindowsForegroundProbe : IForegroundProbe
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame", "TaskSwitcherWnd", "TaskSwitcherOverlayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "ForegroundStaging",
        "LockScreenControllerProxyWindow"
    };

    private static readonly HashSet<string> IgnoredExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "searchhost.exe", "searchapp.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
        "lockapp.exe", "logonui.exe", "textinputhost.exe", "applicationframehost.exe"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whatsapp.root.exe"] = "whatsapp.exe",
    };

    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude.exe"] = "Claude", ["chrome.exe"] = "Google Chrome", ["msedge.exe"] = "Microsoft Edge",
        ["firefox.exe"] = "Firefox", ["brave.exe"] = "Brave", ["opera.exe"] = "Opera", ["vivaldi.exe"] = "Vivaldi",
        ["whatsapp.exe"] = "WhatsApp", ["explorer.exe"] = "File Explorer", ["code.exe"] = "Visual Studio Code",
        ["windowsterminal.exe"] = "Windows Terminal", ["notepad.exe"] = "Notepad", ["winword.exe"] = "Microsoft Word",
        ["excel.exe"] = "Microsoft Excel", ["powerpnt.exe"] = "Microsoft PowerPoint", ["sumatrapdf.exe"] = "SumatraPDF",
        ["acrobat.exe"] = "Adobe Acrobat", ["spotify.exe"] = "Spotify", ["discord.exe"] = "Discord",
        ["telegram.exe"] = "Telegram", ["slack.exe"] = "Slack", ["ms-teams.exe"] = "Microsoft Teams",
        ["teams.exe"] = "Microsoft Teams", ["zoom.exe"] = "Zoom", ["vlc.exe"] = "VLC", ["cmd.exe"] = "Command Prompt",
        ["powershell.exe"] = "PowerShell", ["looppdf.exe"] = "LoopPDF"
    };

    private readonly Dictionary<string, string> _nameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private AppSettings _settings;
    private SiteClassifier _classifier;
    private HashSet<string> _browsers;

    public WindowsForegroundProbe(AppSettings settings)
    {
        _settings = settings;
        _classifier = new SiteClassifier(settings.SiteRules);
        _browsers = new HashSet<string>(settings.BrowserExes, StringComparer.OrdinalIgnoreCase);
    }

    public void Reload(AppSettings settings)
    {
        _settings = settings;
        _classifier = new SiteClassifier(settings.SiteRules);
        _browsers = new HashSet<string>(settings.BrowserExes, StringComparer.OrdinalIgnoreCase);
    }

    public AppInfo? Read()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (NativeMethods.IsIconic(hwnd)) return null;

        if (IgnoredClasses.Contains(ClassName(hwnd))) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == _ownPid) return null;

        string? path = ImagePath(pid);
        string exe;
        if (path == null)
        {
            // Elevated process we cannot open: still count it by process name.
            try { exe = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant() + ".exe"; }
            catch { return null; }
            path = "";
        }
        else
        {
            exe = Path.GetFileName(path).ToLowerInvariant();
        }

        // UWP apps: the visible window belongs to ApplicationFrameHost, the real app is a child window.
        if (exe == "applicationframehost.exe")
        {
            uint inner = FindHostedPid(hwnd, pid);
            string? p2 = inner != 0 ? ImagePath(inner) : null;
            if (p2 == null) return null;
            path = p2;
            exe = Path.GetFileName(p2).ToLowerInvariant();
        }

        if (IgnoredExes.Contains(exe)) return null;

        string key = Aliases.TryGetValue(exe, out var alias) ? alias : exe;

        if (_settings.ClassifySitesByTitle && _browsers.Contains(exe))
        {
            var rule = _classifier.Match(WindowTitle(hwnd));
            if (rule != null) return new AppInfo(rule.Key, rule.Name, exe, path);
        }

        return new AppInfo(key, NameFor(key, path), exe, path);
    }

    private string NameFor(string key, string path)
    {
        if (KnownNames.TryGetValue(key, out var known)) return known;
        if (_nameCache.TryGetValue(key, out var cached)) return cached;
        string name = "";
        try
        {
            if (path.Length > 0 && File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                name = (vi.FileDescription ?? "").Trim();
                if (name.Length == 0) name = (vi.ProductName ?? "").Trim();
            }
        }
        catch { }
        if (name.Length == 0)
        {
            string stem = Path.GetFileNameWithoutExtension(key);
            name = stem.Length > 0 ? char.ToUpperInvariant(stem[0]) + stem.Substring(1) : key;
        }
        _nameCache[key] = name;
        return name;
    }

    private static string? ImagePath(uint pid)
    {
        IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return NativeMethods.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { NativeMethods.CloseHandle(h); }
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(128);
        return NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        return NativeMethods.GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static uint FindHostedPid(IntPtr hwnd, uint hostPid)
    {
        uint found = 0;
        NativeMethods.EnumChildProc cb = (h, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(h, out uint p);
            if (p != 0 && p != hostPid) { found = p; return false; }
            return true;
        };
        NativeMethods.EnumChildWindows(hwnd, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        return found;
    }
}
