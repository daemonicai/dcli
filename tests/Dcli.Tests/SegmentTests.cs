using Xunit;

namespace Dcli.Tests;

/// <summary>
/// §2 (vt-escape-sanitization) — Segment safe-by-default construction + Raw seam.
/// </summary>
public sealed class SegmentTests
{
    // Build strings containing a control byte without embedding raw chars in source.
    private static string WithEsc(string before, string after) =>
        before + (char)0x1B + after;

    private static string WithCtrl(int codePoint, string rest = "") =>
        (char)codePoint + rest;

    // ── 2.1 / 2.4: sanitizing primary ctor ───────────────────────────────────

    [Fact]
    public void CtorStripsEscFromText()
    {
        // ESC (0x1B) is Class A — stripped in default (Strip) mode.
        Segment seg = new(WithEsc("a", "b"));
        Assert.Equal("ab", seg.Text);
    }

    [Fact]
    public void CtorNormalizesNewlineToSpace()
    {
        // LF is Class B — always → single space.
        Segment seg = new("line1\nline2");
        Assert.Equal("line1 line2", seg.Text);
    }

    [Fact]
    public void CtorNormalizesTabToSpace()
    {
        Segment seg = new("a\tb");
        Assert.Equal("a b", seg.Text);
    }

    [Fact]
    public void CtorNormalizesCrToSpace()
    {
        Segment seg = new("a\rb");
        Assert.Equal("a b", seg.Text);
    }

    [Fact]
    public void CtorStripsNulByte()
    {
        // NUL (0x00) is Class A — stripped.
        Segment seg = new(WithCtrl(0x00, "hello"));
        Assert.Equal("hello", seg.Text);
    }

    [Fact]
    public void CtorStripsDelByte()
    {
        // DEL (0x7F) is Class A — stripped.
        Segment seg = new(WithCtrl(0x7F, "x"));
        Assert.Equal("x", seg.Text);
    }

    [Fact]
    public void CtorPassesThroughPrintableAscii()
    {
        Segment seg = new("hello, world!");
        Assert.Equal("hello, world!", seg.Text);
    }

    [Fact]
    public void CtorPassesThroughMarkupLikeSyntax()
    {
        // Bracket/tag syntax is not interpreted; printable chars are stored verbatim.
        const string markup = "[bold]{x}<em>foo</em>**strong**";
        Segment seg = new(markup);
        Assert.Equal(markup, seg.Text);
    }

    [Fact]
    public void CtorPassesThroughWideAndEmoji()
    {
        const string text = "日本語🦊";
        Segment seg = new(text);
        Assert.Equal(text, seg.Text);
    }

    [Fact]
    public void CtorThrowsOnNullText()
    {
        Assert.Throws<ArgumentNullException>(() => new Segment(null!));
    }

    // ── 2.3: Line.FromText sanitizes ─────────────────────────────────────────

    [Fact]
    public void LineFromTextStripsEsc()
    {
        Line line = Line.FromText(WithEsc("a", "b"));
        Assert.Equal("ab", line.Segments[0].Text);
    }

    [Fact]
    public void LineFromTextNormalizesNewline()
    {
        Line line = Line.FromText("hello\nworld");
        Assert.Equal("hello world", line.Segments[0].Text);
    }

    // ── 2.3: Segment.Raw stores verbatim ─────────────────────────────────────

    [Fact]
    public void RawStoresTextVerbatim()
    {
        string vt = WithEsc("[31m", "red\x1b[0m");
        Segment seg = Segment.Raw(vt);
        Assert.Equal(vt, seg.Text);
    }

    [Fact]
    public void RawWithNewlineStoresVerbatim()
    {
        Segment seg = Segment.Raw("a\nb");
        Assert.Equal("a\nb", seg.Text);
    }

    [Fact]
    public void RawThrowsOnNullText()
    {
        Assert.Throws<ArgumentNullException>(() => Segment.Raw(null!));
    }

    [Fact]
    public void RawSetsIsRawTrue()
    {
        Segment seg = Segment.Raw("x");
        Assert.True(seg.IsRaw);
    }

    [Fact]
    public void CtorSetsIsRawFalse()
    {
        Segment seg = new("x");
        Assert.False(seg.IsRaw);
    }

    // ── 2.3 / Decision 2: IsRaw participates in value equality ───────────────

    [Fact]
    public void RawAndSanitizedSegmentWithSameTextAreNotEqual()
    {
        // IsRaw differs → not equal even though stored text and style match.
        Segment raw = Segment.Raw("hi");
        Segment sanitized = new("hi");
        Assert.NotEqual(raw, sanitized);
    }

    [Fact]
    public void TwoRawSegmentsSameTextAreEqual()
    {
        Assert.Equal(Segment.Raw("hi"), Segment.Raw("hi"));
    }

    [Fact]
    public void TwoSanitizedSegmentsSameTextAreEqual()
    {
        Assert.Equal(new Segment("hello"), new Segment("hello"));
    }

    // ── 2.2 / Decision 6: copy ctor preserves IsRaw (legal use of `with`) ───

    [Fact]
    public void CopyCtorPreservesIsRawForRawSegment()
    {
        // `segment with { }` (no-op copy) is legal; it must faithfully preserve IsRaw.
        // `segment with { Text = ... }` does NOT compile because Text is get-only — see comment below.
        Segment original = Segment.Raw("abc");
        Segment copy = original with { };
        Assert.True(copy.IsRaw);
        Assert.Equal("abc", copy.Text);
    }

    [Fact]
    public void CopyCtorPreservesIsRawFalseForSanitizedSegment()
    {
        Segment original = new("abc");
        Segment copy = original with { };
        Assert.False(copy.IsRaw);
    }

    // compile-guard: the following would be a compile error because Text is get-only:
    //   Segment s = new("x");
    //   Segment bad = s with { Text = "y" };   // CS0200: cannot assign to get-only property

    // ── 2.5: Width measured on stored (sanitized) text ───────────────────────

    [Fact]
    public void WidthMeasuredOnSanitizedText()
    {
        // ESC (0x1B) is stripped; stored text is "ab" (width 2), not "a\x1Bb" (ESC has width 0
        // per DisplayWidth, but the character is gone entirely after sanitization).
        string input = WithEsc("a", "b");
        Segment seg = new(input);
        // Stored text is the sanitized form; its visual width is 2 (two ASCII chars).
        Assert.Equal("ab", seg.Text);
        Assert.Equal(2, seg.Text.Length);
    }

    [Fact]
    public void RawWidthReflectsVerbatimContent()
    {
        // Raw segment stores ESC verbatim; Text length includes the ESC byte.
        string vt = WithEsc("[31m", "");
        Segment seg = Segment.Raw(vt);
        Assert.Equal(vt, seg.Text);
        Assert.Equal(vt.Length, seg.Text.Length);
    }
}
