using HydayTalk.Core;
using NAudio.Wave;

namespace HydayTalk;

/// Records the microphone straight to 16 kHz mono 16-bit WAV, the format whisper.cpp,
/// transcribe.cpp and transcription APIs all accept, so no conversion step is needed.
internal sealed class AudioRecorder : IDisposable
{
    private readonly object gate = new();
    private WaveInEvent? input;
    private WaveFileWriter? writer;
    private TaskCompletionSource<string>? stopped;
    private string? path;

    /// 0–1 loudness, raised on the audio thread.
    public event Action<double>? Level;
    public event Action<Exception>? Failed;

    public static string TemporaryDirectory => Path.Combine(Path.GetTempPath(), "HydayTalk");

    public static IReadOnlyList<string> Devices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToList();

    /// Removes recordings left behind by a crash.
    public static void CleanAbandonedAudio()
    {
        try { if (Directory.Exists(TemporaryDirectory)) Directory.Delete(TemporaryDirectory, recursive: true); } catch { }
    }

    public void Start(int? device)
    {
        Cancel();
        if (WaveInEvent.DeviceCount == 0) throw new TalkException("找不到麥克風，請確認裝置已連接，並在 Windows 隱私權設定允許麥克風。");
        var folder = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        path = Path.Combine(folder, "dictation.wav");
        var format = new WaveFormat(16000, 16, 1);
        var deviceNumber = device is { } d && d >= 0 && d < WaveInEvent.DeviceCount ? d : 0;
        var wave = new WaveInEvent { WaveFormat = format, DeviceNumber = deviceNumber, BufferMilliseconds = 50 };
        writer = new WaveFileWriter(path, format);
        stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        wave.DataAvailable += (_, e) =>
        {
            lock (gate) writer?.Write(e.Buffer, 0, e.BytesRecorded);
            double sum = 0; var count = e.BytesRecorded / 2;
            for (var i = 0; i < e.BytesRecorded - 1; i += 2)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i) / 32768.0;
                sum += sample * sample;
            }
            var rms = count > 0 ? Math.Sqrt(sum / count) : 0;
            Level?.Invoke(Math.Clamp(rms * 4, 0, 1));
        };
        wave.RecordingStopped += (_, e) =>
        {
            lock (gate) { writer?.Dispose(); writer = null; }
            if (e.Exception != null) { Failed?.Invoke(e.Exception); stopped?.TrySetException(e.Exception); }
            else stopped?.TrySetResult(path!);
        };
        input = wave;
        try { wave.StartRecording(); }
        catch (Exception)
        {
            Cancel();
            throw new TalkException("無法開始錄音。請在 Windows 設定 > 隱私權與安全性 > 麥克風，允許桌面應用程式存取麥克風。");
        }
    }

    /// Stops recording and returns the finished WAV file.
    public async Task<string> StopAsync()
    {
        if (input == null || stopped == null) throw new TalkException("沒有錄到聲音，請再試一次。");
        var wait = stopped.Task;
        input.StopRecording();
        var file = await wait;
        input.Dispose(); input = null;
        if (new FileInfo(file).Length <= 44 + 16000 / 5) throw new TalkException("錄音太短，請再試一次。");
        return file;
    }

    /// Stops without keeping the audio and deletes it.
    public void Cancel()
    {
        try { input?.StopRecording(); } catch { }
        input?.Dispose(); input = null;
        lock (gate) { writer?.Dispose(); writer = null; }
        stopped?.TrySetCanceled(); stopped = null;
        Delete();
    }

    public void Delete()
    {
        if (path == null) return;
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { }
        path = null;
    }

    public void Dispose() => Cancel();
}
