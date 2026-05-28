using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Behavioural tests for Section 2: styled-text model (Format, Color, Style, Segment, Line, LineBuilder).
/// Each test maps to a specific spec scenario under capability styled-text.
/// </summary>
public sealed class StyledTextTests
{
    // -----------------------------------------------------------------------------------------
    // 2.1 Format enum
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void FormatNoneHasNoFlags()
    {
        Assert.Equal(0, (int)Format.None);
    }

    [Fact]
    public void FormatValuesArePowersOfTwo()
    {
        // Every non-None value must be a distinct power of two so they can be ORed without overlap.
        Format[] flags = [Format.Bold, Format.Italic, Format.Underline, Format.Dim, Format.Reverse, Format.Strikethrough];
        for (int i = 0; i < flags.Length; i++)
        {
            int v = (int)flags[i];
            Assert.True(v > 0 && (v & (v - 1)) == 0, $"{flags[i]} ({v}) is not a power of two");
            for (int j = i + 1; j < flags.Length; j++)
            {
                Assert.True((int)flags[i] != (int)flags[j], $"{flags[i]} and {flags[j]} have the same value");
            }
        }
    }

    [Fact]
    public void FormatCombineBoldAndItalicSetsBothFlags()
    {
        // Spec: Combining attributes — Bold | Italic yields a value with both flags set.
        Format combined = Format.Bold | Format.Italic;
        Assert.True(combined.HasFlag(Format.Bold));
        Assert.True(combined.HasFlag(Format.Italic));
        Assert.False(combined.HasFlag(Format.Underline));
    }

    [Fact]
    public void FormatNoneHasNoAttributes()
    {
        // Spec: No formatting — Format.None has no flags set.
        Format none = Format.None;
        Assert.False(none.HasFlag(Format.Bold));
        Assert.False(none.HasFlag(Format.Italic));
        Assert.False(none.HasFlag(Format.Underline));
        Assert.False(none.HasFlag(Format.Dim));
        Assert.False(none.HasFlag(Format.Reverse));
        Assert.False(none.HasFlag(Format.Strikethrough));
    }

    [Fact]
    public void FormatAllFlagsCombinedContainsAll()
    {
        Format all = Format.Bold | Format.Italic | Format.Underline | Format.Dim | Format.Reverse | Format.Strikethrough;
        Assert.True(all.HasFlag(Format.Bold));
        Assert.True(all.HasFlag(Format.Italic));
        Assert.True(all.HasFlag(Format.Underline));
        Assert.True(all.HasFlag(Format.Dim));
        Assert.True(all.HasFlag(Format.Reverse));
        Assert.True(all.HasFlag(Format.Strikethrough));
    }

    // -----------------------------------------------------------------------------------------
    // 2.2 Color
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ColorNamedRoundTrips()
    {
        Color c = Color.Named(Color.AnsiColor.BrightCyan);
        Assert.Equal(Color.ColorKind.Named, c.Kind);
        Assert.Equal(Color.AnsiColor.BrightCyan, c.NamedValue);
    }

    [Fact]
    public void ColorFromIndexRoundTrips()
    {
        Color c = Color.FromIndex(200);
        Assert.Equal(Color.ColorKind.Indexed, c.Kind);
        Assert.Equal(200, c.IndexValue);
    }

    [Fact]
    public void ColorFromIndexZeroAndMaxRoundTrip()
    {
        Assert.Equal(0, Color.FromIndex(0).IndexValue);
        Assert.Equal(255, Color.FromIndex(255).IndexValue);
    }

    [Fact]
    public void ColorFromRgbRoundTrips()
    {
        // Spec: Truecolor foreground — a 24-bit RGB foreground is carried faithfully.
        Color c = Color.FromRgb(0xDE, 0xAD, 0xBE);
        Assert.Equal(Color.ColorKind.Rgb, c.Kind);
        Assert.Equal(0xDE, c.R);
        Assert.Equal(0xAD, c.G);
        Assert.Equal(0xBE, c.B);
    }

    [Fact]
    public void ColorEqualitySameKindAndValueAreEqual()
    {
        Assert.Equal(Color.FromRgb(1, 2, 3), Color.FromRgb(1, 2, 3));
        Assert.Equal(Color.FromIndex(42), Color.FromIndex(42));
        Assert.Equal(Color.Named(Color.AnsiColor.Red), Color.Named(Color.AnsiColor.Red));
    }

    [Fact]
    public void ColorEqualityDifferentKindNotEqual()
    {
        // Index 1 and Named Red (ANSI index 1) have the same numeric value but different kinds.
        Assert.NotEqual(Color.FromIndex(1), Color.Named(Color.AnsiColor.Red));
    }

    [Fact]
    public void ColorWrongKindAccessorThrows()
    {
        Color rgb = Color.FromRgb(1, 2, 3);
        Assert.Throws<InvalidOperationException>(() => _ = rgb.NamedValue);
        Assert.Throws<InvalidOperationException>(() => _ = rgb.IndexValue);

        Color named = Color.Named(Color.AnsiColor.Blue);
        Assert.Throws<InvalidOperationException>(() => _ = named.R);
    }

    // -----------------------------------------------------------------------------------------
    // 2.2 Style
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void StyleDefaultHasNullColorsAndNoneFormat()
    {
        // Spec: No color — unset fg/bg means terminal default colors.
        Style s = default;
        Assert.Null(s.Foreground);
        Assert.Null(s.Background);
        Assert.Equal(Format.None, s.Format);
    }

    [Fact]
    public void StyleWithForegroundOnlyBackgroundRemainsNull()
    {
        Style s = new(Foreground: Color.Named(Color.AnsiColor.Green));
        Assert.NotNull(s.Foreground);
        Assert.Null(s.Background);
        Assert.Equal(Format.None, s.Format);
    }

    [Fact]
    public void StyleWithAllFieldsRoundTrips()
    {
        Color fg = Color.FromRgb(255, 0, 128);
        Color bg = Color.FromIndex(16);
        Style s = new(fg, bg, Format.Bold | Format.Underline);
        Assert.Equal(fg, s.Foreground);
        Assert.Equal(bg, s.Background);
        Assert.True(s.Format.HasFlag(Format.Bold));
        Assert.True(s.Format.HasFlag(Format.Underline));
    }

    // -----------------------------------------------------------------------------------------
    // 2.3 Segment
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void SegmentStoresTextLiterally()
    {
        // Spec: Markup-like text is literal — "[bold]", "{x}", etc. are stored verbatim.
        string markup = "[bold]{x}<em>foo</em>";
        Segment seg = new(markup);
        Assert.Equal(markup, seg.Text);
    }

    [Fact]
    public void SegmentDefaultStyleIsDefault()
    {
        Segment seg = new("hello");
        Assert.Equal(default, seg.Style);
    }

    [Fact]
    public void SegmentRetainsDistinctStyle()
    {
        Style s = new(Foreground: Color.Named(Color.AnsiColor.Yellow), Format: Format.Bold);
        Segment seg = new("hi", s);
        Assert.Equal(s, seg.Style);
        Assert.Equal("hi", seg.Text);
    }

    [Fact]
    public void SegmentEqualitySameTextAndStyleAreEqual()
    {
        Style s = new(Format: Format.Italic);
        Assert.Equal(new Segment("a", s), new Segment("a", s));
    }

    [Fact]
    public void SegmentEqualityDifferentTextNotEqual()
    {
        Assert.NotEqual(new Segment("a"), new Segment("b"));
    }

    // -----------------------------------------------------------------------------------------
    // 2.3 Line
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void LineSegmentsInOrderPreservedOnConstruction()
    {
        Segment s1 = new("first", new Style(Format: Format.Bold));
        Segment s2 = new("second", new Style(Format: Format.Dim));
        Line line = new(new[] { s1, s2 });
        Assert.Equal(2, line.Segments.Count);
        Assert.Equal(s1, line.Segments[0]);
        Assert.Equal(s2, line.Segments[1]);
    }

    [Fact]
    public void LineStructuralEqualitySameSegmentsAreEqual()
    {
        // Confirms that Line overrides record equality to use sequence equality, not reference equality.
        Segment s1 = new("a");
        Segment s2 = new("b");
        Line line1 = new(new[] { s1, s2 });
        Line line2 = new(new[] { s1, s2 });
        Assert.Equal(line1, line2);
    }

    [Fact]
    public void LineStructuralEqualityDifferentSegmentsNotEqual()
    {
        Line line1 = new(new[] { new Segment("a") });
        Line line2 = new(new[] { new Segment("b") });
        Assert.NotEqual(line1, line2);
    }

    [Fact]
    public void LineStructuralEqualityDifferentOrderNotEqual()
    {
        Segment s1 = new("a");
        Segment s2 = new("b");
        Line line1 = new(new[] { s1, s2 });
        Line line2 = new(new[] { s2, s1 });
        Assert.NotEqual(line1, line2);
    }

    [Fact]
    public void LineEmptyIsValid()
    {
        Line line = new(Array.Empty<Segment>());
        Assert.Empty(line.Segments);
    }

    // -----------------------------------------------------------------------------------------
    // 2.3 Line.FromText
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void LineFromTextDefaultStyleProducesUnstyledSegment()
    {
        // Spec: FromText produces an unstyled line — no style argument → default(Style).
        Line line = Line.FromText("hello");
        Assert.Single(line.Segments);
        Assert.Equal("hello", line.Segments[0].Text);
        Assert.Equal(default, line.Segments[0].Style);
    }

    [Fact]
    public void LineFromTextExplicitStyleIsCarried()
    {
        // Spec: FromText respects an explicit style.
        Style bold = new(Format: Format.Bold);
        Line line = Line.FromText("err", bold);
        Assert.Single(line.Segments);
        Assert.Equal("err", line.Segments[0].Text);
        Assert.True(line.Segments[0].Style.Format.HasFlag(Format.Bold));
    }

    [Fact]
    public void LineFromTextEmptyStringProducesSingleEmptySegment()
    {
        Line line = Line.FromText("");
        Assert.Single(line.Segments);
        Assert.Equal("", line.Segments[0].Text);
    }

    [Fact]
    public void LineFromTextMultiRuneStringRoundTripsTextIntact()
    {
        // Unicode content (combining accent + emoji) must survive unchanged.
        const string text = "héllo🦊";
        Line line = Line.FromText(text);
        Assert.Equal(text, line.Segments[0].Text);
    }

    [Fact]
    public void LineFromTextStructurallyEqualsManualConstruction()
    {
        // Line has sequence equality; FromText must produce the same value as the long form.
        Line fromFactory = Line.FromText("hello");
        Line manual = new(new[] { new Segment("hello") });
        Assert.Equal(manual, fromFactory);
    }

    // -----------------------------------------------------------------------------------------
    // 2.3 LineBuilder
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void LineBuilderAppendsSegmentsInOrder()
    {
        // Spec: Building a line — fragments are stored in the order they were added.
        Line line = new LineBuilder()
            .Text("first")
            .Bold("second")
            .Dim("third")
            .Build();

        Assert.Equal(3, line.Segments.Count);
        Assert.Equal("first", line.Segments[0].Text);
        Assert.Equal("second", line.Segments[1].Text);
        Assert.Equal("third", line.Segments[2].Text);
    }

    [Fact]
    public void LineBuilderTextAppliesDefaultStyle()
    {
        Line line = new LineBuilder().Text("hello").Build();
        Assert.Equal(default, line.Segments[0].Style);
    }

    [Fact]
    public void LineBuilderBoldSetsBoldFlag()
    {
        Line line = new LineBuilder().Bold("hi").Build();
        Assert.True(line.Segments[0].Style.Format.HasFlag(Format.Bold));
    }

    [Fact]
    public void LineBuilderItalicSetsItalicFlag()
    {
        Line line = new LineBuilder().Italic("hi").Build();
        Assert.True(line.Segments[0].Style.Format.HasFlag(Format.Italic));
    }

    [Fact]
    public void LineBuilderDimSetsDimFlag()
    {
        Line line = new LineBuilder().Dim("✻ ").Build();
        Assert.True(line.Segments[0].Style.Format.HasFlag(Format.Dim));
    }

    [Fact]
    public void LineBuilderAppendCarriesArbitraryStyle()
    {
        Style s = new(Color.FromRgb(100, 200, 50), null, Format.Bold | Format.Underline);
        Line line = new LineBuilder().Append("styled", s).Build();
        Assert.Equal(s, line.Segments[0].Style);
        Assert.Equal("styled", line.Segments[0].Text);
    }

    [Fact]
    public void LineBuilderEmptyBuildsEmptyLine()
    {
        Line line = new LineBuilder().Build();
        Assert.Empty(line.Segments);
    }

    [Fact]
    public void LineBuilderSpecExampleDimThenText()
    {
        // Mirrors the spec-sketch: new LineBuilder().Dim("✻ ").Text("Thinking…").Build()
        Line line = new LineBuilder().Dim("✻ ").Text("Thinking…").Build();
        Assert.Equal(2, line.Segments.Count);
        Assert.Equal("✻ ", line.Segments[0].Text);
        Assert.True(line.Segments[0].Style.Format.HasFlag(Format.Dim));
        Assert.Equal("Thinking…", line.Segments[1].Text);
        Assert.Equal(default, line.Segments[1].Style);
    }

    // -----------------------------------------------------------------------------------------
    // Zone-agnostic reuse (Spec: Shared across both zones)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void StyledTextSameTypesUsedInBothZones()
    {
        // Spec: Reuse in both zones — the same Segment/Line/Style types serve scrollback and
        // fixed-region (status) content without any zone-specific coupling.

        // Simulated scrollback line (content zone)
        Line scrollbackLine = new LineBuilder()
            .Text("User: ")
            .Bold("hello")
            .Build();

        // Simulated status line (fixed region)
        Line statusLine = new LineBuilder()
            .Fg("claude-opus-4", Color.Named(Color.AnsiColor.BrightGreen))
            .Text(" | ")
            .Dim("thinking")
            .Build();

        // Both use the same types — no zone-specific types needed.
        Assert.IsType<Line>(scrollbackLine);
        Assert.IsType<Line>(statusLine);
        Assert.Equal(2, scrollbackLine.Segments.Count);
        Assert.Equal(3, statusLine.Segments.Count);
    }

    [Fact]
    public void StyledTextMarkupLikeTextIsLiteralInBothZones()
    {
        // Markup-like strings stay verbatim in all contexts.
        string[] literals = ["[bold]", "{x}", "<em>", "**strong**", "\x1b[1m"];
        foreach (string literal in literals)
        {
            Line line = new LineBuilder().Text(literal).Build();
            Assert.Equal(literal, line.Segments[0].Text);
        }
    }
}
