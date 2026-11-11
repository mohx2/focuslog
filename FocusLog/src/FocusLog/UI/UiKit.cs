using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusLog.Core;
using FocusLog.Native;

namespace FocusLog.UI;

internal interface IPage
{
    FrameworkElement Root { get; }
    /// <summary>Re-query data and update the visuals in place.</summary>
    void Refresh();
    /// <summary>Called once per second while the dashboard is visible (cheap label updates only).</summary>
    void Tick();
    /// <summary>0 = never auto refresh.</summary>
    int AutoRefreshSeconds { get; }
}

internal static class UiKit
{
    public static Brush Br(string key) => (Brush)Application.Current.Resources[key];
    public static Style St(string key) => (Style)Application.Current.Resources[key];

    public static TextBlock Txt(string text, double size = 13, string fg = "Text",
        FontWeight? weight = null, bool tabular = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = Br(fg),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (weight.HasValue) t.FontWeight = weight.Value;
        if (tabular) System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public static TextBlock Title(string s) => Txt(s, 20, "Text", FontWeights.SemiBold);
    public static TextBlock Caption(string s) => Txt(s, 12, "TextMuted");

    public static TextBlock SectionLabel(string s)
    {
        var t = Txt(s, 12, "TextMuted", FontWeights.SemiBold);
        t.Margin = new Thickness(0, 0, 0, 10);
        return t;
    }

    public static Border Panel(UIElement child, double pad = 18) => new()
    {
        Background = Br("Surface"),
        BorderBrush = Br("Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(pad),
        Child = child
    };

    public static Border Divider(double margin = 14) => new()
    {
        Height = 1,
        Background = Br("Border"),
        Margin = new Thickness(0, margin, 0, margin)
    };

    public static Button Btn(string text, Action onClick, string style = "GhostButton")
    {
        var b = new Button { Content = text, Style = St(style), FontSize = 12.5 };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static RadioButton Seg(string text, string group, bool on, Action onChecked)
    {
        var r = new RadioButton { Content = text, GroupName = group, IsChecked = on, Style = St("Seg"), FontSize = 12.5 };
        r.Checked += (_, _) => onChecked();
        return r;
    }

    public static CheckBox Toggle(bool on, Action<bool> changed)
    {
        var c = new CheckBox { IsChecked = on, Style = St("ToggleSwitch"), VerticalAlignment = VerticalAlignment.Center };
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    /// <summary>Label + optional description on the left, control on the right.</summary>
    public static Grid SettingRow(string label, string? description, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Txt(label, 13));
        if (!string.IsNullOrEmpty(description))
        {
            var d = Txt(description, 12, "TextMuted");
            d.TextWrapping = TextWrapping.Wrap;
            d.TextTrimming = TextTrimming.None;
            d.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(d);
        }
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(left);
        g.Children.Add(control);
        return g;
    }

    private static readonly string[] BadgeColors =
        { "#4A5A8A", "#3F7F78", "#8A6C3F", "#7C4C5E", "#5E6572", "#6A5A8A", "#B04A44", "#A94D78", "#3E8F64" };

    public static FrameworkElement AppIcon(string key, string name, string path, double size = 22)
    {
        if (!key.StartsWith("site:", StringComparison.Ordinal))
        {
            var src = ShellIcons.Get(path);
            if (src != null)
            {
                var img = new Image { Source = src, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
        }
        int sum = 0;
        foreach (char ch in key) sum += ch;
        string hex = key switch
        {
            "site:youtube" => "#B04A44",
            "site:instagram" => "#A94D78",
            "site:whatsapp-web" => "#3E8F64",
            _ => BadgeColors[sum % 6]
        };
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?",
                FontSize = size * 0.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    public static string[] Palette { get; } = { "#5B6B8C", "#4D8F86", "#9A7B4F", "#8A5C6E", "#6F7783", "#4B5563" };
}

/// <summary>Thin progress bar using star-sized columns (no animation, no per-frame work).</summary>
internal sealed class ProgressLine : Grid
{
    private readonly ColumnDefinition _a = new(), _b = new();

    public ProgressLine(string fillBrush = "Accent", double height = 4)
    {
        Height = height;
        ColumnDefinitions.Add(_a);
        ColumnDefinitions.Add(_b);
        var track = new Border { Background = UiKit.Br("BorderStrong"), CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        Children.Add(track);
        var fill = new Border { Background = UiKit.Br(fillBrush), CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumn(fill, 0);
        Children.Add(fill);
        Set(0);
    }

    public void Set(double fraction)
    {
        double f = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        _a.Width = new GridLength(f, GridUnitType.Star);
        _b.Width = new GridLength(1 - f, GridUnitType.Star);
    }
}

/// <summary>Today / Yesterday / 7 days / 30 days / This month / Custom range picker shared by report pages.</summary>
internal sealed class RangeSelector : StackPanel
{
    private readonly TrackerHost _h;
    private readonly Action _changed;
    private readonly StackPanel _custom;
    private readonly TextBox _from, _to;

    public RangeSelector(TrackerHost host, Action changed)
    {
        _h = host;
        _changed = changed;
        string group = "range" + Guid.NewGuid().ToString("N");

        var segs = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(string text, RangeKind kind) =>
            segs.Children.Add(UiKit.Seg(text, group, _h.Range == kind, () =>
            {
                _h.Range = kind;
                _custom!.Visibility = kind == RangeKind.Custom ? Visibility.Visible : Visibility.Collapsed;
                if (kind != RangeKind.Custom) _changed();
            }));
        _custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Add("Today", RangeKind.Today);
        Add("Yesterday", RangeKind.Yesterday);
        Add("7 days", RangeKind.Last7);
        Add("30 days", RangeKind.Last30);
        Add("This month", RangeKind.ThisMonth);
        Add("Custom", RangeKind.Custom);
        Children.Add(segs);

        _from = new TextBox { Width = 108, Text = ReportMath.Key(_h.CustomFrom), FontSize = 12.5 };
        _to = new TextBox { Width = 108, Text = ReportMath.Key(_h.CustomTo), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) };
        var apply = UiKit.Btn("Apply", Apply);
        apply.Margin = new Thickness(8, 0, 0, 0);
        _custom.Children.Add(UiKit.Caption("From"));
        _from.Margin = new Thickness(8, 0, 0, 0);
        _custom.Children.Add(_from);
        var toLabel = UiKit.Caption("to");
        toLabel.Margin = new Thickness(8, 0, 0, 0);
        _custom.Children.Add(toLabel);
        _custom.Children.Add(_to);
        _custom.Children.Add(apply);
        _custom.Visibility = _h.Range == RangeKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_custom);
    }

    private void Apply()
    {
        bool ok1 = DateOnly.TryParseExact(_from.Text.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var f);
        bool ok2 = DateOnly.TryParseExact(_to.Text.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t);
        _from.BorderBrush = ok1 ? UiKit.Br("BorderStrong") : UiKit.Br("Negative");
        _to.BorderBrush = ok2 ? UiKit.Br("BorderStrong") : UiKit.Br("Negative");
        if (!ok1 || !ok2) return;
        if (t < f) (f, t) = (t, f);
        _h.CustomFrom = f;
        _h.CustomTo = t;
        _changed();
    }

    public static (DateOnly From, DateOnly To) Resolve(TrackerHost h)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return h.Range switch
        {
            RangeKind.Today => (today, today),
            RangeKind.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            RangeKind.Last7 => (today.AddDays(-6), today),
            RangeKind.Last30 => (today.AddDays(-29), today),
            RangeKind.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
            _ => (h.CustomFrom, h.CustomTo)
        };
    }

    public static string Describe(TrackerHost h)
    {
        var (f, t) = Resolve(h);
        return f == t ? f.ToString("ddd d MMM yyyy") : $"{f:d MMM yyyy} - {t:d MMM yyyy}";
    }
}
