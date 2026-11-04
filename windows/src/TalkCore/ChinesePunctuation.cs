using System.Text;
using System.Text.RegularExpressions;

namespace HydayTalk.Core;

/// Formats Chinese sentences without changing numeric or technical notation.
internal static class ChinesePunctuation
{
    // .NET has no \p{Han}; the CJK ranges below match the ones isHan checks.
    private const string Han = @"㐀-䶿一-鿿豈-﫿";
    private static readonly Regex Technical = new(
        @"```[\s\S]*?```|`[^`\n]+`|(?i:https?://|www\.)[^\s" + Han + @"，。！？；：（）「」『』<>""'`]+|[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}|\b[vV]?\d+(?:[.,:]\d+)+|\b[A-Za-z_][A-Za-z0-9_\-]*(?:\.[A-Za-z0-9_\-]+)+",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Punctuation = new()
    {
        [","] = "，", ["."] = "。", ["!"] = "！", ["?"] = "？", [":"] = "：", [";"] = "；",
        ["("] = "（", [")"] = "）", ["["] = "［", ["]"] = "］",
    };

    public static string Normalize(string text)
    {
        var characters = Text.Elements(text);
        if (!characters.Any(Text.IsHan)) return text;
        // Map UTF-16 offsets from the regex onto character indices.
        var offsets = new Dictionary<int, int>();
        var position = 0;
        for (var i = 0; i < characters.Count; i++) { offsets[position] = i; position += characters[i].Length; }
        offsets[position] = characters.Count;

        var protectedChars = new bool[characters.Count];
        foreach (Match match in Technical.Matches(text))
        {
            if (!offsets.TryGetValue(match.Index, out var start)) continue;
            if (!offsets.TryGetValue(match.Index + match.Length, out var end)) end = characters.Count;
            var lower = match.Value.ToLowerInvariant();
            // A sentence-ending comma/period is not part of the preceding URL.
            if (lower.StartsWith("http") || lower.StartsWith("www."))
                while (end > start && ".,!?;:)]".Contains(characters[end - 1])) end--;
            for (var i = start; i < end; i++) protectedChars[i] = true;
        }
        for (var i = 1; i + 1 < characters.Count; i++)
            if (",.:".Contains(characters[i]) && characters[i].Length == 1 && Text.IsNumber(characters[i - 1]) && Text.IsNumber(characters[i + 1]))
                protectedChars[i] = true;

        var quotes = new List<(int Start, int End, bool Single)>();
        var opening = new Dictionary<string, int>();
        for (var i = 0; i < characters.Count; i++)
        {
            if (protectedChars[i]) continue;
            var c = characters[i];
            if (Text.IsNewline(c)) { opening.Clear(); continue; }
            if (c != "\"" && c != "'") continue;
            // Apostrophes inside Latin words are not quotation marks.
            if (c == "'" && i > 0 && i + 1 < characters.Count && Text.IsLetter(characters[i - 1]) && Text.IsLetter(characters[i + 1])) continue;
            if (opening.Remove(c, out var start))
            {
                quotes.Add((start, i, c == "'"));
                var hasHan = false;
                for (var k = start + 1; k < i; k++) if (Text.IsHan(characters[k])) { hasHan = true; break; }
                if (!hasHan) for (var k = start + 1; k < i; k++) protectedChars[k] = true;
            }
            else opening[c] = i;
        }

        var chinese = new bool[characters.Count];
        var quoted = new bool[characters.Count];
        foreach (var q in quotes) for (var k = q.Start + 1; k < q.End; k++) quoted[k] = true;
        var sentenceStart = 0;
        for (var i = 0; i < characters.Count; i++)
        {
            var endsSentence = Text.IsNewline(characters[i])
                || (!protectedChars[i] && !quoted[i] && characters[i].Length == 1 && ".!?。！？".Contains(characters[i]));
            if (endsSentence || i == characters.Count - 1)
            {
                var hasChinese = false;
                for (var k = sentenceStart; k <= i; k++) if (!protectedChars[k] && Text.IsHan(characters[k])) { hasChinese = true; break; }
                if (hasChinese) for (var k = sentenceStart; k <= i; k++) chinese[k] = true;
                sentenceStart = i + 1;
            }
        }

        var output = new List<string>(characters);
        for (var i = 0; i < characters.Count; i++)
            if (chinese[i] && !protectedChars[i] && Punctuation.TryGetValue(characters[i], out var replacement)) output[i] = replacement;
        foreach (var q in quotes)
        {
            if (!chinese[q.Start] && !chinese[q.End]) continue;
            output[q.Start] = q.Single ? "『" : "「";
            output[q.End] = q.Single ? "』" : "」";
        }
        // Remove ASR-added spaces at Chinese punctuation boundaries; keep
        // English word spacing, line breaks, and numeric/code token contents.
        var result = new StringBuilder();
        var index = 0;
        while (index < output.Count)
        {
            result.Append(output[index]);
            var next = index + 1;
            if (!protectedChars[index] && output[index].Length == 1 && "，。！？；：）］」』".Contains(output[index]))
            {
                while (next < output.Count && (output[next] == " " || output[next] == "\t")) next++;
                if (next < output.Count && (Text.IsHan(output[next]) || (output[next].Length == 1 && "（［「『".Contains(output[next]))))
                {
                    index = next; continue;
                }
            }
            index++;
        }
        return result.ToString();
    }
}
