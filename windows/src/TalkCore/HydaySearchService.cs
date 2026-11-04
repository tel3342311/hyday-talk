using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace HydayTalk.Core;

/// Searches through the `hyday-kb` MCP server bundled in Hyday: Hyday's Electron binary runs as
/// Node over stdio with a throwaway data folder, and only the read-only `search_notes` tool is called.
/// Falls back to the built-in keyword search when Hyday is missing or fails.
public static class HydaySearchService
{
    /// Hyday installed per user (the default for its Windows installer) or for all users.
    public static (string Executable, string Bundle)? Installation()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Hyday"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hyday"),
        };
        foreach (var folder in candidates)
        {
            var executable = Path.Combine(folder, "Hyday.exe");
            var bundle = Path.Combine(folder, "resources", "app.asar.unpacked", "electron", "dist", "hyday-mcp.cjs");
            if (File.Exists(executable) && File.Exists(bundle)) return (executable, bundle);
        }
        return null;
    }

    public static async Task<List<HydayNote>> NotesAsync(string root, string query, int limit = 5, CancellationToken cancellation = default)
    {
        try
        {
            var notes = await SearchAsync(root, query, TimeSpan.FromSeconds(6), cancellation);
            if (notes.Count > 0) return notes.Take(limit).ToList();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch { }
        return await Task.Run(() => HydayNoteSearch.Search(root, query, limit, cancellation), cancellation);
    }

    public static async Task<List<HydayNote>> SearchAsync(string root, string query, TimeSpan timeout, CancellationToken cancellation)
    {
        if (Installation() is not var (executable, bundle)) return [];
        var runtime = Path.Combine(Path.GetTempPath(), "hyday-talk-mcp-" + Guid.NewGuid());
        Directory.CreateDirectory(runtime);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runtime,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
        };
        info.ArgumentList.Add(bundle); info.ArgumentList.Add(root);
        info.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        info.Environment["HYDAY_USER_DATA"] = runtime;
        using var process = Process.Start(info) ?? throw new TalkException("無法啟動 Hyday 搜尋。");
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timer.CancelAfter(timeout);
        try
        {
            _ = process.StandardError.ReadToEndAsync(timer.Token);
            // The server answers in order, so initialize and the search can go out together.
            var messages = new[]
            {
                new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize", ["params"] = new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05", ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject { ["name"] = "hyday-talk", ["version"] = "1" },
                } },
                new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized", ["params"] = new JsonObject() },
                new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/call", ["params"] = new JsonObject
                {
                    ["name"] = "search_notes", ["arguments"] = new JsonObject { ["query"] = query },
                } },
            };
            foreach (var message in messages) await process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), timer.Token);
            await process.StandardInput.FlushAsync(timer.Token);
            while (await process.StandardOutput.ReadLineAsync(timer.Token) is { } line)
            {
                JsonNode? message;
                try { message = JsonNode.Parse(line); } catch { continue; }
                if (message?["id"]?.GetValue<int>() != 2) continue;
                if (message["error"] != null || message["result"]?["isError"]?.GetValue<bool>() == true) return [];
                var text = string.Join("\n", message["result"]?["content"]?.AsArray()
                    .Where(b => b?["type"]?.GetValue<string>() == "text").Select(b => b?["text"]?.GetValue<string>()) ?? []);
                return ParseHits(text);
            }
            return [];
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { Directory.Delete(runtime, true); } catch { }
        }
    }

    /// The search tool answers with a JSON array of hits; anything outside the user's own notes is dropped.
    public static List<HydayNote> ParseHits(string text)
    {
        JsonArray? hits;
        try { hits = JsonNode.Parse(text) as JsonArray; } catch { return []; }
        var result = new List<HydayNote>();
        foreach (var hit in hits ?? [])
        {
            var path = hit?["relPath"]?.GetValue<string>()?.Replace('\\', '/');
            if (path == null || !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.StartsWith('/') || Path.IsPathRooted(path)
                || path.Split('/').Any(p => p is "hyday-wiki" or ".." || p.StartsWith('.'))) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            var lines = hit?["lines"]?.AsArray().Select(l => l?.GetValue<string>()).Where(l => l != null);
            var excerpt = hit?["excerpt"]?.GetValue<string>();
            var snippet = !string.IsNullOrEmpty(excerpt) ? excerpt : lines != null ? string.Join(" ", lines) : "";
            var title = hit?["title"]?.GetValue<string>();
            result.Add(new HydayNote(string.IsNullOrEmpty(title) ? name : title, path, snippet.Length > 240 ? snippet[..240] : snippet));
        }
        return result;
    }
}
