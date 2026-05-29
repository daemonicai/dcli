using Dcli.Testing;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// §3 (vt-escape-sanitization) — wiring tests confirming that every public string→Segment
/// surface funnels through the sanitizing <see cref="Segment"/> primary constructor.
/// These tests assert the wiring, not the sanitizer internals (those are in TextSanitizerTests).
/// </summary>
public sealed class SanitizationWiringTests
{
    // Build strings with embedded control bytes without raw literals in source.
    private static string WithEsc(string before, string after) =>
        before + (char)0x1B + after;

    // ── 3.1 — LineBuilder ────────────────────────────────────────────────────

    [Fact]
    public void LineBuilderTextSanitizes()
    {
        // .Text() routes through new Segment(text, style) — the sanitizing ctor.
        Line line = new LineBuilder().Text(WithEsc("a", "b")).Build();
        Assert.Equal("ab", line.Segments[0].Text);
        Assert.False(line.Segments[0].IsRaw);
    }

    [Fact]
    public void LineBuilderAppendSanitizes()
    {
        Line line = new LineBuilder().Append(WithEsc("x", "y")).Build();
        Assert.Equal("xy", line.Segments[0].Text);
        Assert.False(line.Segments[0].IsRaw);
    }

    [Fact]
    public void LineBuilderBoldSanitizes()
    {
        Line line = new LineBuilder().Bold(WithEsc("a", "b")).Build();
        Assert.Equal("ab", line.Segments[0].Text);
    }

    [Fact]
    public void LineBuilderRawPreservesVerbatim()
    {
        // .Raw() appends Segment.Raw — bypasses sanitization; IsRaw must be true.
        string vt = WithEsc("[31m", "");
        Line line = new LineBuilder().Raw(vt).Build();
        Assert.Equal(vt, line.Segments[0].Text);
        Assert.True(line.Segments[0].IsRaw);
    }

    [Fact]
    public void LineBuilderRawContrastWithText()
    {
        // Same input string: Raw → verbatim (IsRaw=true); Text → sanitized (ESC stripped).
        string input = WithEsc("[31m", "");
        Line rawLine = new LineBuilder().Raw(input).Build();
        Line textLine = new LineBuilder().Text(input).Build();

        Assert.True(rawLine.Segments[0].IsRaw);
        Assert.Equal(input, rawLine.Segments[0].Text);

        Assert.False(textLine.Segments[0].IsRaw);
        Assert.DoesNotContain((char)0x1B, textLine.Segments[0].Text);
    }

    [Fact]
    public void LineBuilderRawStylePropagates()
    {
        Style style = new(Format: Format.Bold);
        Line line = new LineBuilder().Raw("x", style).Build();
        Assert.Equal(style, line.Segments[0].Style);
        Assert.True(line.Segments[0].IsRaw);
    }

    // ── 3.2 — Line.FromText ──────────────────────────────────────────────────

    [Fact]
    public void LineFromTextSanitizesEsc()
    {
        Line line = Line.FromText(WithEsc("a", "b"));
        Assert.Equal("ab", line.Segments[0].Text);
        Assert.False(line.Segments[0].IsRaw);
    }

    // ── 3.2 — SelectRequest string overloads ─────────────────────────────────

    [Fact]
    public void SelectRequestParamsStringSanitizesItems()
    {
        // params string[] items → ConvertItems → Line.FromText → new Segment (sanitizing ctor).
        SelectRequest req = new SelectRequest(WithEsc("item", ""));
        Assert.DoesNotContain((char)0x1B, req.Items[0].Segments[0].Text);
        Assert.Equal("item", req.Items[0].Segments[0].Text);
    }

    [Fact]
    public void SelectRequestIReadOnlyListStringSanitizesItems()
    {
        IReadOnlyList<string> items = [WithEsc("a", "b"), "clean"];
        SelectRequest req = new SelectRequest(items);
        Assert.DoesNotContain((char)0x1B, req.Items[0].Segments[0].Text);
        Assert.Equal("ab", req.Items[0].Segments[0].Text);
    }

    [Fact]
    public void SelectRequestStringTitleSanitizesTitle()
    {
        // IReadOnlyList<string> title overload → ConvertPreamble → Line.FromText → new Segment (sanitizing ctor).
        string escTitle = WithEsc("ti", "tle");
        SelectRequest req = new SelectRequest((IReadOnlyList<string>)["item"], (IReadOnlyList<string>?)[escTitle]);
        Line titleLine = req.Title![0];
        Assert.DoesNotContain((char)0x1B, titleLine.Segments[0].Text);
    }

    // ── 3.2 — MultiSelectRequest string overloads ────────────────────────────

    [Fact]
    public void MultiSelectRequestParamsStringSanitizesItems()
    {
        MultiSelectRequest req = new MultiSelectRequest(WithEsc("opt", "ion"));
        Assert.DoesNotContain((char)0x1B, req.Items[0].Segments[0].Text);
    }

    [Fact]
    public void MultiSelectRequestIReadOnlyListStringSanitizesItems()
    {
        IReadOnlyList<string> items = [WithEsc("x", "y")];
        MultiSelectRequest req = new MultiSelectRequest(items);
        Assert.Equal("xy", req.Items[0].Segments[0].Text);
    }

    [Fact]
    public void MultiSelectRequestStringTitleSanitizesTitle()
    {
        // IReadOnlyList<string> title overload → ConvertPreamble → Line.FromText → new Segment (sanitizing ctor).
        string escTitle = WithEsc("T", "itle");
        MultiSelectRequest req = new MultiSelectRequest((IReadOnlyList<string>)["item"], (IReadOnlyList<string>?)[escTitle]);
        Line titleLine = req.Title![0];
        Assert.DoesNotContain((char)0x1B, titleLine.Segments[0].Text);
    }

    // ── 3.2 — ChoiceRequest string overloads ─────────────────────────────────

    [Fact]
    public void ChoiceRequestParamsStringSanitizesOptions()
    {
        ChoiceRequest req = new ChoiceRequest(WithEsc("yes", ""), WithEsc("no", ""));
        Assert.DoesNotContain((char)0x1B, req.Options[0].Segments[0].Text);
        Assert.DoesNotContain((char)0x1B, req.Options[1].Segments[0].Text);
    }

    [Fact]
    public void ChoiceRequestIReadOnlyListStringSanitizesOptions()
    {
        IReadOnlyList<string> opts = [WithEsc("a", "b")];
        ChoiceRequest req = new ChoiceRequest(opts);
        Assert.Equal("ab", req.Options[0].Segments[0].Text);
    }

    [Fact]
    public void ChoiceRequestStringPromptSanitizesPrompt()
    {
        // IReadOnlyList<string> prompt overload → ConvertPreamble → Line.FromText → new Segment (sanitizing ctor).
        string escPrompt = WithEsc("P", "rompt");
        ChoiceRequest req = new ChoiceRequest((IReadOnlyList<string>)["yes"], (IReadOnlyList<string>?)[escPrompt]);
        Line promptLine = req.Prompt![0];
        Assert.DoesNotContain((char)0x1B, promptLine.Segments[0].Text);
    }

    // ── 3.2 — InputRequest string overload ───────────────────────────────────

    [Fact]
    public void InputRequestStringPromptSanitizesPrompt()
    {
        InputRequest req = new InputRequest(WithEsc("enter ", "name"));
        Line promptLine = req.Prompt![0];
        Assert.DoesNotContain((char)0x1B, promptLine.Segments[0].Text);
        Assert.Equal("enter name", promptLine.Segments[0].Text);
    }

    [Fact]
    public void InputRequestNullPromptIsAllowed()
    {
        InputRequest req = new InputRequest((string?)null);
        Assert.Null(req.Prompt);
    }

    // ── 3.2 — IScrollback.Append(string) ─────────────────────────────────────

    [Fact]
    public async Task ScrollbackAppendStringSanitizesViaHeadlessTerminal()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        harness.Terminal.Scrollback.Append(WithEsc("hello", "world"));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;

        // The appended line should appear in live window rows or newly committed rows.
        // Look for it in both, since the line may have committed depending on timing.
        Line? matchedLine = snap.LiveWindowRows
            .Concat(snap.NewlyCommittedRows)
            .FirstOrDefault(line => line.Segments.Count > 0 &&
                                    line.Segments[0].Text.Contains("helloworld", StringComparison.Ordinal));

        Assert.NotNull(matchedLine);  // proves the appended line actually surfaced

        string segText = matchedLine.Segments[0].Text;
        Assert.True(
            segText.IndexOf((char)0x1B, StringComparison.Ordinal) < 0,
            "Scrollback.Append(string) must sanitize before the segment is stored.");
    }
}
