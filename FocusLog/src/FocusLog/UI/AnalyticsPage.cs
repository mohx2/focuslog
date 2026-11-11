using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusLog.Core;

namespace FocusLog.UI;

internal sealed class AnalyticsPage : IPage
{
    private readonly TrackerHost _h;
    private readonly StackPanel _root = new();
    private readonly TextBlock _sub = UiKit.Caption("");
    private readonly System.Windows.Controls.Primitives.UniformGrid _summary = new() { Columns = 3 };
    private readonly BarChartControl _daily = new(190);
    private readonly TextBlock _dailyTitle = UiKit.SectionLabel("Daily time");
    private readonly StackPanel _dist = new();
    private readonly BarChartControl _weekly = new(170);
    private readonly BarChartControl _monthly = new(170);

    public FrameworkElement Root => _root;
    public int AutoRefreshSeconds => 0;

    public AnalyticsPage(TrackerHost host)
    {
        _h = host;
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(UiKit.Title("Analytics"));
        _sub.Margin = new Thickness(0, 3, 0, 0);
        head.Children.Add(_sub);
        _root.Children.Add(head);

        var sel = new RangeSelector(_h, Refresh) { Margin = new Thickness(0, 0, 0, 14) };
        _root.Children.Add(sel);

        var sp = UiKit.Panel(_summary, 18);
        sp.Margin = new Thickness(0, 0, 0, 14);
        _root.Children.Add(sp);

        var dailyBox = new StackPanel();
        dailyBox.Children.Add(_dailyTitle);
        dailyBox.Children.Add(_daily);
        var dp = UiKit.Panel(dailyBox, 18);
        dp.Margin = new Thickness(0, 0, 0, 14);
        _root.Children.Add(dp);

        var distBox = new StackPanel();
        distBox.Children.Add(UiKit.SectionLabel("Usage distribution"));
        distBox.Children.Add(_dist);
        var dist = UiKit.Panel(distBox, 18);
        dist.Margin = new Thickness(0, 0, 0, 14);
        _root.Children.Add(dist);

        var trends = new Grid();
        trends.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trends.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        trends.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var wb = new StackPanel();
        wb.Children.Add(UiKit.SectionLabel("Weekly trend (8 weeks)"));
        wb.Children.Add(_weekly);
        var mb = new StackPanel();
        mb.Children.Add(UiKit.SectionLabel("Monthly trend (6 months)"));
        mb.Children.Add(_monthly);
        var wp = UiKit.Panel(wb, 18);
        var mp = UiKit.Panel(mb, 18);
        Grid.SetColumn(mp, 2);
        trends.Children.Add(wp);
        trends.Children.Add(mp);
        _root.Children.Add(trends);
    }

    public void Refresh()
    {
        _h.Engine.Checkpoint();
        var (from, to) = RangeSelector.Resolve(_h);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (to > today) to = today;
        _sub.Text = RangeSelector.Describe(_h) + "   |   study = " + _h.Settings.StudyLabel;

        var rows = _h.Db.GetSessions(from, to);
        var usage = ReportMath.Aggregate(rows);
        long total = rows.Sum(r => r.DurMs);
        long study = rows.Where(r => r.IsStudy).Sum(r => r.DurMs);
        int days = Math.Max(1, to.DayNumber - from.DayNumber + 1);

        _summary.Children.Clear();
        _summary.Children.Add(Stat("Total tracked", ReportMath.Dur(total)));
        _summary.Children.Add(Stat(_h.Settings.StudyLabel + " time", ReportMath.Dur(study)));
        _summary.Children.Add(Stat("Other applications", ReportMath.Dur(total - study)));
        _summary.Children.Add(Stat("Daily average", ReportMath.Dur(total / days)));
        _summary.Children.Add(Stat("Longest session", ReportMath.Dur(ReportMath.LongestChain(rows))));
        _summary.Children.Add(Stat("Sessions", rows.Count(r => !r.Continuation).ToString()));

        // daily (or weekly buckets for very long ranges)
        var totals = _h.Db.GetDayTotals(from, to).ToDictionary(d => d.Date);
        var items = new List<BarItem>();
        if (days <= 62)
        {
            _dailyTitle.Text = "Daily time";
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                totals.TryGetValue(d, out var t);
                long s = t?.StudyMs ?? 0, tt = t?.TotalMs ?? 0;
                items.Add(new BarItem
                {
                    Label = days <= 7 ? d.ToString("ddd") : d.Day.ToString(),
                    A = s,
                    B = Math.Max(0, tt - s),
                    Tip = $"{d:ddd d MMM}: study {ReportMath.Dur(s)}, other {ReportMath.Dur(Math.Max(0, tt - s))}"
                });
            }
        }
        else
        {
            _dailyTitle.Text = "Weekly time";
            items = Buckets(totals.Values, d => ReportMath.WeekStart(d), d => d.ToString("d MMM"));
        }
        _daily.Items = items;

        BuildDistribution(usage, total);

        var wkStart = ReportMath.WeekStart(today).AddDays(-7 * 7);
        var wTotals = _h.Db.GetDayTotals(wkStart, today);
        var wItems = new List<BarItem>();
        for (int i = 0; i < 8; i++)
        {
            var w = wkStart.AddDays(7 * i);
            long s = 0, t = 0;
            foreach (var d in wTotals.Where(x => x.Date >= w && x.Date <= w.AddDays(6))) { s += d.StudyMs; t += d.TotalMs; }
            wItems.Add(new BarItem
            {
                Label = w.ToString("d MMM"),
                A = s,
                B = Math.Max(0, t - s),
                Tip = $"Week of {w:d MMM}: study {ReportMath.Dur(s)}, other {ReportMath.Dur(Math.Max(0, t - s))}"
            });
        }
        _weekly.Items = wItems;

        var mStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        var mTotals = _h.Db.GetDayTotals(mStart, today);
        var mItems = new List<BarItem>();
        for (int i = 0; i < 6; i++)
        {
            var ms = mStart.AddMonths(i);
            long s = 0, t = 0;
            foreach (var d in mTotals.Where(x => x.Date.Year == ms.Year && x.Date.Month == ms.Month)) { s += d.StudyMs; t += d.TotalMs; }
            mItems.Add(new BarItem { Label = ms.ToString("MMM"), A = s, B = Math.Max(0, t - s), Tip = $"{ms:MMMM yyyy}: study {ReportMath.Dur(s)}, other {ReportMath.Dur(Math.Max(0, t - s))}" });
        }
        _monthly.Items = mItems;
    }

    private static List<BarItem> Buckets(IEnumerable<DayTotal> days, Func<DateOnly, DateOnly> key, Func<DateOnly, string> label)
    {
        return days.GroupBy(d => key(d.Date)).OrderBy(g => g.Key).Select(g =>
        {
            long s = g.Sum(x => x.StudyMs), t = g.Sum(x => x.TotalMs);
            return new BarItem
            {
                Label = label(g.Key),
                A = s,
                B = Math.Max(0, t - s),
                Tip = $"Week of {g.Key:d MMM}: study {ReportMath.Dur(s)}, other {ReportMath.Dur(Math.Max(0, t - s))}"
            };
        }).ToList();
    }

    private void BuildDistribution(List<AppUsage> usage, long total)
    {
        _dist.Children.Clear();
        if (total <= 0 || usage.Count == 0)
        {
            _dist.Children.Add(UiKit.Caption("Nothing tracked in this period."));
            return;
        }
        var shown = usage.Take(6).ToList();
        long rest = total - shown.Sum(u => u.TotalMs);

        var bar = new Grid { Height = 10, Margin = new Thickness(0, 0, 0, 14) };
        var clip = new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true };
        var inner = new Grid();
        int idx = 0, col = 0;
        var legend = new StackPanel();
        void Seg(string name, long ms, Brush color, bool study)
        {
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(ms, 1), GridUnitType.Star) });
            var b = new Border { Background = color, Margin = new Thickness(0, 0, col == 0 ? 0 : 0, 0), BorderBrush = UiKit.Br("Surface"), BorderThickness = new Thickness(0, 0, 1, 0) };
            Grid.SetColumn(b, col++);
            inner.Children.Add(b);

            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = color, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var pct = UiKit.Txt($"{ms * 100.0 / total:0.#}%", 12.5, "TextMuted", null, true);
            pct.Width = 52; pct.TextAlignment = TextAlignment.Right;
            var dur = UiKit.Txt(ReportMath.Dur(ms), 12.5, "Text", null, true);
            dur.Width = 70; dur.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(pct, Dock.Right);
            DockPanel.SetDock(dur, Dock.Right);
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(pct);
            row.Children.Add(dur);
            row.Children.Add(dot);
            row.Children.Add(UiKit.Txt(name, 12.5, "Text", study ? FontWeights.SemiBold : (FontWeight?)null));
            legend.Children.Add(row);
        }
        foreach (var u in shown)
        {
            Brush c = u.IsStudy ? UiKit.Br("Accent")
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(UiKit.Palette[idx++ % UiKit.Palette.Length]));
            Seg(u.Name, u.TotalMs, c, u.IsStudy);
        }
        if (rest > 0) Seg("Other", rest, UiKit.Br("Other"), false);
        clip.Child = inner;
        bar.Children.Add(clip);
        _dist.Children.Add(bar);
        _dist.Children.Add(legend);
    }

    private static FrameworkElement Stat(string label, string value)
    {
        var p = new StackPanel { Margin = new Thickness(0, 6, 10, 10) };
        p.Children.Add(UiKit.Caption(label));
        var v = UiKit.Txt(value, 18, "Text", FontWeights.SemiBold, true);
        v.Margin = new Thickness(0, 4, 0, 0);
        p.Children.Add(v);
        return p;
    }

    public void Tick() { }
}
