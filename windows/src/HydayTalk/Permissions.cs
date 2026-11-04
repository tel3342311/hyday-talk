using System.Diagnostics;
using Microsoft.Win32;

namespace HydayTalk;

/// Windows privacy switches the app depends on. Desktop apps are not prompted; the user turns
/// these on in Settings, so the app reads the switches and links to the right page.
internal static class Permissions
{
    private const string Consent = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// False when the microphone switch for this device, for the user, or for desktop apps is off.
    public static bool MicrophoneAllowed()
    {
        foreach (var (hive, path) in new[] { (Registry.LocalMachine, Consent), (Registry.CurrentUser, Consent), (Registry.CurrentUser, Consent + @"\NonPackaged") })
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key?.GetValue("Value") is string value && value.Equals("Deny", StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { }
        }
        return true;
    }

    /// Windows dictation needs online speech recognition turned on.
    public static bool OnlineSpeechAllowed()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy");
            return key?.GetValue("HasAccepted") is int accepted && accepted == 1;
        }
        catch { return false; }
    }

    public static void OpenMicrophoneSettings() => Open("ms-settings:privacy-microphone");
    public static void OpenSpeechSettings() => Open("ms-settings:privacy-speech");
    public static void OpenSpeechLanguages() => Open("ms-settings:speech");

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
