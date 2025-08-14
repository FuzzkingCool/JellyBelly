using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.JellyBelly.Vectorization;

/// <summary>
/// Tokenizes library item metadata into canonical token strings.
/// </summary>
public static class Tokenizer
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","of","to","in","on","for","with","by","at","from","as","is","it","this","that","these","those"
    };

    /// <summary>
    /// Produces canonical tokens from item metadata fields for vectorization.
    /// </summary>
    /// <param name="genres">Item genres.</param>
    /// <param name="tags">Item tags.</param>
    /// <param name="people">People associated with the item.</param>
    /// <param name="studios">Studios associated with the item.</param>
    /// <param name="title">Item title.</param>
    /// <param name="overview">Item overview/description.</param>
    /// <returns>Sequence of normalized tokens namespaced by field.</returns>
    public static IEnumerable<string> Tokenize(
        IEnumerable<string> genres,
        IEnumerable<string> tags,
        IEnumerable<string> people,
        IEnumerable<string> studios,
        string? title,
        string? overview)
    {
        foreach (var g in genres ?? Array.Empty<string>())
            yield return "genre:" + Canon(g);
        foreach (var t in tags ?? Array.Empty<string>())
            yield return "tag:" + Canon(t);
        foreach (var p in people ?? Array.Empty<string>())
            yield return "person:" + Canon(p);
        foreach (var s in studios ?? Array.Empty<string>())
            yield return "studio:" + Canon(s);

        foreach (var kw in ExtractKeywords(title))
            yield return "title:" + kw;
        foreach (var kw in ExtractKeywords(overview).Take(10))
            yield return "overview:" + kw;
    }

    private static IEnumerable<string> ExtractKeywords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var norm = new string(text!
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray());
        foreach (var raw in norm.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 3) continue;
            if (Stopwords.Contains(raw)) continue;
            yield return raw;
        }
    }

    private static string Canon(string value)
        => value.Trim().ToLower(CultureInfo.InvariantCulture);
}


