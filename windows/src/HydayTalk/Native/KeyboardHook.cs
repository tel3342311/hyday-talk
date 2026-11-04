using System.Diagnostics;
using System.Runtime.InteropServices;
using HydayTalk.Core;
using static HydayTalk.Native.Win32;

namespace HydayTalk.Native;

/// A low-level keyboard hook that feeds the shortcut state machine. Windows does not report
/// the fn key, so Right Ctrl is the trigger: tap it to dictate, hold it with Left Shift to
/// translate, Left Alt to ask, Space for Hyday; Esc cancels while recording.
internal sealed class KeyboardHook : IDisposable
{
    private readonly ShortcutState state = new();
    private readonly LowLevelKeyboardProc proc;
    private readonly HashSet<uint> down = [];
    private IntPtr hook;
    // Some keyboard drivers, remote desktop clients and Wine report Right Ctrl together with a
    // Left Ctrl event a few milliseconds apart; that pair is one key, not a chord.
    private const uint PairWindow = 50;
    private uint leftCtrlTime, triggerTime;

    public Func<bool> IsRecording { get; set; } = () => false;
    /// Raised on the hook thread (the UI thread that installed it); handlers must return quickly.
    public event Action<ShortcutAction>? Action;
    /// Enter was pressed: inserted text is about to be sent, so corrections are read now.
    public event Action? ReturnPressed;

    public KeyboardHook() { proc = Callback; }

    public bool Start()
    {
        if (hook != IntPtr.Zero) return true;
        using var module = Process.GetCurrentProcess().MainModule;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(module?.ModuleName), 0);
        Log.Write($"hook installed: {hook != IntPtr.Zero}");
        return hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero; state.Reset(); down.Clear();
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(hook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        // Ignore the Ctrl+V the app itself sends while pasting.
        if ((info.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(hook, nCode, wParam, lParam);
        var message = (int)wParam;
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        var vk = info.vkCode;
        Log.Write($"key {vk:X2} {(isDown ? "down" : "up")}");
        var repeated = isDown && !down.Add(vk);
        if (!isDown) down.Remove(vk);
        var swallow = false;
        ShortcutAction? action = null;
        switch ((int)vk)
        {
            case VK_RCONTROL:
                if (!repeated)
                {
                    if (isDown) triggerTime = info.time;
                    var leftCtrlHeld = IsDown(VK_LCONTROL) && info.time - leftCtrlTime > PairWindow;
                    action = state.TriggerChanged(isDown, IsDown(VK_LSHIFT), otherModifier: leftCtrlHeld || IsDown(VK_RSHIFT)
                        || IsDown(VK_RMENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN), askModifier: IsDown(VK_LMENU));
                }
                break;
            case VK_LCONTROL:
                if (isDown && !repeated)
                {
                    leftCtrlTime = info.time;
                    if (!(state.TriggerDown && info.time - triggerTime <= PairWindow)) state.OtherModifierChanged();
                }
                break;
            case VK_LSHIFT: action = state.ShiftChanged(isDown); break;
            case VK_LMENU:
                action = state.AskModifierChanged(isDown);
                // Alt pressed during the chord would otherwise open the target app's menu bar.
                swallow = state.TriggerDown;
                break;
            case VK_RSHIFT or VK_RMENU or VK_LWIN or VK_RWIN:
                if (isDown) state.OtherModifierChanged();
                break;
            default:
                if (!isDown) break;
                if (vk == VK_RETURN && !repeated && !state.TriggerDown) ReturnPressed?.Invoke();
                var key = vk switch { VK_SPACE => ShortcutKey.Space, VK_ESCAPE => ShortcutKey.Escape, _ => ShortcutKey.Other };
                var chord = state.TriggerDown;
                action = state.KeyDown(key, repeated, IsRecording());
                // Ctrl+Space toggles the IME, and Esc would also close the target app's dialog.
                swallow = action != null || (chord && key == ShortcutKey.Space);
                break;
        }
        if (action is { } fired) { Log.Write($"shortcut {fired}"); Action?.Invoke(fired); }
        return swallow ? 1 : CallNextHookEx(hook, nCode, wParam, lParam);
    }

    private bool IsDown(int vk) => down.Contains((uint)vk);

    public void Dispose() => Stop();
}
