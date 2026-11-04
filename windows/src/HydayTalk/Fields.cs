using System.Text;
using System.Windows.Automation;
using static HydayTalk.Native.Win32;

namespace HydayTalk;

/// The text control the user was typing in, read either through Win32 messages (classic Edit and
/// RichEdit controls) or through UI Automation (browsers, WPF, UWP and other modern apps).
internal interface IField
{
    bool Editable { get; }
    bool Secure { get; }
    /// The whole text with line breaks as \n, or null when it cannot be read.
    string? ReadText();
    /// Characters before the caret, in the \n form of the text.
    int? Caret();
    string Selection();
    /// Whether this control still has the keyboard focus.
    bool IsFocused();
}

/// Classic Win32 edit controls answer WM_GETTEXT and EM_GETSEL across processes, without UI Automation.
internal sealed class Win32Field : IField
{
    private const uint Timeout = 300;
    private readonly IntPtr hwnd;
    private readonly uint thread;

    private Win32Field(IntPtr hwnd, uint thread, bool secure, bool readOnly)
    {
        this.hwnd = hwnd; this.thread = thread; Secure = secure; Editable = !readOnly;
    }

    public bool Editable { get; }
    public bool Secure { get; }

    /// The focused control of the foreground window when it is an Edit or RichEdit control.
    public static Win32Field? Focused(IntPtr window)
    {
        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(thread, ref info) || info.hwndFocus == IntPtr.Zero) return null;
        var name = new StringBuilder(64);
        GetClassName(info.hwndFocus, name, name.Capacity);
        var type = name.ToString();
        if (!type.Equals("Edit", StringComparison.OrdinalIgnoreCase) && !type.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
            && !type.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase)) return null;
        var style = GetWindowLong(info.hwndFocus, GWL_STYLE);
        return new Win32Field(info.hwndFocus, thread, (style & ES_PASSWORD) != 0, (style & ES_READONLY) != 0);
    }

    private string? Raw()
    {
        if (Secure) return null;
        if (SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, Timeout, out var length) == IntPtr.Zero) return null;
        var size = (int)Math.Min(length.ToInt64(), Uia.TextLimit) + 1;
        var buffer = new StringBuilder(size);
        if (SendMessageTimeout(hwnd, WM_GETTEXT, (IntPtr)size, buffer, SMTO_ABORTIFHUNG, Timeout, out _) == IntPtr.Zero) return null;
        return buffer.ToString();
    }

    private (int Start, int End)? RawSelection()
    {
        if (SendMessageTimeout(hwnd, EM_GETSEL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, Timeout, out var result) == IntPtr.Zero) return null;
        var value = result.ToInt64();
        int start = (int)(value & 0xFFFF), end = (int)((value >> 16) & 0xFFFF);
        // Positions past 65535 do not fit in the reply.
        return start == 0xFFFF || end == 0xFFFF ? null : (start, end);
    }

    public string? ReadText() => Raw() is { } text ? Uia.Normalize(text) : null;

    public int? Caret()
    {
        if (Raw() is not { } text || RawSelection() is not { } selection) return null;
        return Uia.Normalize(text[..Math.Min(selection.Start, text.Length)]).Length;
    }

    public string Selection()
    {
        if (Raw() is not { } text || RawSelection() is not { } s || s.End <= s.Start) return "";
        return Uia.Normalize(text[Math.Min(s.Start, text.Length)..Math.Min(s.End, text.Length)]).Trim();
    }

    public bool IsFocused()
    {
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(thread, ref info) && info.hwndFocus == hwnd;
    }
}

internal sealed class UiaField(AutomationElement element, bool editable, bool secure) : IField
{
    public bool Editable { get; } = editable;
    public bool Secure { get; } = secure;

    public static UiaField? Focused() => Uia.Run(() =>
    {
        var element = AutomationElement.FocusedElement;
        if (element == null) return null;
        var secure = element.Current.IsPassword;
        return new UiaField(element, secure || Uia.IsEditable(element), secure);
    }, 600, null);

    public string? ReadText() => Secure ? null : Uia.Run(() => Uia.ReadText(element), 400, null);
    public int? Caret() => Secure ? null : Uia.Run(() => Uia.Caret(element), 400, null);
    public string Selection() => Secure ? "" : Uia.Run(() => Uia.Selection(element), 400, "");
    public bool IsFocused() => Uia.Run(() => Uia.Same(AutomationElement.FocusedElement, element), 400, true);
}
