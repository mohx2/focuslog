using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusLog.Core;

namespace FocusLog.UI;

internal sealed class HistoryPage : IPage
{
    private readonly TrackerHost _h;
    private readonly StackPanel _root = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _dayLabel = UiKit.Txt("", 15, "Text", FontWeights.SemiBold);
    private readonly TextBlock _summary = UiKit.Caption("");
    private readonly TextBox _jump = new() { Width = 108, FontSize = 12.5 };
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Now);

    public FrameworkElement Root => _root;
    public int AutoRefreshSeconds => 0;

    public HistoryPage(TrackerHost host)
    {
        _h = host;
        _root.Children.Add(UiKit.Title("History"));

        var nav = new DockPanel { Margin = new Thickness(0, 16, 0, 14) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        _jump.Text = ReportMath.Key(_day);
        var go = UiKit.Btn("Go", Jump);
        go.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(_jump);
        right.Children.Add(go);
        nav.Children.Add(right);

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(UiKit.Btn("\u2039", () => Move(-1)));
        _dayLabel.Margin = new Thickness(14, 0, 14, 0);
        _dayLabel.MinWidth = 170;
        left.Children.Add(_dayLabel);
        left.Children.Add(UiKit.Btn("\u203A", () => Move(1)));
        var today = UiKit.Btn("Today", () => { _day = DateOnly.FromDateTime(DateTime.Now); Refresh(); });
        today.Margin = new Thickness(10, 0, 0, 0);
        left.Children.Add(today);
        nav.Children.Add(left);
        _root.Children.Add(nav);

        _summary.Margin = new Thickness(0, 0, 0, 10);
        _root.Children.Add(_summary);
        _root.Children.Add(UiKit.Panel(_list, 6));
    }

    private void Move(int delta)
    {
        var next = _day.AddDays(delta);
        if (next > DateOnly.FromDateTime(DateTime.Now)) return;
        _day = next;
        Refresh();
    }

    private void Jump()
    {
        if (DateOnly.TryParseExact(_jump.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d <= DateOnly.FromDateTime(DateTime.Now))
        {
            _day = d;
            _jump.BorderBrush = UiKit.Br("BorderStrong");
            Refresh();
        }
        else _jump.BorderBrush = UiKit.Br("Negative");
    }

    public void Refresh()
    {
        _h.Engine.Checkpoint();
        var rows = _h.Db.GetSessions(_day, _day);
        _dayLabel.Text = _day == DateOnly.FromDateTime(DateTime.Now) ? "Today" : _day.ToString("ddd d MMM yyyy");
        _jump.Text = ReportMath.Key(_day);
        long total = rows.Sum(r => r.DurMs);
        _summary.Text = $"{rows.Count(r => !r.Continuation)} sessions  |  {ReportMath.Dur(total)} tracked  |  {_h.Settings.StudyLabel} {ReportMath.Dur(rows.Where(r => r.IsStudy).Sum(r => r.DurMs))}";

        _list.Children.Clear();
        _list.Children.Add(Row(null, true));
        if (rows.Count == 0)
        {
            var e = UiKit.Caption("No sessions on this day.");
            e.Margin = new Thickness(14, 16, 14, 16);
            _list.Children.Add(e);
            return;
        }
        foreach (var r in rows.OrderByDescending(x => x.Start)) _list.Children.Add(Row(r, false));
    }

    private static Grid Row(SessionRecord? r, bool header)
    {
        var g = new Grid { Margin = new Thickness(10, header ? 8 : 6, 10, header ? 8 : 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        void Put(FrameworkElement el, int col, HorizontalAlignment ha = HorizontalAlignment.Left)
        {
            el.HorizontalAlignment = ha;
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }

        if (header)
        {
            Put(UiKit.Caption("Application"), 1);
            Put(UiKit.Caption("Start"), 2, HorizontalAlignment.Right);
            Put(UiKit.Caption("End"), 3, HorizontalAlignment.Right);
            Put(UiKit.Caption("Duration"), 4, HorizontalAlignment.Right);
            return g;
        }
        var s = r!;
        Put(UiKit.AppIcon(s.AppKey, s.AppName, s.Path, 20), 0);
        Put(UiKit.Txt(s.AppName, 13, "Text", s.IsStudy ? FontWeights.SemiBold : (FontWeight?)null), 1, HorizontalAlignment.Stretch);
        Put(UiKit.Txt(s.Start.ToString("HH:mm:ss"), 13, "TextMuted", null, true), 2, HorizontalAlignment.Right);
        Put(UiKit.Txt(s.End.ToString("HH:mm:ss"), 13, "TextMuted", null, true), 3, HorizontalAlignment.Right);
        Put(UiKit.Txt(ReportMath.Dur(s.DurMs), 13, "Text", null, true), 4, HorizontalAlignment.Right);
        return g;
    }

    public void Tick() { }
}
