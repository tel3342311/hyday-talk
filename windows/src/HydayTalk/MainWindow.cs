using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using HydayTalk.Core;
using Microsoft.Win32;

namespace HydayTalk;

/// The settings window, opened from the tray icon.
public sealed class MainWindow : Window
{
    private static readonly (string Code, string Title)[] Locales =
        [("zh-TW", "台灣繁體中文"), ("zh-CN", "簡體中文"), ("en-US", "英文"), ("ja-JP", "日文"), ("ko-KR", "韓文")];
    private static readonly string[] TargetLanguages = ["英文", "台灣繁體中文", "日文", "韓文", "簡體中文", "法文", "德文", "西班牙文"];
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AppController controller;
    private readonly Action quit;
    private readonly ObservableCollection<ModelRow> models = [];
    private bool loading = true;
    private CancellationTokenSource? download;

#pragma warning disable CS0649 // assigned from the XAML by Xaml.Load
    private Image Logo = null!;
    private TabControl Tabs = null!;
    private TabItem OverviewTab = null!, SpeechTab = null!, HydayTab = null!, GeneralTab = null!;
    private TextBlock StatusLine = null!, PermissionText = null!, UpdateText = null!, TodayWords = null!, TotalWords = null!, SavedTime = null!, Speed = null!,
        InsightLine = null!, TopApps = null!, RecentEmpty = null!, SystemSpeechStatus = null!, StyleDetail = null!, StyleExample = null!, PullStatus = null!,
        ClaudeStatus = null!, LearnStatus = null!, HydayFolderText = null!, VocabularyStatus = null!, RecordingSize = null!, MicrophoneStatus = null!,
        VersionText = null!, DataPath = null!;
    private Border PermissionBanner = null!, UpdateBanner = null!;
    private Button PermissionButton = null!, UpdateButton = null!, PullButton = null!, HydayDefaultButton = null!,
        SpeechKeyPage = null!, TextKeyPage = null!;
    private ItemsControl DailyBars = null!, ShortcutList = null!, RecentList = null!, ModelList = null!;
    private StackPanel SystemSpeechPanel = null!, LocalSpeechPanel = null!, SpeechAPIPanel = null!, LocalTextPanel = null!, ClaudePanel = null!, TextAPIPanel = null!;
    private ComboBox SpeechBackendBox = null!, LocaleBox = null!, SpeechProviderBox = null!, TextBackendBox = null!, StyleBox = null!, OllamaModelBox = null!,
        ClaudeModelBox = null!, TextProviderBox = null!, RetentionBox = null!, TargetLanguageBox = null!, MicrophoneBox = null!;
    private TextBox EngineBox = null!, ModelPathBox = null!, SpeechURLBox = null!, SpeechModelBox = null!, TextURLBox = null!, TextModelBox = null!,
        ExcludedAppsBox = null!, TermBox = null!, MisheardBox = null!, TypingSpeedBox = null!;
    private PasswordBox SpeechKeyBox = null!, TextKeyBox = null!;
    private CheckBox ScreenContextBox = null!, LearnBox = null!, AskNotesBox = null!, AutoInsertBox = null!, SaveHistoryBox = null!, SoundsBox = null!, StartupBox = null!;
    private ListView DictionaryList = null!, HistoryList = null!;
    private WrapPanel VocabularyList = null!;
#pragma warning restore CS0649

    private TalkSettings Settings => controller.Settings;

    internal MainWindow(AppController controller, Action quit)
    {
        this.controller = controller; this.quit = quit;
        Title = "Hyday Talk"; Width = 820; Height = 720; MinWidth = 600; MinHeight = 500;
        Background = (System.Windows.Media.Brush)FindResource("Workspace");
        Foreground = (System.Windows.Media.Brush)FindResource("Ink");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Imaging.CreateBitmapSourceFromHIcon(App.AppIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        Content = Xaml.Load<FrameworkElement>(this, "MainWindow.xaml");
        Logo.Source = Icon;
        controller.PropertyChanged += Controller_PropertyChanged;
        controller.Updater.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateUpdateBanner);
        Activated += (_, _) => UpdatePermissions();
        Load();
        loading = false;
    }

    private void Controller_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppController.History) or nameof(AppController.Usage) or nameof(AppController.Retrying) or nameof(AppController.RecordingBytes):
                LoadHistory(); LoadOverview(); break;
            case nameof(AppController.ShortcutReady): UpdateStatus(); break;
            case nameof(AppController.HydayVocabulary) or nameof(AppController.ScanningHydayVocabulary): LoadVocabulary(); break;
            case nameof(AppController.LastCorrectionCheck): UpdateLearnStatus(); break;
            case nameof(AppController.Settings): if (!loading) LoadDictionary(); break;
        }
    }

    internal void ShowPage(string page) => Tabs.SelectedItem = page switch
    {
        "permissions" => GeneralTab,
        "hyday" => HydayTab,
        "speech" => SpeechTab,
        _ => OverviewTab,
    };

    private void Changed() { if (!loading) controller.SettingsChanged(); }

    private void Load()
    {
        UpdateStatus();
        UpdatePermissions();
        UpdateUpdateBanner();
        ShortcutList.ItemsSource = new[] { VoiceAction.Dictate, VoiceAction.Translate, VoiceAction.Ask, VoiceAction.Hyday }
            .Select(a => new ShortcutRow(a.Title(), a.Shortcut())).Append(new ShortcutRow("取消錄音或處理", "Esc")).ToList();
        LoadOverview();

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
        UpdateStyle();
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
        ScreenContextBox.IsChecked = Settings.ReadsScreenContext;
        ExcludedAppsBox.Text = string.Join("、", Settings.ScreenContextExcludedApps);

        LoadDictionary();
        LearnBox.IsChecked = Settings.LearnsFromCorrections;
        UpdateLearnStatus();
        LoadHyday();
        LoadHistory();
        RetentionBox.ItemsSource = RecordingRetention.Choices.Select(c => c.Title).ToList();
        RetentionBox.SelectedIndex = Math.Max(0, Array.IndexOf(RecordingRetention.Choices, Settings.RecordingRetention));

        AutoInsertBox.IsChecked = Settings.AutoInsert;
        SaveHistoryBox.IsChecked = Settings.SaveHistory;
        SoundsBox.IsChecked = Settings.InteractionSounds;
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) StartupBox.IsChecked = key?.GetValue("HydayTalk") != null;
        TargetLanguageBox.ItemsSource = TargetLanguages;
        TargetLanguageBox.Text = Settings.TargetLanguage;
        var devices = AudioRecorder.Devices();
        MicrophoneBox.ItemsSource = devices.Count > 0 ? devices : ["找不到麥克風"];
        MicrophoneBox.SelectedIndex = Settings.MicrophoneDevice is { } d && d < devices.Count ? d : 0;
        TypingSpeedBox.Text = Settings.TypingWordsPerMinute.ToString();
        VersionText.Text = $"目前版本 {AppUpdater.CurrentVersion}";
        DataPath.Text = controller.DataDirectory;
        UpdatePanels();
    }

    private void UpdateStatus() => StatusLine.Text = controller.ShortcutReady
        ? "已在背景執行。在任何 App 的輸入框按一下右 Ctrl 開始聽寫，再按一下完成。"
        : "無法註冊快捷鍵，請重新啟動 Hyday Talk。";

    private Action? fixPermission;

    private void UpdatePermissions()
    {
        var microphone = Permissions.MicrophoneAllowed();
        MicrophoneStatus.Text = microphone ? "Windows 允許桌面應用程式使用麥克風。" : "Windows 目前不允許桌面應用程式使用麥克風。";
        var speechNeeded = Settings.SpeechBackend == SpeechBackend.System;
        var online = Permissions.OnlineSpeechAllowed();
        var language = speechNeeded && SystemSpeechSupported();
        SystemSpeechStatus.Text = !online ? "線上語音辨識尚未開啟。" : !language ? "Windows 沒有這個語言的語音辨識。" : "Windows 語音辨識可以使用。";
        if (!microphone)
        {
            PermissionText.Text = "請允許麥克風：設定 > 隱私權與安全性 > 麥克風，開啟「讓桌面應用程式存取您的麥克風」。";
            PermissionButton.Content = "開啟設定"; fixPermission = Permissions.OpenMicrophoneSettings;
        }
        else if (speechNeeded && !online)
        {
            PermissionText.Text = "Windows 語音辨識需要開啟線上語音辨識，或改用本機模型。";
            PermissionButton.Content = "開啟設定"; fixPermission = Permissions.OpenSpeechSettings;
        }
        else if (!controller.ShortcutReady)
        {
            PermissionText.Text = "無法註冊鍵盤快捷鍵，請重新啟動 Hyday Talk。";
            PermissionButton.Content = "結束"; fixPermission = quit;
        }
        else fixPermission = null;
        PermissionBanner.Visibility = fixPermission == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool SystemSpeechSupported()
    {
        try { return SystemSpeech.Supports(Settings.Locale); } catch { return false; }
    }

    private void FixPermission_Click(object sender, RoutedEventArgs e) => fixPermission?.Invoke();

    private void UpdateUpdateBanner()
    {
        var updater = controller.Updater;
        var version = updater.Update?.Version;
        (string text, string? button) = updater.State switch
        {
            UpdateState.Available => ($"有新版本 {version} 可以更新。", "下載"),
            UpdateState.Downloading => ($"正在下載 {version}… {updater.Progress:P0}", "取消"),
            UpdateState.Ready => AppUpdater.CanReplace ? ($"{version} 已下載完成。", "重新啟動並更新") : ($"{version} 已下載到「下載」資料夾，請解壓縮後取代目前的版本。", "顯示檔案"),
            UpdateState.Installing => ("正在安裝更新…", null),
            UpdateState.Failed => (updater.Message, "重試"),
            _ => ("", null),
        };
        UpdateText.Text = text;
        UpdateButton.Content = button ?? "";
        UpdateButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
        UpdateBanner.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (updater.State == UpdateState.Idle && updater.Message.Length > 0) VersionText.Text = $"目前版本 {AppUpdater.CurrentVersion}，{updater.Message}";
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        var updater = controller.Updater;
        switch (updater.State)
        {
            case UpdateState.Available or UpdateState.Failed: updater.StartDownload(); break;
            case UpdateState.Downloading: updater.CancelDownload(); break;
            case UpdateState.Ready when AppUpdater.CanReplace: updater.RestartToUpdate(quit); break;
            case UpdateState.Ready: updater.ShowPackage(); break;
        }
    }

    private void CheckUpdate_Click(object sender, RoutedEventArgs e) => controller.Updater.CheckIfDue(force: true);

    private void UpdatePanels()
    {
        SystemSpeechPanel.Visibility = Settings.SpeechBackend == SpeechBackend.System ? Visibility.Visible : Visibility.Collapsed;
        LocalSpeechPanel.Visibility = Settings.SpeechBackend == SpeechBackend.Whisper ? Visibility.Visible : Visibility.Collapsed;
        SpeechAPIPanel.Visibility = Settings.SpeechBackend == SpeechBackend.Api ? Visibility.Visible : Visibility.Collapsed;
        LocalTextPanel.Visibility = Settings.TextBackend == TextBackend.Local ? Visibility.Visible : Visibility.Collapsed;
        ClaudePanel.Visibility = Settings.TextBackend == TextBackend.Claude ? Visibility.Visible : Visibility.Collapsed;
        TextAPIPanel.Visibility = Settings.TextBackend == TextBackend.Api ? Visibility.Visible : Visibility.Collapsed;
        SpeechKeyPage.Visibility = APIProvider.Matching(Settings.SpeechAPI.BaseURL).KeyPage == null ? Visibility.Collapsed : Visibility.Visible;
        TextKeyPage.Visibility = APIProvider.Matching(Settings.TextAPI.BaseURL).KeyPage == null ? Visibility.Collapsed : Visibility.Visible;
        UpdatePermissions();
    }

    // MARK: Overview

    private void LoadOverview()
    {
        var insights = new UsageInsights(controller.Usage, Settings.TypingWordsPerMinute);
        TodayWords.Text = $"{insights.TodayWords:N0} 字";
        TotalWords.Text = $"{insights.TotalWords:N0} 字";
        SavedTime.Text = insights.SavedMinutes >= 60 ? $"{insights.SavedMinutes / 60:0.#} 小時" : $"{insights.SavedMinutes:0} 分";
        Speed.Text = insights.WordsPerMinute is { } wpm ? $"{wpm} 字/分" : "—";
        var parts = new List<string>();
        if (insights.CurrentStreak > 0) parts.Add($"連續使用 {insights.CurrentStreak} 天");
        if (insights.LongestStreak > 0) parts.Add($"最長連續 {insights.LongestStreak} 天");
        if (insights.PeakHour is { } hour) parts.Add($"最常在 {hour}:00 口述");
        if (insights.WeekSavedMinutes > 0) parts.Add($"這週節省 {insights.WeekSavedMinutes:0} 分鐘");
        InsightLine.Text = parts.Count > 0 ? string.Join(" · ", parts) : "開始聽寫後，這裡會顯示你的使用統計。統計只記錄字數與時間，不保存文字內容。";
        var days = insights.DailyWords(14);
        var max = Math.Max(1, days.Max());
        DailyBars.ItemsSource = days.Select((words, i) => new BarRow(Math.Max(2, 66.0 * words / max),
            $"{DateTime.Today.AddDays(i - 13):M/d}：{words} 字")).ToList();
        TopApps.Text = insights.Apps.Count == 0 ? "還沒有資料。" : string.Join("、", insights.Apps.Take(5).Select(a => $"{a.Name}（{a.Count} 次）"));
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
        Settings.Locale = Locales[LocaleBox.SelectedIndex].Code; UpdatePermissions(); Changed();
    }

    private void OpenSpeechPrivacy_Click(object sender, RoutedEventArgs e) => Permissions.OpenSpeechSettings();
    private void OpenSpeechLanguages_Click(object sender, RoutedEventArgs e) => Permissions.OpenSpeechLanguages();

    private void Engine_LostFocus(object sender, RoutedEventArgs e)
    {
        if (EngineBox.Text.Trim() == Settings.WhisperExecutable) return;
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
        if ((sender as FrameworkElement)?.Tag is not ModelRow row) return;
        if (row.Installed) { ModelPathBox.Text = Settings.WhisperModel = row.Path; Changed(); RefreshModels(); return; }
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
        SpeechURLBox.Text = Settings.SpeechAPI.BaseURL; SpeechModelBox.Text = Settings.SpeechAPI.Model; UpdatePanels(); Changed();
    }

    private void SpeechAPI_LostFocus(object sender, RoutedEventArgs e)
    {
        Settings.SpeechAPI.BaseURL = SpeechURLBox.Text.Trim(); Settings.SpeechAPI.Model = SpeechModelBox.Text.Trim(); Changed();
    }

    private void SaveSpeechKey_Click(object sender, RoutedEventArgs e) => SaveKey(SpeechKeyBox.Password, "speech");
    private void OpenSpeechKeyPage_Click(object sender, RoutedEventArgs e) => OpenKeyPage(Settings.SpeechAPI.BaseURL);
    private void OpenTextKeyPage_Click(object sender, RoutedEventArgs e) => OpenKeyPage(Settings.TextAPI.BaseURL);
    private static void OpenKeyPage(string baseURL) { if (APIProvider.Matching(baseURL).KeyPage is { } page) Permissions.Open(page); }

    // MARK: Text

    private void TextBackend_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || TextBackendBox.SelectedIndex < 0) return;
        Settings.TextBackend = TextBackends.Available[TextBackendBox.SelectedIndex];
        // The default for screen context depends on where the text goes; keep an explicit choice.
        ScreenContextBox.IsChecked = Settings.ReadsScreenContext;
        UpdatePanels(); UpdateStyle(); Changed();
        if (Settings.TextBackend == TextBackend.Local) RefreshOllama_Click(this, new RoutedEventArgs());
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || StyleBox.SelectedIndex < 0) return;
        Settings.WritingStyle = (WritingStyle)StyleBox.SelectedIndex; UpdateStyle(); Changed();
    }

    private void UpdateStyle()
    {
        StyleDetail.Text = Settings.WritingStyle.Detail();
        StyleExample.Text = Settings.TextBackend == TextBackend.Basic
            ? "基礎整理只轉換字形、標點與個人辭典；要依風格改寫，請選擇本機 AI、Claude 或 API。"
            : $"例：「{WritingStyles.ExampleInput}」→「{Settings.WritingStyle.ExampleOutput()}」";
    }

    private async void RefreshOllama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var names = await LocalTextClient.ModelsAsync();
            OllamaModelBox.ItemsSource = names;
            if (Settings.LocalTextModel is { } current && names.Contains(current)) OllamaModelBox.SelectedItem = current;
            else if (names.Count > 0) OllamaModelBox.SelectedIndex = Math.Max(0, names.IndexOf(LocalTextClient.RecommendedModel));
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
        TextURLBox.Text = Settings.TextAPI.BaseURL; TextModelBox.Text = Settings.TextAPI.Model; UpdatePanels(); Changed();
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

    private void ScreenContext_Changed(object sender, RoutedEventArgs e) { Settings.ReadsScreenContext = ScreenContextBox.IsChecked == true; Changed(); }

    private void ExcludedApps_LostFocus(object sender, RoutedEventArgs e)
    {
        var apps = ExcludedAppsBox.Text.Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (apps.SequenceEqual(Settings.ScreenContextExcludedApps)) return;
        Settings.ScreenContextExcludedApps = apps; Changed();
    }

    // MARK: Dictionary

    private void LoadDictionary() => DictionaryList.ItemsSource = RecognitionHints.Ordered(Settings.Dictionary).Select(e => new DictionaryRow(e)).ToList();

    private void UpdateLearnStatus()
    {
        var learned = Settings.Dictionary.Count(e => e.IsAutomatic);
        var check = controller.LastCorrectionCheck;
        LearnStatus.Text = (learned > 0 ? $"已自動加入 {learned} 個詞彙。" : "修改插入的文字後，App 會比對修改前後的內容。")
            + (check != null ? $" 最近一次（{check.Date:HH:mm}）：{check.Message}。" : "");
    }

    private void Learn_Changed(object sender, RoutedEventArgs e) { Settings.LearnsFromCorrections = LearnBox.IsChecked == true; Changed(); }

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
        foreach (var row in DictionaryList.SelectedItems.Cast<DictionaryRow>().ToList()) controller.RemoveDictionaryEntry(row.Entry);
        LoadDictionary(); UpdateLearnStatus();
    }

    // MARK: Hyday

    private void LoadHyday()
    {
        var root = controller.HydayRoot;
        HydayFolderText.Text = root != null
            ? root + (Settings.HydayFolder == null ? "（沿用 Hyday 的設定）" : "")
            : Settings.HydayFolder != null ? $"找不到 {Settings.HydayFolder}，請重新選擇。" : "找不到 Hyday 的筆記資料夾，請選擇資料夾。";
        HydayDefaultButton.Visibility = Settings.HydayFolder == null ? Visibility.Collapsed : Visibility.Visible;
        AskNotesBox.IsChecked = Settings.AsksHydayNotes;
        LoadVocabulary();
    }

    private void LoadVocabulary()
    {
        var terms = controller.HydayVocabulary;
        VocabularyStatus.Text = controller.ScanningHydayVocabulary ? "正在掃描 Hyday 筆記…"
            : controller.HydayRoot == null ? "設定 Hyday 資料夾後，會讀取筆記中的 @(名稱) 與 #標籤作為辨識提示。"
            : $"從筆記中找到 {terms.Count} 個 @(名稱) 與 #標籤，會作為語音辨識的提示。點一下可加入個人辭典。";
        VocabularyList.Children.Clear();
        var known = Settings.Dictionary.Select(e => e.Term.ToLowerInvariant()).ToHashSet();
        foreach (var term in terms.Take(200))
        {
            var spoken = term.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? term;
            var button = new Button { Content = term, Tag = spoken, Margin = new Thickness(0, 0, 6, 6), IsEnabled = !known.Contains(spoken.ToLowerInvariant()) };
            button.Click += (_, _) =>
            {
                Settings.Dictionary.Add(new DictionaryEntry(spoken)); button.IsEnabled = false;
                LoadDictionary(); Changed();
            };
            VocabularyList.Children.Add(button);
        }
    }

    private void ChooseHydayFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "選擇 Hyday 筆記所在的資料夾" };
        if (dialog.ShowDialog(this) != true) return;
        Settings.HydayFolder = dialog.FolderName; Changed(); LoadHyday(); controller.ReloadHydayVocabulary();
    }

    private void ResetHydayFolder_Click(object sender, RoutedEventArgs e)
    {
        Settings.HydayFolder = null; Changed(); LoadHyday(); controller.ReloadHydayVocabulary();
    }

    private void OpenJournal_Click(object sender, RoutedEventArgs e)
    {
        if (controller.HydayRoot is not { } root) return;
        var path = new HydayVault(root).JournalPath(DateTime.Now);
        Permissions.Open(File.Exists(path) ? path : Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir) ? dir : root);
    }

    private void AskNotes_Changed(object sender, RoutedEventArgs e) { Settings.AsksHydayNotes = AskNotesBox.IsChecked == true; Changed(); }
    private void RescanVocabulary_Click(object sender, RoutedEventArgs e) => controller.ReloadHydayVocabulary();

    // MARK: History

    private void LoadHistory()
    {
        var rows = controller.History.Select(r => new HistoryRow(r, controller.HasRecording(r), controller.Retrying.Contains(r.Id))).ToList();
        HistoryList.ItemsSource = rows;
        RecentList.ItemsSource = rows.Take(5).ToList();
        RecentEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var bytes = controller.RecordingBytes;
        RecordingSize.Text = $"錄音佔用 {bytes / 1_048_576.0:0.#} MB" + (controller.FailedCount > 0 ? $"，{controller.FailedCount} 筆失敗可重新處理" : "");
    }

    private HistoryRow? SelectedRecord => HistoryList.SelectedItem as HistoryRow;

    private void CopyRecord_Click(object sender, RoutedEventArgs e) { if (SelectedRecord is { } row) TextInsertion.Copy(row.Record.Text); }

    private void RetryRecord_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRecord is not { } row) return;
        if (!row.HasRecording) { MessageBox.Show(this, "這筆紀錄沒有保留錄音，無法重新處理。", "Hyday Talk"); return; }
        controller.Retry(row.Record);
    }

    private void DeleteRecord_Click(object sender, RoutedEventArgs e) { if (SelectedRecord is { } row) controller.RemoveRecord(row.Record.Id); }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "要清除全部聽寫紀錄嗎？", "Hyday Talk", MessageBoxButton.OKCancel) == MessageBoxResult.OK) controller.ClearHistory();
    }

    private void Retention_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || RetentionBox.SelectedIndex < 0) return;
        Settings.RecordingRetention = RecordingRetention.Choices[RetentionBox.SelectedIndex]; Changed(); controller.PruneRecordings();
    }

    private void DeleteRecordings_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "要刪除全部錄音嗎？失敗的紀錄將無法重新處理。", "Hyday Talk", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            controller.RemoveAllRecordings();
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

    private void OpenMicrophonePrivacy_Click(object sender, RoutedEventArgs e) => Permissions.OpenMicrophoneSettings();

    private void TypingSpeed_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TypingSpeedBox.Text.Trim(), out var speed) && speed is >= 5 and <= 300)
        {
            if (speed != Settings.TypingWordsPerMinute) { Settings.TypingWordsPerMinute = speed; Changed(); LoadOverview(); }
        }
        else TypingSpeedBox.Text = Settings.TypingWordsPerMinute.ToString();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(controller.DataDirectory);
        Permissions.Open(controller.DataDirectory);
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => quit();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Commit fields still being edited.
        Engine_LostFocus(this, new RoutedEventArgs());
        SpeechAPI_LostFocus(this, new RoutedEventArgs());
        TextAPI_LostFocus(this, new RoutedEventArgs());
        ExcludedApps_LostFocus(this, new RoutedEventArgs());
        TypingSpeed_LostFocus(this, new RoutedEventArgs());
        download?.Cancel();
        controller.PropertyChanged -= Controller_PropertyChanged;
        controller.SaveSettings();
        base.OnClosing(e);
    }

    private sealed record ShortcutRow(string Title, string Keys);
    private sealed record BarRow(double Height, string Tip);

    private sealed class DictionaryRow(DictionaryEntry entry)
    {
        public DictionaryEntry Entry { get; } = entry;
        public string Term => Entry.Term;
        public string Heard => Entry.Misheard ?? "";
        public string Source => Entry.IsAutomatic ? "自動加入" : "手動";
    }

    private sealed class HistoryRow(DictationRecord record, bool hasRecording, bool retrying)
    {
        public DictationRecord Record { get; } = record;
        public bool HasRecording { get; } = hasRecording;
        public string Time { get; } = record.Date.LocalDateTime.ToString("M/d HH:mm");
        public string Action { get; } = record.Action.Title();
        public string Destination { get; } = record.Destination;
        public string Text { get; } = retrying ? "正在重新處理…" : record.Failure is { } failure
            ? $"失敗：{failure}" + (hasRecording ? "（可重新處理）" : "") : record.Text.Replace('\n', ' ');
        public string Caption { get; } = $"{record.Date.LocalDateTime:M/d HH:mm} · {record.Action.Title()} · {record.Destination}";
    }
}

internal sealed class ModelRow(LocalSpeechModel model, string path, bool installed, bool selected) : INotifyPropertyChanged
{
    public LocalSpeechModel Model { get; } = model;
    public string Path { get; } = path;
    public bool Installed { get; } = installed;
    public string Title => Model.Title + (Model.IsRecommended ? "（推薦）" : "") + (selected ? " · 使用中" : "");
    public string Detail => $"{Model.Detail} {Model.SizeLabel}";
    private double? progress;
    public double? Progress { get => progress; set { progress = value; Notify(nameof(ActionTitle)); } }
    public string ActionTitle => Installed ? (selected ? "使用中" : "使用") : progress is { } p ? $"取消 {p:P0}" : "下載";
    public bool CanAct => !(Installed && selected);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
