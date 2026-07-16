using System.Text.RegularExpressions;

namespace LyricFloat.Lyrics;

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled)]
    private static partial Regex TimeTag();

    public static List<LyricLine> Parse(string lrc)
    {
        var result = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var matches = TimeTag().Matches(line);
            if (matches.Count == 0) continue;

            var text = TimeTag().Replace(line, string.Empty).Trim();

            foreach (Match m in matches)
            {
                var min = int.Parse(m.Groups[1].Value);
                var sec = int.Parse(m.Groups[2].Value);
                var ms = 0;
                if (m.Groups[3].Success)
                {
                    // ".5" => 500ms, ".50" => 500ms, ".500" => 500ms
                    ms = int.Parse(m.Groups[3].Value.PadRight(3, '0'));
                }
                result.Add(new LyricLine(min * 60_000 + sec * 1_000 + ms, text));
            }
        }
        result.Sort(static (a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return result;
    }
}
