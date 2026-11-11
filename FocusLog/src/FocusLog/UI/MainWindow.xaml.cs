using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using FocusLog.Core;
using FocusLog.Native;

namespace FocusLog.UI;

internal partial class MainWindow : Window
{
    private readonly TrackerHost _host;
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1) };
    private IPage? _page;
    private string _key = "overview";
    private int _sec;

    public MainWindow(TrackerHost host)
    {
        _host = host;
        InitializeComponent();
        var wa = SystemParameters.WorkArea;
        Width = Math.Min(1000, wa.Width - 40);
        Height = Math.Min(660, wa.Height - 24);

        PauseBtn.Click += (_, _) => _host.SetPaused(!_host.IsPaused);
        _live.Tick += (_, _) => OnLive();
        _host.StateChanged += OnHostChanged;
        _host.ThemeChanged += OnThemeChanged;
        StateChanged += (_, _) => UpdateLive();
        IsVisibleChanged += (_, _) => UpdateLive();
        Loaded += (_, _) => { Navigate(_key); UpdateSidebar(); UpdateLive(); };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _live.Stop();
            _host.StateChanged -= OnHostChanged;
            _host.ThemeChanged -= OnThemeChanged;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        int on = 1;
        NativeMethods.DwmSetWindowAttribute(h, 20, ref on, 4);        // dark title bar
        int caption = 0x00161312;                                     // #121316 (BGR)
        NativeMethods.DwmSetWindowAttribute(h, 35, ref caption, 4);   // caption colour (Win11)
        int text = 0x00EBE8E7;
        NativeMethods.DwmSetWindowAttribute(h, 36, ref text, 4);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_host.IsExiting && !_host.Settings.CloseToTray)
        {
            e.Cancel = true;
            _host.Exit();
        }
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string key && IsLoaded) Navigate(key);
    }

    private void Navigate(string key)
    {
        _key = key;
        _sec = 0;
        try
        {
            _page = key switch
            {
                "apps" => new AppsPage(_host),
                "analytics" => new AnalyticsPage(_host),
                "history" => new HistoryPage(_host),
                "settings" => new SettingsPage(_host),
                _ => new OverviewPage(_host)
            };
            PageHost.Content = _page.Root;
            PageScroll.ScrollToTop();
            _page.Refresh();
        }
        catch (Exception ex) { Log.Write(ex); }
    }

    private void OnThemeChanged() => Navigate(_key);

    private void OnHostChanged()
    {
        UpdateSidebar();
        if (_page is OverviewPage p) { try { p.Refresh(); } catch (Exception ex) { Log.Write(ex); } }
    }

    private void UpdateSidebar()
    {
        var e = _host.Engine;
        StatusText.Text = e.State switch
        {
            TrackingState.Tracking => "Tracking " + e.CurrentApp?.Name,
            TrackingState.Paused => "Paused",
            TrackingState.Locked => "Locked",
            TrackingState.Asleep => "Screen off",
            _ => "Idle"
        };
        StatusDot.Fill = e.State == TrackingState.Tracking ? UiKit.Br("Accent") : UiKit.Br("TextDim");
        PauseBtn.Content = _host.IsPaused ? "Resume tracking" : "Pause tracking";
    }

    // The 1 s UI timer only runs while the window is visible and not minimised.
    private void UpdateLive()
    {
        bool show = IsVisible && WindowState != WindowState.Minimized;
        if (show && !_live.IsEnabled) _live.Start();
        else if (!show && _live.IsEnabled) _live.Stop();
    }

    private void OnLive()
    {
        try
        {
            _sec++;
            _page?.Tick();
            if (_page != null && _page.AutoRefreshSeconds > 0 && _sec % _page.AutoRefreshSeconds == 0) _page.Refresh();
        }
        catch (Exception ex) { Log.Write(ex); }
    }
}
