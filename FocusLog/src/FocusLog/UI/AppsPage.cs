using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusLog.Core;

namespace FocusLog.UI;

internal sealed class AppsPage : IPage
{
    private readonly TrackerHost _h;
    private readonly StackPanel _root = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _sub = UiKit.Caption("");
    private string _sort = "duration";

    public FrameworkElement Root => _root;
    public int AutoRefreshSeconds => 0;

    public AppsPage(TrackerHost host)
    {
        _h = host;
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(UiKit.Title("Applications"));
        _sub.Margin = new Thickness(0, 3, 0, 0);
        head.Children.Add(_sub);
        _root.Children.Add(head);

        var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var sort = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        string g = "sort" + Guid.NewGuid().ToString("N");
        var lbl = UiKit.Caption("Sort");
        lbl.Margin = new Thickness(0, 0, 8, 0);
        sort.Children.Add(lbl);
        sort.Children.Add(UiKit.Seg("Duration", g, true, () => { _sort = "duration"; Refresh(); }));
        sort.Children.Add(UiKit.Seg("Sessions", g, false, () => { _sort = "sessions"; Refresh(); }));
        sort.Children.Add(UiKit.Seg("Name", g, false, () => { _sort = "name"; Refresh(); }));
        DockPanel.SetDock(sort, Dock.Right);
        controls.Children.Add(sort);
        controls.Children.Add(new RangeSelector(_h, Refresh));
        _root.Children.Add(controls);

        _root.Children.Add(UiKit.Panel(_list, 6));
    }

    public void Refresh()
    {
        _h.Engine.Checkpoint();
        var (from, to) = RangeSelector.Resolve(_h);
        _sub.Text = RangeSelector.Describe(_h);
        var usage = ReportMath.Aggregate(_h.Db.GetSessions(from, to));
        long total = usage.Sum(u => u.TotalMs);
        usage = _sort switch
        {
            "sessions" => usage.OrderByDescending(u => u.Sessions).ThenByDescending(u => u.TotalMs).ToList(),
            "name" => usage.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => usage.OrderByDescending(u => u.TotalMs).ToList()
        };

        _list.Children.Clear();
        _list.Children.Add(Row(null, 0, 0, true));
        if (usage.Count == 0)
        {
            var empty = UiKit.Caption("Nothing tracked in this period.");
            empty.Margin = new Thickness(14, 16, 14, 16);
            _list.Children.Add(empty);
            return;
        }
        long max = usage.Max(u => u.TotalMs);
        foreach (var u in usage) _list.Children.Add(Row(u, total, max, false));
    }

    private static Grid Row(AppUsage? u, long total, long max, bool header)
    {
        var g = new Grid { Margin = new Thickness(10, header ? 8 : 7, 10, header ? 8 : 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        void Put(FrameworkElement el, int col, HorizontalAlignment ha = HorizontalAlignment.Left)
        {
            el.HorizontalAlignment = ha;
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }

        if (header)
        {
            Put(UiKit.Caption("Application"), 1);
            Put(UiKit.Caption("Time"), 3, HorizontalAlignment.Right);
            Put(UiKit.Caption("Share"), 4, HorizontalAlignment.Right);
            Put(UiKit.Caption("Sessions"), 5, HorizontalAlignment.Right);
            return g;
        }

        var a = u!;
        Put(UiKit.AppIcon(a.Key, a.Name, a.Path, 22), 0);
        var name = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        name.Children.Add(UiKit.Txt(a.Name, 13, "Text", a.IsStudy ? FontWeights.SemiBold : (FontWeight?)null));
        name.Children.Add(UiKit.Txt(a.Exe, 11, "TextDim"));
        Put(name, 1, HorizontalAlignment.Stretch);
        var bar = new ProgressLine(a.IsStudy ? "Accent" : "TextDim", 4) { Margin = new Thickness(0, 0, 14, 0) };
        bar.Set(a.TotalMs / (double)Math.Max(1, max));
        Put(bar, 2, HorizontalAlignment.Stretch);
        Put(UiKit.Txt(ReportMath.Dur(a.TotalMs), 13, "Text", null, true), 3, HorizontalAlignment.Right);
        Put(UiKit.Txt(total > 0 ? $"{a.TotalMs * 100.0 / total:0.#}%" : "-", 13, "TextMuted", null, true), 4, HorizontalAlignment.Right);
        Put(UiKit.Txt(a.Sessions.ToString(), 13, "TextMuted", null, true), 5, HorizontalAlignment.Right);
        return g;
    }

    public void Tick() { }
}
