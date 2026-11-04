using System.Text;
using System.Text.RegularExpressions;

namespace HydayTalk.Core;

public sealed record HydayNote(string Title, string Path, string Snippet);

/// Markdown notes the user wrote, skipping Hyday's own index, generated wiki and hidden folders.
internal static class HydayNoteFiles
{
    public const int MaxFiles = 5000;
    public const int MaxBytes = 1_000_000;

    public static List<(string Path, string Text)> List(string root, CancellationToken cancellation = default)
    {
        var baseDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var files = new List<(string, string)>();
        var pending = new Stack<string>(); pending.Push(baseDir);
        while (pending.Count > 0 && files.Count < MaxFiles)
        {
            cancellation.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); } catch { continue; }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.')) continue;
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); } catch { continue; }
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    // Junctions could point outside the folder.
                    if (name != "hyday-wiki" && (attributes & FileAttributes.ReparsePoint) == 0) pending.Push(entry);
                    continue;
                }
                if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                try { if (new FileInfo(entry).Length > MaxBytes) continue; } catch { continue; }
                files.Add((entry[baseDir.Length..].Replace('\\', '/'), entry));
                if (files.Count >= MaxFiles) break;
            }
        }
        var result = new List<(string, string)>();
        foreach (var (relative, full) in files.OrderBy(f => f.Item1, StringComparer.Ordinal))
            try { result.Add((relative, File.ReadAllText(full, Encoding.UTF8).Replace("\r\n", "\n"))); } catch { }
        return result;
    }

    /// Frontmatter `title`, otherwise the file name.
    public static (string Title, string Body) Split(string text, string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var lines = text.Split('\n');
        if (lines.Length == 0 || Text.TrimSpaces(lines[0]) != "---") return (name, text);
        var end = Array.FindIndex(lines, 1, l => Text.TrimSpaces(l) == "---");
        if (end < 0) return (name, text);
        var title = lines.Skip(1).Take(end - 1).FirstOrDefault(l => l.StartsWith("title:"))?[6..].Trim(' ', '\t', '"', '\'');
        return (string.IsNullOrEmpty(title) ? name : title, string.Join("\n", lines.Skip(end + 1)));
    }
}

/// Keyword search used when Hyday's own search service is unavailable.
public static class HydayNoteSearch
{
    private static bool IsCJK(int v) => (v >= 0x3400 && v <= 0x4DBF) || (v >= 0x4E00 && v <= 0x9FFF) || (v >= 0xF900 && v <= 0xFAFF)
        || (v >= 0x20000 && v <= 0x323AF) || (v >= 0x3040 && v <= 0x30FF) || (v >= 0xAC00 && v <= 0xD7AF) || v == 0x3007;

    /// Words split on spaces and punctuation; CJK runs, which have no spaces, become overlapping pairs.
    public static List<string> Terms(string query)
    {
        var result = new List<string>();
        void Add(string term) { if (term.Length > 0 && !result.Contains(term)) result.Add(term); }
        var words = Regex.Split(query.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0);
        foreach (var word in words)
        {
            var run = new List<string>(); var runIsCJK = false;
            void Flush()
            {
                if (runIsCJK)
                {
                    if (run.Count == 1) Add(run[0]);
                    else for (var i = 0; i < run.Count - 1; i++) Add(run[i] + run[i + 1]);
                }
                else Add(string.Concat(run));
                run.Clear();
            }
            foreach (var element in Text.Elements(word))
            {
                var cjk = IsCJK(char.ConvertToUtf32(element, 0));
                if (run.Count > 0 && cjk != runIsCJK) Flush();
                runIsCJK = cjk; run.Add(element);
            }
            if (run.Count > 0) Flush();
        }
        return result;
    }

    public static List<HydayNote> Search(string root, string query, int limit = 5, CancellationToken cancellation = default)
    {
        var terms = Terms(query).Take(40).ToList();
        if (terms.Count == 0) return [];
        var scored = new List<(int Score, HydayNote Note)>();
        foreach (var (path, text) in HydayNoteFiles.List(root, cancellation))
        {
            cancellation.ThrowIfCancellationRequested();
            var (title, body) = HydayNoteFiles.Split(text, path);
            var lowered = body.ToLowerInvariant(); var loweredTitle = title.ToLowerInvariant();
            var score = 0; var first = -1;
            foreach (var term in terms)
            {
                var count = Math.Min(Occurrences(lowered, term), 5);
                var inTitle = loweredTitle.Contains(term, StringComparison.Ordinal);
                if (count == 0 && !inTitle) continue;
                score += 10 + count + (inTitle ? 5 : 0);
                var at = lowered.IndexOf(term, StringComparison.Ordinal);
                if (at >= 0 && (first < 0 || at < first)) first = at;
            }
            if (score > 0) scored.Add((score, new HydayNote(title, path, Snippet(body, Math.Max(first, 0)))));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Note.Path, StringComparer.Ordinal).Take(limit).Select(s => s.Note).ToList();
    }

    private static int Occurrences(string text, string term)
    {
        var count = 0; var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.Ordinal)) >= 0) { count++; index += term.Length; }
        return count;
    }

    // ToLowerInvariant keeps UTF-16 lengths, so the offset maps straight back to the body.
    private static string Snippet(string body, int offset)
    {
        var start = Math.Max(0, Math.Min(offset, body.Length) - 60);
        var end = Math.Min(body.Length, start + 220);
        if (end < body.Length && char.IsHighSurrogate(body[end - 1])) end--;
        if (start > 0 && char.IsLowSurrogate(body[start])) start++;
        var text = string.Join(" ", body[start..end].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (start > 0) text = "…" + text;
        if (end < body.Length) text += "…";
        return text;
    }
}

/// `@(Name)` entities and `#tags` from the user's notes, used only as recognition hints.
public static class HydayVocabulary
{
    private static readonly Regex Pattern = new(@"(?<!\S)(?:@\(([^()\n]+)\)|@\{([^{}\n]+)\}|#(\p{L}[\p{L}\p{N}_/\-]*))", RegexOptions.Compiled);

    public static List<string> Terms(string text)
    {
        var result = new List<string>(); var fenced = false;
        foreach (var line in text.Split('\n'))
        {
            if (Text.TrimSpaces(line).StartsWith("```")) { fenced = !fenced; continue; }
            if (fenced) continue;
            var plain = Regex.Replace(line, "`[^`]*`", " ");
            foreach (Match match in Pattern.Matches(plain))
                for (var group = 1; group <= 3; group++)
                {
                    if (!match.Groups[group].Success) continue;
                    var term = match.Groups[group].Value.Trim(' ', '\t', '/');
                    if (term.Length > 0 && !result.Contains(term)) result.Add(term);
                }
        }
        return result;
    }

    public static List<string> Scan(string root, int limit = 2000, CancellationToken cancellation = default)
    {
        var result = new List<string>(); var seen = new HashSet<string>();
        foreach (var (_, text) in HydayNoteFiles.List(root, cancellation))
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var term in Terms(text))
            {
                if (!seen.Add(term)) continue;
                result.Add(term);
                if (result.Count >= limit) return result;
            }
        }
        return result;
    }
}
