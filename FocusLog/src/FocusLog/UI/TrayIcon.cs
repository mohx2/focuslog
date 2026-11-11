using System;
using System.IO;
using System.Windows;
using FocusLog.Core;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace FocusLog.UI;

internal sealed class TrayIcon : IDisposable
{
    private readonly TrackerHost _host;
    private readonly WinForms.NotifyIcon _ni;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _status, _open, _pause, _resume, _exit;

    public TrayIcon(TrackerHost host)
    {
        _host = host;
        _menu = new WinForms.ContextMenuStrip
        {
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false },
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.5f),
            BackColor = Drawing.Color.FromArgb(0x17, 0x18, 0x1C),
            ForeColor = Drawing.Color.FromArgb(0xE7, 0xE8, 0xEB)
        };

        _status = Item("FocusLog", null); _status.Enabled = false;
        _open = Item("Open Dashboard", () => _host.ShowDashboard());
        _pause = Item("Pause Tracking", () => _host.SetPaused(true));
        _resume = Item("Resume Tracking", () => _host.SetPaused(false));
        _exit = Item("Exit", () => _host.Exit());

        _menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _status, new WinForms.ToolStripSeparator(), _open, _pause, _resume,
            new WinForms.ToolStripSeparator(), _exit
        });
        _menu.Opening += (_, _) =>
        {
            bool paused = _host.IsPaused;
            _pause.Enabled = !paused;
            _resume.Enabled = paused;
            _status.Text = StatusLine(_host.Engine);
        };

        _ni = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "FocusLog",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _ni.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) _host.ShowDashboard();
        };
    }

    public void SetStatus(TrackingEngine engine, string studyToday)
    {
        string text = $"FocusLog - {_host.Settings.StudyLabel} {studyToday} today";
        _ni.Text = text.Length > 60 ? text.Substring(0, 60) : text;
    }

    private static string StatusLine(TrackingEngine e) => e.State switch
    {
        TrackingState.Tracking => "Tracking: " + e.CurrentApp?.Name,
        TrackingState.Paused => "Paused",
        TrackingState.Locked => "Locked (not counting)",
        TrackingState.Asleep => "Screen off / asleep",
        _ => "Idle (nothing to track)"
    };

    private WinForms.ToolStripMenuItem Item(string text, Action? onClick)
    {
        var it = new WinForms.ToolStripMenuItem(text)
        {
            ForeColor = Drawing.Color.FromArgb(0xE7, 0xE8, 0xEB),
            BackColor = Drawing.Color.FromArgb(0x17, 0x18, 0x1C)
        };
        if (onClick != null) it.Click += (_, _) => onClick();
        return it;
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info != null)
            {
                using var s = info.Stream;
                return new Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
            }
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _ni.Visible = false;
        _ni.Dispose();
        _menu.Dispose();
    }

    private sealed class DarkColors : WinForms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Bg = Drawing.Color.FromArgb(0x17, 0x18, 0x1C);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(0x2A, 0x2D, 0x34);
        private static readonly Drawing.Color Line = Drawing.Color.FromArgb(0x33, 0x36, 0x3D);

        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Line;
    }
}
