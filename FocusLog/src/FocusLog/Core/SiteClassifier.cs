using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FocusLog.Core;

/// <summary>A rule that recognises a website from a browser window title (title is never stored).</summary>
public sealed class SiteRule
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";

    public static List<SiteRule> Defaults() => new()
    {
        new SiteRule { Key = "site:youtube", Name = "YouTube",
            Pattern = @"(^\s*(\(\d+\)\s*)?|[-\u2013\u2014|]\s*)YouTube\s*($|[-\u2013\u2014|]|and \d+ more)" },
        new SiteRule { Key = "site:instagram", Name = "Instagram",
            Pattern = @"(^\s*(\(\d+\)\s*)?|[-\u2013\u2014|\u2022]\s*)Instagram(\s*$|\s*[-\u2013\u2014|\u2022]|\s+and \d+ more|\s+photos and videos)" },
        new SiteRule { Key = "site:whatsapp-web", Name = "WhatsApp Web",
            Pattern = @"^\s*(\(\d+\)\s*)?WhatsApp\s*($|[-\u2013\u2014|]|and \d+ more)" },
    };
}

public sealed class SiteClassifier
{
    private readonly List<(SiteRule Rule, Regex Rx)> _rules = new();

    public SiteClassifier(IEnumerable<SiteRule> rules)
    {
        foreach (var r in rules)
        {
            try
            {
                _rules.Add((r, new Regex(r.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))));
            }
            catch (ArgumentException) { /* ignore invalid user pattern */ }
        }
    }

    public SiteRule? Match(string title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        foreach (var (rule, rx) in _rules)
        {
            try { if (rx.IsMatch(title)) return rule; }
            catch (RegexMatchTimeoutException) { }
        }
        return null;
    }
}
