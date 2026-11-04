using System.Text;

namespace HydayTalk.Core;

/// Simplified to Traditional Chinese with OpenCC's standard tables (Resources/s2t.txt): at each
/// position the longest phrase match wins, otherwise the single character is mapped. It runs the
/// same on every platform, unlike LCMapStringEx, which is missing outside Windows (and under Wine).
public static class ChineseConverter
{
    private static readonly Lazy<(Dictionary<string, string> Map, int Longest)> Table = new(Load);

    private static (Dictionary<string, string>, int) Load()
    {
        using var stream = typeof(ChineseConverter).Assembly.GetManifestResourceStream("s2t.txt")
            ?? throw new InvalidOperationException("Missing s2t.txt");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var map = new Dictionary<string, string>(60_000, StringComparer.Ordinal);
        var longest = 1;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            var key = line[..tab];
            map[key] = line[(tab + 1)..];
            longest = Math.Max(longest, key.Length);
        }
        return (map, longest);
    }

    public static string ToTraditional(string text)
    {
        if (text.Length == 0) return text;
        var (map, longest) = Table.Value;
        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            // Only CJK text can match; copy everything else straight through.
            if (text[i] < 0x2E80) { result.Append(text[i]); i++; continue; }
            var matched = false;
            for (var length = Math.Min(longest, text.Length - i); length >= 1; length--)
            {
                // Never split a surrogate pair.
                if (i + length < text.Length && char.IsLowSurrogate(text[i + length])) continue;
                if (map.TryGetValue(text.Substring(i, length), out var value))
                {
                    result.Append(value); i += length; matched = true; break;
                }
            }
            if (matched) continue;
            var step = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            result.Append(text, i, step); i += step;
        }
        return result.ToString();
    }
}
