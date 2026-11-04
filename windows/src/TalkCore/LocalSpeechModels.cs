using System.Security.Cryptography;

namespace HydayTalk.Core;

/// whisper.cpp runs GGML `.bin` Whisper models; transcribe.cpp runs GGUF Qwen3-ASR models.
public enum SpeechEngine { Whisper, Qwen }

public sealed record LocalSpeechModel(string Id, string Title, string Detail, string Filename, long Bytes, string Sha256,
    string Repository = "ggerganov/whisper.cpp")
{
    public SpeechEngine Engine => EngineFor(Filename);
    public static SpeechEngine EngineFor(string path) => path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? SpeechEngine.Qwen : SpeechEngine.Whisper;
    public Uri DownloadURL => new($"https://huggingface.co/{Repository}/resolve/main/{Filename}");
    public string SizeLabel => $"{Bytes / 1_000_000_000.0:0.##} GB";

    public static readonly LocalSpeechModel WhisperTurbo = new("large-v3-turbo-q5_0", "Whisper Large v3 Turbo",
        "支援中文與多語辨識。", "ggml-large-v3-turbo-q5_0.bin", 574_041_195,
        "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2");
    public static readonly LocalSpeechModel QwenSmall = new("qwen3-asr-0.6b-q8_0", "Qwen3-ASR 0.6B",
        "中文口語與中英夾雜辨識較準，速度快。", "Qwen3-ASR-0.6B-Q8_0.gguf", 850_423_456,
        "f081b2d5e23bd669d92cc331d722a8a0681943b8e6f34b48996fd5c319b5acd8", "handy-computer/Qwen3-ASR-0.6B-gguf");
    public static readonly LocalSpeechModel QwenLarge = new("qwen3-asr-1.7b-q6_k", "Qwen3-ASR 1.7B",
        "辨識最準確，需要較多儲存空間與記憶體。", "Qwen3-ASR-1.7B-Q6_K.gguf", 1_692_554_208,
        "c75a961b7134a6c952d89797865cb0d0376876185aee04ef6d12c31c2952e4e1", "handy-computer/Qwen3-ASR-1.7B-gguf");
    /// Qwen3-ASR 1.7B fine-tuned for Taiwan Mandarin (TEA-ASR 1.1, MIT), converted for transcribe.cpp.
    public static readonly LocalSpeechModel Tea = new("tea-asr-1.1-q6_k", "TEA-ASR 1.1",
        "針對台灣口語調校，直接輸出繁體與台灣用詞，中英夾雜也準。", "TEA-ASR-1.1-Q6_K.gguf", 1_692_286_464,
        "2417fa52d49f852bfc11704190e10ddb681c343a661a3a3d1bb213876c780b68", "mukiwu/TEA-ASR-1.1-transcribe-gguf");
    public static readonly LocalSpeechModel[] All = [WhisperTurbo, QwenSmall, QwenLarge, Tea];
    public bool IsRecommended => this == QwenSmall || this == Tea;
    public override string ToString() => $"{Title}（{SizeLabel}）";
}

public static class LocalModelInstaller
{
    /// Streams the model into a staging file, verifying size and SHA-256 before an atomic
    /// replacement, so a cancelled or corrupt download keeps the previous model.
    public static async Task<string> DownloadAsync(LocalSpeechModel model, string directory, IProgress<double>? progress = null,
        HttpClient? client = null, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, model.Filename);
        var staging = Path.Combine(directory, ".download-" + Guid.NewGuid());
        try
        {
            using var response = await (client ?? APIClient.Shared).GetAsync(model.DownloadURL, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode) throw new TalkException("無法下載模型，請確認網路後重試。");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var output = File.Create(staging))
                await CopyVerifiedAsync(input, output, model, progress, cancellation);
            File.Move(staging, destination, overwrite: true);
            return destination;
        }
        catch (HttpRequestException) { throw new TalkException("無法下載模型，請確認網路後重試。"); }
        finally { try { File.Delete(staging); } catch { } }
    }

    internal static async Task CopyVerifiedAsync(Stream input, Stream? output, LocalSpeechModel model, IProgress<double>? progress, CancellationToken cancellation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20]; long count = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
        {
            count += read;
            if (count > model.Bytes) throw new TalkException("模型檔案驗證失敗，請重新下載。");
            hash.AppendData(buffer, 0, read);
            if (output != null) await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
            progress?.Report((double)count / model.Bytes);
        }
        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (count != model.Bytes || digest != model.Sha256) throw new TalkException("模型檔案驗證失敗，請重新下載。");
    }
}
