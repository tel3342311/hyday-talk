using System.ComponentModel;
using System.Media;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using HydayTalk.Core;

namespace HydayTalk;

internal enum CapturePhase { Idle, Preparing, Recording, Transcribing, Processing, Done, Failed }

internal static class CapturePhases
{
    public static bool IsBusy(this CapturePhase phase) =>
        phase is CapturePhase.Preparing or CapturePhase.Recording or CapturePhase.Transcribing or CapturePhase.Processing;

    public static string Title(this CapturePhase phase) => phase switch
    {
        CapturePhase.Idle => "等待語音輸入",
        CapturePhase.Preparing => "正在準備麥克風",
        CapturePhase.Recording => "正在錄音",
        CapturePhase.Transcribing => "正在辨識語音",
        CapturePhase.Processing => "正在整理文字",
        CapturePhase.Done => "文字處理完成",
        _ => "處理失敗",
    };
}

/// The Windows counterpart of the macOS AppModel: owns settings and history and runs one
/// dictation at a time from the shortcut through recognition, clean-up and insertion.
internal sealed class AppController : INotifyPropertyChanged, IDisposable
{
    private const int HistoryLimit = 200;
    private readonly Dispatcher dispatcher;
    private readonly JsonStore store = new(JsonStore.DefaultDirectory);
    private readonly AudioRecorder audio = new();
    private readonly TextPipeline pipeline;
    private readonly DispatcherTimer recordingTimer;
    private readonly DispatcherTimer saveTimer;
    private ClaudeSession? claudeSession;
    private CancellationTokenSource? operation;
    private DateTime started;
    private InputTarget? target;
    private TalkSettings sessionSettings = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    /// Raised when the floating panel should appear, update or hide.
    public event Action? PanelChanged;

    public TalkSettings Settings { get; private set; }
    public List<DictationRecord> History { get; private set; }

    private CapturePhase phase;
    public CapturePhase Phase { get => phase; private set { phase = value; Notify(); Notify(nameof(PhaseTitle)); PanelChanged?.Invoke(); } }
    public string PhaseTitle => Phase.Title();
    private VoiceAction action;
    public VoiceAction Action { get => action; private set { action = value; Notify(); } }
    private string transcript = "", result = "", notice = "", destination = "";
    public string Transcript { get => transcript; private set { transcript = value; Notify(); } }
    public string Result { get => result; private set { result = value; Notify(); } }
    public string Notice { get => notice; set { notice = value; Notify(); } }
    public string Destination { get => destination; private set { destination = value; Notify(); } }
    private double level, elapsed;
    public double Level { get => level; private set { level = value; Notify(); } }
    public double Elapsed { get => elapsed; private set { elapsed = value; Notify(); } }
    private bool shortcutReady;
    public bool ShortcutReady { get => shortcutReady; set { shortcutReady = value; Notify(); } }

    public string ModelDirectory => Path.Combine(store.Directory, "Models");

    public AppController(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        TextProcessing.ToTraditional = Native.ScriptConverter.ToTraditional;
        try { Settings = store.Load<TalkSettings>("settings.json") ?? new TalkSettings(); }
        catch { Settings = new TalkSettings(); notice = "無法讀取本機設定，原檔案已備份。請檢查 %APPDATA%\\HydayTalk。"; }
        try { History = store.Load<List<DictationRecord>>("history.json") ?? []; }
        catch { History = []; notice += " 無法讀取本機紀錄，原檔案已備份。"; }
        Settings.DictationLayout = null; // 段落與列表改由自動整理混合排版
        if (!SpeechBackends.Available.Contains(Settings.SpeechBackend)) Settings.SpeechBackend = SpeechBackend.Whisper;
        if (!TextBackends.Available.Contains(Settings.TextBackend)) Settings.TextBackend = TextBackend.Basic;
        if (Settings.WhisperExecutable.Length == 0 || !File.Exists(Settings.WhisperExecutable))
            Settings.WhisperExecutable = BundledEngine ?? Settings.WhisperExecutable;
        if (Settings.WhisperModel.Length == 0)
            Settings.WhisperModel = LocalSpeechModel.All.Select(m => Path.Combine(ModelDirectory, m.Filename)).FirstOrDefault(File.Exists) ?? "";

        pipeline = new TextPipeline(KeyStore.Read, Claude);
        audio.Level += value => dispatcher.BeginInvoke(() => { if (Phase == CapturePhase.Recording) Level = value; });
        audio.Failed += error => dispatcher.BeginInvoke(() =>
        {
            if (Phase != CapturePhase.Recording) return;
            Notice = "錄音中斷：" + error.Message; Finish();
        });
        recordingTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) => Tick(), dispatcher);
        recordingTimer.Stop();
        saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => { saveTimer!.Stop(); SaveSettings(); }, dispatcher);
        saveTimer.Stop();
        AudioRecorder.CleanAbandonedAudio();
    }

    /// whisper-cli.exe shipped in the app's runtime folder by the build script.
    public static string? BundledEngine
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "runtime", "whisper-cli.exe");
            return File.Exists(path) ? path : null;
        }
    }

    /// Called by the settings window after any change; saves shortly after the last edit.
    public void SettingsChanged() { saveTimer.Stop(); saveTimer.Start(); Notify(nameof(Settings)); }

    private ClaudeSession? Claude()
    {
        if (claudeSession != null) return claudeSession;
        if (ClaudeSession.DiscoverExecutable() is not { } executable) return null;
        return claudeSession = new ClaudeSession(executable, Path.Combine(store.Directory, "Claude"));
    }

    public void Handle(ShortcutAction shortcut)
    {
        if (shortcut.Kind == ShortcutKind.Cancel) { Cancel(); return; }
        if (Phase == CapturePhase.Recording) Finish();
        else if (!Phase.IsBusy()) Begin(shortcut.Action);
    }

    public void Begin(VoiceAction voiceAction)
    {
        if (Phase.IsBusy()) return;
        var snapshot = Settings.Snapshot();
        Action = voiceAction; Result = ""; Transcript = ""; Notice = ""; Elapsed = 0; Level = 0;
        try { pipeline.Validate(snapshot, voiceAction); }
        catch (TalkException error) { Fail(error); return; }
        // Start Claude Code while the user is still speaking, so formatting skips the CLI start-up.
        if (snapshot.TextBackend == TextBackend.Claude && snapshot.RequiresTextModel(voiceAction) && Claude() is { } session)
            Task.Run(() => session.Prepare(snapshot.ClaudeModel));
        target = TextInsertion.Capture();
        if (target?.Secure == true) { Fail(new TalkException("這是密碼欄位。請改在一般文字輸入框使用。")); return; }
        sessionSettings = snapshot;
        Destination = snapshot.AutoInsert && voiceAction != VoiceAction.Ask && target?.Editable == true ? target.Name
            : voiceAction == VoiceAction.Ask ? "結果區" : "剪貼簿";
        Phase = CapturePhase.Preparing;
        try { audio.Start(snapshot.MicrophoneDevice); }
        catch (TalkException error) { Fail(error); return; }
        started = DateTime.Now; Phase = CapturePhase.Recording;
        if (snapshot.InteractionSounds) SystemSounds.Asterisk.Play();
        recordingTimer.Start();
    }

    private void Tick()
    {
        if (Phase != CapturePhase.Recording) { recordingTimer.Stop(); return; }
        Elapsed = (DateTime.Now - started).TotalSeconds;
        if (Elapsed >= 180) { Notice = "已達單次錄音上限（180 秒），正在完成辨識。"; Finish(); }
    }

    public async void Finish()
    {
        if (Phase != CapturePhase.Recording) return;
        recordingTimer.Stop();
        var duration = (DateTime.Now - started).TotalSeconds; Elapsed = duration; Level = 0;
        var snapshot = sessionSettings; var voiceAction = Action; var inputTarget = target;
        operation?.Cancel(); var cancellation = new CancellationTokenSource(); operation = cancellation;
        var token = cancellation.Token;
        Phase = CapturePhase.Transcribing;
        if (snapshot.InteractionSounds) SystemSounds.Exclamation.Play();
        try
        {
            var file = await audio.StopAsync();
            var hints = RecognitionHints.Terms(snapshot.Dictionary, []);
            var key = snapshot.SpeechBackend == SpeechBackend.Api ? KeyStore.Read("speech") : "";
            var raw = await Task.Run(() => SpeechTranscription.TranscribeAsync(file, snapshot, key, hints, cancellation: token), token);
            if (RecognitionHints.IsEcho(raw, hints))
            {
                // The model recited the hint list instead of the speech; listen again without it.
                raw = await Task.Run(() => SpeechTranscription.TranscribeAsync(file, snapshot, key, [], cancellation: token), token);
                if (RecognitionHints.IsEcho(raw, hints)) throw new TalkException("沒有辨識到文字，請再試一次。");
            }
            audio.Delete();
            token.ThrowIfCancellationRequested();
            Transcript = raw;
            if (snapshot.RequiresTextModel(voiceAction) && snapshot.TextBackend != TextBackend.Basic) Phase = CapturePhase.Processing;
            var processed = await Task.Run(() => pipeline.RunAsync(raw, voiceAction, inputTarget?.Selection ?? "", snapshot, token), token);
            token.ThrowIfCancellationRequested();
            Result = processed.Text; Notice = processed.Notice;
            var delivered = false;
            if (snapshot.AutoInsert && voiceAction != VoiceAction.Ask)
            {
                try
                {
                    var message = await TextInsertion.InsertAsync(processed.Text, inputTarget);
                    Notice = string.IsNullOrEmpty(Notice) ? message : Notice + " " + message;
                    delivered = inputTarget?.Editable == true;
                }
                catch (TalkException error) { Notice = string.IsNullOrEmpty(Notice) ? error.Message : Notice + " " + error.Message; }
            }
            else if (string.IsNullOrEmpty(Notice)) Notice = "文字已保留，可複製到需要的地方。";
            if (snapshot.SaveHistory)
            {
                History.Insert(0, new DictationRecord
                {
                    RawText = raw, Text = processed.Text, Action = voiceAction, Backend = snapshot.SpeechBackend,
                    Duration = duration, Destination = voiceAction == VoiceAction.Ask ? "結果區" : inputTarget?.Name ?? "剪貼簿",
                });
                if (History.Count > HistoryLimit) History.RemoveRange(HistoryLimit, History.Count - HistoryLimit);
                SaveHistory();
            }
            // Insertion finished: hide the panel; otherwise keep it up with the text to copy.
            Phase = delivered ? CapturePhase.Idle : CapturePhase.Done;
        }
        catch (OperationCanceledException) { audio.Delete(); }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            audio.Delete();
            Fail(error is TalkException ? error : new TalkException("處理失敗：" + error.Message));
        }
    }

    public void Cancel()
    {
        operation?.Cancel(); operation = null;
        recordingTimer.Stop(); audio.Cancel();
        Level = 0; Elapsed = 0; Transcript = "";
        Phase = CapturePhase.Idle;
    }

    public void DismissPanel() { if (Phase.IsBusy()) Cancel(); else Phase = CapturePhase.Idle; }

    public void CopyResult()
    {
        if (Result.Length == 0) return;
        try { TextInsertion.Copy(Result); Notice = "已複製文字。"; } catch (TalkException e) { Notice = e.Message; }
    }

    public void ClearHistory() { History.Clear(); SaveHistory(); Notify(nameof(History)); }

    private void Fail(Exception error)
    {
        recordingTimer.Stop(); audio.Cancel();
        Notice = error.Message; Level = 0; Phase = CapturePhase.Failed;
    }

    public void SaveSettings()
    {
        try { store.Save(Settings, "settings.json"); }
        catch { Notice = "設定未儲存。請確認磁碟空間與資料夾權限。"; }
    }

    private void SaveHistory()
    {
        try { store.Save(History, "history.json"); }
        catch { Notice += " 紀錄未儲存，請確認磁碟空間。"; }
        Notify(nameof(History));
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        Cancel(); saveTimer.Stop(); SaveSettings();
        claudeSession?.Dispose(); audio.Dispose();
    }
}
