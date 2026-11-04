namespace HydayTalk.Core;

public sealed record PipelineResult(string Text, string Notice);

/// The part of a dictation after recognition: clean-up, the optional text AI pass and output
/// formatting. Platform pieces (API keys, the Claude process) are passed in.
public sealed class TextPipeline(Func<string, string> readKey, Func<ClaudeSession?> claude, HttpClient? client = null)
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

    public async Task<PipelineResult> RunAsync(string raw, VoiceAction action, string selection, TalkSettings settings, CancellationToken cancellation)
    {
        var cleaned = TextProcessing.Clean(raw, settings);
        var output = cleaned; var notice = "";
        if (settings.RequiresTextModel(action) && settings.TextBackend != TextBackend.Basic)
        {
            try
            {
                var processed = await CompleteAsync(TextProcessing.Prompt(action, cleaned, selection, settings), settings, cancellation);
                output = action == VoiceAction.Dictate ? TextProcessing.CheckedDictationOutput(processed, cleaned) : processed;
                if (action == VoiceAction.Dictate && (settings.DictationLayout ?? DictationLayout.Automatic) == DictationLayout.Automatic
                    && ParagraphLayout.NeedsLayout(output))
                {
                    try { output = ParagraphLayout.Accept(await CompleteAsync(ParagraphLayout.Prompt(output), settings, cancellation), output); }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch when (action == VoiceAction.Dictate)
            {
                output = cleaned; notice = "AI 整理未完成，已保留原始辨識文字。";
            }
        }
        return new PipelineResult(TextProcessing.FormatOutput(output, action, settings), notice);
    }

    /// Checks a snapshot of the settings before recording, so problems surface before the user speaks.
    public void Validate(TalkSettings settings, VoiceAction action)
    {
        if (action == VoiceAction.Hyday) throw new TalkException("Windows 版尚未支援記到 Hyday。");
        switch (settings.SpeechBackend)
        {
            case SpeechBackend.Whisper:
                var engine = LocalSpeechModel.EngineFor(settings.WhisperModel) == SpeechEngine.Qwen
                    ? SpeechTranscription.QwenExecutable(settings) : settings.WhisperExecutable;
                if (!File.Exists(engine)) throw new TalkException("找不到本機辨識引擎，請在「語音辨識」選擇 whisper-cli.exe。");
                if (!File.Exists(settings.WhisperModel)) throw new TalkException("請在「語音辨識」下載並選擇本機模型。");
                break;
            case SpeechBackend.Api: ValidateAPI(settings.SpeechAPI, "speech"); break;
            default: throw new TalkException("Windows 版不支援這個辨識來源，請改用本機模型或 API。");
        }
        if (action != VoiceAction.Dictate && settings.TextBackend == TextBackend.Basic)
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
