using System;
using System.Windows;
using System.Windows.Media;

namespace FocusLog.UI;

internal static class Theme
{
    public static readonly (string Name, string Hex)[] Accents =
    {
        ("Indigo", "#7C8AF5"),
        ("Teal",   "#4FBFAE"),
        ("Amber",  "#E0A84B"),
        ("Rose",   "#E5707E"),
        ("Slate",  "#8FA3B8"),
    };

    public static void ApplyAccent(string hex)
    {
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(hex); }
        catch { c = (Color)ColorConverter.ConvertFromString("#7C8AF5"); }
        var res = Application.Current.Resources;
        res["Accent"] = new SolidColorBrush(c);
        res["AccentSoft"] = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
    }
}
