using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using HydayTalk.Core;
using static HydayTalk.Native.Win32;

namespace HydayTalk;

/// Where the user was typing when recording started.
internal sealed record InputTarget(IntPtr Window, string Name, string Title, bool Editable, bool Secure, string Selection,
    IField? Field, string? Text, int? Caret)
{
    public ScreenContext Context => new(Name, Title, Text, Caret);
}

/// The field right after a paste, for reading back the user's corrections later.
internal sealed record Insertion(InputTarget Target, IField Field, int Location, string Inserted, string After);

/// Inserts text by pasting: the text goes on the clipboard, Ctrl+V is sent to the window the
/// user started in, and the previous clipboard is restored afterwards.
internal static class TextInsertion
{
    public static string ForegroundName()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return "";
        GetWindowThreadProcessId(window, out var pid);
        return AppName(pid, window);
    }

    /// Reads the focused window and, through UI Automation, the focused control.
    public static InputTarget? Capture()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == Environment.ProcessId) return null;
        var name = AppName(pid, window);
        var title = new StringBuilder(512);
        GetWindowText(window, title, title.Capacity);
        // Classic edit controls answer directly; everything else goes through UI Automation.
        IField? field = Win32Field.Focused(window);
        field ??= UiaField.Focused();
        // Without either, assume a text field and let the paste decide.
        if (field == null) return new InputTarget(window, name, title.ToString(), true, false, "", null, null, null);
        // Password fields are never read.
        if (field.Secure) return new InputTarget(window, name, title.ToString(), true, true, "", null, null, null);
        return new InputTarget(window, name, title.ToString(), field.Editable, false, field.Selection(), field, field.ReadText(), field.Caret());
    }

    private static string AppName(uint pid, IntPtr window)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            return process.ProcessName;
        }
        catch
        {
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            return title.Length > 0 ? title.ToString() : "目前的 App";
        }
    }

    public static void Copy(string text)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { Clipboard.SetDataObject(text.Replace("\r\n", "\n").Replace("\n", "\r\n"), true); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(50); }
        }
        throw new TalkException("無法使用剪貼簿，請稍後重試。");
    }

    /// Pastes into the target. Returns the panel message and, when the field can be read back,
    /// where the text landed. Throws when the text could only be copied.
    public static async Task<(string Message, Insertion? Insertion, bool Delivered)> InsertAsync(string text, InputTarget? target)
    {
        if (target == null || !IsWindow(target.Window) || !target.Editable)
        {
            Copy(text);
            return ("找不到輸入框，文字已複製，可按 Ctrl+V 貼上。", null, false);
        }
        if (GetForegroundWindow() != target.Window && !SetForegroundWindow(target.Window))
        {
            Copy(text);
            throw new TalkException("輸入位置已變更，文字已複製，可按 Ctrl+V 貼上。");
        }
        var saved = SaveClipboard();
        var field = target.Field;
        // Read just before pasting: the user may have typed while the text was being processed.
        var before = field == null ? null : await Task.Run(field.ReadText);
        Copy(text);
        await Task.Delay(30);
        if (GetForegroundWindow() != target.Window)
            throw new TalkException("輸入位置已變更，文字已複製，可按 Ctrl+V 貼上。");
        SendPaste();
        // The target reads the clipboard asynchronously; wait before putting the old contents back.
        await Task.Delay(800);
        RestoreClipboard(saved);
        Insertion? insertion = null;
        if (field != null && before != null)
        {
            var after = await Task.Run(field.ReadText);
            if (after != null && after != before)
            {
                // The paste replaced whatever was selected, so it begins where the two texts first differ.
                var location = 0;
                while (location < before.Length && location < after.Length && before[location] == after[location]) location++;
                if (location > 0 && char.IsLowSurrogate(after[location - 1]) ) location--;
                insertion = new Insertion(target, field, location, Uia.Normalize(text), after);
            }
        }
        return ($"已插入到 {target.Name}。", insertion, true);
    }

    private static void SendPaste()
    {
        // Release modifiers the user may still be holding (Right Ctrl, Shift, Alt), then Ctrl+V.
        var keys = new List<INPUT>();
        foreach (var vk in new[] { VK_RCONTROL, VK_LCONTROL, VK_LSHIFT, VK_RSHIFT, VK_LMENU, VK_RMENU })
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) keys.Add(Key(vk, up: true));
        keys.Add(Key(VK_CONTROL, false)); keys.Add(Key(VK_V, false)); keys.Add(Key(VK_V, true)); keys.Add(Key(VK_CONTROL, true));
        var array = keys.ToArray();
        SendInput((uint)array.Length, array, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    private static DataObject? SaveClipboard()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            if (current == null) return null;
            var copy = new DataObject();
            foreach (var format in current.GetFormats(false))
            {
                try { if (current.GetData(format, false) is { } data) copy.SetData(format, data); } catch { }
            }
            return copy;
        }
        catch { return null; }
    }

    private static void RestoreClipboard(DataObject? saved)
    {
        try
        {
            if (saved == null || saved.GetFormats().Length == 0) Clipboard.Clear();
            else Clipboard.SetDataObject(saved, true);
        }
        catch { }
    }
}
