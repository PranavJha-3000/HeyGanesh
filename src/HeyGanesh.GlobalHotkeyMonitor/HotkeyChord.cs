namespace HeyGanesh.GlobalHotkeyMonitor;

/// <summary>The decision the chord tracker reached for a single keyboard event.</summary>
internal enum HotkeyTransition
{
    /// <summary>Nothing publishable changed.</summary>
    None,

    /// <summary>The chord just became complete.</summary>
    Pressed,

    /// <summary>The chord was released, or was broken by real typing.</summary>
    Released,
}

/// <summary>
/// Modifier-only chord state machine: "left Ctrl + left Alt held, and nothing else typed
/// in between" is the push-to-talk trigger.
/// </summary>
/// <remarks>
/// Rules, in order of importance:
/// <list type="number">
/// <item>The chord completes only while <em>both</em> required modifiers are down and no
/// non-modifier key has been pressed since the attempt began. Any real key press while the
/// chord is held ends the press ("dirty" chord) — the user is typing, not talking.</item>
/// <item>Alt must be <em>left</em> Alt by default. On many layouts AltGr is literally
/// LCtrl + RAlt, so requiring the left side keeps ordinary AltGr typing from ever
/// completing the chord.</item>
/// <item>Other modifiers (Shift, Win) are neutral: they neither complete nor break a chord.</item>
/// <item>Repeated key-down notifications while a modifier auto-repeats are idempotent and
/// cannot publish a second <see cref="HotkeyTransition.Pressed"/>.</item>
/// </list>
/// </remarks>
internal sealed class ChordTracker
{
    private readonly HotkeyMonitorOptions _options;

    private bool _controlDown;
    private bool _leftAltDown;
    private bool _rightAltDown;
    private bool _pressed;
    private bool _dirty;

    internal ChordTracker(HotkeyMonitorOptions options) => _options = options;

    internal bool IsChordComplete
        => _controlDown && (_leftAltDown || (_rightAltDown && !_options.RequireLeftAlt));

    /// <summary>Feeds one hook notification and returns what (if anything) should be published.</summary>
    internal HotkeyTransition Feed(uint virtualKey, bool isDown, bool isInjected)
    {
        if (isInjected && _options.IgnoreInjectedEvents)
        {
            return HotkeyTransition.None;
        }

        switch (virtualKey)
        {
            case NativeMethods.VK_LCONTROL:
            case NativeMethods.VK_RCONTROL:
            case NativeMethods.VK_CONTROL:
                _controlDown = isDown;
                break;

            case NativeMethods.VK_LMENU:
                _leftAltDown = isDown;
                break;

            case NativeMethods.VK_RMENU:
                _rightAltDown = isDown;
                break;

            case NativeMethods.VK_MENU:
                // Generic Alt. Physical keyboards always report the sided VK (VK_LMENU /
                // VK_RMENU); an ambiguous generic Alt is treated as the right-hand key,
                // which the AltGr guard rejects. Fail closed rather than risk triggering
                // on AltGr traffic.
                _rightAltDown = isDown;
                break;

            case NativeMethods.VK_SHIFT:
            case NativeMethods.VK_LSHIFT:
            case NativeMethods.VK_RSHIFT:
            case NativeMethods.VK_LWIN:
            case NativeMethods.VK_RWIN:
                return HotkeyTransition.None;

            default:
                if (!isDown)
                {
                    return HotkeyTransition.None;
                }

                if (_pressed)
                {
                    // A real key arrived while the chord was held: the user means to type, not to
                    // talk. End the press, and keep the attempt dirty until the chord is let go.
                    _pressed = false;
                    _dirty = true;
                    return HotkeyTransition.Released;
                }

                // Only an in-progress attempt (a required modifier already down) can be dirtied.
                // Typing with no modifiers down must not poison the next chord — otherwise every
                // keystroke in any app would cost the user a wasted hold.
                if (_controlDown || _leftAltDown || _rightAltDown)
                {
                    _dirty = true;
                }

                return HotkeyTransition.None;
        }

        if (isDown)
        {
            ResyncModifiersExcept(virtualKey);
        }

        var complete = IsChordComplete;

        if (complete && !_pressed && !_dirty)
        {
            _pressed = true;
            return HotkeyTransition.Pressed;
        }

        if (_pressed && !complete)
        {
            _pressed = false;
            return HotkeyTransition.Released;
        }

        if (!complete && !_controlDown && !_leftAltDown && !_rightAltDown)
        {
            // Fully released: re-arm for the next attempt.
            _dirty = false;
        }

        return HotkeyTransition.None;
    }

    /// <summary>
    /// Re-reads modifier state from the OS for the modifiers <em>other than</em> the one this event
    /// is about.
    /// </summary>
    /// <remarks>
    /// Two reasons this exists. (1) The hook only sees events from the moment it was installed, so
    /// a chord already half-held at start-up would otherwise never complete — the OS is asked for
    /// the modifiers we never saw pressed. (2) The key in the current event is deliberately skipped:
    /// the asynchronous key state is not guaranteed to have caught up with the event being
    /// delivered, so consulting it for that key would overwrite good event-derived state with stale
    /// state and the chord would never complete.
    /// </remarks>
    private void ResyncModifiersExcept(uint currentKey)
    {
        if (currentKey is not (NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or NativeMethods.VK_CONTROL))
        {
            _controlDown = IsDown(NativeMethods.VK_LCONTROL) || IsDown(NativeMethods.VK_RCONTROL);
        }

        if (currentKey is not (NativeMethods.VK_LMENU or NativeMethods.VK_MENU))
        {
            _leftAltDown = IsDown(NativeMethods.VK_LMENU);
        }

        if (currentKey is not (NativeMethods.VK_RMENU or NativeMethods.VK_MENU))
        {
            _rightAltDown = IsDown(NativeMethods.VK_RMENU);
        }
    }

    private static bool IsDown(uint virtualKey)
        => (NativeMethods.GetAsyncKeyState((int)virtualKey) & NativeMethods.KeyPressedMask) != 0;
}
