using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HydayTalk.Core;

/// Settings and history as JSON in one folder, keyed like the macOS app so files can be compared.
public sealed class JsonStore(string directory)
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Directory { get; } = directory;

    /// %APPDATA%\HydayTalk on Windows.
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HydayTalk");

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    public T? Load<T>(string name) where T : class
    {
        var path = Path.Combine(Directory, name);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options); }
        catch (JsonException)
        {
            // Preserve unreadable data before a later save can replace it.
            File.Copy(path, path + ".unreadable-" + Guid.NewGuid() + ".bak");
            throw;
        }
    }

    public void Save<T>(T value, string name)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, name);
        var staging = path + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(value, Options));
        File.Move(staging, path, overwrite: true);
    }
}
