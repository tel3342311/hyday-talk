using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using HydayTalk.Core;

namespace HydayTalk;

internal enum UpdateState { Idle, Available, Downloading, Ready, Installing, Failed }

/// Checks GitHub releases for a newer Windows zip, downloads it, and swaps it in after the app exits.
internal sealed class AppUpdater : INotifyPropertyChanged
{
    private DateTime? lastCheck;
    private CancellationTokenSource? download;
    private string? package;

    public event PropertyChangedEventHandler? PropertyChanged;
    public AppUpdate? Update { get; private set; }
    public UpdateState State { get; private set; }
    public double Progress { get; private set; }
    public string Message { get; private set; } = "";

    public static string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0";

    /// Checks at most every six hours; failures stay silent, since this is only a hint.
    public async void CheckIfDue(bool force = false)
    {
        if (!force && lastCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(6)) return;
        if (download != null) return;
        lastCheck = DateTime.Now;
        var found = await AppUpdate.CheckAsync(CurrentVersion);
        if (found != null && found != Update) { Update = found; Set(UpdateState.Available, ""); }
        else if (force && found == null) Set(State, "目前已是最新版本。");
    }

    public async void StartDownload()
    {
        if (Update is not { } update || download != null) return;
        download = new CancellationTokenSource();
        Progress = 0; Set(UpdateState.Downloading, "");
        try
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            var destination = Path.Combine(downloads, $"Hyday-Talk-{update.Version}-win-x64.zip");
            using (var response = await APIClient.Shared.GetAsync(update.Package, HttpCompletionOption.ResponseHeadersRead, download.Token))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var input = await response.Content.ReadAsStreamAsync(download.Token);
                await using var output = File.Create(destination + ".part");
                var buffer = new byte[1 << 16]; long read = 0; int count;
                while ((count = await input.ReadAsync(buffer, download.Token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), download.Token);
                    read += count;
                    if (total > 0) { Progress = (double)read / total; Notify(nameof(Progress)); }
                }
            }
            File.Move(destination + ".part", destination, true);
            package = destination;
            Set(UpdateState.Ready, "");
        }
        catch (OperationCanceledException) { Set(UpdateState.Available, ""); }
        catch { Set(UpdateState.Failed, "更新下載失敗，請重試。"); }
        finally { download = null; }
    }

    public void CancelDownload() => download?.Cancel();

    /// False when the app sits somewhere this user cannot write, so the zip has to be unpacked by hand.
    public static bool CanReplace
    {
        get
        {
            try
            {
                var probe = Path.Combine(AppContext.BaseDirectory, ".write-test-" + Guid.NewGuid());
                File.WriteAllText(probe, ""); File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }

    public void ShowPackage()
    {
        if (package != null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{package}\"") { UseShellExecute = true });
    }

    /// Unpacks the new version, checks it is Hyday Talk and newer, then lets a helper script copy it
    /// over this folder once the app has exited and start it again.
    public void RestartToUpdate(Action shutdown)
    {
        if (package == null || Update == null) return;
        Set(UpdateState.Installing, "");
        try
        {
            var staging = Path.Combine(Path.GetTempPath(), "HydayTalkUpdate", Update.Version);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(package, staging);
            var exe = Path.Combine(staging, "HydayTalk.exe");
            if (!File.Exists(exe)) throw new TalkException("更新檔內容不正確，請改用手動安裝。");
            var info = FileVersionInfo.GetVersionInfo(exe);
            if (info.ProductName != "Hyday Talk" || !AppUpdate.IsNewer($"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}", CurrentVersion))
                throw new TalkException("更新檔版本不正確，請改用手動安裝。");
            var target = AppContext.BaseDirectory.TrimEnd('\\');
            var script = Path.Combine(Path.GetTempPath(), "HydayTalkUpdate", "apply.ps1");
            File.WriteAllText(script, $$"""
                $ErrorActionPreference = 'Stop'
                Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 500
                Copy-Item -Path '{{staging.Replace("'", "''")}}\*' -Destination '{{target.Replace("'", "''")}}' -Recurse -Force
                Start-Process -FilePath '{{Path.Combine(target, "HydayTalk.exe").Replace("'", "''")}}'
                """);
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true });
            shutdown();
        }
        catch (Exception error)
        {
            Set(UpdateState.Failed, error is TalkException ? error.Message : "無法自動更新，請改用手動安裝。");
        }
    }

    private void Set(UpdateState state, string message)
    {
        State = state; Message = message;
        Notify(nameof(State)); Notify(nameof(Message)); Notify(nameof(Update));
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
