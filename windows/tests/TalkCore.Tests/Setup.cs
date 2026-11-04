using System.Runtime.CompilerServices;

namespace TalkCore.Tests;

internal static class Setup
{
    /// The Windows app converts script with LCMapStringEx; tests use a small table so they run anywhere.
    [ModuleInitializer]
    internal static void Initialize()
    {
        var table = new Dictionary<char, char> { ['们'] = '們', ['带'] = '帶', ['价'] = '價', ['谢'] = '謝', ['议'] = '議' };
        TextProcessing.ToTraditional = text => new string(text.Select(c => table.GetValueOrDefault(c, c)).ToArray());
    }
}
