using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using HydayTalk.Core;
using HydayTalk.Native;
using static HydayTalk.Native.Win32;

namespace HydayTalk;

/// Where the user was typing when recording started.
internal sealed record InputTarget(IntPtr Window, string Name, bool Editable, bool Secure, string Selection);

/// Inserts text by pasting: the text goes on the clipboard, Ctrl+V is sent to the window the
/// user started in, and the previous clipboard is restored afterwards.
internal static class TextInsertion
{
    /// Reads the focused window and, through UI Automation, the focused control. UI Automation
    /// can stall on unresponsive apps, so it gets a short time budget.
    public static InputTarget? Capture()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == Environment.ProcessId) return null;
        var name = AppName(pid, window);
        var focus = Task.Run(ReadFocus);
        var (editable, secure, selection) = focus.Wait(TimeSpan.FromMilliseconds(400)) ? focus.Result : (true, false, "");
        return new InputTarget(window, name, editable, secure, selection);
    }

    private static (bool Editable, bool Secure, string Selection) ReadFocus()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return (false, false, "");
            var current = element.Current;
            var type = current.ControlType;
            var editable = type == ControlType.Edit || type == ControlType.Document || type == ControlType.ComboBox
                || element.TryGetCurrentPattern(ValuePattern.Pattern, out _) || element.TryGetCurrentPattern(TextPattern.Pattern, out _);
            var selection = "";
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                selection = string.Join("\n", ((TextPattern)pattern).GetSelection().Select(r => r.GetText(4000))).Trim();
            return (editable, current.IsPassword, selection);
        }
        catch { return (true, false, ""); }
    }

    private static string AppName(uint pid, IntPtr window)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description;
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
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { Clipboard.SetDataObject(text, true); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(50); }
        }
        throw new TalkException("無法使用剪貼簿，請稍後重試。");
    }

    /// Returns a message for the panel. Throws when the text could only be copied.
    public static async Task<string> InsertAsync(string text, InputTarget? target)
    {
        if (target == null || !IsWindow(target.Window) || !target.Editable)
        {
            Copy(text);
            return "找不到輸入框，文字已複製，可按 Ctrl+V 貼上。";
        }
        if (GetForegroundWindow() != target.Window && !SetForegroundWindow(target.Window))
        {
            Copy(text);
            throw new TalkException("輸入位置已變更，文字已複製，可按 Ctrl+V 貼上。");
        }
        var saved = SaveClipboard();
        Copy(text);
        await Task.Delay(30);
        if (GetForegroundWindow() != target.Window)
        {
            throw new TalkException("輸入位置已變更，文字已複製，可按 Ctrl+V 貼上。");
        }
        SendPaste();
        // The target reads the clipboard asynchronously; wait before putting the old contents back.
        await Task.Delay(800);
        RestoreClipboard(saved, text);
        return $"已插入到 {target.Name}。";
    }

    private static void SendPaste()
    {
        // Release modifiers the user may still be holding (Right Ctrl, Shift, Alt), then Ctrl+V.
        var keys = new List<INPUT>();
        foreach (var vk in new[] { VK_RCONTROL, VK_LSHIFT, VK_RSHIFT, VK_LMENU, VK_RMENU })
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

    private static void RestoreClipboard(DataObject? saved, string inserted)
    {
        try
        {
            // Leave the clipboard alone if something else replaced it in the meantime.
            if (Clipboard.ContainsText() && Clipboard.GetText() != inserted) return;
            if (saved == null || saved.GetFormats().Length == 0) Clipboard.Clear();
            else Clipboard.SetDataObject(saved, true);
        }
        catch { }
    }
}
