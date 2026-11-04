using static HydayTalk.Native.Win32;

namespace HydayTalk.Native;

/// Simplified-to-Traditional conversion through Windows NLS, standing in for ICU's Hans-Hant on macOS.
internal static class ScriptConverter
{
    public static string ToTraditional(string text)
    {
        if (text.Length == 0) return text;
        var size = LCMapStringEx("zh-TW", LCMAP_TRADITIONAL_CHINESE, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (size <= 0) return text;
        var buffer = new char[size];
        var written = LCMapStringEx("zh-TW", LCMAP_TRADITIONAL_CHINESE, text, text.Length, buffer, size, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return written > 0 ? new string(buffer, 0, written) : text;
    }
}
