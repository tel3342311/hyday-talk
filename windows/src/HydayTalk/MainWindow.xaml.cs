using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using HydayTalk.Core;
using Microsoft.Win32;

namespace HydayTalk;

/// The settings window, opened from the tray icon.
public partial class MainWindow : Window
{
    private static readonly (string Code, string Title)[] Locales =
        [("zh-TW", "台灣繁體中文"), ("zh-CN", "簡體中文"), ("en-US", "英文"), ("ja-JP", "日文"), ("ko-KR", "韓文")];
    private static readonly string[] TargetLanguages = ["英文", "台灣繁體中文", "日文", "韓文", "簡體中文", "法文", "德文", "西班牙文"];
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AppController controller;
    private readonly ObservableCollection<ModelRow> models = [];
    private bool loading = true;
    private CancellationTokenSource? download;

    private TalkSettings Settings => controller.Settings;

    internal MainWindow(AppController controller)
    {
        InitializeComponent();
        this.controller = controller;
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppController.History)) Dispatcher.BeginInvoke(LoadHistory);
            if (e.PropertyName is nameof(AppController.ShortcutReady)) Dispatcher.BeginInvoke(UpdateStatus);
        };
        Load();
        loading = false;
    }

    private void Changed() { if (!loading) controller.SettingsChanged(); }

    private void Load()
    {
        UpdateStatus();
        ShortcutList.ItemsSource = new[] { VoiceAction.Dictate, VoiceAction.Translate, VoiceAction.Ask }
            .Select(a => new ShortcutRow(a.Title(), a.Shortcut())).Append(new ShortcutRow("取消錄音或處理", "Esc")).ToList();

        SpeechBackendBox.ItemsSource = SpeechBackends.Available.Select(b => $"{b.Title()}：{b.Detail()}").ToList();
        SpeechBackendBox.SelectedIndex = Math.Max(0, Array.IndexOf(SpeechBackends.Available, Settings.SpeechBackend));
        LocaleBox.ItemsSource = Locales.Select(l => l.Title).ToList();
        LocaleBox.SelectedIndex = Math.Max(0, Array.FindIndex(Locales, l => l.Code == Settings.Locale));
        EngineBox.Text = Settings.WhisperExecutable;
        ModelPathBox.Text = Settings.WhisperModel;
        ModelList.ItemsSource = models;
        RefreshModels();
        SpeechProviderBox.ItemsSource = APIProvider.Choices(APIPurpose.Speech);
        SpeechProviderBox.SelectedItem = APIProvider.Matching(Settings.SpeechAPI.BaseURL);
        SpeechURLBox.Text = Settings.SpeechAPI.BaseURL; SpeechModelBox.Text = Settings.SpeechAPI.Model;
        SpeechKeyBox.Password = KeyStore.Read("speech");

        TextBackendBox.ItemsSource = TextBackends.Available.Select(b => b.Title()).ToList();
        TextBackendBox.SelectedIndex = Math.Max(0, Array.IndexOf(TextBackends.Available, Settings.TextBackend));
        StyleBox.ItemsSource = Enum.GetValues<WritingStyle>().Select(s => s.Title()).ToList();
        StyleBox.SelectedIndex = (int)Settings.WritingStyle;
        StyleDetail.Text = Settings.WritingStyle.Detail();
        ClaudeModelBox.ItemsSource = ClaudeTextModels.All.Select(m => $"{m.Title}：{m.Detail}").ToList();
        ClaudeModelBox.SelectedIndex = Math.Max(0, Array.FindIndex(ClaudeTextModels.All, m => m.Id == Settings.ClaudeModel));
        ClaudeStatus.Text = ClaudeSession.DiscoverExecutable() is { } claude
            ? $"使用這台電腦上已登入的 Claude Code（{claude}），會使用你自己的 Claude 訂閱額度。"
            : "找不到 Claude Code。請先安裝並登入 Claude Code，再回到這裡。";
        TextProviderBox.ItemsSource = APIProvider.Choices(APIPurpose.Text);
        TextProviderBox.SelectedItem = APIProvider.Matching(Settings.TextAPI.BaseURL);
        TextURLBox.Text = Settings.TextAPI.BaseURL; TextModelBox.Text = Settings.TextAPI.Model;
        TextKeyBox.Password = KeyStore.Read("text");
        if (Settings.LocalTextModel is { } local) { OllamaModelBox.ItemsSource = new[] { local }; OllamaModelBox.SelectedItem = local; }

        LoadDictionary();
        LoadHistory();

        AutoInsertBox.IsChecked = Settings.AutoInsert;
        SaveHistoryBox.IsChecked = Settings.SaveHistory;
        SoundsBox.IsChecked = Settings.InteractionSounds;
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) StartupBox.IsChecked = key?.GetValue("HydayTalk") != null;
        TargetLanguageBox.ItemsSource = TargetLanguages;
        TargetLanguageBox.Text = Settings.TargetLanguage;
        var devices = AudioRecorder.Devices();
        MicrophoneBox.ItemsSource = devices.Count > 0 ? devices : ["找不到麥克風"];
        MicrophoneBox.SelectedIndex = Settings.MicrophoneDevice is { } d && d < devices.Count ? d : 0;
        DataPath.Text = JsonStore.DefaultDirectory;
        UpdatePanels();
    }

    private void UpdateStatus() => StatusLine.Text = controller.ShortcutReady
        ? "已在背景執行。在任何 App 的輸入框按一下右 Ctrl 開始聽寫，再按一下完成。"
        : "無法註冊快捷鍵，請重新啟動 Hyday Talk。";

    private void UpdatePanels()
    {
        LocalSpeechPanel.Visibility = Settings.SpeechBackend == SpeechBackend.Whisper ? Visibility.Visible : Visibility.Collapsed;
        SpeechAPIPanel.Visibility = Settings.SpeechBackend == SpeechBackend.Api ? Visibility.Visible : Visibility.Collapsed;
        LocalTextPanel.Visibility = Settings.TextBackend == TextBackend.Local ? Visibility.Visible : Visibility.Collapsed;
        ClaudePanel.Visibility = Settings.TextBackend == TextBackend.Claude ? Visibility.Visible : Visibility.Collapsed;
        TextAPIPanel.Visibility = Settings.TextBackend == TextBackend.Api ? Visibility.Visible : Visibility.Collapsed;
    }

    // MARK: Speech

    private void SpeechBackend_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || SpeechBackendBox.SelectedIndex < 0) return;
        Settings.SpeechBackend = SpeechBackends.Available[SpeechBackendBox.SelectedIndex]; UpdatePanels(); Changed();
    }

    private void Locale_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || LocaleBox.SelectedIndex < 0) return;
        Settings.Locale = Locales[LocaleBox.SelectedIndex].Code; Changed();
    }

    private void Engine_LostFocus(object sender, RoutedEventArgs e)
    {
        Settings.WhisperExecutable = EngineBox.Text.Trim(); Changed();
    }

    private void BrowseEngine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "whisper-cli|whisper-cli.exe|程式|*.exe", Title = "選擇 whisper-cli.exe" };
        if (dialog.ShowDialog(this) != true) return;
        EngineBox.Text = Settings.WhisperExecutable = dialog.FileName; Changed();
    }

    private void BrowseModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "語音模型|*.bin;*.gguf", Title = "選擇語音模型" };
        if (dialog.ShowDialog(this) != true) return;
        ModelPathBox.Text = Settings.WhisperModel = dialog.FileName; Changed(); RefreshModels();
    }

    private void RefreshModels()
    {
        models.Clear();
        foreach (var model in LocalSpeechModel.All)
        {
            var path = Path.Combine(controller.ModelDirectory, model.Filename);
            models.Add(new ModelRow(model, path, File.Exists(path), string.Equals(Settings.WhisperModel, path, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private async void ModelAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ModelRow row) return;
        if (row.Installed)
        {
            ModelPathBox.Text = Settings.WhisperModel = row.Path; Changed(); RefreshModels(); return;
        }
        if (download != null) { download.Cancel(); return; }
        download = new CancellationTokenSource();
        row.Progress = 0;
        try
        {
            var progress = new Progress<double>(value => row.Progress = value);
            var path = await LocalModelInstaller.DownloadAsync(row.Model, controller.ModelDirectory, progress, cancellation: download.Token);
            ModelPathBox.Text = Settings.WhisperModel = path; Changed();
        }
        catch (OperationCanceledException) { }
        catch (TalkException error) { MessageBox.Show(this, error.Message, "Hyday Talk"); }
        finally { download = null; RefreshModels(); }
    }

    private void SpeechProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || SpeechProviderBox.SelectedItem is not APIProvider provider) return;
        provider.Apply(Settings.SpeechAPI, APIPurpose.Speech);
        SpeechURLBox.Text = Settings.SpeechAPI.BaseURL; SpeechModelBox.Text = Settings.SpeechAPI.Model; Changed();
    }

    private void SpeechAPI_LostFocus(object sender, RoutedEventArgs e)
    {
        Settings.SpeechAPI.BaseURL = SpeechURLBox.Text.Trim(); Settings.SpeechAPI.Model = SpeechModelBox.Text.Trim(); Changed();
    }

    private void SaveSpeechKey_Click(object sender, RoutedEventArgs e) => SaveKey(SpeechKeyBox.Password, "speech");

    // MARK: Text

    private void TextBackend_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || TextBackendBox.SelectedIndex < 0) return;
        Settings.TextBackend = TextBackends.Available[TextBackendBox.SelectedIndex]; UpdatePanels(); Changed();
        if (Settings.TextBackend == TextBackend.Local) RefreshOllama_Click(this, new RoutedEventArgs());
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || StyleBox.SelectedIndex < 0) return;
        Settings.WritingStyle = (WritingStyle)StyleBox.SelectedIndex; StyleDetail.Text = Settings.WritingStyle.Detail(); Changed();
    }

    private async void RefreshOllama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var names = await LocalTextClient.ModelsAsync();
            OllamaModelBox.ItemsSource = names;
            if (Settings.LocalTextModel is { } current && names.Contains(current)) OllamaModelBox.SelectedItem = current;
            else if (names.Count > 0) OllamaModelBox.SelectedIndex = names.Contains(LocalTextClient.RecommendedModel) ? names.IndexOf(LocalTextClient.RecommendedModel) : 0;
            PullStatus.Text = names.Count == 0 ? "Ollama 還沒有可用的文字模型。" : "";
        }
        catch (TalkException error) { PullStatus.Text = error.Message; }
    }

    private void OllamaModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || OllamaModelBox.SelectedItem is not string model) return;
        Settings.LocalTextModel = model; Changed();
    }

    private async void PullModel_Click(object sender, RoutedEventArgs e)
    {
        PullButton.IsEnabled = false;
        try
        {
            await LocalTextClient.PullAsync(LocalTextClient.RecommendedModel,
                value => Dispatcher.BeginInvoke(() => PullStatus.Text = value is { } v ? $"下載中 {v:P0}" : "下載中…"));
            PullStatus.Text = "安裝完成。";
            Settings.LocalTextModel = LocalTextClient.RecommendedModel; Changed();
            RefreshOllama_Click(sender, e);
        }
        catch (TalkException error) { PullStatus.Text = error.Message; }
        finally { PullButton.IsEnabled = true; }
    }

    private void ClaudeModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ClaudeModelBox.SelectedIndex < 0) return;
        Settings.ClaudeTextModel = ClaudeTextModels.All[ClaudeModelBox.SelectedIndex].Id; Changed();
    }

    private void TextProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || TextProviderBox.SelectedItem is not APIProvider provider) return;
        provider.Apply(Settings.TextAPI, APIPurpose.Text);
        TextURLBox.Text = Settings.TextAPI.BaseURL; TextModelBox.Text = Settings.TextAPI.Model; Changed();
    }

    private void TextAPI_LostFocus(object sender, RoutedEventArgs e)
    {
        Settings.TextAPI.BaseURL = TextURLBox.Text.Trim(); Settings.TextAPI.Model = TextModelBox.Text.Trim(); Changed();
    }

    private void SaveTextKey_Click(object sender, RoutedEventArgs e) => SaveKey(TextKeyBox.Password, "text");

    private void SaveKey(string value, string account)
    {
        try { KeyStore.Write(value.Trim(), account); MessageBox.Show(this, value.Length == 0 ? "已清除 API Key。" : "已儲存 API Key。", "Hyday Talk"); }
        catch (TalkException error) { MessageBox.Show(this, error.Message, "Hyday Talk"); }
    }

    // MARK: Dictionary

    private void LoadDictionary() => DictionaryList.ItemsSource = Settings.Dictionary.Select(e => new DictionaryRow(e)).ToList();

    private void AddTerm_Click(object sender, RoutedEventArgs e)
    {
        var term = TermBox.Text.Trim();
        if (term.Length == 0) return;
        // Spellings that differ only in case are the same term; keep the newest.
        Settings.Dictionary.RemoveAll(entry => string.Equals(entry.Term, term, StringComparison.OrdinalIgnoreCase));
        var misheard = MisheardBox.Text.Trim();
        Settings.Dictionary.Add(new DictionaryEntry(term, misheard.Length > 0 ? misheard : null));
        TermBox.Text = MisheardBox.Text = "";
        LoadDictionary(); Changed();
    }

    private void RemoveTerm_Click(object sender, RoutedEventArgs e)
    {
        var selected = DictionaryList.SelectedItems.Cast<DictionaryRow>().Select(row => row.Entry).ToList();
        if (selected.Count == 0) return;
        Settings.Dictionary.RemoveAll(selected.Contains);
        LoadDictionary(); Changed();
    }

    // MARK: History

    private void LoadHistory()
    {
        var rows = controller.History.Select(r => new HistoryRow(r)).ToList();
        HistoryList.ItemsSource = rows;
        RecentList.ItemsSource = rows.Take(5).ToList();
        RecentEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyRecord_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow row) TextInsertion.Copy(row.Text);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "要清除全部聽寫紀錄嗎？", "Hyday Talk", MessageBoxButton.OKCancel) == MessageBoxResult.OK) controller.ClearHistory();
    }

    // MARK: General

    private void General_Changed(object sender, RoutedEventArgs e)
    {
        Settings.AutoInsert = AutoInsertBox.IsChecked == true;
        Settings.SaveHistory = SaveHistoryBox.IsChecked == true;
        Settings.InteractionSounds = SoundsBox.IsChecked == true;
        Changed();
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartupBox.IsChecked == true) key.SetValue("HydayTalk", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("HydayTalk", throwOnMissingValue: false);
    }

    private void TargetLanguage_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        var value = (TargetLanguageBox.SelectedItem as string ?? TargetLanguageBox.Text).Trim();
        if (value.Length == 0 || value == Settings.TargetLanguage) return;
        Settings.TargetLanguage = value; Changed();
    }

    private void Microphone_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || MicrophoneBox.SelectedIndex < 0) return;
        Settings.MicrophoneDevice = MicrophoneBox.SelectedIndex; Changed();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(JsonStore.DefaultDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", JsonStore.DefaultDirectory) { UseShellExecute = true });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Commit fields still being edited.
        Engine_LostFocus(this, new RoutedEventArgs());
        SpeechAPI_LostFocus(this, new RoutedEventArgs());
        TextAPI_LostFocus(this, new RoutedEventArgs());
        download?.Cancel();
        controller.SaveSettings();
        base.OnClosing(e);
    }

    private sealed record ShortcutRow(string Title, string Keys);

    private sealed class DictionaryRow(DictionaryEntry entry)
    {
        public DictionaryEntry Entry { get; } = entry;
        public string Term => Entry.Term;
        public string Heard => Entry.Misheard ?? "";
        public string Source => Entry.IsAutomatic ? "自動加入" : "手動";
    }

    private sealed class HistoryRow(DictationRecord record)
    {
        public string Time { get; } = record.Date.LocalDateTime.ToString("M/d HH:mm");
        public string Action { get; } = record.Action.Title();
        public string Text { get; } = record.Failure ?? record.Text;
        public string Caption { get; } = $"{record.Date.LocalDateTime:M/d HH:mm} · {record.Action.Title()} · {record.Destination}";
    }
}

internal sealed class ModelRow(LocalSpeechModel model, string path, bool installed, bool selected) : INotifyPropertyChanged
{
    public LocalSpeechModel Model { get; } = model;
    public string Path { get; } = path;
    public bool Installed { get; } = installed;
    public string Title => Model.Title + (Model.IsRecommended ? "（推薦）" : "") + (selected ? " · 使用中" : "");
    public string Detail => $"{Model.Detail} {Model.SizeLabel}" + (Model.Engine == SpeechEngine.Qwen ? " · 需要 transcribe-cli.exe" : "");
    private double? progress;
    public double? Progress { get => progress; set { progress = value; Notify(nameof(ActionTitle)); } }
    public string ActionTitle => Installed ? (selected ? "使用中" : "使用") : progress is { } p ? $"取消 {p:P0}" : "下載";
    public bool CanAct => !(Installed && selected);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
