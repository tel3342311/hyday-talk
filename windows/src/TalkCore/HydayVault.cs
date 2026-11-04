using System.Text;
using System.Text.Json.Nodes;

namespace HydayTalk.Core;

/// The user's Hyday note folder. Hyday Talk only appends to the end of daily
/// journals, which Hyday merges into an open, unsaved draft; edits anywhere else
/// would make Hyday keep its draft and later overwrite them.
public sealed class HydayVault(string root)
{
    private static readonly object WriteLock = new();
    public string Root { get; } = Path.GetFullPath(root);
    /// Test hook that runs just before the staged file replaces the journal.
    internal Action<string>? BeforeReplace { get; set; }

    /// Hyday's desktop settings: %APPDATA%\Hyday\settings.json on Windows.
    public static string DefaultSettingsFile => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hyday", "settings.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Hyday", "settings.json");

    /// Reads `vault.root`; the top-level `journalPath` key is Hyday's retired v1 setting.
    public static string? ConfiguredRoot(string? settingsFile = null)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(settingsFile ?? DefaultSettingsFile));
            var path = json?["vault"]?["root"]?.GetValue<string>();
            return path != null && Path.IsPathRooted(path) && Directory.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    /// Spoken text without AI classification becomes one plain list item.
    public static string Bullet(string text) =>
        "- " + string.Join(" ", text.Split(['\n', '\r']).Select(Text.TrimSpaces).Where(l => l.Length > 0));

    public string JournalPath(DateTime date) =>
        Path.Combine(Root, "journal", date.ToString("yyyy"), date.ToString("yyyy-MM-dd") + ".md");

    /// Appends Markdown to the end of the day's journal, creating it from the journal template when
    /// needed. The write is staged and swapped in, and retried if the file changed meanwhile.
    public string AppendToJournal(string markdown, DateTime? at = null)
    {
        var date = at ?? DateTime.Now;
        var path = JournalPath(date);
        var folder = Path.GetDirectoryName(path)!;
        var parts = Root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.GetFullPath(path).StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || parts.Contains(".hyday") || parts.Contains("hyday-wiki"))
            throw new TalkException("Hyday 資料夾路徑無效，請選擇 Hyday 筆記所在的資料夾。");
        try { Directory.CreateDirectory(folder); }
        catch { throw new TalkException("無法建立 Hyday 日誌資料夾，請確認資料夾權限。"); }
        var stamp = Timestamp(date);
        lock (WriteLock)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var original = Contents(path);
                var source = original != null ? Encoding.UTF8.GetString(original) : NewJournal(stamp);
                if (source.Length > 0 && source[0] == '﻿') source = source[1..];
                var updated = Appending(markdown, source, stamp);
                var staging = Path.Combine(folder, $".hyday-talk-{Guid.NewGuid()}.tmp");
                try
                {
                    try { File.WriteAllText(staging, updated, new UTF8Encoding(false)); }
                    catch { throw new TalkException("無法寫入 Hyday 日誌，請確認磁碟空間與資料夾權限。"); }
                    BeforeReplace?.Invoke(path);
                    // Only swap in the staged file if nobody changed the journal meanwhile.
                    var current = Contents(path);
                    if (!(current == null ? original == null : original != null && current.AsSpan().SequenceEqual(original))) continue;
                    try
                    {
                        if (original == null) File.Move(staging, path);
                        else File.Replace(staging, path, null, ignoreMetadataErrors: true);
                    }
                    catch { throw new TalkException("無法寫入 Hyday 日誌，請確認資料夾權限。"); }
                    return path;
                }
                finally { try { File.Delete(staging); } catch { } }
            }
        }
        throw new TalkException("Hyday 日誌持續被修改，請稍後再試。");
    }

    /// null only when the journal does not exist; an unreadable journal (cloud placeholder,
    /// permissions) must never be treated as empty and replaced.
    internal static byte[]? Contents(string path)
    {
        if (!File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { throw new TalkException("無法讀取今天的 Hyday 日誌，請確認檔案已下載且可以開啟。"); }
    }

    private string NewJournal(string stamp)
    {
        try { return Setting("createdAt", stamp, File.ReadAllText(Path.Combine(Root, "templates", "journal.md")).Replace("\r\n", "\n")); }
        catch { return $"---\ncreatedAt: {stamp}\nlastModified: {stamp}\n---\n"; }
    }

    internal static string Appending(string markdown, string text, string stamp)
    {
        var body = Setting("lastModified", stamp, text);
        // Follow the journal's own list marker so the new lines match.
        var lines = body.Split('\n');
        var bodyStart = 0;
        if (lines.Length > 0 && lines[0] == "---")
        {
            var end = Array.IndexOf(lines, "---", 1);
            bodyStart = end >= 0 ? end + 1 : 0;
        }
        var marker = lines.Skip(bodyStart).FirstOrDefault(l => l.StartsWith("* ") || l.StartsWith("- "))?[..1] ?? "-";
        var entry = string.Join("\n", markdown.Split('\n').Select(line =>
            marker == "*" && line.StartsWith("- ") && !line.StartsWith("- [") ? "* " + line[2..] : line)).Trim('\n', '\r');
        return body.TrimEnd('\n') + "\n\n" + entry + "\n";
    }

    /// Sets a frontmatter field, adding frontmatter or the field when missing.
    internal static string Setting(string field, string value, string text)
    {
        var lines = text.Split('\n').ToList();
        var end = lines.Count > 0 && lines[0] == "---" ? lines.IndexOf("---", 1) : -1;
        if (end < 0) return $"---\ncreatedAt: {value}\nlastModified: {value}\n---\n" + text;
        var index = lines.FindIndex(1, end - 1, l => l.StartsWith(field + ":"));
        if (index >= 0) lines[index] = $"{field}: {value}";
        else
        {
            lines.Insert(end, $"{field}: {value}");
            if (field == "createdAt" && !lines.Skip(1).Take(end).Any(l => l.StartsWith("lastModified:")))
                lines.Insert(end + 1, $"lastModified: {value}");
        }
        return string.Join("\n", lines);
    }

    /// Hyday writes `YYYY-MM-DD HH:mm:ss` in local time.
    internal static string Timestamp(DateTime date) => date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
