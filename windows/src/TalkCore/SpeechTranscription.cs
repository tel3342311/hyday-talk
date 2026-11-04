namespace HydayTalk.Core;

public static class SpeechTranscription
{
    /// Recordings are captured as 16 kHz mono 16-bit WAV, which every backend accepts directly.
    public static async Task<string> TranscribeAsync(string file, TalkSettings settings, string key = "",
        IReadOnlyList<string>? hints = null, HttpClient? client = null, CancellationToken cancellation = default)
    {
        var terms = hints ?? RecognitionHints.Terms(settings.Dictionary, []);
        string output;
        switch (settings.SpeechBackend)
        {
            case SpeechBackend.Whisper:
                var prefix = Path.Combine(Path.GetDirectoryName(file)!, "transcript");
                try
                {
                    if (LocalSpeechModel.EngineFor(settings.WhisperModel) == SpeechEngine.Qwen)
                        await ProcessRunner.RunAsync(QwenExecutable(settings), QwenArguments(settings, file, prefix + ".txt", terms),
                            timeout: TimeSpan.FromSeconds(180), cancellation: cancellation);
                    else
                        await ProcessRunner.RunAsync(settings.WhisperExecutable, WhisperArguments(settings, file, prefix, terms),
                            timeout: TimeSpan.FromSeconds(180), cancellation: cancellation);
                }
                catch (OperationCanceledException) { throw; }
                catch { throw new TalkException("本機語音辨識未完成，請確認模型已下載後重試。"); }
                try { output = await File.ReadAllTextAsync(prefix + ".txt", cancellation); }
                catch (IOException) { throw new TalkException("本機模型未回傳文字，請重新選擇模型後重試。"); }
                finally { try { File.Delete(prefix + ".txt"); } catch { } }
                break;
            case SpeechBackend.Api:
                var data = await File.ReadAllBytesAsync(file, cancellation);
                if (data.Length > 25_000_000) throw new TalkException("錄音檔超過上傳大小限制。請縮短錄音。");
                output = await APIClient.TranscriptionAsync(data, settings.SpeechAPI, key, settings.LanguageCode, terms, client, cancellation);
                break;
            default:
                throw new TalkException("Windows 版不支援這個辨識來源，請改用本機模型或 API。");
        }
        return ValidatedText(output);
    }

    /// The Qwen3-ASR engine ships with whisper-cli: in its own transcribe\ folder on Windows, since it
    /// carries its own ggml DLLs, or right next to it.
    public static string QwenExecutable(TalkSettings settings)
    {
        var folder = Path.GetDirectoryName(settings.WhisperExecutable) ?? "";
        var name = OperatingSystem.IsWindows() ? "transcribe-cli.exe" : "transcribe-cli";
        var nested = Path.Combine(folder, "transcribe", name);
        return File.Exists(nested) ? nested : Path.Combine(folder, name);
    }

    public static List<string> QwenArguments(TalkSettings settings, string file, string output, IReadOnlyList<string> hints)
    {
        var arguments = new List<string> { "-q", "-m", Environment.ExpandEnvironmentVariables(settings.WhisperModel), "-l", settings.LanguageCode,
            "--timestamps", "none", "-o", output };
        // The vocabulary is comma separated, so commas inside a term would split it.
        var terms = hints.Select(t => t.Replace(",", "").Trim()).Where(t => t.Length > 0).Take(60).ToList();
        if (terms.Count > 0) arguments.AddRange(["--vocabulary", string.Join(",", terms)]);
        arguments.Add(file);
        return arguments;
    }

    /// Whisper biases toward words in its initial prompt, so dictionary terms are spelled right,
    /// and a Traditional Chinese prompt keeps the output in Traditional script.
    public static List<string> WhisperArguments(TalkSettings settings, string file, string prefix, IReadOnlyList<string> hints)
    {
        var arguments = new List<string> { "-m", Environment.ExpandEnvironmentVariables(settings.WhisperModel), "-f", file,
            "-l", settings.LanguageCode, "-otxt", "-of", prefix, "-nt", "-np" };
        var chinese = settings.Locale == "zh-TW";
        var terms = new List<string>();
        foreach (var term in hints)
            if (Text.Length(string.Join("、", terms.Append(term))) <= 180) terms.Add(term);
        var prompt = chinese
            ? (terms.Count == 0 ? "以下是台灣繁體中文的口語。" : $"以下是台灣繁體中文的口語，可能提到：{string.Join("、", terms)}。")
            : (terms.Count == 0 ? "" : string.Join(", ", terms) + ".");
        if (prompt.Length > 0) arguments.AddRange(["--prompt", prompt]);
        return arguments;
    }

    public static string ValidatedText(string output)
    {
        var lines = output.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).Where(line =>
        {
            var trimmed = Text.TrimSpaces(line);
            if (trimmed.ToUpperInvariant() is "[BLANK_AUDIO]" or "[NO_SPEECH]") return false;
            var runes = trimmed.EnumerateRunes().ToList();
            // CLI frames and separator-only hallucinations are not speech;
            // retain punctuation within sentences and useful Markdown.
            var boxRule = runes.Count >= 3 && runes.All(r => r.Value is >= 0x2500 and <= 0x257F);
            var asciiRule = runes.Count >= 8 && runes.All(r => r.Value is '-' or '_' or '=' or '*');
            return !boxRule && !asciiRule;
        });
        var text = string.Join("\n", lines).Trim();
        if (text.Length == 0) throw new TalkException("未辨識到文字，請靠近麥克風後重試。");
        return text;
    }
}
