using System.Diagnostics;
using System.Text;

namespace HydayTalk.Core;

public static class ProcessRunner
{
    /// Receives the command lines run, for the app's troubleshooting log.
    public static Action<string>? Diagnostics { get; set; }

    /// Runs a tool without a shell and returns its standard output; the whole process tree is
    /// stopped on cancellation or timeout so wrapper children do not linger.
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, string? input = null,
        TimeSpan? timeout = null, CancellationToken cancellation = default)
    {
        var path = Environment.ExpandEnvironmentVariables(executable);
        if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new TalkException("找不到可執行的 CLI。請選擇工具的完整路徑。");
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Diagnostics?.Invoke($"run {Path.GetFileName(path)} {string.Join(' ', info.ArgumentList.Select(a => a.Contains(' ') ? '"' + a + '"' : a))}");
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch { throw new TalkException("無法啟動 CLI。請確認工具可執行，且相依環境已安裝。"); }
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timer.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timer.Token);
            var error = process.StandardError.ReadToEndAsync(timer.Token);
            if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), timer.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timer.Token);
            var text = await output; await error;
            if (process.ExitCode != 0) throw new TalkException($"CLI 執行失敗（代碼 {process.ExitCode}）。");
            return text.Trim();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (cancellation.IsCancellationRequested) throw;
            throw new TalkException("CLI 執行逾時。");
        }
    }
}
