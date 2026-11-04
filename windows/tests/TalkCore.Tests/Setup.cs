namespace TalkCore.Tests;

public class ChineseConverterTests
{
    [Fact]
    public void ConvertsCharactersAndPrefersPhrases()
    {
        Assert.Equal("我們使用海帶。", ChineseConverter.ToTraditional("我们使用海带。"));
        Assert.Equal("價格1,234.56元, 謝謝!", ChineseConverter.ToTraditional("价格1,234.56元, 谢谢!"));
        // One simplified character can stand for several traditional ones; phrases pick the right one.
        Assert.Equal("頭髮", ChineseConverter.ToTraditional("头发"));
        Assert.Equal("發現", ChineseConverter.ToTraditional("发现"));
        Assert.Equal("皇后", ChineseConverter.ToTraditional("皇后"));
        Assert.Equal("以後", ChineseConverter.ToTraditional("以后"));
        Assert.Equal("乾淨", ChineseConverter.ToTraditional("干净"));
        Assert.Equal("幹活", ChineseConverter.ToTraditional("干活"));
    }

    [Fact]
    public void LeavesTraditionalLatinAndEmojiAlone()
    {
        const string text = "已經是繁體 Hyday API 3點 😊 𠀀";
        Assert.Equal(text, ChineseConverter.ToTraditional(text));
        Assert.Equal("", ChineseConverter.ToTraditional(""));
    }
}
