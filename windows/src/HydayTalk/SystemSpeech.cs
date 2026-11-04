using System.Text;
using HydayTalk.Core;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace HydayTalk;

/// Windows speech recognition (Windows.Media.SpeechRecognition) listening to the microphone directly,
/// the counterpart of Apple Speech on macOS. It writes no audio file.
internal sealed class SystemSpeech : IDisposable
{
    private const int PrivacyNotAccepted = unchecked((int)0x80045509);
    private SpeechRecognizer? recognizer;
    private readonly StringBuilder final = new();
    private string hypothesis = "";
    private TaskCompletionSource? completed;

    /// Raised on a background thread with everything heard so far.
    public event Action<string>? Transcript;
    public bool UsedPartialTranscript { get; private set; }

    public static bool Supports(string locale)
    {
        try { return SpeechRecognizer.SupportedTopicLanguages.Any(l => string.Equals(l.LanguageTag, locale, StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    public async Task StartAsync(string locale)
    {
        Dispose();
        final.Clear(); hypothesis = ""; UsedPartialTranscript = false;
        if (!Supports(locale))
            throw new TalkException("Windows 沒有這個語言的語音辨識，請到設定 > 時間與語言 > 語音新增語言，或改用本機模型。");
        var engine = new SpeechRecognizer(new Language(locale));
        recognizer = engine;
        engine.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        try
        {
            var compiled = await engine.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
                throw new TalkException("Windows 語音辨識無法啟動，請確認已開啟線上語音辨識，或改用本機模型。");
            engine.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(3);
            engine.HypothesisGenerated += (_, e) => { hypothesis = e.Hypothesis.Text; Transcript?.Invoke(Current); };
            engine.ContinuousRecognitionSession.ResultGenerated += (_, e) =>
            {
                if (e.Result.Status == SpeechRecognitionResultStatus.Success && e.Result.Confidence != SpeechRecognitionConfidence.Rejected)
                    final.Append(e.Result.Text);
                hypothesis = "";
                Transcript?.Invoke(Current);
            };
            completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.ContinuousRecognitionSession.Completed += (_, _) => completed?.TrySetResult();
            await engine.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception error) when (error.HResult == PrivacyNotAccepted)
        {
            throw new TalkException("請到 Windows 設定 > 隱私權與安全性 > 語音，開啟線上語音辨識，或改用本機模型。");
        }
        catch (Exception error) when (error is not TalkException)
        {
            throw new TalkException("Windows 語音辨識無法啟動：" + error.Message);
        }
    }

    private string Current => final.ToString() + hypothesis;

    /// Stops listening and returns the final text, or the last partial result if none was finalised.
    public async Task<string> StopAsync()
    {
        if (recognizer == null) return "";
        try
        {
            await recognizer.ContinuousRecognitionSession.StopAsync();
            if (completed != null) await Task.WhenAny(completed.Task, Task.Delay(3000));
        }
        catch { }
        var text = final.ToString();
        if (text.Trim().Length == 0 && hypothesis.Trim().Length > 0) { text = hypothesis; UsedPartialTranscript = true; }
        Dispose();
        return text;
    }

    public void Dispose()
    {
        var engine = recognizer; recognizer = null;
        if (engine == null) return;
        try { _ = engine.ContinuousRecognitionSession.CancelAsync(); } catch { }
        engine.Dispose();
    }
}
