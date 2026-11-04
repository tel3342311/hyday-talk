using System.Runtime.InteropServices;
using System.Text;
using HydayTalk.Core;
using static HydayTalk.Native.Win32;

namespace HydayTalk;

/// API keys live in Windows Credential Manager, never in settings.json.
internal static class KeyStore
{
    private const string Service = "tw.muki.hyday-talk";
    private static string Target(string account) => $"{Service}/{account}";

    public static string Read(string account)
    {
        if (!CredRead(Target(account), CRED_TYPE_GENERIC, 0, out var pointer)) return "";
        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            if (credential.CredentialBlobSize == 0) return "";
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { CredFree(pointer); }
    }

    public static void Write(string value, string account)
    {
        if (value.Length == 0)
        {
            if (!CredDelete(Target(account), CRED_TYPE_GENERIC, 0) && Marshal.GetLastWin32Error() != 1168 /* ERROR_NOT_FOUND */)
                throw new TalkException("無法清除認證管理員中的 API Key。");
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC, TargetName = Target(account), UserName = account,
                CredentialBlob = blob, CredentialBlobSize = (uint)bytes.Length, Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            if (!CredWrite(ref credential, 0)) throw new TalkException("無法將 API Key 存入認證管理員。");
        }
        finally { Marshal.FreeHGlobal(blob); }
    }
}
