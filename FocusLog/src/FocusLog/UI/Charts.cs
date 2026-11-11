using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FocusLog.UI;

internal sealed class BarItem
{
    public string Label { get; set; } = "";
    public double A { get; set; }   // study ms (accent)
    public double B { get; set; }   // other ms (muted)
    public string Tip { get; set; } = "";
}

/// <summary>Minimal stacked bar chart drawn once with DrawingContext. Redrawn only when data/size changes.</summary>
internal sealed class BarChartControl : FrameworkElement
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private List<BarItem> _items = new();
    private int _hover = -1;

    public BarChartControl(double height = 180)
    {
        Height = height;
        SnapsToDevicePixels = true;
        ToolTipService.SetInitialShowDelay(this, 150);
    }

    public List<BarItem> Items
    {
        get => _items;
        set { _items = value ?? new List<BarItem>(); _hover = -1; InvalidateVisual(); }
    }

    private const double Left = 36, Top = 8, Bottom = 22, Right = 4;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Math.Max(w, 0), Math.Max(h, 0)));
        if (w < 60 || h < 70) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double plotW = w - Left - Right, plotH = h - Top - Bottom;

        double maxMs = 0;
        foreach (var it in _items) maxMs = Math.Max(maxMs, it.A + it.B);
        double maxH = Math.Max(maxMs / 3600000.0, 0.5);
        double step = NiceStep(maxH / 3.0);
        double axisMax = Math.Ceiling(maxH / step - 1e-9) * step;

        var gridPen = new Pen(UiKit.Br("Border"), 1);
        for (double v = 0; v <= axisMax + 1e-9; v += step)
        {
            double y = Math.Round(Top + plotH - plotH * (v / axisMax)) + 0.5;
            dc.DrawLine(gridPen, new Point(Left, y), new Point(w - Right, y));
            var ft = Text(AxisLabel(v), 11, "TextDim", dpi);
            dc.DrawText(ft, new Point(Left - 6 - ft.Width, y - ft.Height / 2));
        }

        int n = _items.Count;
        if (n == 0)
        {
            var ft = Text("No data for this period", 12, "TextDim", dpi);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            return;
        }

        double slot = plotW / n;
        double bw = Math.Min(30, slot * 0.58);
        int every = Math.Max(1, (int)Math.Ceiling(36.0 / slot));
        var other = UiKit.Br("Other");
        var accent = UiKit.Br("Accent");

        for (int i = 0; i < n; i++)
        {
            var it = _items[i];
            double x = Left + slot * i + (slot - bw) / 2;
            if (i == _hover)
                dc.DrawRoundedRectangle(UiKit.Br("SurfaceHover"), null, new Rect(Left + slot * i, Top, slot, plotH), 3, 3);
            double total = (it.A + it.B) / 3600000.0 / axisMax * plotH;
            double study = it.A / 3600000.0 / axisMax * plotH;
            if (total >= 1)
                dc.DrawRoundedRectangle(other, null, new Rect(x, Top + plotH - total, bw, total), 2, 2);
            if (study >= 1)
                dc.DrawRoundedRectangle(accent, null, new Rect(x, Top + plotH - study, bw, study), 2, 2);
            if (i % every == 0)
            {
                var ft = Text(it.Label, 11, "TextDim", dpi);
                dc.DrawText(ft, new Point(x + bw / 2 - ft.Width / 2, Top + plotH + 5));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_items.Count == 0) return;
        double plotW = ActualWidth - Left - Right;
        int i = (int)Math.Floor((e.GetPosition(this).X - Left) / (plotW / _items.Count));
        if (i < 0 || i >= _items.Count) i = -1;
        if (i == _hover) return;
        _hover = i;
        ToolTip = i >= 0 ? _items[i].Tip : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; ToolTip = null; InvalidateVisual(); }
    }

    private static FormattedText Text(string s, double size, string brush, double dpi) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, UiKit.Br(brush), dpi);

    private static string AxisLabel(double hours)
    {
        if (hours <= 0) return "0";
        if (hours < 1) return $"{hours * 60:0}m";
        return $"{hours:0.#}h";
    }

    private static double NiceStep(double x)
    {
        double[] c = { 0.25, 0.5, 1, 2, 3, 4, 6, 8, 12, 24, 48, 96 };
        foreach (var v in c) if (v >= x) return v;
        return c[^1];
    }
}
