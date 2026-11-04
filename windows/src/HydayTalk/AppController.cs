using System.ComponentModel;
using System.Media;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using HydayTalk.Core;
using static HydayTalk.Native.Win32;

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

/// The Windows counterpart of the macOS AppModel: owns settings, history and usage, and runs one
/// dictation at a time from the shortcut through recognition, clean-up, insertion and read-back.
internal sealed class AppController : INotifyPropertyChanged, IDisposable
{
    private const int HistoryLimit = 200;
    private static readonly TimeSpan CorrectionWindow = TimeSpan.FromMinutes(2);
    private readonly Dispatcher dispatcher;
    private readonly JsonStore store = new(JsonStore.DefaultDirectory);
    private readonly AudioRecorder audio = new();
    private readonly SystemSpeech systemSpeech = new();
    private readonly TextPipeline pipeline;
    private readonly DispatcherTimer recordingTimer, saveTimer, correctionTimer, pruneTimer;
    private ClaudeSession? claudeSession;
    private CancellationTokenSource? operation;
    private CancellationTokenSource? vocabularyScan;
    private DateTime started;
    private InputTarget? target;
    private ScreenContext? sessionContext;
    private List<string> sessionHints = [];
    private TalkSettings sessionSettings = new();
    private Guid sessionId;
    private PendingCorrection? pendingCorrection;

    public event PropertyChangedEventHandler? PropertyChanged;
    /// Raised when the floating panel should appear, update or hide.
    public event Action? PanelChanged;
    /// Asks the app to show the settings window, e.g. when a permission is missing.
    public event Action<string?>? ShowSettingsRequested;

    public TalkSettings Settings { get; private set; }
    public List<DictationRecord> History { get; private set; }
    public UsageLedger Usage { get; private set; }
    public RecordingStore Recordings { get; }
    public AppUpdater Updater { get; } = new();

    private CapturePhase phase;
    public CapturePhase Phase { get => phase; private set { phase = value; Log.Write($"phase {value} {notice}"); Notify(); PanelChanged?.Invoke(); } }
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
    private Guid? failedRecordId;
    /// This session's failure, so the panel can offer to run it again.
    public Guid? FailedRecordId { get => failedRecordId; private set { failedRecordId = value; Notify(); } }
    public HashSet<Guid> Retrying { get; } = [];
    private List<string> hydayVocabulary = [];
    /// Entities and tags from the Hyday folder; only ever used as recognition hints.
    public List<string> HydayVocabulary { get => hydayVocabulary; private set { hydayVocabulary = value; Notify(); } }
    private bool scanningVocabulary;
    public bool ScanningHydayVocabulary { get => scanningVocabulary; private set { scanningVocabulary = value; Notify(); } }
    private CorrectionCheck? lastCorrectionCheck;
    public CorrectionCheck? LastCorrectionCheck { get => lastCorrectionCheck; private set { lastCorrectionCheck = value; Log.Write($"correction check: {value?.Outcome} {value?.Count}"); Notify(); } }

    public string DataDirectory => store.Directory;
    public string ModelDirectory => Path.Combine(store.Directory, "Models");

    public AppController(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        try { Settings = store.Load<TalkSettings>("settings.json") ?? new TalkSettings(); }
        catch { Settings = new TalkSettings(); notice = "無法讀取本機設定，原檔案已備份。請檢查 %APPDATA%\\HydayTalk。"; }
        try { History = store.Load<List<DictationRecord>>("history.json") ?? []; }
        catch { History = []; notice += " 無法讀取本機紀錄，原檔案已備份。"; }
        try { Usage = store.Load<UsageLedger>("usage.json") ?? UsageLedger.Seeded(History); }
        catch { Usage = UsageLedger.Seeded(History); }
        Settings.DictationLayout = null; // 段落與列表改由自動整理混合排版
        if (!SpeechBackends.Available.Contains(Settings.SpeechBackend)) Settings.SpeechBackend = SpeechBackend.Whisper;
        if (!TextBackends.Available.Contains(Settings.TextBackend)) Settings.TextBackend = TextBackend.Basic;
        // A bundled engine from another copy of the app (moved or updated) follows the running copy.
        var bundled = Settings.WhisperExecutable.EndsWith(@"\runtime\whisper-cli.exe", StringComparison.OrdinalIgnoreCase);
        if (Settings.WhisperExecutable.Length == 0 || bundled || !File.Exists(Settings.WhisperExecutable))
            Settings.WhisperExecutable = BundledEngine ?? Settings.WhisperExecutable;
        if (Settings.WhisperModel.Length == 0 || !File.Exists(Settings.WhisperModel))
            Settings.WhisperModel = new[] { LocalSpeechModel.QwenSmall, LocalSpeechModel.Tea, LocalSpeechModel.QwenLarge, LocalSpeechModel.WhisperTurbo }
                .Select(m => Path.Combine(ModelDirectory, m.Filename)).FirstOrDefault(File.Exists) ?? Settings.WhisperModel;
        Recordings = new RecordingStore(Path.Combine(store.Directory, "Recordings"));

        pipeline = new TextPipeline(KeyStore.Read, Claude, () => HydayRoot);
        audio.Level += value => dispatcher.BeginInvoke(() => { if (Phase == CapturePhase.Recording) Level = value; });
        audio.Failed += error => dispatcher.BeginInvoke(() =>
        {
            if (Phase != CapturePhase.Recording) return;
            Notice = "錄音中斷：" + error.Message; Finish();
        });
        systemSpeech.Transcript += text => dispatcher.BeginInvoke(() => { if (Phase == CapturePhase.Recording) Transcript = text; });
        recordingTimer = Timer(TimeSpan.FromMilliseconds(100), Tick);
        saveTimer = Timer(TimeSpan.FromMilliseconds(300), () => { saveTimer!.Stop(); SaveSettings(); });
        correctionTimer = Timer(TimeSpan.FromMilliseconds(700), PollCorrection);
        pruneTimer = Timer(TimeSpan.FromHours(1), PruneRecordings);
    }

    private DispatcherTimer Timer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(interval, DispatcherPriority.Normal, (_, _) => tick(), dispatcher);
        timer.Stop();
        return timer;
    }

    public void Start()
    {
        AudioRecorder.CleanAbandonedAudio();
        Uia.WarmUp();
        PruneRecordings(); pruneTimer.Start();
        ReloadHydayVocabulary();
        Updater.CheckIfDue();
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, _) => dispatcher.BeginInvoke(Cancel);
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend) dispatcher.BeginInvoke(Cancel);
        };
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

    /// The folder chosen on the Hyday page, otherwise Hyday's own configured folder.
    public string? HydayRoot => Settings.HydayFolder is { } folder ? (Directory.Exists(folder) ? folder : null) : HydayVault.ConfiguredRoot();

    /// Called by the settings window after any change; saves shortly after the last edit.
    public void SettingsChanged() { saveTimer.Stop(); saveTimer.Start(); Notify(nameof(Settings)); }

    private ClaudeSession? Claude()
    {
        if (claudeSession != null) return claudeSession;
        if (ClaudeSession.DiscoverExecutable() is not { } executable) return null;
        return claudeSession = new ClaudeSession(executable, Path.Combine(store.Directory, "Claude"));
    }

    public void ReloadHydayVocabulary()
    {
        vocabularyScan?.Cancel();
        if (HydayRoot is not { } root) { HydayVocabulary = []; ScanningHydayVocabulary = false; return; }
        var scan = new CancellationTokenSource(); vocabularyScan = scan;
        ScanningHydayVocabulary = true;
        Task.Run(() => HydayTalk.Core.HydayVocabulary.Scan(root, cancellation: scan.Token), scan.Token).ContinueWith(task =>
        {
            if (scan.IsCancellationRequested) return;
            HydayVocabulary = task.IsCompletedSuccessfully ? task.Result : [];
            ScanningHydayVocabulary = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void Handle(ShortcutAction shortcut)
    {
        if (shortcut.Kind == ShortcutKind.Cancel) { Cancel(); return; }
        if (Phase == CapturePhase.Recording) Finish();
        else if (!Phase.IsBusy()) Begin(shortcut.Action);
    }

    public async void Begin(VoiceAction voiceAction)
    {
        if (Phase.IsBusy()) return;
        CheckCorrections();
        var snapshot = Settings.Snapshot();
        Action = voiceAction; Result = ""; Transcript = ""; Notice = ""; Elapsed = 0; Level = 0; FailedRecordId = null;
        if (!Permissions.MicrophoneAllowed())
        {
            Fail(new TalkException("Windows 不允許桌面應用程式使用麥克風，請在設定 > 隱私權與安全性 > 麥克風開啟。"));
            ShowSettingsRequested?.Invoke("permissions");
            return;
        }
        try { pipeline.Validate(snapshot, voiceAction); }
        catch (TalkException error) { Fail(error); return; }
        // Start Claude Code while the user is still speaking, so formatting skips the CLI start-up.
        if (snapshot.TextBackend == TextBackend.Claude && snapshot.RequiresTextModel(voiceAction) && Claude() is { } session)
            _ = Task.Run(() => session.Prepare(snapshot.ClaudeModel));
        var focused = TextInsertion.Capture();
        target = voiceAction == VoiceAction.Hyday ? null : focused;
        if (target?.Secure == true) { Fail(new TalkException("這是密碼欄位。請改在一般文字輸入框使用。")); return; }
        sessionContext = focused != null && !focused.Secure && snapshot.ShouldReadScreenContext(voiceAction, focused.Name) ? focused.Context : null;
        sessionSettings = snapshot;
        sessionHints = RecognitionHints.Terms(snapshot.Dictionary, HydayVocabulary);
        var current = sessionId = Guid.NewGuid();
        Destination = voiceAction == VoiceAction.Hyday ? "Hyday"
            : voiceAction == VoiceAction.Ask ? "結果區"
            : snapshot.AutoInsert && target?.Editable == true ? target.Name : "剪貼簿";
        Phase = CapturePhase.Preparing;
        try
        {
            if (snapshot.SpeechBackend == SpeechBackend.System)
            {
                var start = systemSpeech.StartAsync(snapshot.Locale);
                if (await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(10))) != start)
                    throw new TalkException("Windows 語音辨識沒有回應，請改用本機模型或 API。");
                await start;
            }
            else audio.Start(snapshot.MicrophoneDevice);
        }
        catch (Exception error)
        {
            Log.Write("start failed: " + error.GetType().Name + " " + error.Message);
            if (sessionId == current) Fail(error is TalkException ? error : new TalkException("無法開始錄音：" + error.Message));
            return;
        }
        if (sessionId != current) { audio.Cancel(); systemSpeech.Dispose(); return; }
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
        var spokeAt = DateTime.Now;
        var duration = (spokeAt - started).TotalSeconds; Elapsed = duration; Level = 0;
        var snapshot = sessionSettings; var voiceAction = Action; var inputTarget = target; var hints = sessionHints; var context = sessionContext;
        var current = sessionId;
        operation?.Cancel(); var cancellation = new CancellationTokenSource(); operation = cancellation;
        var token = cancellation.Token;
        Phase = CapturePhase.Transcribing;
        if (snapshot.InteractionSounds) SystemSounds.Exclamation.Play();
        var recordId = Guid.NewGuid(); var saved = false; var heard = "";
        try
        {
            string raw;
            if (snapshot.SpeechBackend == SpeechBackend.System)
            {
                raw = SpeechTranscription.ValidatedText(await systemSpeech.StopAsync());
                if (systemSpeech.UsedPartialTranscript) Notice = "系統未回傳完整結果，這次保留最後的辨識文字。";
                if (RecognitionHints.IsEcho(raw, hints)) throw new TalkException("沒有辨識到文字，請再試一次。");
            }
            else
            {
                var file = await audio.StopAsync();
                // Saved before transcribing, so a failure can be retried from the history page.
                if (snapshot.RecordingRetention.Kind != RetentionKind.Off)
                    try { Recordings.Save(file, recordId); saved = true; } catch { }
                raw = await TranscribeAsync(file, snapshot, hints, token);
                audio.Delete();
            }
            token.ThrowIfCancellationRequested();
            Transcript = raw; heard = raw;
            if (snapshot.RequiresTextModel(voiceAction) && snapshot.TextBackend != TextBackend.Basic) Phase = CapturePhase.Processing;
            var processed = await Task.Run(() => pipeline.RunAsync(raw, voiceAction, inputTarget?.Selection ?? "", snapshot, token, context), token);
            token.ThrowIfCancellationRequested();
            var output = processed.Text;
            Result = output;
            if (processed.Notice.Length > 0) Notice = Join(Notice, processed.Notice);
            var delivered = false;
            if (voiceAction == VoiceAction.Hyday)
            {
                // Kept as the result first, so a failed write still leaves the text to copy.
                output = TextPipeline.HydayEntry(processed, spokeAt); Result = output;
                var root = HydayRoot ?? throw new TalkException("找不到 Hyday 資料夾，請到 Hyday 頁重新設定。");
                await Task.Run(() => new HydayVault(root).AppendToJournal(output, spokeAt), token);
                Notice = Join(Notice, "已記到 Hyday 今天的日誌。");
            }
            else if (snapshot.AutoInsert && voiceAction != VoiceAction.Ask)
            {
                try
                {
                    var (message, insertion, ok) = await TextInsertion.InsertAsync(output, inputTarget);
                    Notice = Join(Notice, message); delivered = ok;
                    if (voiceAction == VoiceAction.Dictate && snapshot.LearnsFromCorrections) WatchCorrections(insertion, inputTarget, snapshot);
                }
                catch (Exception error) when (error is not OperationCanceledException) { Notice = Join(Notice, error is TalkException ? error.Message : "無法插入文字：" + error.Message); }
            }
            else if (Notice.Length == 0) Notice = "文字已保留，可複製到需要的地方。";
            var place = voiceAction == VoiceAction.Hyday ? "Hyday" : inputTarget?.Name ?? "結果區";
            Usage.Record(TextProcessing.WordCount(output), duration, spokeAt, place, voiceAction); SaveUsage();
            if (snapshot.SaveHistory)
            {
                AddRecord(new DictationRecord
                {
                    Id = recordId, Date = spokeAt, RawText = raw, Text = output, Action = voiceAction,
                    Backend = snapshot.SpeechBackend, Duration = duration, Destination = place,
                });
            }
            if (saved && !snapshot.RecordingRetention.Keeps(false, snapshot.SaveHistory)) Recordings.Remove(recordId);
            PruneRecordings();
            // Insertion finished: hide the panel; otherwise keep it up with the text to copy.
            Phase = delivered ? CapturePhase.Idle : CapturePhase.Done;
            if (voiceAction == VoiceAction.Hyday)
            {
                // Confirm briefly, then get out of the way.
                await Task.Delay(2000);
                if (sessionId == current && Phase == CapturePhase.Done) Phase = CapturePhase.Idle;
            }
        }
        catch (OperationCanceledException) { audio.Delete(); if (saved) Recordings.Remove(recordId); }
        catch (Exception error) when (!token.IsCancellationRequested && sessionId == current)
        {
            audio.Delete();
            var message = error is TalkException ? error.Message : "處理失敗：" + error.Message;
            if (saved)
            {
                // Kept with the error, so the history page can run it again without speaking twice.
                // Without saved history, the record holds only what a retry needs, never the words.
                var words = snapshot.SaveHistory ? heard : "";
                AddRecord(new DictationRecord
                {
                    Id = recordId, Date = spokeAt, RawText = words, Text = words, Action = voiceAction, Backend = snapshot.SpeechBackend,
                    Duration = duration, Destination = voiceAction == VoiceAction.Hyday ? "Hyday" : inputTarget?.Name ?? "結果區", Failure = message,
                });
                Fail(new TalkException(message + " 錄音已保留，可以重新處理。"));
                FailedRecordId = recordId;
            }
            else Fail(new TalkException(message));
        }
        catch { audio.Delete(); }
    }

    private static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + " " + b;

    /// Transcribes a recorded file, listening again without hints if the model recited them.
    private static async Task<string> TranscribeAsync(string file, TalkSettings settings, List<string> hints, CancellationToken token)
    {
        if (settings.SpeechBackend == SpeechBackend.System)
            throw new TalkException("Windows 語音辨識只能即時聽寫，請改用本機模型或 API 重新處理錄音。");
        var key = settings.SpeechBackend == SpeechBackend.Api ? KeyStore.Read("speech") : "";
        var raw = await Task.Run(() => SpeechTranscription.TranscribeAsync(file, settings, key, hints, cancellation: token), token);
        if (RecognitionHints.IsEcho(raw, hints))
            raw = await Task.Run(() => SpeechTranscription.TranscribeAsync(file, settings, key, [], cancellation: token), token);
        if (RecognitionHints.IsEcho(raw, hints)) throw new TalkException("沒有辨識到文字，請再試一次。");
        return raw;
    }

    // MARK: Saved recordings

    public long RecordingBytes => Recordings.TotalBytes;
    public int FailedCount => History.Count(r => r.Failure != null);
    public bool HasRecording(DictationRecord record) => Recordings.Has(record.Id);

    /// Deletes expired recordings and those whose record is gone.
    public void PruneRecordings()
    {
        var infos = History.Select(r => new RecordingInfo(r.Id, r.Date, r.Failure != null));
        foreach (var id in Settings.RecordingRetention.Stale(Recordings.Ids, infos)) Recordings.Remove(id);
        Notify(nameof(RecordingBytes));
    }

    public void RemoveAllRecordings() { Recordings.RemoveAll(); Notify(nameof(RecordingBytes)); Notify(nameof(History)); }

    public void RetryFailed()
    {
        if (FailedRecordId is { } id && History.Find(r => r.Id == id) is { } record) Retry(record, inPanel: true);
    }

    /// Runs a saved recording through the current speech and text settings again. From the panel the
    /// result goes where the dictation was meant to go; from history it is copied, or written to Hyday.
    public async void Retry(DictationRecord record, bool inPanel = false)
    {
        if (!Recordings.Has(record.Id) || Retrying.Contains(record.Id) || (inPanel && Phase.IsBusy())) return;
        Retrying.Add(record.Id); Notify(nameof(Retrying));
        var panelTarget = inPanel ? target : null;
        if (inPanel) { Notice = ""; Result = ""; Phase = CapturePhase.Transcribing; }
        var snapshot = Settings.Snapshot();
        var hints = RecognitionHints.Terms(snapshot.Dictionary, HydayVocabulary);
        string? failure = null; string raw = record.RawText, output = record.Text;
        var missed = false;
        try
        {
            if (record.Action == VoiceAction.Hyday && HydayRoot == null) throw new TalkException("找不到 Hyday 資料夾，請到 Hyday 頁重新設定。");
            raw = await TranscribeAsync(Recordings.PathFor(record.Id), snapshot, hints, CancellationToken.None);
            var processed = await Task.Run(() => pipeline.RunAsync(raw, record.Action, "", snapshot, CancellationToken.None));
            output = processed.Text;
            if (record.Action == VoiceAction.Hyday && HydayRoot is { } root)
            {
                output = TextPipeline.HydayEntry(processed, record.Date.LocalDateTime);
                await Task.Run(() => new HydayVault(root).AppendToJournal(output, record.Date.LocalDateTime));
                Notice = "已重新處理，並記到 Hyday 今天的日誌。";
            }
            else if (inPanel && snapshot.AutoInsert && record.Action != VoiceAction.Ask)
            {
                try { var (message, _, ok) = await TextInsertion.InsertAsync(output, panelTarget); Notice = "已重新處理。" + message; missed = !ok; }
                catch (Exception error) { Notice = "已重新處理。" + error.Message; missed = true; }
            }
            else if (inPanel) { missed = record.Action != VoiceAction.Ask; Notice = "已重新處理，文字已保留供複製。"; }
            else { TextInsertion.Copy(output); Notice = "已重新處理，文字已複製。"; }
            if (inPanel) { Result = output; FailedRecordId = null; Phase = missed || record.Action == VoiceAction.Ask ? CapturePhase.Done : CapturePhase.Idle; }
            if (!snapshot.RecordingRetention.Keeps(false, snapshot.SaveHistory)) Recordings.Remove(record.Id);
        }
        catch (Exception error)
        {
            failure = error is TalkException ? error.Message : "處理失敗：" + error.Message;
            Notice = "重新處理失敗：" + failure;
            if (inPanel) Phase = CapturePhase.Failed;
        }
        var index = History.FindIndex(r => r.Id == record.Id);
        if (index >= 0)
        {
            // A retried failure stays only as long as text history is on.
            if (failure == null && !snapshot.SaveHistory) { History.RemoveAt(index); Recordings.Remove(record.Id); }
            else
            {
                History[index].RawText = raw; History[index].Text = output; History[index].Failure = failure;
                History[index].Backend = snapshot.SpeechBackend;
            }
            SaveHistory();
        }
        Retrying.Remove(record.Id); Notify(nameof(Retrying));
        PruneRecordings();
    }

    // MARK: Learning from corrections

    private sealed class PendingCorrection(Insertion insertion, TalkSettings settings)
    {
        public Insertion Insertion { get; } = insertion;
        public TalkSettings Settings { get; } = settings;
        public DateTime Expires { get; } = DateTime.Now + CorrectionWindow;
        public string LastText { get; set; } = insertion.After;
        public bool Reading { get; set; }
    }

    private void WatchCorrections(Insertion? insertion, InputTarget? inputTarget, TalkSettings snapshot)
    {
        StopWatchingCorrections();
        if (insertion == null)
        {
            if (inputTarget is { Editable: true })
                LastCorrectionCheck = new CorrectionCheck(inputTarget.Name, DateTime.Now,
                    inputTarget.Field == null ? CorrectionOutcome.UnsupportedField : CorrectionOutcome.Unreadable);
            return;
        }
        Log.Write($"watching corrections at {insertion.Location}");
        pendingCorrection = new PendingCorrection(insertion, snapshot);
        correctionTimer.Start();
    }

    private void StopWatchingCorrections() { correctionTimer.Stop(); pendingCorrection = null; }

    /// Keeps a fresh copy of the field while the user may be fixing words; leaving the field, switching
    /// apps or the time limit ends the watch and the last copy is compared.
    private async void PollCorrection()
    {
        if (pendingCorrection is not { Reading: false } pending) return;
        if (DateTime.Now > pending.Expires || GetForegroundWindow() != pending.Insertion.Target.Window) { CheckCorrections(); return; }
        pending.Reading = true;
        var field = pending.Insertion.Field;
        var (text, sameField) = await Task.Run(() => (field.ReadText(), field.IsFocused()));
        pending.Reading = false;
        if (pendingCorrection != pending) return;
        if (text != null) pending.LastText = text;
        if (!sameField) CheckCorrections();
    }

    /// Enter sends the text, so the last copy (at most 0.7 s old) is compared before it disappears.
    public void ReturnPressed()
    {
        if (pendingCorrection != null && GetForegroundWindow() == pendingCorrection.Insertion.Target.Window) CheckCorrections();
    }

    private async void CheckCorrections()
    {
        if (pendingCorrection is not { } pending) return;
        StopWatchingCorrections();
        var insertion = pending.Insertion; var app = insertion.Target.Name;
        if (pending.LastText == insertion.After) { LastCorrectionCheck = new CorrectionCheck(app, DateTime.Now, CorrectionOutcome.NoEdit); return; }
        var following = insertion.Location <= pending.LastText.Length ? pending.LastText[insertion.Location..] : "";
        var corrections = CorrectionFinder.Find(insertion.Inserted, following);
        if (corrections.Count == 0) { LastCorrectionCheck = new CorrectionCheck(app, DateTime.Now, CorrectionOutcome.NoCorrection); return; }
        var settings = pending.Settings;
        var terms = new List<Correction>();
        if (settings.TextBackend == TextBackend.Basic) terms.AddRange(corrections.Where(c => CorrectionJudge.LearnableWithoutAI(c.Corrected)));
        else
        {
            try
            {
                foreach (var correction in corrections)
                {
                    var answer = await Task.Run(() => pipeline.CompleteAsync(CorrectionJudge.Prompt(correction), settings, CancellationToken.None));
                    if (CorrectionJudge.IsTerm(answer)) terms.Add(correction);
                }
            }
            catch { LastCorrectionCheck = new CorrectionCheck(app, DateTime.Now, CorrectionOutcome.JudgeFailed); return; }
        }
        if (terms.Count == 0) { LastCorrectionCheck = new CorrectionCheck(app, DateTime.Now, CorrectionOutcome.NotTerms, corrections.Count); return; }
        var learned = terms.Count(c => DictionaryLearning.Learn(c, Settings));
        SettingsChanged();
        LastCorrectionCheck = new CorrectionCheck(app, DateTime.Now, CorrectionOutcome.Learned, learned);
    }

    public void RemoveDictionaryEntry(DictionaryEntry entry) { DictionaryLearning.Remove(entry, Settings); SettingsChanged(); }

    // MARK: Panel and history

    public void Cancel()
    {
        sessionId = Guid.NewGuid();
        operation?.Cancel(); operation = null;
        recordingTimer.Stop(); audio.Cancel(); systemSpeech.Dispose();
        Level = 0; Elapsed = 0; Transcript = "";
        Phase = CapturePhase.Idle;
    }

    public void DismissPanel() { if (Phase.IsBusy()) Cancel(); else Phase = CapturePhase.Idle; }

    public void CopyResult()
    {
        if (Result.Length == 0) return;
        try { TextInsertion.Copy(Result); Notice = "已複製文字。"; } catch (TalkException e) { Notice = e.Message; }
    }

    private void AddRecord(DictationRecord record)
    {
        History.Insert(0, record);
        if (History.Count > HistoryLimit) History.RemoveRange(HistoryLimit, History.Count - HistoryLimit);
        SaveHistory();
    }

    public void RemoveRecord(Guid id) { History.RemoveAll(r => r.Id == id); Recordings.Remove(id); SaveHistory(); PruneRecordings(); }
    public void ClearHistory() { History.Clear(); SaveHistory(); PruneRecordings(); }

    private void Fail(Exception error)
    {
        recordingTimer.Stop(); audio.Cancel(); systemSpeech.Dispose();
        Notice = error.Message; Level = 0; Phase = CapturePhase.Failed;
    }

    public void SaveSettings()
    {
        try { store.Save(Settings, "settings.json"); }
        catch { Notice = "設定未儲存。請確認磁碟空間與資料夾權限。"; }
    }

    private void SaveUsage()
    {
        try { store.Save(Usage, "usage.json"); } catch { }
        Notify(nameof(Usage));
    }

    private void SaveHistory()
    {
        try { store.Save(History, "history.json"); }
        catch { Notice = Join(Notice, "紀錄未儲存，請確認磁碟空間。"); }
        Notify(nameof(History));
    }

    private void Notify([CallerMemberName] string? name = null)
    {
        if (dispatcher.CheckAccess()) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        else dispatcher.BeginInvoke(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }

    public void Dispose()
    {
        Cancel(); saveTimer.Stop(); correctionTimer.Stop(); pruneTimer.Stop(); SaveSettings();
        claudeSession?.Dispose(); audio.Dispose(); systemSpeech.Dispose();
    }
}
