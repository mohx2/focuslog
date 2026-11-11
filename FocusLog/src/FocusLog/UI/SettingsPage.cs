using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusLog.Core;
using FocusLog.Native;
using Microsoft.Win32;

namespace FocusLog.UI;

internal sealed class SettingsPage : IPage
{
    private readonly TrackerHost _h;
    private readonly StackPanel _root = new();
    private readonly TextBlock _status = UiKit.Caption("");
    private readonly StackPanel _apps = new();
    private readonly TextBlock _goal = UiKit.Txt("", 13, "Text", null, true);
    private readonly TextBox _addExe = new() { Width = 170, FontSize = 12.5 };

    public FrameworkElement Root => _root;
    public int AutoRefreshSeconds => 0;

    public SettingsPage(TrackerHost host)
    {
        _h = host;
        var s = _h.Settings;
        _root.Children.Add(UiKit.Title("Settings"));
        _root.Children.Add(Spacer(16));

        // General
        var general = new StackPanel();
        general.Children.Add(UiKit.SectionLabel("General"));
        general.Children.Add(UiKit.SettingRow("Closing the window keeps tracking in the tray",
            "When off, closing the dashboard exits FocusLog. The tray menu always has Exit.",
            UiKit.Toggle(s.CloseToTray, v => { s.CloseToTray = v; _h.SaveSettings(); })));
        general.Children.Add(UiKit.SettingRow("Launch at Windows startup",
            "Starts minimised in the tray. Uses the per-user Run key (no admin rights).",
            UiKit.Toggle(StartupRegistration.IsEnabled(), v =>
            {
                try { StartupRegistration.Set(v); s.LaunchAtStartup = v; _h.SaveSettings(); }
                catch (Exception ex) { Log.Write(ex); _status.Text = "Could not change startup setting."; }
            })));
        general.Children.Add(UiKit.SettingRow("Recognise YouTube / Instagram / WhatsApp Web in browsers",
            "Matches the browser window title in memory only. Titles are never stored.",
            UiKit.Toggle(s.ClassifySitesByTitle, v => { s.ClassifySitesByTitle = v; _h.SaveSettings(); _h.ApplyProbeSettings(); })));
        general.Children.Add(UiKit.SettingRow("Weekly study goal", "Used for the progress bar on the overview.", GoalControl()));
        _root.Children.Add(UiKit.Panel(general, 18));
        _root.Children.Add(Spacer(14));

        // Study apps
        var study = new StackPanel();
        study.Children.Add(UiKit.SectionLabel("Study applications"));
        var info = UiKit.Txt("Foreground time in these executables counts as study time. Claude Desktop is matched by its executable name (claude.exe), so helper processes and window-title changes do not matter. If a future update renames it, add the new name here.", 12, "TextMuted");
        info.TextWrapping = TextWrapping.Wrap;
        info.TextTrimming = TextTrimming.None;
        study.Children.Add(info);
        _apps.Margin = new Thickness(0, 10, 0, 0);
        study.Children.Add(_apps);
        var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        add.Children.Add(_addExe);
        var addBtn = UiKit.Btn("Add executable", AddExe);
        addBtn.Margin = new Thickness(8, 0, 0, 0);
        add.Children.Add(addBtn);
        study.Children.Add(add);
        _root.Children.Add(UiKit.Panel(study, 18));
        _root.Children.Add(Spacer(14));

        // Appearance
        var look = new StackPanel();
        look.Children.Add(UiKit.SectionLabel("Appearance"));
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, hex) in Theme.Accents)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            bool sel = string.Equals(s.Accent, hex, StringComparison.OrdinalIgnoreCase);
            var b = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(c), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name,
                BorderThickness = new Thickness(2),
                BorderBrush = sel ? UiKit.Br("Text") : Brushes.Transparent
            };
            string h2 = hex;
            b.MouseLeftButtonUp += (_, _) => _h.ApplyAccent(h2);
            swatches.Children.Add(b);
        }
        look.Children.Add(UiKit.SettingRow("Accent colour", "FocusLog is dark-only; the accent is the single colour used for study time.", swatches));
        _root.Children.Add(UiKit.Panel(look, 18));
        _root.Children.Add(Spacer(14));

        // Data
        var data = new StackPanel();
        data.Children.Add(UiKit.SectionLabel("Data and privacy"));
        var priv = UiKit.Txt("Everything stays on this PC: " + Paths.DataDir + ". No accounts, no network, no telemetry. FocusLog never records keystrokes, screenshots, clipboard, window titles, browsing history or files - only which executable was in front, and for how long.", 12, "TextMuted");
        priv.TextWrapping = TextWrapping.Wrap;
        priv.TextTrimming = TextTrimming.None;
        data.Children.Add(priv);
        var btns = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        btns.Children.Add(WithGap(UiKit.Btn("Export all to CSV", () => Export(false))));
        btns.Children.Add(WithGap(UiKit.Btn("Export Analytics range", () => Export(true))));
        btns.Children.Add(WithGap(UiKit.Btn("Back up database", Backup)));
        btns.Children.Add(WithGap(UiKit.Btn("Open data folder", () => Open(Paths.DataDir))));
        btns.Children.Add(WithGap(UiKit.Btn("Clear all tracking data", Clear, "DangerButton")));
        data.Children.Add(btns);
        _status.Margin = new Thickness(0, 8, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        data.Children.Add(_status);
        _root.Children.Add(UiKit.Panel(data, 18));
        _root.Children.Add(Spacer(14));

        // Shortcuts + about
        var sc = new StackPanel();
        sc.Children.Add(UiKit.SectionLabel("Shortcuts"));
        var sb = new WrapPanel();
        sb.Children.Add(WithGap(UiKit.Btn("Add Start menu shortcut", () => MakeShortcut(Shortcuts.StartMenuPath, "Start menu"))));
        sb.Children.Add(WithGap(UiKit.Btn("Add desktop shortcut", () => MakeShortcut(Shortcuts.DesktopPath, "Desktop"))));
        sc.Children.Add(sb);
        var tip = UiKit.Caption("To pin to Start: open the Start menu, find FocusLog, right-click, Pin to Start.");
        tip.Margin = new Thickness(0, 8, 0, 0);
        sc.Children.Add(tip);
        var about = UiKit.Caption("FocusLog 1.0.0");
        about.Margin = new Thickness(0, 12, 0, 0);
        sc.Children.Add(about);
        _root.Children.Add(UiKit.Panel(sc, 18));
    }

    private static FrameworkElement WithGap(Button b) { b.Margin = new Thickness(0, 0, 8, 8); return b; }
    private static FrameworkElement Spacer(double h) => new Border { Height = h };

    private FrameworkElement GoalControl()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var minus = UiKit.Btn("-", () => Goal(-1));
        var plus = UiKit.Btn("+", () => Goal(1));
        _goal.Margin = new Thickness(12, 0, 12, 0);
        _goal.Text = $"{_h.Settings.WeeklyGoalHours:0.#} h";
        p.Children.Add(minus);
        p.Children.Add(_goal);
        p.Children.Add(plus);
        return p;
    }

    private void Goal(double delta)
    {
        _h.Settings.WeeklyGoalHours = Math.Clamp(_h.Settings.WeeklyGoalHours + delta, 1, 100);
        _goal.Text = $"{_h.Settings.WeeklyGoalHours:0.#} h";
        _h.SaveSettings();
    }

    public void Refresh()
    {
        _apps.Children.Clear();
        var known = _h.Db.GetKnownApps();
        // make sure configured study executables are listed even if never seen yet
        foreach (var k in _h.Settings.StudyAppKeys)
            if (!known.Any(a => string.Equals(a.Key, k, StringComparison.OrdinalIgnoreCase)))
                known.Add(new KnownApp(k, k, k, "", true));
        known = known.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var a in known)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UiKit.AppIcon(a.Key, a.Name, a.Path, 20);
            row.Children.Add(icon);
            var t = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            t.Children.Add(UiKit.Txt(a.Name, 13));
            t.Children.Add(UiKit.Txt(a.Path.Length > 0 ? a.Path : a.Exe, 11, "TextDim"));
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            string key = a.Key;
            var tg = UiKit.Toggle(a.Study, on =>
            {
                var list = _h.Settings.StudyAppKeys;
                list.RemoveAll(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
                if (on) list.Add(key.ToLowerInvariant());
                _h.ApplyStudyKeys();
            });
            Grid.SetColumn(tg, 2);
            row.Children.Add(tg);
            _apps.Children.Add(row);
        }
        if (known.Count == 0) _apps.Children.Add(UiKit.Caption("Applications appear here after they have been in the foreground."));
    }

    private void AddExe()
    {
        string v = _addExe.Text.Trim().ToLowerInvariant();
        if (v.Length == 0) return;
        if (!v.EndsWith(".exe", StringComparison.Ordinal)) v += ".exe";
        if (!_h.Settings.StudyAppKeys.Contains(v, StringComparer.OrdinalIgnoreCase)) _h.Settings.StudyAppKeys.Add(v);
        _addExe.Text = "";
        _h.ApplyStudyKeys();
        Refresh();
    }

    private void Export(bool range)
    {
        try
        {
            var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"focuslog-{DateTime.Now:yyyyMMdd}.csv" };
            if (dlg.ShowDialog() != true) return;
            _h.Engine.Checkpoint();
            var (f, t) = range ? RangeSelector.Resolve(_h) : (new DateOnly(2000, 1, 1), DateOnly.FromDateTime(DateTime.Now));
            _h.Db.ExportCsv(dlg.FileName, f, t);
            _status.Text = "Exported to " + dlg.FileName;
        }
        catch (Exception ex) { Log.Write(ex); _status.Text = "Export failed: " + ex.Message; }
    }

    private void Backup()
    {
        try
        {
            var dlg = new SaveFileDialog { Filter = "SQLite database (*.db)|*.db", FileName = $"focuslog-backup-{DateTime.Now:yyyyMMdd}.db" };
            if (dlg.ShowDialog() != true) return;
            _h.Engine.Checkpoint();
            _h.Db.Backup(dlg.FileName);
            _status.Text = "Backup saved to " + dlg.FileName;
        }
        catch (Exception ex) { Log.Write(ex); _status.Text = "Backup failed: " + ex.Message; }
    }

    private void Clear()
    {
        var r = MessageBox.Show("Delete ALL tracked sessions and history? This cannot be undone.\n\nTip: use \"Back up database\" first.",
            "FocusLog", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        try { _h.ClearAllData(); _status.Text = "All tracking data deleted."; Refresh(); }
        catch (Exception ex) { Log.Write(ex); _status.Text = "Could not clear data: " + ex.Message; }
    }

    private void MakeShortcut(string path, string where)
    {
        try { Shortcuts.Create(path); _status.Text = where + " shortcut created."; }
        catch (Exception ex) { Log.Write(ex); _status.Text = "Could not create shortcut: " + ex.Message; }
    }

    private static void Open(string folder)
    {
        try { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write(ex); }
    }

    public void Tick() { }
}
