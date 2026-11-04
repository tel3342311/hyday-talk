namespace HydayTalk.Core;

public sealed record PipelineResult(string Text, string Notice, bool AIFormatted);

/// The part of a dictation after recognition: clean-up, the optional text AI pass and output
/// formatting. Platform pieces (API keys, the Claude process, the Hyday folder) are passed in.
public sealed class TextPipeline(Func<string, string> readKey, Func<ClaudeSession?> claude, Func<string?> hydayRoot, HttpClient? client = null)
{
    public async Task<string> CompleteAsync(string prompt, TalkSettings settings, CancellationToken cancellation)
    {
        switch (settings.TextBackend)
        {
            case TextBackend.Local:
                return await LocalTextClient.CompleteAsync(prompt, settings.LocalTextModel ?? "", client, cancellation);
            case TextBackend.Api:
                return await APIClient.CompleteAsync(prompt, settings.TextAPI, readKey("text"), client, cancellation);
            case TextBackend.Claude:
                var session = claude() ?? throw new TalkException("找不到 Claude Code，請先安裝並登入 Claude Code。");
                return await session.CompleteAsync(prompt, settings.ClaudeModel, cancellation: cancellation);
            default:
                throw new TalkException("基礎整理沒有文字 AI。");
        }
    }

    /// Notes for 問筆記: searched only when it is on and a text AI will read them.
    private async Task<List<HydayNote>?> NotesAsync(string text, string selection, TalkSettings settings, CancellationToken cancellation)
    {
        if (!settings.AsksHydayNotes || settings.TextBackend == TextBackend.Basic || hydayRoot() is not { } root) return null;
        var query = string.Join(" ", new[] { text, selection }.Where(s => s.Length > 0));
        if (query.Length > 200) query = query[..200];
        return await HydaySearchService.NotesAsync(root, query, cancellation: cancellation);
    }

    private async Task<string> ProcessAsync(string text, string selection, VoiceAction action, TalkSettings settings,
        ScreenContext? context, CancellationToken cancellation)
    {
        var notes = action == VoiceAction.Ask ? await NotesAsync(text, selection, settings, cancellation) : null;
        return await CompleteAsync(TextProcessing.Prompt(action, text, selection, settings, notes, context), settings, cancellation);
    }

    public async Task<PipelineResult> RunAsync(string raw, VoiceAction action, string selection, TalkSettings settings,
        CancellationToken cancellation, ScreenContext? context = null)
    {
        var cleaned = TextProcessing.Clean(raw, settings);
        var output = cleaned; var notice = ""; var aiFormatted = false;
        if (settings.RequiresTextModel(action) && settings.TextBackend != TextBackend.Basic)
        {
            try
            {
                var processed = await ProcessAsync(cleaned, selection, action, settings, context, cancellation);
                // The AI copied screen text; redo the clean-up without it.
                if (context != null && context.Leaked(processed, cleaned))
                    processed = await ProcessAsync(cleaned, selection, action, settings, null, cancellation);
                output = action == VoiceAction.Dictate ? TextProcessing.CheckedDictationOutput(processed, cleaned) : processed;
                if (action == VoiceAction.Dictate && (settings.DictationLayout ?? DictationLayout.Automatic) == DictationLayout.Automatic
                    && ParagraphLayout.NeedsLayout(output))
                {
                    try { output = ParagraphLayout.Accept(await CompleteAsync(ParagraphLayout.Prompt(output), settings, cancellation), output); }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
                aiFormatted = true;
            }
            catch (OperationCanceledException) { throw; }
            catch when (action is VoiceAction.Dictate or VoiceAction.Hyday)
            {
                output = cleaned;
                notice = action == VoiceAction.Dictate ? "AI 整理未完成，已保留原始辨識文字。" : "AI 整理未完成，已記成一般列點。";
            }
        }
        return new PipelineResult(TextProcessing.FormatOutput(output, action, settings), notice, aiFormatted);
    }

    /// The journal lines for a Hyday entry: AI-classified marks, or one plain bullet, stamped with when it was spoken.
    public static string HydayEntry(PipelineResult result, DateTime spokeAt)
    {
        var time = HydayEntryFormatting.Time(spokeAt);
        var formatted = result.AIFormatted ? HydayEntryFormatting.Entry(result.Text, time) : "";
        return HydayEntryFormatting.Stamped(formatted.Length == 0 ? HydayVault.Bullet(result.Text) : formatted, time);
    }

    /// Checks a snapshot of the settings before recording, so problems surface before the user speaks.
    public void Validate(TalkSettings settings, VoiceAction action)
    {
        if (action == VoiceAction.Hyday && hydayRoot() == null) throw new TalkException("請先在 Hyday 頁設定 Hyday 資料夾。");
        switch (settings.SpeechBackend)
        {
            case SpeechBackend.System: break;
            case SpeechBackend.Whisper:
                var engine = LocalSpeechModel.EngineFor(settings.WhisperModel) == SpeechEngine.Qwen
                    ? SpeechTranscription.QwenExecutable(settings) : settings.WhisperExecutable;
                if (!File.Exists(engine)) throw new TalkException("找不到本機辨識引擎，請在「語音辨識」選擇 whisper-cli.exe。");
                if (!File.Exists(settings.WhisperModel)) throw new TalkException("請在「語音辨識」下載並選擇本機模型。");
                break;
            case SpeechBackend.Api: ValidateAPI(settings.SpeechAPI, "speech"); break;
            default: throw new TalkException("Windows 版不支援這個辨識來源，請改用本機模型或 API。");
        }
        if (action is VoiceAction.Translate or VoiceAction.Ask && settings.TextBackend == TextBackend.Basic)
            throw new TalkException("翻譯與提問需要文字 AI，請在「文字整理」選擇本機 AI 或 API Key。");
        if (!settings.RequiresTextModel(action)) return;
        switch (settings.TextBackend)
        {
            case TextBackend.Local when string.IsNullOrEmpty(settings.LocalTextModel):
                throw new TalkException("請在「文字整理」選擇本機文字模型。");
            case TextBackend.Api: ValidateAPI(settings.TextAPI, "text"); break;
            case TextBackend.Claude when claude() == null:
                throw new TalkException("找不到 Claude Code，請先安裝並登入 Claude Code，或改用其他整理方式。");
        }
    }

    private void ValidateAPI(APISettings api, string account)
    {
        var endpoint = APIClient.Endpoint(api.BaseURL, "");
        if (api.Model.Trim().Length == 0) throw new TalkException("請先填入模型名稱。");
        if (!APIClient.IsLocal(endpoint) && readKey(account).Length == 0) throw new TalkException("請先儲存 API Key。本機服務可以不填 Key。");
    }
}
