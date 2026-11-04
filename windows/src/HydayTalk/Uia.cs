using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace HydayTalk;

/// UI Automation reads, run off the UI thread with a time budget: a hung app must never stall
/// the shortcut or the panel.
internal static class Uia
{
    public const int TextLimit = 200_000;

    /// The first UI Automation call in a process sets up COM and the provider proxies, which can take
    /// longer than a capture may wait; doing it at start-up keeps the first dictation responsive.
    public static void WarmUp() => Task.Run(() =>
    {
        try { _ = AutomationElement.RootElement.Current.Name; _ = AutomationElement.FocusedElement; Log.Write("UI Automation ready"); }
        catch (Exception error) { Log.Write("UI Automation warm-up: " + error.Message); }
    });

    public static T Run<T>(Func<T> read, int milliseconds, T fallback)
    {
        var task = Task.Run(() => { try { return read(); } catch (Exception error) { Log.Write("UI Automation: " + error.GetType().Name + " " + error.Message); return fallback; } });
        if (task.Wait(milliseconds)) return task.Result;
        Log.Write("UI Automation timed out");
        return fallback;
    }

    public static Task<T> RunAsync<T>(Func<T> read, int milliseconds, T fallback) => Task.Run(() => Run(read, milliseconds, fallback));

    /// Line breaks come back as \r\n or \r from Win32 edit controls; compare text in one form.
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// The full text of an editable control, from its text pattern or its value.
    public static string? ReadText(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
            return Normalize(((TextPattern)text).DocumentRange.GetText(TextLimit));
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            return Normalize(((ValuePattern)value).Current.Value ?? "");
        return null;
    }

    /// Characters before the caret (or the start of the selection), when the control exposes a text pattern.
    public static int? Caret(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var text = (TextPattern)pattern;
        var selection = text.GetSelection();
        if (selection.Length == 0) return null;
        var before = text.DocumentRange.Clone();
        before.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.Start);
        return Normalize(before.GetText(TextLimit)).Length;
    }

    public static string Selection(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return "";
        return string.Join("\n", ((TextPattern)pattern).GetSelection().Select(r => r.GetText(4000))).Trim();
    }

    public static bool IsEditable(AutomationElement element)
    {
        var current = element.Current;
        if (current.ControlType == ControlType.Edit || current.ControlType == ControlType.Document) return true;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && !((ValuePattern)value).Current.IsReadOnly) return true;
        return element.TryGetCurrentPattern(TextPattern.Pattern, out _) && current.IsKeyboardFocusable;
    }

    public static bool Same(AutomationElement? a, AutomationElement? b)
    {
        if (a == null || b == null) return false;
        try { return Automation.Compare(a, b); } catch { return false; }
    }
}
