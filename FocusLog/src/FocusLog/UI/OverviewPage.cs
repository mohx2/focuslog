using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusLog.Core;

namespace FocusLog.UI;

internal sealed class OverviewPage : IPage
{
    private readonly TrackerHost _h;
    private readonly StackPanel _root = new();
    private readonly TextBlock _statusText = UiKit.Caption("");
    private readonly TextBlock _heroValue = UiKit.Txt("0m", 44, "Text", FontWeights.SemiLight, true);
    private readonly TextBlock _heroDelta = UiKit.Txt("", 12.5, "TextMuted");
    private readonly TextBlock _weekText = UiKit.Txt("", 12.5, "TextMuted");
    private readonly ProgressLine _weekBar = new();
    private readonly ContentControl _nowIcon = new();
    private readonly TextBlock _nowName = UiKit.Txt("-", 15, "Text", FontWeights.SemiBold);
    private readonly TextBlock _nowTimer = UiKit.Txt("-", 26, "Text", FontWeights.SemiLight, true);
    private readonly TextBlock _total = UiKit.Txt("-", 13, "Text", null, true);
    private readonly TextBlock _other = UiKit.Txt("-", 13, "Text", null, true);
    private readonly TextBlock _sessAll = UiKit.Txt("-", 13, "Text", null, true);
    private readonly TextBlock _curSession, _sessToday, _longest, _avg, _week, _month, _yesterday, _goalPct;
    private readonly BarChartControl _chart = new(170);
    private readonly StackPanel _top = new();
    private readonly TextBlock _heroCaption;

    private long _baseStudy, _baseTotal, _baseTs;
    private bool _curIsStudy;
    private string _heroShown = "";

    public FrameworkElement Root => _root;
    public int AutoRefreshSeconds => 15;

    public OverviewPage(TrackerHost host)
    {
        _h = host;
        string label = _h.Settings.StudyLabel;

        // header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var titles = new StackPanel();
        titles.Children.Add(UiKit.Title("Overview"));
        var date = UiKit.Caption(DateTime.Now.ToString("dddd, d MMMM yyyy"));
        date.Margin = new Thickness(0, 3, 0, 0);
        titles.Children.Add(date);
        DockPanel.SetDock(_statusText, Dock.Right);
        header.Children.Add(_statusText);
        header.Children.Add(titles);
        _root.Children.Add(header);

        // hero row
        var row1 = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var hero = new StackPanel();
        _heroCaption = UiKit.Caption(label + " study time today");
        hero.Children.Add(_heroCaption);
        _heroValue.Margin = new Thickness(0, 6, 0, 2);
        _heroValue.Foreground = UiKit.Br("Text");
        hero.Children.Add(_heroValue);
        hero.Children.Add(_heroDelta);
        hero.Children.Add(UiKit.Divider(16));
        var wk = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var wkLabel = UiKit.Txt("This week", 12.5, "TextMuted");
        DockPanel.SetDock(_weekText, Dock.Right);
        wk.Children.Add(_weekText);
        wk.Children.Add(wkLabel);
        hero.Children.Add(wk);
        hero.Children.Add(_weekBar);
        var heroPanel = UiKit.Panel(hero, 20);
        Grid.SetColumn(heroPanel, 0);
        row1.Children.Add(heroPanel);

        var now = new StackPanel();
        now.Children.Add(UiKit.Caption("Now"));
        var nowRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
        _nowIcon.Margin = new Thickness(0, 0, 10, 0);
        nowRow.Children.Add(_nowIcon);
        nowRow.Children.Add(_nowName);
        now.Children.Add(nowRow);
        now.Children.Add(_nowTimer);
        now.Children.Add(UiKit.Divider(14));
        now.Children.Add(KeyValue("Total tracked", _total));
        now.Children.Add(KeyValue("Other apps", _other));
        now.Children.Add(KeyValue("Sessions (all)", _sessAll));
        var nowPanel = UiKit.Panel(now, 20);
        Grid.SetColumn(nowPanel, 2);
        row1.Children.Add(nowPanel);
        _root.Children.Add(row1);

        // stats
        var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        (_curSession, var c1) = StatCell(label + " current session"); stats.Children.Add(c1);
        (_sessToday, var c2) = StatCell(label + " sessions today"); stats.Children.Add(c2);
        (_longest, var c3) = StatCell("Longest session"); stats.Children.Add(c3);
        (_avg, var c4) = StatCell("Average session"); stats.Children.Add(c4);
        (_week, var c5) = StatCell(label + " this week"); stats.Children.Add(c5);
        (_month, var c6) = StatCell(label + " this month"); stats.Children.Add(c6);
        (_yesterday, var c7) = StatCell("Yesterday"); stats.Children.Add(c7);
        (_goalPct, var c8) = StatCell("Weekly goal"); stats.Children.Add(c8);
        foreach (FrameworkElement fe in stats.Children) fe.Margin = new Thickness(0, 6, 0, 10);
        var statPanel = UiKit.Panel(stats, 18);
        statPanel.Margin = new Thickness(0, 0, 0, 14);
        _root.Children.Add(statPanel);

        // chart + top apps
        var row3 = new Grid();
        row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chartBox = new StackPanel();
        chartBox.Children.Add(UiKit.SectionLabel("Last 7 days"));
        chartBox.Children.Add(_chart);
        var cp = UiKit.Panel(chartBox, 18);
        Grid.SetColumn(cp, 0);
        row3.Children.Add(cp);
        var topBox = new StackPanel();
        topBox.Children.Add(UiKit.SectionLabel("Top applications today"));
        topBox.Children.Add(_top);
        var tp = UiKit.Panel(topBox, 18);
        Grid.SetColumn(tp, 2);
        row3.Children.Add(tp);
        _root.Children.Add(row3);
    }

    private static (TextBlock, FrameworkElement) StatCell(string label)
    {
        var v = UiKit.Txt("-", 18, "Text", FontWeights.SemiBold, true);
        v.Margin = new Thickness(0, 4, 0, 0);
        var p = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        p.Children.Add(UiKit.Caption(label));
        p.Children.Add(v);
        return (v, p);
    }

    private static DockPanel KeyValue(string k, TextBlock v)
    {
        var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(v, Dock.Right);
        d.Children.Add(v);
        d.Children.Add(UiKit.Txt(k, 12.5, "TextMuted"));
        return d;
    }

    public void Refresh()
    {
        var e = _h.Engine;
        e.Checkpoint();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var yday = today.AddDays(-1);
        var wk = ReportMath.WeekStart(today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var from = new[] { monthStart, wk, today.AddDays(-6), yday }.Min();
        var totals = _h.Db.GetDayTotals(from, today).ToDictionary(d => d.Date);
        long Study(DateOnly d) => totals.TryGetValue(d, out var t) ? t.StudyMs : 0;
        long Total(DateOnly d) => totals.TryGetValue(d, out var t) ? t.TotalMs : 0;

        var rows = _h.Db.GetSessions(today, today);
        var study = rows.Where(r => r.IsStudy).ToList();
        long studyToday = Study(today), totalToday = Total(today);

        _baseStudy = studyToday;
        _baseTotal = totalToday;
        _baseTs = Stopwatch.GetTimestamp();
        _curIsStudy = e.CurrentApp != null && _h.Settings.StudyAppKeys.Contains(e.CurrentApp.Key, StringComparer.OrdinalIgnoreCase);

        // hero + delta
        long dy = Study(yday);
        long diff = studyToday - dy;
        if (studyToday == 0 && dy == 0) { _heroDelta.Text = "No study time recorded yet"; _heroDelta.Foreground = UiKit.Br("TextMuted"); }
        else
        {
            _heroDelta.Text = (diff >= 0 ? "\u25B2 " : "\u25BC ") + ReportMath.Dur(Math.Abs(diff)) + " vs yesterday";
            _heroDelta.Foreground = UiKit.Br(diff >= 0 ? "Positive" : "Negative");
        }

        // week
        long weekStudy = 0;
        for (var d = wk; d <= today; d = d.AddDays(1)) weekStudy += Study(d);
        double goalMs = Math.Max(0.1, _h.Settings.WeeklyGoalHours) * 3600000.0;
        _weekText.Text = $"{ReportMath.Dur(weekStudy)} of {_h.Settings.WeeklyGoalHours:0.#}h goal";
        _weekBar.Set(weekStudy / goalMs);
        long monthStudy = 0;
        for (var d = monthStart; d <= today; d = d.AddDays(1)) monthStudy += Study(d);

        // stats
        int sessions = study.Count(r => !r.Continuation);
        long studySum = study.Sum(r => r.DurMs);
        _sessToday.Text = sessions.ToString();
        _longest.Text = ReportMath.Dur(ReportMath.LongestChain(study));
        _avg.Text = sessions > 0 ? ReportMath.Dur(studySum / sessions) : "-";
        _week.Text = ReportMath.Dur(weekStudy);
        _month.Text = ReportMath.Dur(monthStudy);
        _yesterday.Text = ReportMath.Dur(dy);
        _goalPct.Text = $"{Math.Min(999, weekStudy / goalMs * 100):0}%";

        _total.Text = ReportMath.Dur(totalToday);
        _other.Text = ReportMath.Dur(Math.Max(0, totalToday - studyToday));
        _sessAll.Text = rows.Count(r => !r.Continuation).ToString();

        // chart
        var items = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(d =>
        {
            long s = Study(d), t = Total(d);
            return new BarItem
            {
                Label = d.ToString("ddd"),
                A = s,
                B = Math.Max(0, t - s),
                Tip = $"{d:ddd d MMM}: {_h.Settings.StudyLabel} {ReportMath.Dur(s)}, other {ReportMath.Dur(Math.Max(0, t - s))}"
            };
        }).ToList();
        _chart.Items = items;

        // top apps
        _top.Children.Clear();
        var usage = ReportMath.Aggregate(rows).Take(5).ToList();
        if (usage.Count == 0) _top.Children.Add(UiKit.Caption("Nothing tracked yet today."));
        long max = usage.Count > 0 ? usage.Max(u => u.TotalMs) : 1;
        foreach (var u in usage)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UiKit.AppIcon(u.Key, u.Name, u.Path, 20);
            icon.Margin = new Thickness(0, 0, 10, 0);
            g.Children.Add(icon);
            var mid = new StackPanel();
            mid.Children.Add(UiKit.Txt(u.Name, 12.5));
            var bar = new ProgressLine(u.IsStudy ? "Accent" : "TextDim", 3) { Margin = new Thickness(0, 4, 12, 0) };
            bar.Set(u.TotalMs / (double)max);
            mid.Children.Add(bar);
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);
            var dur = UiKit.Txt(ReportMath.Dur(u.TotalMs), 12.5, "TextMuted", null, true);
            Grid.SetColumn(dur, 2);
            g.Children.Add(dur);
            _top.Children.Add(g);
        }

        UpdateNow();
        Tick();
    }

    private void UpdateNow()
    {
        var e = _h.Engine;
        _statusText.Text = e.State switch
        {
            TrackingState.Tracking => "\u25CF Tracking",
            TrackingState.Paused => "Paused",
            TrackingState.Locked => "Locked - not counting",
            TrackingState.Asleep => "Screen off - not counting",
            _ => "Idle - nothing to track"
        };
        var app = e.CurrentApp;
        _nowName.Text = app?.Name ?? "Nothing active";
        _nowIcon.Content = app != null ? UiKit.AppIcon(app.Key, app.Name, app.Path, 24) : null;
    }

    public void Tick()
    {
        var e = _h.Engine;
        var el = e.CurrentElapsed;
        _nowTimer.Text = e.CurrentApp != null ? ReportMath.Clock(el) : "-";
        _curSession.Text = _curIsStudy && e.CurrentApp != null ? ReportMath.Clock(el) : "-";

        long since = (Stopwatch.GetTimestamp() - _baseTs) * 1000 / Stopwatch.Frequency;
        string hero = ReportMath.Dur(_baseStudy + (_curIsStudy ? since : 0));
        if (hero != _heroShown) { _heroShown = hero; _heroValue.Text = hero; }
        if (e.CurrentApp != null) _total.Text = ReportMath.Dur(_baseTotal + since);
    }
}
