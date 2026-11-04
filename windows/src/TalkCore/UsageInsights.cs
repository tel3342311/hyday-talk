using System.Globalization;

namespace HydayTalk.Core;

/// One day of dictation usage. Holds only counts, never what was said.
public sealed class DailyUsage
{
    public string Day { get; set; } = "";
    public int Words { get; set; }
    public double Seconds { get; set; }
    public int Count { get; set; }
}

public sealed class AppTally
{
    public int Count { get; set; }
    public int Words { get; set; }
}

/// Long-term usage kept apart from history, so clearing or disabling history keeps the figures.
public sealed class UsageLedger
{
    public List<DailyUsage> Days { get; set; } = [];
    public int[] Hours { get; set; } = new int[24];
    public Dictionary<string, AppTally> Apps { get; set; } = [];
    public Dictionary<string, int> Actions { get; set; } = [];
    public double LongestSeconds { get; set; }

    public static UsageLedger Seeded(IEnumerable<DictationRecord> history)
    {
        var ledger = new UsageLedger();
        foreach (var item in history.OrderBy(r => r.Date))
            ledger.Record(TextProcessing.WordCount(item.Text), item.Duration, item.Date.LocalDateTime, item.Destination, item.Action);
        return ledger;
    }

    public void Record(int words, double seconds, DateTime? at = null, string? app = null, VoiceAction? action = null)
    {
        var date = at ?? DateTime.Now;
        if (Hours.Length != 24) Hours = new int[24];
        Hours[date.Hour]++;
        if (!string.IsNullOrEmpty(app))
        {
            if (!Apps.TryGetValue(app, out var tally)) Apps[app] = tally = new AppTally();
            tally.Count++; tally.Words += words;
        }
        if (action is { } a) { var key = JsonNamingCamel(a); Actions[key] = Actions.GetValueOrDefault(key) + 1; }
        LongestSeconds = Math.Max(LongestSeconds, seconds);
        var day = Key(date);
        var existing = Days.Find(d => d.Day == day);
        if (existing != null) { existing.Words += words; existing.Seconds += seconds; existing.Count++; }
        else { Days.Add(new DailyUsage { Day = day, Words = words, Seconds = seconds, Count = 1 }); Days.Sort((x, y) => string.CompareOrdinal(x.Day, y.Day)); }
    }

    internal static string JsonNamingCamel(VoiceAction action) { var s = action.ToString(); return char.ToLowerInvariant(s[0]) + s[1..]; }
    internal static string Key(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed record WeekUsage(DateTime Start, int Words, double Seconds)
{
    public int? WordsPerMinute => Seconds > 0 ? (int)Math.Round(Words / (Seconds / 60)) : null;
}

public sealed record AppUsage(string Name, int Count, int Words);

public sealed class UsageInsights
{
    public int[] HourCounts { get; }
    public List<AppUsage> Apps { get; }
    public Dictionary<VoiceAction, int> ActionCounts { get; }
    public DailyUsage? BestDay { get; }
    public double LongestSession { get; }
    public int TotalWords { get; }
    public double TotalMinutes { get; }
    public double SavedMinutes { get; }
    public int TodayWords { get; }
    public double WeekSavedMinutes { get; }
    public int YesterdayWords { get; }
    /// Days 8 to 14 before today, for comparing with `WeekSavedMinutes`.
    public double PreviousWeekSavedMinutes { get; }
    public int ActiveDays { get; }
    public int CurrentStreak { get; }
    public int LongestStreak { get; }
    private readonly List<DailyUsage> days;
    private readonly Dictionary<string, int> wordsByDay;
    private readonly DateTime today;

    /// The hour with the most dictations, or null before any were recorded by hour.
    public int? PeakHour => HourCounts.Max() is var peak and > 0 ? Array.IndexOf(HourCounts, peak) : null;
    public int? WordsPerMinute => TotalMinutes > 0 ? (int)Math.Round(TotalWords / TotalMinutes) : null;

    /// Time saved estimates typing the same words at `typingSpeed` words per minute, minus the time spent speaking.
    public UsageInsights(UsageLedger ledger, int typingSpeed, DateTime? now = null)
    {
        today = (now ?? DateTime.Now).Date;
        days = ledger.Days;
        HourCounts = ledger.Hours.Length == 24 ? ledger.Hours : new int[24];
        Apps = ledger.Apps.Select(p => new AppUsage(p.Key, p.Value.Count, p.Value.Words))
            .OrderByDescending(a => a.Count).ThenByDescending(a => a.Words).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
        ActionCounts = ledger.Actions.Select(p => (Ok: Enum.TryParse<VoiceAction>(p.Key, true, out var a), a, p.Value))
            .Where(p => p.Ok).ToDictionary(p => p.a, p => p.Value);
        BestDay = days.Count > 0 ? days.MaxBy(d => d.Words) : null;
        LongestSession = ledger.LongestSeconds;
        double speed = Math.Max(1, typingSpeed);
        double Saved(IEnumerable<DailyUsage> list) { var l = list.ToList(); return Math.Max(0, l.Sum(d => d.Words) / speed - l.Sum(d => d.Seconds) / 60); }
        TotalWords = days.Sum(d => d.Words);
        TotalMinutes = days.Sum(d => d.Seconds) / 60;
        SavedMinutes = Saved(days);
        wordsByDay = days.GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.Sum(d => d.Words));
        var week = Enumerable.Range(0, 7).Select(i => UsageLedger.Key(today.AddDays(-i))).ToHashSet();
        WeekSavedMinutes = Saved(days.Where(d => week.Contains(d.Day)));
        var previous = Enumerable.Range(7, 7).Select(i => UsageLedger.Key(today.AddDays(-i))).ToHashSet();
        PreviousWeekSavedMinutes = Saved(days.Where(d => previous.Contains(d.Day)));
        YesterdayWords = wordsByDay.GetValueOrDefault(UsageLedger.Key(today.AddDays(-1)));
        TodayWords = wordsByDay.GetValueOrDefault(UsageLedger.Key(today));
        var dates = days.Where(d => d.Count > 0).Select(d => DateTime.TryParseExact(d.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : (DateTime?)null)
            .Where(d => d != null).Select(d => d!.Value.Date).Distinct().Order().ToList();
        ActiveDays = dates.Count;
        int longest = 0, run = 0; DateTime? last = null;
        foreach (var date in dates)
        {
            run = last is { } p && (date - p).Days == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run); last = date;
        }
        LongestStreak = longest;
        CurrentStreak = dates.Count > 0 && (dates[^1] == today || dates[^1] == today.AddDays(-1)) ? run : 0;
    }

    /// Rolling seven-day windows, oldest first; the last one ends today.
    public List<WeekUsage> Weekly(int weeks) => Enumerable.Range(0, weeks).Reverse().Select(offset =>
    {
        var start = today.AddDays(-(offset * 7 + 6));
        var keys = Enumerable.Range(0, 7).Select(i => UsageLedger.Key(start.AddDays(i))).ToHashSet();
        var window = days.Where(d => keys.Contains(d.Day)).ToList();
        return new WeekUsage(start, window.Sum(d => d.Words), window.Sum(d => d.Seconds));
    }).ToList();

    /// Words per day, oldest first, ending today.
    public List<int> DailyWords(int count) => Enumerable.Range(0, count).Reverse()
        .Select(offset => wordsByDay.GetValueOrDefault(UsageLedger.Key(today.AddDays(-offset)))).ToList();
}
