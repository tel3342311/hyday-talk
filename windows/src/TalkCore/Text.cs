using System.Globalization;

namespace HydayTalk.Core;

/// Swift strings count user-perceived characters; these helpers keep the ported rules counting the same way.
internal static class Text
{
    public static int Length(string text) => new StringInfo(text).LengthInTextElements;

    public static List<string> Elements(string text)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) result.Add((string)enumerator.Current);
        return result;
    }

    public static bool IsHan(string element)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            var v = rune.Value;
            if ((v >= 0x3400 && v <= 0x4DBF) || (v >= 0x4E00 && v <= 0x9FFF)
                || (v >= 0xF900 && v <= 0xFAFF) || (v >= 0x20000 && v <= 0x323AF)) return true;
        }
        return false;
    }

    public static bool IsNewline(string element) => element is "\n" or "\r" or "\r\n" or "\u2028" or "\u2029" or "\u0085" or "\u000B" or "\u000C";
    public static bool IsNumber(string element) => element.Length > 0 && char.IsNumber(element, 0);
    public static bool IsLetter(string element) => element.Length > 0 && char.IsLetter(element, 0);
    public static bool IsWhitespace(string element) => element.Length > 0 && char.IsWhiteSpace(element, 0);

    /// Swift's trimmingCharacters(in: .whitespacesAndNewlines).
    public static string Trim(string text) => text.Trim();
    /// Swift's trimmingCharacters(in: .whitespaces): spaces and tabs, not line breaks.
    public static string TrimSpaces(string text) => text.Trim(' ', '\t', ' ', '　');
}
