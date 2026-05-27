using System.Text;
using Dcli.Internal.Input;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Behavioural tests for Section 5 (Chunk A + B): VT input parser, event model, and decoding rules.
/// Each test maps to a specific spec scenario or design-decision rule.
/// </summary>
public sealed class VtInputParserTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static List<InputEvent> Parse(params byte[] bytes)
    {
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed(bytes, events);
        return events;
    }

    private static List<InputEvent> ParseAndFlush(params byte[] bytes)
    {
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed(bytes, events);
        parser.Flush(events);
        return events;
    }

    private static KeyEvent ExpectKey(List<InputEvent> events, int index = 0)
    {
        Assert.True(index < events.Count, $"Expected at least {index + 1} event(s); got {events.Count}.");
        Assert.IsType<KeyEvent>(events[index]);
        return (KeyEvent)events[index];
    }

    /// <summary>Builds the bracketed-paste byte sequence: ESC [ 200 ~ + content + ESC [ 201 ~.</summary>
    private static byte[] BracketedPaste(string content)
    {
        byte[] start = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~'];
        byte[] end = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];
        byte[] body = Encoding.UTF8.GetBytes(content);
        byte[] result = new byte[start.Length + body.Length + end.Length];
        start.CopyTo(result, 0);
        body.CopyTo(result, start.Length);
        end.CopyTo(result, start.Length + body.Length);
        return result;
    }

    // ─── 5.2 Named-key table ─────────────────────────────────────────────────

    [Fact]
    public void TabByteProducesNamedTabWithNoModifiers()
    {
        // Spec scenario: byte 0x09 → Named(Tab), not Ctrl+I.
        List<InputEvent> events = Parse(0x09);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Tab), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void EnterByteProducesNamedEnter()
    {
        List<InputEvent> events = Parse(0x0D);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Enter), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void BackspaceDelByteProducesNamedBackspace()
    {
        // 0x7F (DEL) is the standard backspace in raw mode.
        List<InputEvent> events = Parse(0x7F);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Backspace), ke.Code);
    }

    [Fact]
    public void BackspaceBsByteProducesNamedBackspace()
    {
        // 0x08 (BS) is also mapped to Backspace.
        List<InputEvent> events = Parse(0x08);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Backspace), ke.Code);
    }

    // ─── 5.5 Terminal-truth rules ────────────────────────────────────────────

    [Fact]
    public void CtrlCProducesLowercaseCWithCtrlModifier()
    {
        // Spec scenario: 0x03 → KeyEvent(Char('c'), Ctrl). Lowercase.
        List<InputEvent> events = Parse(0x03);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('c')), ke.Code);
        Assert.Equal(Modifiers.Ctrl, ke.Modifiers);
    }

    [Fact]
    public void CtrlAProducesLowercaseAWithCtrlModifier()
    {
        // 0x01 → FromRune('a') + Ctrl.
        List<InputEvent> events = Parse(0x01);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ke.Code);
        Assert.Equal(Modifiers.Ctrl, ke.Modifiers);
    }

    [Fact]
    public void CtrlZProducesLowercaseZWithCtrlModifier()
    {
        // 0x1A → FromRune('z') + Ctrl.
        List<InputEvent> events = Parse(0x1A);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('z')), ke.Code);
        Assert.Equal(Modifiers.Ctrl, ke.Modifiers);
    }

    [Fact]
    public void UppercaseAProducesRuneWithNoModifiers()
    {
        // Spec scenario: 'A' → FromRune('A'), Modifiers.None — Shift is implicit in the rune.
        List<InputEvent> events = Parse((byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('A')), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void LowercaseAProducesRuneWithNoModifiers()
    {
        List<InputEvent> events = Parse((byte)'a');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    // ─── 5.1 CSI arrow keys ──────────────────────────────────────────────────

    [Fact]
    public void CsiUpProducesNamedUp()
    {
        // Spec scenario: ESC [ A → Named(Up).
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void CsiDownProducesNamedDown()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'B');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Down), ke.Code);
    }

    [Fact]
    public void CsiRightProducesNamedRight()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'C');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Right), ke.Code);
    }

    [Fact]
    public void CsiLeftProducesNamedLeft()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'D');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Left), ke.Code);
    }

    // ─── SS3 arrow keys (macOS terminal reality) ─────────────────────────────

    [Fact]
    public void Ss3UpProducesNamedUp()
    {
        // macOS Terminal sends ESC O A for Up — MUST decode correctly.
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void Ss3DownProducesNamedDown()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'B');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Down), ke.Code);
    }

    [Fact]
    public void Ss3RightProducesNamedRight()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'C');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Right), ke.Code);
    }

    [Fact]
    public void Ss3LeftProducesNamedLeft()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'D');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Left), ke.Code);
    }

    [Fact]
    public void Ss3HomeProducesNamedHome()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'H');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Home), ke.Code);
    }

    [Fact]
    public void Ss3EndProducesNamedEnd()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'F');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.End), ke.Code);
    }

    // ─── SS3 F1–F4 ───────────────────────────────────────────────────────────

    [Fact]
    public void Ss3F1ProducesNamedF1()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'P');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F1), ke.Code);
    }

    [Fact]
    public void Ss3F2ProducesNamedF2()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'Q');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F2), ke.Code);
    }

    [Fact]
    public void Ss3F3ProducesNamedF3()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'R');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F3), ke.Code);
    }

    [Fact]
    public void Ss3F4ProducesNamedF4()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'S');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F4), ke.Code);
    }

    // ─── CSI Home/End ────────────────────────────────────────────────────────

    [Fact]
    public void CsiHomeProducesNamedHome()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'H');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Home), ke.Code);
    }

    [Fact]
    public void CsiEndProducesNamedEnd()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'F');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.End), ke.Code);
    }

    // ─── Tilde sequences ─────────────────────────────────────────────────────

    [Fact]
    public void CsiInsert2TildeProducesNamedInsert()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'2', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Insert), ke.Code);
    }

    [Fact]
    public void CsiDelete3TildeProducesNamedDelete()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'3', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Delete), ke.Code);
    }

    [Fact]
    public void CsiPageUp5TildeProducesNamedPageUp()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'5', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.PageUp), ke.Code);
    }

    [Fact]
    public void CsiPageDown6TildeProducesNamedPageDown()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'6', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.PageDown), ke.Code);
    }

    [Fact]
    public void CsiHome1TildeProducesNamedHome()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Home), ke.Code);
    }

    [Fact]
    public void CsiHome7TildeProducesNamedHome()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'7', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Home), ke.Code);
    }

    [Fact]
    public void CsiEnd4TildeProducesNamedEnd()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'4', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.End), ke.Code);
    }

    [Fact]
    public void CsiEnd8TildeProducesNamedEnd()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'8', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.End), ke.Code);
    }

    // ─── F-key tilde sequences ────────────────────────────────────────────────

    [Fact]
    public void CsiF5Via15TildeProducesNamedF5()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F5), ke.Code);
    }

    [Fact]
    public void CsiF6Via17TildeProducesNamedF6()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)'7', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F6), ke.Code);
    }

    [Fact]
    public void CsiF12Via24TildeProducesNamedF12()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'2', (byte)'4', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F12), ke.Code);
    }

    // ─── BackTab ─────────────────────────────────────────────────────────────

    [Fact]
    public void CsiZProducesBackTabWithShiftModifier()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'Z');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.BackTab), ke.Code);
        Assert.Equal(Modifiers.Shift, ke.Modifiers);
    }

    // ─── 5.3 ESC disambiguation ──────────────────────────────────────────────

    [Fact]
    public void LoneEscAfterFlushProducesNamedEscape()
    {
        // Spec scenario: lone ESC + no follow-up → Flush → Named(Escape).
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed([0x1B], events);
        Assert.Empty(events); // still pending
        parser.Flush(events);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Escape), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void EscFollowedByBracketAProducesUpNotEscape()
    {
        // Spec scenario: ESC then [ A → Up, not Escape.
        List<InputEvent> events = ParseAndFlush(0x1B, (byte)'[', (byte)'A');
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
    }

    [Fact]
    public void AltPrefixEscThenAProducesAltModifiedA()
    {
        // ESC followed by printable 'a' → FromRune('a') + Alt.
        List<InputEvent> events = ParseAndFlush(0x1B, (byte)'a');
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ke.Code);
        Assert.Equal(Modifiers.Alt, ke.Modifiers);
    }

    [Fact]
    public void AltPrefixEscThenCtrlCProducesAltCtrlC()
    {
        // ESC + 0x03 → FromRune('c') + Alt|Ctrl.
        List<InputEvent> events = ParseAndFlush(0x1B, 0x03);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('c')), ke.Code);
        Assert.True((ke.Modifiers & Modifiers.Alt) != 0);
        Assert.True((ke.Modifiers & Modifiers.Ctrl) != 0);
    }

    // ─── Modifier-encoded sequences ──────────────────────────────────────────

    [Fact]
    public void CsiShiftUpProducesShiftModifier()
    {
        // ESC [ 1 ; 2 A → Up + Shift. m=2 → bitmask=1 → Shift.
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'2', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.Shift, ke.Modifiers);
    }

    [Fact]
    public void CsiCtrlUpProducesCtrlModifier()
    {
        // ESC [ 1 ; 5 A → Up + Ctrl. m=5 → bitmask=4 → Ctrl.
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'5', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.Ctrl, ke.Modifiers);
    }

    [Fact]
    public void CsiShiftCtrlUpStripsShiftUnderCtrl()
    {
        // ESC [ 1 ; 6 A → Up + Shift+Ctrl on the wire. Terminal-truth rule: Ctrl strips Shift.
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'6', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.Ctrl, ke.Modifiers);
        Assert.Equal(0, (int)(ke.Modifiers & Modifiers.Shift));
    }

    [Fact]
    public void CsiAltUpProducesAltModifier()
    {
        // ESC [ 1 ; 3 A → Up + Alt. m=3 → bitmask=2 → Alt.
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'3', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.Alt, ke.Modifiers);
    }

    // ─── Unicode / UTF-8 ─────────────────────────────────────────────────────

    [Fact]
    public void TwoByteUtf8ScalarDecodesCorrectly()
    {
        // Spec scenario: multi-byte UTF-8 char → KeyEvent carrying that scalar.
        // U+00E9 (é) = 0xC3 0xA9
        List<InputEvent> events = Parse(0xC3, 0xA9);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('é')), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void ThreeByteUtf8CjkScalarDecodes()
    {
        // U+4E2D (中) = 0xE4 0xB8 0xAD
        List<InputEvent> events = Parse(0xE4, 0xB8, 0xAD);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('中')), ke.Code);
    }

    [Fact]
    public void FourByteUtf8EmojiDecodes()
    {
        // U+1F600 (😀) = 0xF0 0x9F 0x98 0x80
        List<InputEvent> events = Parse(0xF0, 0x9F, 0x98, 0x80);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune(0x1F600)), ke.Code);
    }

    [Fact]
    public void Utf8ScalarSplitAcrossFeedsDecodesCorrectly()
    {
        // U+00E9 split: first byte in one Feed call, second in another.
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed([0xC3], events);
        Assert.Empty(events); // incomplete
        parser.Feed([0xA9], events);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('é')), ke.Code);
    }

    [Fact]
    public void InvalidUtf8ContinuationInGroundEmitsReplacementChar()
    {
        // 0x80 alone (continuation byte with no lead) → U+FFFD.
        List<InputEvent> events = Parse(0x80);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(Rune.ReplacementChar), ke.Code);
    }

    [Fact]
    public void InvalidUtf8TruncatedSequenceEmitsReplacementThenNextChar()
    {
        // Lead byte 0xC3 followed by non-continuation 0x41 ('A') — truncated sequence.
        // Should emit U+FFFD then FromRune('A').
        List<InputEvent> events = Parse(0xC3, (byte)'A');
        Assert.Equal(2, events.Count);
        KeyEvent ke0 = ExpectKey(events, 0);
        KeyEvent ke1 = ExpectKey(events, 1);
        Assert.Equal(KeyCode.FromRune(Rune.ReplacementChar), ke0.Code);
        Assert.Equal(KeyCode.FromRune(new Rune('A')), ke1.Code);
    }

    // ─── Split CSI across feeds ───────────────────────────────────────────────

    [Fact]
    public void CsiUpSplitAcrossFeedsDecodesCorrectly()
    {
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed([0x1B], events);
        Assert.Empty(events);
        parser.Feed([(byte)'['], events);
        Assert.Empty(events);
        parser.Feed([(byte)'A'], events);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
    }

    // ─── Multiple events in one Feed ─────────────────────────────────────────

    [Fact]
    public void MultipleKeysInSingleFeedAllDecoded()
    {
        // 'a' then 'b' — two events.
        List<InputEvent> events = Parse((byte)'a', (byte)'b');
        Assert.Equal(2, events.Count);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ((KeyEvent)events[0]).Code);
        Assert.Equal(KeyCode.FromRune(new Rune('b')), ((KeyEvent)events[1]).Code);
    }

    [Fact]
    public void ArrowKeyThenPrintableCharBothDecoded()
    {
        // SS3 Up followed by 'x'.
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'A', (byte)'x');
        Assert.Equal(2, events.Count);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ((KeyEvent)events[0]).Code);
        Assert.Equal(KeyCode.FromRune(new Rune('x')), ((KeyEvent)events[1]).Code);
    }

    // ─── Unrecognised sequences ───────────────────────────────────────────────

    [Fact]
    public void UnrecognisedCsiSequenceIsDroppedWithNoDesync()
    {
        // ESC [ 9 9 m (unrecognised) followed by 'a' — only 'a' should come out.
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'9', (byte)'9', (byte)'m', (byte)'a');
        Assert.Single(events);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ((KeyEvent)events[0]).Code);
    }

    [Fact]
    public void UnrecognisedSs3FinalIsDroppedWithNoDesync()
    {
        // ESC O X (unrecognised) followed by 'b'.
        List<InputEvent> events = Parse(0x1B, (byte)'O', (byte)'X', (byte)'b');
        Assert.Single(events);
        Assert.Equal(KeyCode.FromRune(new Rune('b')), ((KeyEvent)events[0]).Code);
    }

    [Fact]
    public void UnrecognisedTildeCodeIsDroppedWithNoDesync()
    {
        // ESC [ 9 9 ~ (unrecognised tilde) followed by 'z'.
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'9', (byte)'9', (byte)'~', (byte)'z');
        Assert.Single(events);
        Assert.Equal(KeyCode.FromRune(new Rune('z')), ((KeyEvent)events[0]).Code);
    }

    // ─── Flush no-op when not pending ────────────────────────────────────────

    [Fact]
    public void FlushWhenNothingPendingIsNoop()
    {
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Flush(events);
        Assert.Empty(events);
    }

    // ─── Event model construction (Decision 12 / test-harness spec) ──────────

    [Fact]
    public void KeyEventIsPubliclyConstructible()
    {
        KeyEvent ke = new(KeyCode.Named(NamedKey.Enter), Modifiers.None);
        Assert.Equal(KeyCode.Named(NamedKey.Enter), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void PasteEventIsPubliclyConstructible()
    {
        PasteEvent pe = new("hello");
        Assert.Equal("hello", pe.Text);
    }

    [Fact]
    public void ResizeEventIsPubliclyConstructible()
    {
        ResizeEvent re = new(80, 24);
        Assert.Equal(80, re.Columns);
        Assert.Equal(24, re.Rows);
    }

    [Fact]
    public void KeyCodeFromRuneRoundTrips()
    {
        KeyCode kc = KeyCode.FromRune(new Rune('x'));
        Assert.Equal(KeyCode.KeyCodeKind.UnicodeScalar, kc.Kind);
        Assert.Equal(new Rune('x'), kc.RuneValue);
    }

    [Fact]
    public void KeyCodeNamedRoundTrips()
    {
        KeyCode kc = KeyCode.Named(NamedKey.Delete);
        Assert.Equal(KeyCode.KeyCodeKind.Named, kc.Kind);
        Assert.Equal(NamedKey.Delete, kc.NamedValue);
    }

    [Fact]
    public void KeyCodeNamedAccessorOnScalarKindThrows()
    {
        KeyCode kc = KeyCode.FromRune(new Rune('a'));
        Assert.Throws<InvalidOperationException>(() => _ = kc.NamedValue);
    }

    [Fact]
    public void KeyCodeRuneAccessorOnNamedKindThrows()
    {
        KeyCode kc = KeyCode.Named(NamedKey.Up);
        Assert.Throws<InvalidOperationException>(() => _ = kc.RuneValue);
    }

    [Fact]
    public void ModifiersIsFlagsEnumAndCombinesCorrectly()
    {
        Modifiers combined = Modifiers.Ctrl | Modifiers.Alt;
        Assert.True((combined & Modifiers.Ctrl) != 0);
        Assert.True((combined & Modifiers.Alt) != 0);
        Assert.False((combined & Modifiers.Shift) != 0);
    }

    // ─── InputEvent is abstract record hierarchy ──────────────────────────────

    [Fact]
    public void InputEventPatternMatchWorksOnAllSubtypes()
    {
        InputEvent[] events =
        [
            new KeyEvent(KeyCode.Named(NamedKey.Tab), Modifiers.None),
            new PasteEvent("text"),
            new ResizeEvent(120, 40),
        ];

        int keys = 0, pastes = 0, resizes = 0;
        foreach (InputEvent ev in events)
        {
            switch (ev)
            {
                case KeyEvent: keys++; break;
                case PasteEvent: pastes++; break;
                case ResizeEvent: resizes++; break;
            }
        }

        Assert.Equal(1, keys);
        Assert.Equal(1, pastes);
        Assert.Equal(1, resizes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Chunk B — Section 5.4: Bracketed paste
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BracketedPasteSimpleTextEmitsSinglePasteEvent()
    {
        // ESC [ 200 ~ hello ESC [ 201 ~ → one PasteEvent("hello")
        List<InputEvent> events = Parse(BracketedPaste("hello"));
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("hello", pe.Text);
    }

    [Fact]
    public void BracketedPasteEmptyBodyEmitsPasteEventWithEmptyString()
    {
        // ESC [ 200 ~ immediately followed by ESC [ 201 ~ → PasteEvent("")
        List<InputEvent> events = Parse(BracketedPaste(string.Empty));
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal(string.Empty, pe.Text);
    }

    [Fact]
    public void BracketedPasteMultilinePreservesNewline()
    {
        // A pasted newline must NOT become an Enter key event — it is literal text.
        List<InputEvent> events = Parse(BracketedPaste("line1\nline2"));
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("line1\nline2", pe.Text);
    }

    [Fact]
    public void BracketedPasteNewlineIsNotSubmittedAsEnter()
    {
        // Verify no KeyEvent(Enter) is produced alongside the paste event.
        List<InputEvent> events = Parse(BracketedPaste("a\nb"));
        Assert.Single(events);
        Assert.IsType<PasteEvent>(events[0]);
    }

    [Fact]
    public void BracketedPasteSplitAcrossFeeds()
    {
        // Full paste sequence split arbitrarily across two Feed calls.
        byte[] full = BracketedPaste("abc");
        byte[] first = full[..^3];
        byte[] second = full[^3..];

        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed(first, events);
        Assert.Empty(events); // not yet complete
        parser.Feed(second, events);
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("abc", pe.Text);
    }

    [Fact]
    public void BracketedPasteEndMarkerSplitAcrossFeeds()
    {
        // End marker ESC [ 2 0 1 ~ split so that "ESC [ 2 0" arrives in first feed
        // and "1 ~" in the second.
        byte[] start = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~'];
        byte[] end = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];
        byte[] content = "xy"u8.ToArray();

        // First feed: paste-start + content + first 4 bytes of end-marker (ESC [ 2 0)
        byte[] feed1 = [.. start, .. content, end[0], end[1], end[2], end[3]];
        // Second feed: remaining 2 bytes of end-marker (1 ~)
        byte[] feed2 = [end[4], end[5]];

        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed(feed1, events);
        Assert.Empty(events);
        parser.Feed(feed2, events);
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("xy", pe.Text);
    }

    [Fact]
    public void BracketedPasteStrayEscNotEndMarkerIsLiteralText()
    {
        // A lone ESC inside paste that is NOT followed by [ 2 0 1 ~ is literal.
        // We send: ESC [ 200 ~ "a" ESC "b" ESC [ 201 ~
        // The stray ESC + "b" should appear verbatim in the pasted text.
        byte[] start = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~'];
        byte[] end = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];
        // body = 'a' ESC 'b'  — the ESC is not followed by the end marker
        byte[] body = [(byte)'a', 0x1B, (byte)'b'];

        byte[] input = [.. start, .. body, .. end];
        List<InputEvent> events = Parse(input);
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        // The stray ESC should appear as U+001B in the decoded string: "a\u001Bb" (3 chars).
        Assert.Equal("a\u001Bb", pe.Text);
    }

    [Fact]
    public void BracketedPasteStrayEscFollowedByNonEndMarkerSplitAcrossFeeds()
    {
        // Like the test above but the stray ESC arrives at the end of one feed call
        // and the non-matching byte arrives in the next feed.
        byte[] start = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~'];
        byte[] end = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];

        // Feed 1: paste-start + "a" + ESC  (partial candidate: matched 1 byte)
        byte[] feed1 = [.. start, (byte)'a', 0x1B];
        // Feed 2: "x" (diverges → ESC becomes literal) + end marker
        byte[] feed2 = [(byte)'x', .. end];

        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed(feed1, events);
        Assert.Empty(events);
        parser.Feed(feed2, events);
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("a\x1Bx", pe.Text);
    }

    [Fact]
    public void BracketedPasteContainsUtf8MultibyteCjk()
    {
        // Paste containing 中 (U+4E2D, 3-byte UTF-8) is decoded correctly.
        List<InputEvent> events = Parse(BracketedPaste("中"));
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("中", pe.Text);
    }

    [Fact]
    public void BracketedPasteContainsUtf8FourByteEmoji()
    {
        // Paste containing 😀 (U+1F600, 4-byte UTF-8) is decoded correctly.
        List<InputEvent> events = Parse(BracketedPaste("😀"));
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("😀", pe.Text);
    }

    [Fact]
    public void BracketedPasteFollowedByRegularKeyBothParsed()
    {
        // After the paste, a subsequent regular key should still parse.
        byte[] input = [.. BracketedPaste("hi"), (byte)'a'];
        List<InputEvent> events = Parse(input);
        Assert.Equal(2, events.Count);
        Assert.IsType<PasteEvent>(events[0]);
        KeyEvent ke = ExpectKey(events, 1);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ke.Code);
    }

    [Fact]
    public void BracketedPasteEndMarkerOverlapDivergesAndRestarts()
    {
        // N1: overlap / re-start — body bytes partially match the end-marker prefix then diverge.
        // Body = [ESC, '[', '3']: ESC matches marker[0], '[' matches marker[1], but '3' ≠ marker[2]('2').
        // On diverge, the two matched bytes (ESC '[') are flushed as literal to _pasteBuf,
        // then '3' is stored as ordinary content. The real end marker ESC[201~ then fires.
        // Expected PasteEvent("\x1B[3") — one ESC, one '[', one '3'.
        byte[] startMarker = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~'];
        byte[] endMarker = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];
        byte[] body = [0x1B, (byte)'[', (byte)'3'];

        byte[] input = [.. startMarker, .. body, .. endMarker];

        List<InputEvent> events = Parse(input);
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("\x1B[3", pe.Text);
    }

    [Fact]
    public void BracketedPasteEndMarkerDivergingEscReseedsCandidate()
    {
        // N1b: diverging byte is itself an ESC — it must re-seed a fresh candidate, not be dropped.
        // Body = [ESC, '[', '2']: matches end-marker template through index 2 (candidate len 3).
        // Next byte = 0x1B (the real end-marker's first byte): diverges at template index 3 ('0').
        // The matched prefix [ESC '[' '2'] flushes as literal text ("\x1B[2") into _pasteBuf.
        // The diverging ESC re-seeds _pasteEndCandidateLen to 1; the remaining [' ' 2 0 1 ~]
        // completes the end marker → PasteEvent("[2").
        // NOTE: "\x1B[2" has ESC as first char; PasteEvent.Text = "[2" (only the non-ESC body
        // bytes that made it to _pasteBuf before the candidate was started at ESC).
        // Trace: start marker consumed → in paste mode.
        //   ESC  → candidate[0] match, candidateLen=1
        //   '['  → candidate[1] match, candidateLen=2
        //   '2'  → candidate[2] match, candidateLen=3  (end-marker is ESC [ 2 0 1 ~)
        //   0x1B → candidate[3] expected '0', got ESC → flush [ESC '[' '2'] to _pasteBuf, re-seed: candidateLen=1
        //   '['  → candidate[1] match, candidateLen=2
        //   '2'  → candidate[2] match, candidateLen=3
        //   '0'  → candidate[3] match, candidateLen=4
        //   '1'  → candidate[4] match, candidateLen=5
        //   '~'  → candidate[5] match → end marker complete → emit PasteEvent("[2")
        // Wait — the flushed prefix is ESC '[' '2', giving _pasteBuf = [ESC, '[', '2'].
        // But the start-marker was already consumed; _pasteBuf only holds post-start content.
        // So Text = "\x1B[2". Assert that, then assert a key after proves no desync.
        byte[] startMarker = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~'];
        byte[] endMarker = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];
        // Body: ESC '[' '2' — matches first 3 bytes of end-marker, then the real end marker follows.
        // The real end marker's leading ESC is the diverging byte that re-seeds the candidate.
        byte[] input = [.. startMarker, 0x1B, (byte)'[', (byte)'2', .. endMarker, (byte)'a'];

        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed(input, events);

        // Exactly two events: one PasteEvent then one KeyEvent('a').
        Assert.Equal(2, events.Count);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("\x1B[2", pe.Text);
        // The 'a' after the paste bracket parses correctly — no desync.
        KeyEvent ke = Assert.IsType<KeyEvent>(events[1]);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ke.Code);
    }

    [Fact]
    public void BracketedPasteUtf8ScalarStraddlingFeedBoundaryDecodesCorrectly()
    {
        // N2: a multibyte UTF-8 scalar split across two Feed calls inside a paste body.
        // U+4E2D (中) = 0xE4 0xB8 0xAD (3 bytes).
        // Feed 1: start marker + first byte of 中 (0xE4).
        // Feed 2: remaining two bytes of 中 (0xB8 0xAD) + end marker.
        // The parser accumulates raw bytes and UTF-8 decodes at the end marker;
        // feed-call boundaries inside paste are irrelevant.
        byte[] startMarker = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~'];
        byte[] endMarker = [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];

        byte[] feed1 = [.. startMarker, 0xE4];
        byte[] feed2 = [0xB8, 0xAD, .. endMarker];

        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed(feed1, events);
        Assert.Empty(events);
        parser.Feed(feed2, events);
        Assert.Single(events);
        PasteEvent pe = Assert.IsType<PasteEvent>(events[0]);
        Assert.Equal("中", pe.Text);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Section 5.6: Exhaustive byte-fixture suite (collision-direction guards,
    // modifier assertions, unrecognised-sequence resilience)
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Collision-direction guards (reviewer nit 3) ──────────────────────────

    [Fact]
    public void Byte0x09IsNamedTabNotCtrlI()
    {
        // 0x09 must produce Named(Tab) with Modifiers.None, NOT Ctrl+I.
        List<InputEvent> events = Parse(0x09);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Tab), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void Byte0x0DIsNamedEnterNotCtrlM()
    {
        // 0x0D must produce Named(Enter) with Modifiers.None, NOT Ctrl+M.
        List<InputEvent> events = Parse(0x0D);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Enter), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void Byte0x08IsNamedBackspaceNotCtrlH()
    {
        // 0x08 must produce Named(Backspace) with Modifiers.None, NOT Ctrl+H.
        List<InputEvent> events = Parse(0x08);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Backspace), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    // ── 'A' has no Shift ─────────────────────────────────────────────────────

    [Fact]
    public void UppercaseAProducesRuneWithModifiersNoneNoShift()
    {
        // Spec rule: Shift is never synthesised for printable runes.
        List<InputEvent> events = Parse((byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('A')), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
        Assert.Equal(0, (int)(ke.Modifiers & Modifiers.Shift));
    }

    [Fact]
    public void CtrlLetterHasNoShiftModifier()
    {
        // 0x03 → Char('c') + Ctrl. Ctrl letter must not have Shift.
        List<InputEvent> events = Parse(0x03);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('c')), ke.Code);
        Assert.True((ke.Modifiers & Modifiers.Ctrl) != 0);
        Assert.Equal(0, (int)(ke.Modifiers & Modifiers.Shift));
    }

    // ── Arrows: both CSI and SS3 encodings ───────────────────────────────────

    [Theory]
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'A' }, NamedKey.Up)]
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'B' }, NamedKey.Down)]
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'C' }, NamedKey.Right)]
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'D' }, NamedKey.Left)]
    public void CsiArrowProducesCorrectNamedKey(byte[] input, NamedKey expected)
    {
        List<InputEvent> events = Parse(input);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(expected), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Theory]
    [InlineData(new byte[] { 0x1B, (byte)'O', (byte)'A' }, NamedKey.Up)]
    [InlineData(new byte[] { 0x1B, (byte)'O', (byte)'B' }, NamedKey.Down)]
    [InlineData(new byte[] { 0x1B, (byte)'O', (byte)'C' }, NamedKey.Right)]
    [InlineData(new byte[] { 0x1B, (byte)'O', (byte)'D' }, NamedKey.Left)]
    public void Ss3ArrowProducesCorrectNamedKey(byte[] input, NamedKey expected)
    {
        List<InputEvent> events = Parse(input);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(expected), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    // ── Unicode scalar fixture: 2-byte, 3-byte, 4-byte ───────────────────────

    [Fact]
    public void TwoByteUtf8EAccentProducesCorrectScalar()
    {
        // U+00E9 é = 0xC3 0xA9
        List<InputEvent> events = Parse(0xC3, 0xA9);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('é')), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void ThreeByteUtf8CjkProducesCorrectScalar()
    {
        // U+4E2D 中 = 0xE4 0xB8 0xAD
        List<InputEvent> events = Parse(0xE4, 0xB8, 0xAD);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('中')), ke.Code);
    }

    [Fact]
    public void FourByteUtf8EmojiProducesCorrectScalar()
    {
        // U+1F600 😀 = 0xF0 0x9F 0x98 0x80
        List<InputEvent> events = Parse(0xF0, 0x9F, 0x98, 0x80);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune(0x1F600)), ke.Code);
    }

    [Fact]
    public void TwoByteUtf8ScalarSplitAcrossFeedsFixture()
    {
        // U+00E9 é split across two feeds.
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed([0xC3], events);
        Assert.Empty(events);
        parser.Feed([0xA9], events);
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('é')), ke.Code);
    }

    // ── ESC disambiguation ────────────────────────────────────────────────────

    [Fact]
    public void LoneEscPlusFlushProducesEscape()
    {
        VtInputParser parser = new();
        List<InputEvent> events = new();
        parser.Feed([0x1B], events);
        Assert.Empty(events);
        parser.Flush(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Escape), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void EscThenBracketAProducesUpNotEscape()
    {
        List<InputEvent> events = ParseAndFlush(0x1B, (byte)'[', (byte)'A');
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
    }

    [Fact]
    public void EscThenPrintableProducesAltKey()
    {
        List<InputEvent> events = ParseAndFlush(0x1B, (byte)'a');
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('a')), ke.Code);
        Assert.Equal(Modifiers.Alt, ke.Modifiers);
    }

    // ── Modifier-encoded sequences ────────────────────────────────────────────

    [Fact]
    public void CsiCtrlUpFixture()
    {
        // ESC [ 1 ; 5 A → Named(Up) + Ctrl
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'5', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.Ctrl, ke.Modifiers);
    }

    [Fact]
    public void CsiShiftUpFixture()
    {
        // ESC [ 1 ; 2 A → Named(Up) + Shift
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'2', (byte)'A');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.Shift, ke.Modifiers);
    }

    // ── F-key tilde sequences ─────────────────────────────────────────────────

    [Fact]
    public void CsiF5Via15TildeFixture()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F5), ke.Code);
    }

    [Fact]
    public void CsiF12Via24TildeFixture()
    {
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'2', (byte)'4', (byte)'~');
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.Named(NamedKey.F12), ke.Code);
    }

    // ── Unrecognised sequence → no desync → next key parses ──────────────────

    [Fact]
    public void UnrecognisedCsiDroppedNoDesyncFixtureSuite()
    {
        // ESC [ 9 9 m (unrecognised) then 'z' — only 'z' comes out.
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'9', (byte)'9', (byte)'m', (byte)'z');
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('z')), ke.Code);
    }

    [Fact]
    public void UnrecognisedTildeCodeDroppedNoDesyncFixtureSuite()
    {
        // ESC [ 9 9 ~ then 'q'
        List<InputEvent> events = Parse(0x1B, (byte)'[', (byte)'9', (byte)'9', (byte)'~', (byte)'q');
        Assert.Single(events);
        KeyEvent ke = ExpectKey(events);
        Assert.Equal(KeyCode.FromRune(new Rune('q')), ke.Code);
    }

    // ── KeyCode.ToString() consistency ───────────────────────────────────────

    [Fact]
    public void KeyCodeFromRuneToStringIsUnicodeScalarFormat()
    {
        KeyCode kc = KeyCode.FromRune(new Rune('A'));
        Assert.StartsWith("UnicodeScalar(", kc.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyCodeNamedToStringIsNamedFormat()
    {
        KeyCode kc = KeyCode.Named(NamedKey.Up);
        Assert.StartsWith("Named(", kc.ToString(), StringComparison.Ordinal);
    }
}
