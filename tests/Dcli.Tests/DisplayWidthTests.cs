using System.Text;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Behavioural tests for Section 3: display-width measurement and line wrapping.
/// Each test maps to a spec scenario described in spec.md / the Section-3 brief.
/// </summary>
public sealed class DisplayWidthTests
{
    // -----------------------------------------------------------------------------------------
    // 3.1  DisplayWidth.Measure(Rune) — per-scalar width
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void AsciiLetterIsWidth1()
    {
        Assert.Equal(1, DisplayWidth.Measure(new Rune('A')));
        Assert.Equal(1, DisplayWidth.Measure(new Rune('z')));
        Assert.Equal(1, DisplayWidth.Measure(new Rune('0')));
    }

    [Fact]
    public void CjkIdeographIsWidth2()
    {
        // U+4E2D (中) is a CJK Unified Ideograph.
        Assert.Equal(2, DisplayWidth.Measure(new Rune(0x4E2D)));
        // U+6587 (文)
        Assert.Equal(2, DisplayWidth.Measure(new Rune(0x6587)));
    }

    [Fact]
    public void HiraganaIsWidth2()
    {
        // U+3042 (あ) — Hiragana block is Wide.
        Assert.Equal(2, DisplayWidth.Measure(new Rune(0x3042)));
    }

    [Fact]
    public void HangulSyllableIsWidth2()
    {
        // U+AC00 (가) — first Hangul syllable.
        Assert.Equal(2, DisplayWidth.Measure(new Rune(0xAC00)));
    }

    [Fact]
    public void FullwidthLatinIsWidth2()
    {
        // U+FF21 (Ａ) — Fullwidth Latin Capital A.
        Assert.Equal(2, DisplayWidth.Measure(new Rune(0xFF21)));
    }

    [Fact]
    public void WideEmojiScalarIsWidth2()
    {
        // U+1F600 (😀) — Grinning Face emoji, wide.
        Assert.Equal(2, DisplayWidth.Measure(new Rune(0x1F600)));
    }

    [Fact]
    public void CombiningMarkIsWidth0()
    {
        // U+0301 (COMBINING ACUTE ACCENT) — NonSpacingMark.
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x0301)));
        // U+0308 (COMBINING DIAERESIS)
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x0308)));
    }

    [Fact]
    public void ZeroWidthSpaceIsWidth0()
    {
        // U+200B ZERO WIDTH SPACE
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x200B)));
    }

    [Fact]
    public void ZeroWidthJoinerIsWidth0()
    {
        // U+200D ZERO WIDTH JOINER
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x200D)));
    }

    [Fact]
    public void ZeroWidthNonJoinerIsWidth0()
    {
        // U+200C ZERO WIDTH NON-JOINER
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x200C)));
    }

    [Fact]
    public void BomIsWidth0()
    {
        // U+FEFF BOM / ZERO WIDTH NO-BREAK SPACE — Format category.
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0xFEFF)));
    }

    [Fact]
    public void SoftHyphenIsWidth0()
    {
        // U+00AD SOFT HYPHEN — explicitly zero-width.
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x00AD)));
    }

    [Fact]
    public void ControlCharsBelowSpaceAreWidth0()
    {
        // ESC, NUL, BEL, etc. — all control chars < 0x20 except TAB.
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x00)));  // NUL
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x1B)));  // ESC
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x07)));  // BEL
        Assert.Equal(0, DisplayWidth.Measure(new Rune(0x7F)));  // DEL
    }

    [Fact]
    public void TabFromColumn0IsWidth8()
    {
        // Tab at column 0 advances to column 8.
        Assert.Equal(DisplayWidth.TabWidth, DisplayWidth.MeasureWithColumn(new Rune('\t'), 0));
    }

    [Fact]
    public void TabFromColumn4IsWidth4()
    {
        // Column 4 → next stop at 8 → width = 4.
        Assert.Equal(4, DisplayWidth.MeasureWithColumn(new Rune('\t'), 4));
    }

    [Fact]
    public void TabFromColumn8IsWidth8()
    {
        // Column 8 is itself a stop → next stop at 16 → width = 8.
        Assert.Equal(8, DisplayWidth.MeasureWithColumn(new Rune('\t'), 8));
    }

    [Fact]
    public void TabFromColumn7IsWidth1()
    {
        // Column 7 → next stop at 8 → width = 1.
        Assert.Equal(1, DisplayWidth.MeasureWithColumn(new Rune('\t'), 7));
    }

    // -----------------------------------------------------------------------------------------
    // 3.1  DisplayWidth.Measure(string)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void MeasureStringPureAsciiEqualsLength()
    {
        Assert.Equal(5, DisplayWidth.Measure("hello"));
    }

    [Fact]
    public void MeasureStringWithCjkCountsDoubleColumns()
    {
        // "中文" = 2 + 2 = 4
        Assert.Equal(4, DisplayWidth.Measure("中文"));
    }

    [Fact]
    public void MeasureStringBaseCharPlusCombiningMarkEqualsBase()
    {
        // "é" = é (e + combining acute) → 1 + 0 = 1
        Assert.Equal(1, DisplayWidth.Measure("é"));
    }

    [Fact]
    public void MeasureStringZwjSequenceWidthIsBaseScalarsOnly()
    {
        // "A‍B" = A + ZWJ + B → 1 + 0 + 1 = 2 (per-scalar, no grapheme clustering)
        Assert.Equal(2, DisplayWidth.Measure("A‍B"));
    }

    [Fact]
    public void MeasureStringTabAtStartIsFullTabWidth()
    {
        // "\thello" — tab expands to 8, then 5 ASCII = 13.
        Assert.Equal(13, DisplayWidth.Measure("\thello"));
    }

    [Fact]
    public void MeasureEmptyStringIsZero()
    {
        Assert.Equal(0, DisplayWidth.Measure(""));
    }

    [Fact]
    public void MeasureStringMixedAsciiAndCjk()
    {
        // "AB中C" = 1+1+2+1 = 5
        Assert.Equal(5, DisplayWidth.Measure("AB中C"));
    }

    [Fact]
    public void MeasureStringWideSurrogatePairEmoji()
    {
        // U+1F600 😀 is a surrogate pair in UTF-16: should be width 2.
        Assert.Equal(2, DisplayWidth.Measure("\U0001F600"));
    }
}
