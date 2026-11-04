using System.Text.Json.Serialization;

namespace HydayTalk.Core;

public enum SpeechBackend { System, Whisper, Cli, Api }

public enum TextBackend { Basic, Local, Cli, Api, Claude }

public enum DictationLayout { Automatic, Paragraph, List }

public enum VoiceAction { Dictate, Translate, Ask, Hyday }

public enum WritingStyle { Verbatim, Natural, Professional }

public static class SpeechBackends
{
    /// System recognition (Apple Speech) and the CLI backend are macOS-only; Windows offers these.
    public static readonly SpeechBackend[] Available = [SpeechBackend.System, SpeechBackend.Whisper, SpeechBackend.Api];

    public static string Title(this SpeechBackend backend) => backend switch
    {
        SpeechBackend.System => "系統辨識",
        SpeechBackend.Whisper => "本機模型",
        SpeechBackend.Cli => "CLI",
        _ => "API Key",
    };

    public static string Detail(this SpeechBackend backend) => backend switch
    {
        SpeechBackend.System => "使用 Windows 語音辨識",
        SpeechBackend.Whisper => "在這台電腦離線辨識",
        SpeechBackend.Cli => "自訂辨識工具與執行參數",
        _ => "連接 OpenAI 相容的服務",
    };
}

public static class TextBackends
{
    public static readonly TextBackend[] Available = [TextBackend.Basic, TextBackend.Local, TextBackend.Claude, TextBackend.Api];

    public static string Title(this TextBackend backend) => backend switch
    {
        TextBackend.Basic => "基礎整理",
        TextBackend.Local => "本機 AI",
        TextBackend.Cli => "CLI",
        TextBackend.Api => "API Key",
        _ => "Claude",
    };
}

public static class DictationLayouts
{
    public static string Instruction(this DictationLayout layout) => layout switch
    {
        DictationLayout.Automatic => "依內容自動排版，同一段輸出可同時包含段落與編號列表：一般敘述保留為段落，話題轉換時換段，超過三句的敘述依意思分成數段，每段二到三句，段落之間空一行，絕對不要把四句以上的敘述擠成一整段；遇到第一、第二、第三等連續列舉，或用「再來」「然後」「接著」「還有」「另外」「最後」串起的多個並列事項、步驟或修改項目，改用編號列表，並刪掉這些串接詞，每項獨立一行並完整保留該項內容，行首使用阿拉伯數字與句點，例如1. 內容，不以第一、第二作為行首；列表前後的敘述仍保留為段落，不要為一般敘述強行條列。範例：逐字稿「這次改了兩個地方然後第一個是按鈕改成藍色再來是標題字變大了」應輸出「這次改了兩個地方：\n1. 按鈕改成藍色。\n2. 標題字變大。」；逐字稿「新版首頁上線了速度比之前快很多大家反應都不錯不過手機版還有跑版的問題按鈕會疊在一起我們下週要先修這個」應分兩段輸出「新版首頁上線了，速度比之前快很多，大家反應都不錯。\n\n不過手機版還有跑版的問題，按鈕會疊在一起，我們下週要先修這個。」",
        DictationLayout.Paragraph => "以完整句子與段落呈現，不使用列表。",
        _ => "輸出必須為編號列表，每個獨立事項各佔一行，依序以1.、2.、3.開頭；依語意將原文中的多個事項分開，每項保留完整資訊。只有一個事項就輸出單項列表，禁止虛構事項。",
    };
}

public static class VoiceActions
{
    public static string Title(this VoiceAction action) => action switch
    {
        VoiceAction.Dictate => "聽寫",
        VoiceAction.Translate => "翻譯",
        VoiceAction.Ask => "提問 / 編輯",
        _ => "記到 Hyday",
    };

    /// Windows keyboards expose no fn key to applications, so Right Ctrl takes its place.
    public static string Shortcut(this VoiceAction action) => action switch
    {
        VoiceAction.Dictate => "右 Ctrl",
        VoiceAction.Translate => "右 Ctrl + 左 Shift",
        VoiceAction.Ask => "右 Ctrl + 左 Alt",
        _ => "右 Ctrl + Space",
    };
}

public static class WritingStyles
{
    public static string Title(this WritingStyle style) => style switch
    {
        WritingStyle.Natural => "自然",
        WritingStyle.Verbatim => "保留原話",
        _ => "正式",
    };

    public static string Detail(this WritingStyle style) => style switch
    {
        WritingStyle.Natural => "保留口語語氣與情緒並補上標點，刪除多餘的贅詞與思考片段，適合日常訊息。",
        WritingStyle.Verbatim => "保留辨識原文的字句，僅套用字形、既有標點與個人辭典，不使用 AI 改寫。",
        _ => "刪除所有贅詞，改用清楚簡潔的書面用語並補上標點，適合工作郵件與文件。",
    };

    public const string ExampleInput = "呃明天下午3點要開會，然後再麻煩Amy把API文件帶過來這樣";

    public static string ExampleOutput(this WritingStyle style) => style switch
    {
        WritingStyle.Natural => "明天下午3點要開會，再麻煩Amy把API文件帶過來。",
        WritingStyle.Verbatim => ExampleInput,
        _ => "明天下午3點開會，請Amy攜帶API文件。",
    };

    public static string Instruction(this WritingStyle style) => style switch
    {
        WritingStyle.Natural => "保留說話者自己的用詞，只刪不換，不把口語詞換成意思相同的書面詞，例如我覺得就保持我覺得；自行判斷哪些簡單口語能帶出說話者的情緒與語氣轉折，感受、強調、遲疑、句首的嘆氣聲、無奈或勉強答應的語氣都算情緒要保留，其餘不帶意思的贅詞與說到一半的思考片段都刪除。",
        WritingStyle.Verbatim => "保留辨識原文，不改寫字句。",
        _ => "贅詞、語助詞、口頭禪與思考過程的片段全部刪除，一個都不保留；將口語用詞換成簡潔書面用詞，可調整句構並保留原意；使用「請」取代「麻煩」等口語請求詞，不疊加客套詞，不要加入原文沒有的「請問」「能否」「是否」等問句，保留「可能」「應該」等原有的確定程度；正式風格也必須保留原文的阿拉伯數字，不改成國字數字。",
    };
}

/// Same JSON keys as the macOS app: `replacement` is the term, `heard` the optional misheard spelling.
public sealed class DictionaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Heard { get; set; } = "";
    public string Replacement { get; set; } = "";
    [JsonPropertyName("automatic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Automatic { get; set; }

    public DictionaryEntry() { }
    public DictionaryEntry(string term, string? misheard = null, bool isAutomatic = false)
    {
        Heard = misheard ?? ""; Replacement = term; Automatic = isAutomatic ? true : null;
    }

    [JsonIgnore] public bool IsAutomatic => Automatic == true;
    [JsonIgnore] public string Term { get => Replacement; set => Replacement = value; }
    /// Only manual entries may name a misheard spelling, which is then replaced by the term.
    [JsonIgnore] public string? Misheard => Heard.Length == 0 || IsAutomatic ? null : Heard;
    /// A term can be misheard several ways; they are entered separated by 、 or commas.
    [JsonIgnore]
    public IReadOnlyList<string> MisheardSpellings => (Misheard ?? "")
        .Split(['、', ',', '，'])
        .Select(s => s.Trim(' ', '\t'))
        .Where(s => s.Length > 0)
        .ToList();
}

public sealed class APISettings
{
    public string BaseURL { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "";
    public APISettings() { }
    public APISettings(string model, string baseURL = "https://api.openai.com/v1") { Model = model; BaseURL = baseURL; }
    public APISettings Copy() => new(Model, BaseURL);
}

public sealed class TalkSettings
{
    public SpeechBackend SpeechBackend { get; set; } = SpeechBackend.Whisper;
    public TextBackend TextBackend { get; set; } = TextBackend.Basic;
    public string? LocalTextModel { get; set; }
    public DictationLayout? DictationLayout { get; set; }
    public string Locale { get; set; } = "zh-TW";
    public string TargetLanguage { get; set; } = "英文";
    public WritingStyle WritingStyle { get; set; } = WritingStyle.Natural;
    public bool AutoInsert { get; set; } = true;
    public bool SaveHistory { get; set; } = true;
    public bool InteractionSounds { get; set; } = true;
    public string WhisperExecutable { get; set; } = "";
    public string WhisperModel { get; set; } = "";
    public APISettings SpeechAPI { get; set; } = new("gpt-transcribe");
    public APISettings TextAPI { get; set; } = new("gpt-4.1-mini");
    public List<DictionaryEntry> Dictionary { get; set; } = [];
    public string? ClaudeTextModel { get; set; }
    public string? HydayFolder { get; set; }
    /// Saved per-device so the recorder can be pinned to a headset instead of the default input.
    public int? MicrophoneDevice { get; set; }
    public bool SystemOfflineOnly { get; set; }
    // Optional so settings saved by earlier versions still read; null means the default.
    public bool? LearnFromCorrections { get; set; }
    public List<DictionaryEntry>? BlockedCorrections { get; set; }
    public int? TypingSpeed { get; set; }
    public bool? AskHydayNotes { get; set; }
    public bool? ReadScreenContext { get; set; }
    public List<string>? ScreenContextExcluded { get; set; }
    public string? KeepRecordings { get; set; }

    [JsonIgnore] public bool LearnsFromCorrections { get => LearnFromCorrections ?? true; set => LearnFromCorrections = value; }
    /// Used to estimate time saved; 40 sits in the common 20–50 range for Taiwanese Zhuyin typists.
    [JsonIgnore] public int TypingWordsPerMinute { get => TypingSpeed ?? 40; set => TypingSpeed = value; }
    /// Text questions answer from notes found in the Hyday folder; off by default.
    [JsonIgnore] public bool AsksHydayNotes { get => AskHydayNotes ?? false; set => AskHydayNotes = value; }
    [JsonIgnore] public RecordingRetention RecordingRetention { get => RecordingRetention.Parse(KeepRecordings); set => KeepRecordings = value.Code; }
    /// Local text AI keeps screen text on this computer, so it is on by default; an API or Claude sends it out, so it is off.
    [JsonIgnore]
    public bool ReadsScreenContext
    {
        get => ReadScreenContext ?? (TextBackend != TextBackend.Api && TextBackend != TextBackend.Claude);
        set => ReadScreenContext = value;
    }
    [JsonIgnore]
    public List<string> ScreenContextExcludedApps { get => ScreenContextExcluded ?? [.. ScreenContext.DefaultExcludedApps]; set => ScreenContextExcluded = value; }

    public bool ShouldReadScreenContext(VoiceAction action, string app) =>
        ReadsScreenContext && action != VoiceAction.Ask && TextBackend != TextBackend.Basic && RequiresTextModel(action)
        && !ScreenContextExcludedApps.Any(a => string.Equals(a, app, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore] public string LanguageCode => Locale.Length >= 2 ? Locale[..2] : Locale;
    [JsonIgnore] public string ClaudeModel => string.IsNullOrEmpty(ClaudeTextModel) ? ClaudeTextModels.Recommended : ClaudeTextModel;

    public bool RequiresTextModel(VoiceAction action)
    {
        // Without a text AI, Hyday entries are stored as one cleaned list item.
        if (action == VoiceAction.Hyday) return TextBackend != TextBackend.Basic;
        return action != VoiceAction.Dictate || (TextBackend != TextBackend.Basic && WritingStyle != WritingStyle.Verbatim);
    }

    public TalkSettings Snapshot() => JsonStore.Clone(this);
}

public static class ClaudeTextModels
{
    public const string Recommended = "claude-haiku-5-5";
    public static readonly (string Id, string Title, string Detail)[] All =
    [
        ("claude-haiku-5-5", "Haiku 5.5", "速度最快，適合日常聽寫。"),
        ("claude-sonnet-5-5", "Sonnet 5.5", "整理得更細，回應稍慢。"),
        ("claude-opus-5-5", "Opus 5.5", "品質最好，最慢，也最耗用量。"),
    ];
}

public sealed class DictationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;
    public string RawText { get; set; } = "";
    public string Text { get; set; } = "";
    public VoiceAction Action { get; set; }
    public SpeechBackend Backend { get; set; }
    public double Duration { get; set; }
    public string Destination { get; set; } = "";
    public string? Failure { get; set; }
}

public sealed class TalkException(string message) : Exception(message);
