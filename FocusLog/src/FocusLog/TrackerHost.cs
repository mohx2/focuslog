using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using FocusLog.Core;
using FocusLog.Native;
using FocusLog.UI;

namespace FocusLog;

internal enum RangeKind { Today, Yesterday, Last7, Last30, ThisMonth, Custom }

/// <summary>Composition root. The tracking engine lives here and never depends on the dashboard being open.</summary>
internal sealed class TrackerHost
{
    private WindowsForegroundProbe _probe = null!;
    private ForegroundHook _hook = null!;
    private PowerWatcher _power = null!;
    private TrayIcon _tray = null!;
    private DispatcherTimer _tick = null!;
    private DispatcherTimer _debounce = null!;
    private MainWindow? _window;
    private long _pendingTs;
    private int _tickCount;

    public AppSettings Settings { get; private set; } = new();
    public Database Db { get; private set; } = null!;
    public TrackingEngine Engine { get; private set; } = null!;
    public bool IsExiting { get; private set; }

    // Shared report-range selection for the Applications / Analytics pages
    public RangeKind Range { get; set; } = RangeKind.Last7;
    public DateOnly CustomFrom { get; set; } = DateOnly.FromDateTime(DateTime.Now).AddDays(-6);
    public DateOnly CustomTo { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    public event Action? StateChanged;
    public event Action? ThemeChanged;

    public void Start(bool background)
    {
        System.IO.Directory.CreateDirectory(Paths.DataDir);
        Settings = AppSettings.Load(Paths.SettingsFile);
        Theme.ApplyAccent(Settings.Accent);

        Db = new Database(Paths.DbFile);
        Db.SetStudyKeys(Settings.StudyAppKeys);

        _probe = new WindowsForegroundProbe(Settings);
        Engine = new TrackingEngine(new SystemClock(), Db, _probe)
        {
            MinSessionMs = Math.Max(0, Settings.MinSessionSeconds) * 1000
        };
        Engine.Changed += OnEngineChanged;

        // 5 s heartbeat: runs ONLY while a session is open (zero wake-ups when idle/locked/asleep/paused).
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _tick.Tick += (_, _) =>
        {
            Safe(() => Engine.Tick());
            if (++_tickCount % 6 == 0) Safe(UpdateTrayText);
        };

        // Foreground switches are coalesced for 600 ms so Alt+Tab flicker is not recorded.
        _debounce = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(600) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Safe(() => Engine.Evaluate(_pendingTs));
        };

        _hook = new ForegroundHook();
        _hook.Changed += () =>
        {
            if (_debounce.IsEnabled) return;
            _pendingTs = Stopwatch.GetTimestamp();
            _debounce.Start();
        };

        _power = new PowerWatcher();
        _power.BlockChanged += (flag, on) => Safe(() => Engine.SetBlock(flag, on));
        _power.TimeChanged += () => TimeZoneInfo.ClearCachedData();

        _tray = new TrayIcon(this);

        if (Settings.LaunchAtStartup) Safe(() => StartupRegistration.Set(true)); // keeps the path current

        Safe(() => Engine.Evaluate());
        if (!background) ShowDashboard();
    }

    // ---- actions used by UI ----------------------------------------------------------------

    public void ShowDashboard()
    {
        if (IsExiting) return;
        if (_window == null)
        {
            _window = new MainWindow(this);
            _window.Closed += (_, _) =>
            {
                _window = null;
                // Dashboard is destroyed (not hidden) so no UI runs in the background.
                Application.Current.Dispatcher.BeginInvoke(new Action(MemoryTrim.Run), DispatcherPriority.ApplicationIdle);
            };
            _window.Show();
        }
        else
        {
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
    }

    public bool IsPaused => (Engine.Blocks & Block.User) != 0;

    public void SetPaused(bool paused) => Safe(() => Engine.SetBlock(Block.User, paused));

    public void SaveSettings() => Settings.Save(Paths.SettingsFile);

    public void ApplyProbeSettings() => _probe.Reload(Settings);

    public void ApplyStudyKeys()
    {
        Db.SetStudyKeys(Settings.StudyAppKeys);
        SaveSettings();
        RaiseStateChanged();
    }

    public void ApplyAccent(string hex)
    {
        Settings.Accent = hex;
        Theme.ApplyAccent(hex);
        SaveSettings();
        ThemeChanged?.Invoke();
    }

    public void ClearAllData()
    {
        Db.ClearAll();
        Db.SetStudyKeys(Settings.StudyAppKeys);
        Engine.Discard();
        Engine.Evaluate();
        RaiseStateChanged();
    }

    public void RaiseStateChanged() => StateChanged?.Invoke();

    public string StudyTodayText()
    {
        Engine.Checkpoint();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var t = Db.GetDayTotals(today, today).FirstOrDefault();
        return ReportMath.Dur(t?.StudyMs ?? 0);
    }

    public void Exit()
    {
        if (IsExiting) return;
        IsExiting = true;
        Safe(() => { _tick.Stop(); _debounce.Stop(); });
        Safe(() => Engine.Shutdown());          // closes the open session and flushes it
        Safe(() => _hook.Dispose());
        Safe(() => _power.Dispose());
        Safe(() => _tray.Dispose());            // removes the tray icon (no ghost icon)
        Safe(() => _window?.Close());
        Safe(() => Db.Dispose());
        Application.Current.Shutdown();
    }

    // ---- internals -------------------------------------------------------------------------

    private void OnEngineChanged()
    {
        if (Engine.HasOpenSession) { if (!_tick.IsEnabled) _tick.Start(); }
        else _tick.Stop();
        Safe(UpdateTrayText);
        RaiseStateChanged();
    }

    private void UpdateTrayText() => _tray.SetStatus(Engine, StudyTodayText());

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Write(ex); }
    }
}
