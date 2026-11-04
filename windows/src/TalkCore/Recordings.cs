namespace HydayTalk.Core;

public enum RetentionKind { Off, FailedOnly, Days, Forever }

/// How long dictation audio stays on this computer, so a failed transcription can run again
/// without speaking twice. Failed recordings wait for a retry or a manual delete.
public sealed record RecordingRetention(RetentionKind Kind, int Days = 0)
{
    public static readonly RecordingRetention Off = new(RetentionKind.Off);
    public static readonly RecordingRetention FailedOnly = new(RetentionKind.FailedOnly);
    public static readonly RecordingRetention Forever = new(RetentionKind.Forever);
    public static RecordingRetention ForDays(int days) => new(RetentionKind.Days, days);
    public static readonly RecordingRetention Recommended = ForDays(7);
    public static readonly RecordingRetention[] Choices = [Off, FailedOnly, ForDays(1), ForDays(7), ForDays(30), Forever];

    public string Title => Kind switch
    {
        RetentionKind.Off => "不保留",
        RetentionKind.FailedOnly => "只保留失敗的",
        RetentionKind.Days => $"{Days} 天",
        _ => "永久",
    };

    /// Stored in settings as off, failedOnly, forever or days:N.
    public string Code => Kind == RetentionKind.Days ? $"days:{Days}" : Kind switch
    {
        RetentionKind.Off => "off", RetentionKind.FailedOnly => "failedOnly", _ => "forever",
    };

    public static RecordingRetention Parse(string? code) => code switch
    {
        "off" => Off,
        "failedOnly" => FailedOnly,
        "forever" => Forever,
        { } c when c.StartsWith("days:") && int.TryParse(c[5..], out var d) && d > 0 => ForDays(d),
        _ => Recommended,
    };

    /// Whether a recording stays after it is processed. Without saved history there is no
    /// record to replay a success from, so only failures are kept.
    public bool Keeps(bool failed, bool savesHistory) => Kind switch
    {
        RetentionKind.Off => false,
        RetentionKind.FailedOnly => failed,
        _ => failed || savesHistory,
    };

    public bool Expired(DateTimeOffset recordedAt, bool failed, DateTimeOffset? now = null) =>
        !failed && Kind == RetentionKind.Days && ((now ?? DateTimeOffset.Now) - recordedAt).TotalSeconds > Days * 86_400.0;

    /// Recording files to delete: expired ones and any without a record left to replay them.
    public List<Guid> Stale(IEnumerable<Guid> files, IEnumerable<RecordingInfo> records, DateTimeOffset? now = null)
    {
        var byId = new Dictionary<Guid, RecordingInfo>();
        foreach (var record in records) byId.TryAdd(record.Id, record);
        return files.Where(id => !byId.TryGetValue(id, out var r) || Expired(r.Date, r.Failed, now)).ToList();
    }
}

public sealed record RecordingInfo(Guid Id, DateTimeOffset Date, bool Failed);

/// Recordings saved as 16 kHz mono WAV, named by their dictation record's id.
public sealed class RecordingStore(string folder)
{
    public string Folder { get; } = folder;
    public string PathFor(Guid id) => Path.Combine(Folder, id + ".wav");
    public bool Has(Guid id) => File.Exists(PathFor(id));

    public void Save(string file, Guid id)
    {
        Directory.CreateDirectory(Folder);
        File.Copy(file, PathFor(id), overwrite: true);
    }

    public void Remove(Guid id) { try { File.Delete(PathFor(id)); } catch { } }

    public List<Guid> Ids => Directory.Exists(Folder)
        ? Directory.EnumerateFiles(Folder, "*.wav").Select(f => Guid.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).ToList()
        : [];

    public long TotalBytes => Ids.Sum(id => { try { return new FileInfo(PathFor(id)).Length; } catch { return 0L; } });

    public void RemoveAll() { foreach (var id in Ids) Remove(id); }
}
