namespace TalkCore.Tests;

public class ShortcutTests
{
    [Fact]
    public void TriggerTapCommitsOnlyOnRelease()
    {
        var state = new ShortcutState();
        Assert.Null(state.TriggerChanged(true, false));
        Assert.Null(state.TriggerChanged(true, false));
        Assert.Equal(ShortcutAction.Voice(VoiceAction.Dictate), state.TriggerChanged(false, false));
        Assert.Null(state.TriggerChanged(false, false));
    }

    [Fact]
    public void TriggerSpaceDoesNotAlsoDictate()
    {
        var state = new ShortcutState();
        state.TriggerChanged(true, false);
        Assert.Equal(ShortcutAction.Voice(VoiceAction.Hyday), state.KeyDown(ShortcutKey.Space, false, false));
        Assert.Null(state.KeyDown(ShortcutKey.Space, true, false));
        Assert.Null(state.TriggerChanged(false, false));
    }

    [Fact]
    public void AskWorksInEitherKeyOrder()
    {
        var state = new ShortcutState();
        state.TriggerChanged(true, false);
        Assert.Equal(ShortcutAction.Voice(VoiceAction.Ask), state.AskModifierChanged(true));
        Assert.Null(state.AskModifierChanged(true));
        Assert.Null(state.TriggerChanged(false, false));
        state.Reset();
        Assert.Null(state.AskModifierChanged(true));
        Assert.Equal(ShortcutAction.Voice(VoiceAction.Ask), state.TriggerChanged(true, false, askModifier: true));
        Assert.Null(state.TriggerChanged(false, false));
    }

    [Fact]
    public void TranslateWorksInEitherKeyOrder()
    {
        var state = new ShortcutState();
        Assert.Equal(ShortcutAction.Voice(VoiceAction.Translate), state.TriggerChanged(true, true));
        Assert.Null(state.TriggerChanged(false, true));
        state.Reset();
        state.TriggerChanged(true, false);
        Assert.Equal(ShortcutAction.Voice(VoiceAction.Translate), state.ShiftChanged(true));
        Assert.Null(state.ShiftChanged(true));
        Assert.Null(state.TriggerChanged(false, false));
    }

    [Fact]
    public void OtherChordsAndCancellation()
    {
        var state = new ShortcutState();
        state.TriggerChanged(true, false);
        Assert.Null(state.KeyDown(ShortcutKey.Other, false, false));
        Assert.Null(state.TriggerChanged(false, false));
        Assert.Null(state.KeyDown(ShortcutKey.Escape, false, false));
        Assert.Equal(ShortcutAction.Cancel, state.KeyDown(ShortcutKey.Escape, false, true));
    }
}
