using System.Text.RegularExpressions;

namespace LyricFloat.Lyrics;

/// <summary>
/// Strips noise from track titles so LRCLIB lookups succeed more often:
/// "(feat. X)", "- Remastered 2011", "[Explicit]", "- Radio Edit", etc.
/// </summary>
public static partial class TitleNormalizer
{
    [GeneratedRegex(@"\s*[\(\[](feat\.?|ft\.?|with)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FeatTag();

    [GeneratedRegex(@"\s*-\s*(remaster(ed)?( \d{4})?|radio edit|single version|album version|live|mono|stereo|bonus track|explicit|deluxe).*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SuffixTag();

    [GeneratedRegex(@"\s*[\(\[](remaster(ed)?( \d{4})?|explicit|clean|bonus track|deluxe|from [^\)\]]*)[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BracketTag();

    public static string Normalize(string title)
    {
        var t = FeatTag().Replace(title, string.Empty);
        t = BracketTag().Replace(t, string.Empty);
        t = SuffixTag().Replace(t, string.Empty);
        return t.Trim();
    }

    /// <summary>Primary artist only: "A, B" / "A feat. B" / "A; B" => "A".</summary>
    public static string PrimaryArtist(string artist)
    {
        var separators = new[] { ",", ";", " feat.", " feat ", " ft.", " ft ", " & ", " x " };
        var result = artist;
        foreach (var sep in separators)
        {
            var i = result.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (i > 0) result = result[..i];
        }
        return result.Trim();
    }
}
