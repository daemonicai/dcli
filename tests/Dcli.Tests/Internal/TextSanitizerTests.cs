using Dcli.Internal;
using Xunit;

namespace Dcli.Tests.Internal;

/// <summary>
/// §1 (vt-escape-sanitization) — TextSanitizer unit tests.
/// All mode-specific tests use the explicit Apply(string, SanitizeMode) overload
/// to avoid mutating the process environment.
/// </summary>
public sealed class TextSanitizerTests
{
    // Build test strings from explicit integer casts so no \x ambiguity occurs
    // and no raw control characters are embedded in the source file.
    private static string WithControl(char before, int codePoint, char after) =>
        new([before, (char)codePoint, after]);

    private static string WithControl(int codePoint, string after) =>
        (char)codePoint + after;

    // ── Class-A bytes in strip mode ───────────────────────────────────────────

    [Fact]
    public void StripModeNulByteIsRemoved()
    {
        // NUL U+0000 is Class A; strip removes it.
        string result = TextSanitizer.Apply(WithControl('a', 0x00, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", result);
    }

    [Fact]
    public void StripModeBelByteIsRemoved()
    {
        // BEL U+0007 is Class A; strip removes it.
        string result = TextSanitizer.Apply(WithControl('a', 0x07, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", result);
    }

    [Fact]
    public void StripModeEscByteIsRemoved()
    {
        // ESC U+001B is Class A; strip removes it.
        string result = TextSanitizer.Apply(WithControl('a', 0x1B, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", result);
    }

    [Fact]
    public void StripModeDelByteIsRemoved()
    {
        // DEL U+007F is Class A; strip removes it.
        string result = TextSanitizer.Apply(WithControl('a', 0x7F, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", result);
    }

    [Fact]
    public void StripModeC1ByteIsRemoved()
    {
        // U+009F is Class A (C1 range 0x80–0x9F); strip removes it.
        string result = TextSanitizer.Apply(WithControl('a', 0x9F, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", result);
    }

    [Fact]
    public void StripModeC1LowestByteIsRemoved()
    {
        // U+0080 is the start of the C1 range; strip removes it.
        string result = TextSanitizer.Apply(WithControl('a', 0x80, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", result);
    }

    [Fact]
    public void StripModeMultipleControlBytesAreAllRemoved()
    {
        // ESC U+001B is Class A; '[' and 'A' are printable and pass through.
        string result = TextSanitizer.Apply(WithControl(0x1B, "[A"), SanitizeMode.Strip);
        Assert.Equal("[A", result);
    }

    // ── Class-A bytes in replace mode ─────────────────────────────────────────

    [Fact]
    public void ReplaceModeNulBecomesControlPicture()
    {
        // NUL U+0000 → U+2400 (␀)
        string result = TextSanitizer.Apply(WithControl('a', 0x00, 'b'), SanitizeMode.Replace);
        Assert.Equal("a" + (char)0x2400 + "b", result);
    }

    [Fact]
    public void ReplaceModeBelBecomesControlPicture()
    {
        // BEL U+0007 → U+2407 (␇)
        string result = TextSanitizer.Apply(WithControl('a', 0x07, 'b'), SanitizeMode.Replace);
        Assert.Equal("a" + (char)0x2407 + "b", result);
    }

    [Fact]
    public void ReplaceModeEscBecomesControlPicture()
    {
        // ESC U+001B → U+241B (␛)
        string result = TextSanitizer.Apply(WithControl('a', 0x1B, 'b'), SanitizeMode.Replace);
        Assert.Equal("a" + (char)0x241B + "b", result);
    }

    [Fact]
    public void ReplaceModeDelBecomesDeleteSymbol()
    {
        // DEL U+007F → U+2421 (␡)
        string result = TextSanitizer.Apply(WithControl('a', 0x7F, 'b'), SanitizeMode.Replace);
        Assert.Equal("a" + (char)0x2421 + "b", result);
    }

    [Fact]
    public void ReplaceModeC1BecomesReplacementCharacter()
    {
        // C1 U+009F → U+FFFD
        string result = TextSanitizer.Apply(WithControl('a', 0x9F, 'b'), SanitizeMode.Replace);
        Assert.Equal("a" + (char)0xFFFD + "b", result);
    }

    [Fact]
    public void ReplaceModeEscGlyphHasDisplayWidth3ForThreeCharString()
    {
        // ESC → U+241B (␛); result is "a" + U+241B + "b", each char width 1.
        string result = TextSanitizer.Apply(WithControl('a', 0x1B, 'b'), SanitizeMode.Replace);
        Assert.Equal(3, DisplayWidth.Measure(result));
    }

    // ── Class-B bytes (whitespace controls → single space) ────────────────────

    [Fact]
    public void TabBecomesSpace()
    {
        // Spec scenario: "a\tb" → "a b"
        string result = TextSanitizer.Apply("a\tb", SanitizeMode.Strip);
        Assert.Equal("a b", result);
    }

    [Fact]
    public void TabBecomesSpaceInReplaceMode()
    {
        string result = TextSanitizer.Apply("a\tb", SanitizeMode.Replace);
        Assert.Equal("a b", result);
    }

    [Fact]
    public void NewlineBecomesSpace()
    {
        // Spec scenario: "line1\nline2" → "line1 line2"
        string result = TextSanitizer.Apply("line1\nline2", SanitizeMode.Strip);
        Assert.Equal("line1 line2", result);
    }

    [Fact]
    public void CarriageReturnBecomesSpace()
    {
        string result = TextSanitizer.Apply("a\rb", SanitizeMode.Strip);
        Assert.Equal("a b", result);
    }

    [Fact]
    public void VerticalTabBecomesSpace()
    {
        // VT U+000B is Class B.
        string result = TextSanitizer.Apply(WithControl('a', 0x0B, 'b'), SanitizeMode.Strip);
        Assert.Equal("a b", result);
    }

    [Fact]
    public void FormFeedBecomesSpace()
    {
        // FF U+000C is Class B.
        string result = TextSanitizer.Apply(WithControl('a', 0x0C, 'b'), SanitizeMode.Strip);
        Assert.Equal("a b", result);
    }

    // ── Class boundary assertions ─────────────────────────────────────────────
    // These pin the exact byte-class edges so a future refactor cannot silently
    // flip an endpoint.

    [Fact]
    public void ClassABLowerEdgeBsIsClassATabIsClassB()
    {
        // 0x08 (BS) → Class A: stripped in strip mode.
        string bsResult = TextSanitizer.Apply(WithControl('a', 0x08, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", bsResult);

        // 0x09 (TAB) → Class B: becomes a space.
        string tabResult = TextSanitizer.Apply(WithControl('a', 0x09, 'b'), SanitizeMode.Strip);
        Assert.Equal("a b", tabResult);
    }

    [Fact]
    public void ClassBAUpperEdgeCrIsClassBSoIsClassA()
    {
        // 0x0D (CR) → Class B: becomes a space.
        string crResult = TextSanitizer.Apply(WithControl('a', 0x0D, 'b'), SanitizeMode.Strip);
        Assert.Equal("a b", crResult);

        // 0x0E (SO) → Class A: stripped in strip mode.
        string soResult = TextSanitizer.Apply(WithControl('a', 0x0E, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", soResult);
    }

    [Fact]
    public void DelEdgeTildeIsPrintableDelIsClassA()
    {
        // 0x7E (~) → printable: passes through unchanged.
        string tildeInput = "a~b";
        string tildeResult = TextSanitizer.Apply(tildeInput, SanitizeMode.Strip);
        Assert.Same(tildeInput, tildeResult);

        // 0x7F (DEL) → Class A: stripped in strip mode.
        string delResult = TextSanitizer.Apply(WithControl('a', 0x7F, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", delResult);
    }

    [Fact]
    public void C1UpperEdgeNoBreakSpaceIsPrintable()
    {
        // 0xA0 (NO-BREAK SPACE) is above the C1 range (0x80–0x9F) and must pass
        // through untouched — this is the exact off-by-one the design calls out.
        string input = "a" + (char)0xA0 + "b";
        string result = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Assert.Same(input, result);
    }

    // ── Spec scenario: escape byte stripped leaving remainder ─────────────────

    [Fact]
    public void SpecScenarioEscByteIsStrippedLeavingRemainder()
    {
        // Spec scenario: "Escape byte is stripped by default".
        // Input: "X" + ESC + "[2JY"  →  strip removes the ESC byte only.
        // '[', '2', 'J', 'Y' are all printable and pass through.
        string result = TextSanitizer.Apply("X\x1b[2JY", SanitizeMode.Strip);
        Assert.Equal("X[2JY", result);
    }

    // ── Printable / wide / emoji passthrough ──────────────────────────────────

    [Fact]
    public void PrintableAsciiIsUnchanged()
    {
        // Spec scenario: "[bold]" passes through unchanged.
        string input = "[bold]";
        string result = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Assert.Same(input, result);
    }

    [Fact]
    public void WideCjkCharactersAreUnchanged()
    {
        string input = "中文";
        string result = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Assert.Same(input, result);
    }

    [Fact]
    public void EmojiSurrogatePairsAreUnchanged()
    {
        // U+1F600 is a surrogate pair in UTF-16.
        string input = "hi \U0001F600 there";
        string result = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Assert.Same(input, result);
    }

    // ── Fast path — same instance returned ────────────────────────────────────

    [Fact]
    public void FastPathCleanStringReturnsSameInstance()
    {
        string input = "hello world";
        string result = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Assert.Same(input, result);
    }

    [Fact]
    public void FastPathEmptyStringReturnsSameInstance()
    {
        string input = string.Empty;
        string result = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Assert.Same(input, result);
    }

    // ── Idempotence ───────────────────────────────────────────────────────────

    [Fact]
    public void IdempotenceStripModeOutputHitsFastPathOnReapply()
    {
        // ESC is Class A; '[' '3' '1' 'm' 'b' are printable and pass through.
        string dirty = WithControl(0x1B, "[31mb");
        string once = TextSanitizer.Apply(dirty, SanitizeMode.Strip);
        string twice = TextSanitizer.Apply(once, SanitizeMode.Strip);
        Assert.Same(once, twice);
    }

    [Fact]
    public void IdempotenceReplaceModeOutputHitsFastPathOnReapply()
    {
        // ESC → U+241B, C1 → U+FFFD; both replacement glyphs are outside Class A/B.
        string dirty = WithControl('a', 0x1B, 'b') + WithControl(0x9F, "c");
        string once = TextSanitizer.Apply(dirty, SanitizeMode.Replace);
        string twice = TextSanitizer.Apply(once, SanitizeMode.Replace);
        Assert.Same(once, twice);
    }

    [Fact]
    public void ControlPictureGlyphsAreNotClassA()
    {
        // U+241B is in the Control Pictures block, not the C0 range — passes through.
        string input = "a" + (char)0x241B + "b";
        string result = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Assert.Same(input, result);
    }

    // ── Env-var / DefaultMode ─────────────────────────────────────────────────

    [Fact]
    public void ExplicitStripModeStrips()
    {
        // Strip removes ESC.
        string result = TextSanitizer.Apply(WithControl('a', 0x1B, 'b'), SanitizeMode.Strip);
        Assert.Equal("ab", result);
    }

    [Fact]
    public void ExplicitReplaceModeReplaces()
    {
        // Replace maps ESC to U+241B.
        string result = TextSanitizer.Apply(WithControl('a', 0x1B, 'b'), SanitizeMode.Replace);
        Assert.Equal("a" + (char)0x241B + "b", result);
    }

    [Fact]
    public void DefaultModeIsStripOrReplace()
    {
        // DefaultMode must be one of the two valid enum values.
        Assert.True(
            TextSanitizer.DefaultMode == SanitizeMode.Strip ||
            TextSanitizer.DefaultMode == SanitizeMode.Replace);
    }

    [Fact]
    public void DefaultModeIsStripWhenEnvVarAbsent()
    {
        // When DCLI_SANITIZE_MODE is unset (or not "replace"), DefaultMode must be Strip.
        string? envValue = Environment.GetEnvironmentVariable("DCLI_SANITIZE_MODE");
        if (string.IsNullOrEmpty(envValue) ||
            !envValue.Equals("replace", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(SanitizeMode.Strip, TextSanitizer.DefaultMode);
        }
    }
}
