using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FocusLog.Native;

namespace FocusLog;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _wait;

    internal static TrackerHost Host { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Local\FocusLog.Singleton", out bool created);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FocusLog.Show");
        if (!created)
        {
            // Already running: ask the running instance to show its dashboard, then quit.
            _showEvent.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write(args.Exception);
            args.Handled = true; // never let a UI glitch kill the tracker
        };

        try
        {
            Host = new TrackerHost();
            Host.Start(background: e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Write(ex);
            MessageBox.Show("FocusLog could not start:\n\n" + ex.Message, "FocusLog",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(() => Host.ShowDashboard())),
            null, Timeout.Infinite, false);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Host?.Exit();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wait?.Unregister(null);
        base.OnExit(e);
    }
}
