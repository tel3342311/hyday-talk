namespace HydayTalk.Core;

public enum ShortcutKind { Voice, Cancel }
public readonly record struct ShortcutAction(ShortcutKind Kind, VoiceAction Action = VoiceAction.Dictate)
{
    public static ShortcutAction Voice(VoiceAction action) => new(ShortcutKind.Voice, action);
    public static readonly ShortcutAction Cancel = new(ShortcutKind.Cancel);
}

/// Keys the shortcut state machine cares about. Windows keyboards do not report fn,
/// so the trigger is Right Ctrl; Left Alt takes the place of Control for questions.
public enum ShortcutKey { Space, Escape, Other }

/// Port of the macOS FnShortcutState. A tap of the trigger is committed on release;
/// chords never also trigger the plain dictation action.
public sealed class ShortcutState
{
    public bool TriggerDown { get; private set; }
    private bool usedInChord, leftShiftDown, askModifierDown;

    public ShortcutAction? TriggerChanged(bool down, bool leftShift, bool otherModifier = false, bool askModifier = false)
    {
        if (down == TriggerDown) return null;
        TriggerDown = down; leftShiftDown = leftShift; askModifierDown = askModifier;
        if (down)
        {
            usedInChord = leftShift || otherModifier || askModifier;
            if (otherModifier) return null;
            if (leftShift) return askModifier ? null : ShortcutAction.Voice(VoiceAction.Translate);
            return askModifier ? ShortcutAction.Voice(VoiceAction.Ask) : null;
        }
        return usedInChord ? null : ShortcutAction.Voice(VoiceAction.Dictate);
    }

    public ShortcutAction? ShiftChanged(bool down)
    {
        var newlyDown = down && !leftShiftDown;
        leftShiftDown = down;
        if (!TriggerDown || !newlyDown || usedInChord) return null;
        usedInChord = true;
        return ShortcutAction.Voice(VoiceAction.Translate);
    }

    public ShortcutAction? AskModifierChanged(bool down)
    {
        var newlyDown = down && !askModifierDown;
        askModifierDown = down;
        if (!TriggerDown || !newlyDown || usedInChord) return null;
        usedInChord = true;
        return ShortcutAction.Voice(VoiceAction.Ask);
    }

    public void OtherModifierChanged() { if (TriggerDown) usedInChord = true; }

    public ShortcutAction? KeyDown(ShortcutKey key, bool repeated, bool recording)
    {
        if (key == ShortcutKey.Escape && recording && !repeated) return ShortcutAction.Cancel;
        if (!TriggerDown || repeated) return null;
        var canRecord = !usedInChord;
        usedInChord = true;
        return key == ShortcutKey.Space && canRecord ? ShortcutAction.Voice(VoiceAction.Hyday) : null;
    }

    public void Reset() { TriggerDown = usedInChord = leftShiftDown = askModifierDown = false; }
}
