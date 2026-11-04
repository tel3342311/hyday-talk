using System.Text.RegularExpressions;

namespace HydayTalk.Core;

/// Text around the cursor when recording starts, given to the text AI as reference only.
/// It is never inserted or saved.
public sealed class ScreenContext
{
    public const int BeforeLimit = 800;
    public const int AfterLimit = 200;
    public static readonly string[] DefaultExcludedApps = ["1Password", "Bitwarden", "KeePass", "KeePassXC", "認證管理員", "Credential Manager"];
    private static readonly Regex TermPattern = new(@"[A-Za-z][A-Za-z0-9+#]*(?:[.-][A-Za-z0-9+#]+)*", RegexOptions.Compiled);

    public string App { get; }
    public string WindowTitle { get; }
    public string Before { get; }
    public string After { get; }

    /// `cursor` counts UTF-16 units from the start of `text`; apps without readable text pass null.
    public ScreenContext(string app, string windowTitle, string? text, int? cursor)
    {
        App = app; WindowTitle = windowTitle;
        if (text == null || cursor == null) { Before = ""; After = ""; return; }
        var index = Math.Clamp(cursor.Value, 0, text.Length);
        Before = text[..index]; After = text[index..];
        if (Before.Length > BeforeLimit) Before = Before[^BeforeLimit..];
        if (After.Length > AfterLimit) After = After[..AfterLimit];
    }

    public bool IsEmpty => App.Length == 0 && WindowTitle.Length == 0 && Before.Length == 0 && After.Length == 0;

    /// True when the output carries a stretch of screen text that the user never said,
    /// which means the AI copied or continued the screen instead of only borrowing spellings.
    public bool Leaked(string output, string transcript, int window = 10)
    {
        var source = Text.Elements(Letters(Before + After)); var said = Letters(transcript); var written = Letters(output);
        if (source.Count < window) return false;
        for (var start = 0; start + window <= source.Count; start++)
        {
            var piece = string.Concat(source.Skip(start).Take(window));
            if (written.Contains(piece, StringComparison.Ordinal) && !said.Contains(piece, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string Letters(string text) => string.Concat(Text.Elements(text).Where(e => char.IsLetterOrDigit(e, 0)));

    /// Capitalised English words, acronyms and product names near the cursor. Small models copy
    /// whole screen text into their output, so only these spellings are handed to the AI.
    public List<string> Terms
    {
        get
        {
            var seen = new HashSet<string>(); var result = new List<string>();
            foreach (Match match in TermPattern.Matches(WindowTitle + " " + Before + " " + After))
            {
                var word = match.Value;
                if (word.Length < 2 || !word.Any(char.IsUpper) || !seen.Add(word.ToLowerInvariant())) continue;
                result.Add(word);
                if (result.Count == 40) break;
            }
            return result;
        }
    }

    internal SortedDictionary<string, object> Payload => new(StringComparer.Ordinal) { ["app"] = App, ["terms"] = Terms };
}
