using Dcli.Internal.RenderLoop;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Behavioural tests for §8 (Chunk A + Chunk B): SGR translation, single-frame VT emission,
/// and cross-frame cursor accounting (move-to-anchor, commit choreography, frozen-never-rewritten).
/// The injected <see cref="StringWriter"/> captures the byte stream; helper methods
/// decode structural facts (sync fence, SGR codes, erase-to-EOL, cursor positioning)
/// so tests assert behaviour rather than raw escape soup.
/// </summary>
public sealed class VtFrameRendererTests
{
    // ── VT escape-sequence constants used in assertions ───────────────────────
    private const string _syncStart = "\x1b[?2026h";
    private const string _syncEnd = "\x1b[?2026l";
    private const string _eraseEol = "\x1b[K";
    private const string _cursorHide = "\x1b[?25l";
    private const string _cursorShow = "\x1b[?25h";
    private const string _sgrReset = "\x1b[0m";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (VtFrameRenderer renderer, StringWriter writer, RenderModel model) MakeRenderer(
        int cols = 80, int rows = 24)
    {
        StringWriter w = new();
        VtFrameRenderer r = new(w);
        RenderModel m = new(new FixedSizeSource(cols, rows));
        return (r, w, m);
    }

    private static Line L(params Segment[] segments) => new(segments);
    private static Line PlainLine(string text) => L(new Segment(text));

    // ── §8.2  Synchronized-output fence ──────────────────────────────────────

    [Fact]
    public void EmptyFrameWrappedInSyncFence()
    {
        var (renderer, writer, model) = MakeRenderer();
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.StartsWith(_syncStart, output, StringComparison.Ordinal);
        Assert.EndsWith(_syncEnd, output, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncFenceBracketsAllContent()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("hello")];
        renderer.Paint(model);

        string output = writer.ToString();
        int startIdx = output.IndexOf(_syncStart, StringComparison.Ordinal);
        int endIdx = output.LastIndexOf(_syncEnd, StringComparison.Ordinal);

        Assert.True(startIdx >= 0, "_syncStart not found");
        Assert.True(endIdx > startIdx + _syncStart.Length, "_syncEnd must appear after _syncStart");

        // "hello" must be between the fence brackets.
        string interior = output.Substring(startIdx + _syncStart.Length,
                                           endIdx - startIdx - _syncStart.Length);
        Assert.Contains("hello", interior, StringComparison.Ordinal);
    }

    // ── Plain row (no SGR) ────────────────────────────────────────────────────

    [Fact]
    public void PlainRowEmitsTextAndEraseEolWithNoSgr()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("plain text")];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("plain text", output, StringComparison.Ordinal);
        Assert.Contains(_eraseEol, output, StringComparison.Ordinal);
        // No SGR attributes or reset should appear for an unstyled row.
        Assert.DoesNotContain(_sgrReset, output, StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b[1m", output, StringComparison.Ordinal); // no bold
    }

    // ── Multi-segment styled row ──────────────────────────────────────────────

    [Fact]
    public void BoldSegmentEmitsSgr1ThenTextThenReset()
    {
        var (renderer, writer, model) = MakeRenderer();
        Segment bold = new("BOLD", new Style(Format: Format.Bold));
        model.LiveWindowRows = [L(bold)];
        renderer.Paint(model);

        string output = writer.ToString();
        // Bold SGR code 1
        int boldIdx = output.IndexOf("\x1b[1m", StringComparison.Ordinal);
        int textIdx = output.IndexOf("BOLD", StringComparison.Ordinal);
        int resetIdx = output.IndexOf(_sgrReset, StringComparison.Ordinal);

        Assert.True(boldIdx >= 0, "bold SGR not found");
        Assert.True(textIdx > boldIdx, "text must follow SGR open");
        Assert.True(resetIdx > textIdx, "reset must follow text");
    }

    [Fact]
    public void MultipleFormatFlagsAllCodesPresent()
    {
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("BOTH", new Style(Format: Format.Bold | Format.Underline));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        // Bold=1 and Underline=4 must both appear inside one SGR sequence before the text.
        int textIdx = output.IndexOf("BOTH", StringComparison.Ordinal);
        string beforeText = output.Substring(0, textIdx);
        Assert.Contains("1", beforeText, StringComparison.Ordinal);   // bold code
        Assert.Contains("4", beforeText, StringComparison.Ordinal);   // underline code
        Assert.Contains(_sgrReset, output, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedForegroundEmitsCorrectFgCode()
    {
        // Red = AnsiColor 1 → fg code 31
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("RED", new Style(Foreground: Color.Named(Color.AnsiColor.Red)));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("\x1b[31m", output, StringComparison.Ordinal); // fg red
        Assert.Contains("RED", output, StringComparison.Ordinal);
        Assert.Contains(_sgrReset, output, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedBackgroundEmitsCorrectBgCode()
    {
        // Green = AnsiColor 2 → bg code 42
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("GRN", new Style(Background: Color.Named(Color.AnsiColor.Green)));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("\x1b[42m", output, StringComparison.Ordinal); // bg green
    }

    [Fact]
    public void BrightNamedForegroundEmitsHighIntensityCode()
    {
        // BrightRed = AnsiColor 9 → fg code 91
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("BR", new Style(Foreground: Color.Named(Color.AnsiColor.BrightRed)));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("91", output, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexedForegroundEmitsExtendedColorSequence()
    {
        // 256-color fg: ESC[38;5;200m
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("IDX", new Style(Foreground: Color.FromIndex(200)));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("38;5;200", output, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexedBackgroundEmitsExtendedColorSequence()
    {
        // 256-color bg: ESC[48;5;100m
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("IDX", new Style(Background: Color.FromIndex(100)));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("48;5;100", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TruecolorForegroundEmitsRgbSequence()
    {
        // Truecolor fg: ESC[38;2;255;128;0m
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("TC", new Style(Foreground: Color.FromRgb(255, 128, 0)));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("38;2;255;128;0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TruecolorBackgroundEmitsRgbSequence()
    {
        // Truecolor bg: ESC[48;2;10;20;30m
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("TC", new Style(Background: Color.FromRgb(10, 20, 30)));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("48;2;10;20;30", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatComboAndTruecolorAllCodesPresent()
    {
        // Bold + Italic foreground truecolor = codes 1;3 + 38;2;r;g;b
        var (renderer, writer, model) = MakeRenderer();
        Segment seg = new("COMBO",
            new Style(Foreground: Color.FromRgb(1, 2, 3), Format: Format.Bold | Format.Italic));
        model.LiveWindowRows = [L(seg)];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains("38;2;1;2;3", output, StringComparison.Ordinal);
        // Bold and italic codes must appear before the text.
        int textIdx = output.IndexOf("COMBO", StringComparison.Ordinal);
        string beforeText = output.Substring(0, textIdx);
        Assert.Contains("1", beforeText, StringComparison.Ordinal);
        Assert.Contains("3", beforeText, StringComparison.Ordinal);
        Assert.Contains(_sgrReset, output, StringComparison.Ordinal);
    }

    // ── Each row ends with ESC[K ──────────────────────────────────────────────

    [Fact]
    public void EachRowEndsWithEraseEol()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("row1"), PlainLine("row2")];
        renderer.Paint(model);

        string output = writer.ToString();
        // Two rows → two ESC[K occurrences.
        int count = CountOccurrences(output, _eraseEol);
        Assert.Equal(2, count);
    }

    [Fact]
    public void FixedRegionRowsAlsoGetEraseEol()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("live")];
        model.FixedRegionRows = [PlainLine("status")];
        renderer.Paint(model);

        string output = writer.ToString();
        int count = CountOccurrences(output, _eraseEol);
        Assert.Equal(2, count); // one per row across both lists
    }

    // ── Caret placement ───────────────────────────────────────────────────────

    [Fact]
    public void NoCaretCursorVisibleShowsAndNoPositioning()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.IsCursorVisible = true;
        model.CaretPosition = null;
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains(_cursorShow, output, StringComparison.Ordinal);
        Assert.DoesNotContain(_cursorHide, output, StringComparison.Ordinal);
        // No cursor-up (ESC[nA) or CHA (ESC[nG) when CaretPosition is null.
        Assert.DoesNotContain("\x1b[1A", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CaretAtLastRowNoCursorUpColumnPlaced()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("only row")];
        // Caret at row 0 col 3 (the only row, which is the last row → 0 steps up).
        model.CaretPosition = (0, 3);
        model.IsCursorVisible = true;
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains(_cursorShow, output, StringComparison.Ordinal);
        // CHA to column 4 (1-based): ESC[4G
        Assert.Contains("\x1b[4G", output, StringComparison.Ordinal);
        // No cursor-up needed.
        Assert.DoesNotContain("\x1b[1A", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CaretAboveLastRowCursorUpEmitted()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("line0"), PlainLine("line1"), PlainLine("line2")];
        // Caret at row 0, col 0 → 2 steps up from the last row.
        model.CaretPosition = (0, 0);
        model.IsCursorVisible = true;
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains(_cursorShow, output, StringComparison.Ordinal);
        // Two rows up: ESC[2A
        Assert.Contains("\x1b[2A", output, StringComparison.Ordinal);
        // Move to column 1 (1-based): ESC[1G
        Assert.Contains("\x1b[1G", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CaretPositioningLandsOnCorrectRow()
    {
        // 3 rows. After emitting the last row WITHOUT a trailing newline, the cursor rests
        // on row index 2 (the last content row). Caret at row 1, col 5:
        //   stepsUp = totalRows - 1 - caretRow = 3 - 1 - 1 = 1
        //   CHA     = col + 1 = 6
        // The emitted sequence after cursor-show must be exactly ESC[1A then ESC[6G.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("line0"), PlainLine("line1"), PlainLine("line2")];
        model.CaretPosition = (1, 5);
        model.IsCursorVisible = true;
        renderer.Paint(model);

        string output = writer.ToString();

        // Verify resting-position convention: final row must NOT be followed by '\n'
        // immediately before the sync-end fence (no spurious blank line).
        int lastEraseIdx = output.LastIndexOf(_eraseEol, StringComparison.Ordinal);
        Assert.True(lastEraseIdx >= 0, "ESC[K not found");
        // The character after the last ESC[K must not be '\n' — cursor rests on that row.
        int afterLastErase = lastEraseIdx + _eraseEol.Length;
        Assert.True(afterLastErase < output.Length, "output ends immediately after last ESC[K");
        Assert.NotEqual('\n', output[afterLastErase]);

        // Exactly 1 step up: ESC[1A
        Assert.Contains("\x1b[1A", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b[2A", output, StringComparison.Ordinal);

        // CHA to column 6: ESC[6G
        Assert.Contains("\x1b[6G", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalRowHasNoTrailingNewlineBeforeSyncEnd()
    {
        // Verifies the no-trailing-newline convention for single-row and multi-row frames.
        // The last ESC[K in the output must not be immediately followed by '\n'.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("row0"), PlainLine("row1")];
        model.FixedRegionRows = [PlainLine("status")];
        renderer.Paint(model);

        string output = writer.ToString();
        int lastEraseIdx = output.LastIndexOf(_eraseEol, StringComparison.Ordinal);
        Assert.True(lastEraseIdx >= 0, "ESC[K not found");
        int afterLastErase = lastEraseIdx + _eraseEol.Length;
        Assert.True(afterLastErase < output.Length, "output ends immediately after last ESC[K");
        Assert.NotEqual('\n', output[afterLastErase]);
    }

    // ── §8.3  Cursor hide (modal-dialog case) ────────────────────────────────

    [Fact]
    public void CursorHiddenEmitsHideAndNoShow()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.IsCursorVisible = false;
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains(_cursorHide, output, StringComparison.Ordinal);
        Assert.DoesNotContain(_cursorShow, output, StringComparison.Ordinal);
    }

    [Fact]
    public void CursorHiddenNoCursorPositioning()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("dialog")];
        model.IsCursorVisible = false;
        model.CaretPosition = (0, 5); // set but should be ignored
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains(_cursorHide, output, StringComparison.Ordinal);
        // CHA sequences must not appear — caret is ignored when cursor hidden.
        Assert.DoesNotContain("\x1b[6G", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b[1G", output, StringComparison.Ordinal);
    }

    // ── LastPaintedHeight seam (Chunk B) ─────────────────────────────────────

    [Fact]
    public void LastPaintedHeightReflectsTotalRowCount()
    {
        var (renderer, _, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("a"), PlainLine("b")];
        model.FixedRegionRows = [PlainLine("status")];
        renderer.Paint(model);

        Assert.Equal(3, renderer.LastPaintedHeight);
    }

    [Fact]
    public void LastPaintedHeightZeroBeforeFirstPaint()
    {
        StringWriter w = new();
        VtFrameRenderer r = new(w);
        Assert.Equal(0, r.LastPaintedHeight);
    }

    // ── SGR unit tests (pure SgrTranslator, no I/O) ──────────────────────────

    [Fact]
    public void SgrDefaultStyleReturnsEmpty()
    {
        string sgr = SgrTranslator.ToOpenSgr(default);
        Assert.Equal(string.Empty, sgr);
    }

    [Fact]
    public void SgrBoldContainsCode1()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Format: Format.Bold));
        Assert.Contains("1", sgr, StringComparison.Ordinal);
        Assert.StartsWith("\x1b[", sgr, StringComparison.Ordinal);
        Assert.EndsWith("m", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrDimContainsCode2()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Format: Format.Dim));
        Assert.Contains("2", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrItalicContainsCode3()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Format: Format.Italic));
        Assert.Contains("3", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrUnderlineContainsCode4()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Format: Format.Underline));
        Assert.Contains("4", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrReverseContainsCode7()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Format: Format.Reverse));
        Assert.Contains("7", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrStrikethroughContainsCode9()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Format: Format.Strikethrough));
        Assert.Contains("9", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrNamedBlackFg30()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Foreground: Color.Named(Color.AnsiColor.Black)));
        Assert.Contains("30", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrNamedWhiteFg37()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Foreground: Color.Named(Color.AnsiColor.White)));
        Assert.Contains("37", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrBrightBlackFg90()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Foreground: Color.Named(Color.AnsiColor.BrightBlack)));
        Assert.Contains("90", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrBrightWhiteFg97()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Foreground: Color.Named(Color.AnsiColor.BrightWhite)));
        Assert.Contains("97", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrNamedBgBlueCode44()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Background: Color.Named(Color.AnsiColor.Blue)));
        Assert.Contains("44", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrIndexedFgContains38With5AndN()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Foreground: Color.FromIndex(77)));
        Assert.Contains("38;5;77", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrIndexedBgContains48With5AndN()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Background: Color.FromIndex(200)));
        Assert.Contains("48;5;200", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrTruecolorFgContains38With2AndRgb()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Foreground: Color.FromRgb(10, 20, 30)));
        Assert.Contains("38;2;10;20;30", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrTruecolorBgContains48With2AndRgb()
    {
        string sgr = SgrTranslator.ToOpenSgr(new Style(Background: Color.FromRgb(100, 200, 50)));
        Assert.Contains("48;2;100;200;50", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SgrBoldAndTruecolorFgBothPresentCorrectOrder()
    {
        // Bold first (format flags before colors), then truecolor.
        string sgr = SgrTranslator.ToOpenSgr(
            new Style(Foreground: Color.FromRgb(1, 2, 3), Format: Format.Bold));
        int boldPos = sgr.IndexOf('1', StringComparison.Ordinal);
        int colorPos = sgr.IndexOf("38;2;1;2;3", StringComparison.Ordinal);
        Assert.True(boldPos >= 0 && colorPos > boldPos,
            $"Expected bold code before truecolor in: {sgr}");
    }

    // ── §8.1 / §8.5  Multi-frame cross-frame cursor accounting ──────────────

    // ESC[nA (cursor up n rows)
    private static string CursorUp(int n) => $"\x1b[{n}A";

    // ESC[0J (erase to end-of-screen — the region clear)
    private const string _eraseEos = "\x1b[0J";

    [Fact]
    public void FirstFramePaintsAtCurrentPositionNoMoveUp()
    {
        // The very first frame must NOT emit any cursor-up sequence — it paints
        // at whatever position the cursor is currently at.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("hello")];

        renderer.Paint(model);
        string output = writer.ToString();

        // No ESC[nA before the content (no move-up on first frame).
        // The only cursor-up sequences allowed are those from caret parking, which
        // requires CaretPosition set; here it is null.
        Assert.DoesNotContain("\x1b[1A", output, StringComparison.Ordinal);
        Assert.Contains("hello", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondFrameWithNoCaretMovesUpByHeightMinusOne()
    {
        // No caret set → cursor rests on the last row of the region (row 2 of a 3-row region).
        // Frame 2 must emit ESC[2A (lastRestingRow = 2) to reach the anchor.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("r0"), PlainLine("r1"), PlainLine("r2")];
        // CaretPosition is null and IsCursorVisible defaults to true but no position
        // is set, so ParkCursor leaves the cursor on the last row (row 2).

        renderer.Paint(model); // frame 1 — no move-up, _lastRestingRow = 2
        writer.GetStringBuilder().Clear();

        model.LiveWindowRows = [PlainLine("A"), PlainLine("B"), PlainLine("C")];
        renderer.Paint(model); // frame 2

        string output = writer.ToString();
        // Move up 2 rows (lastRestingRow = 2 → 2 steps up to reach top).
        Assert.Contains(CursorUp(2), output, StringComparison.Ordinal);
        // Erase to EOS after move-up.
        Assert.Contains(_eraseEos, output, StringComparison.Ordinal);
        // New content present.
        Assert.Contains("A", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondFrameWithCaretAtRow0NoMoveUp()
    {
        // Visible caret on row 0 of a 3-row region: after frame 1, the cursor rests at
        // row 0 (_lastRestingRow = 0). Frame 2 move-up must be 0 — no ESC[nA emitted
        // before ESC[0J. Emitting ESC[1A or ESC[2A would overshoot into frozen scrollback.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("r0"), PlainLine("r1"), PlainLine("r2")];
        model.CaretPosition = (0, 0);
        model.IsCursorVisible = true;

        renderer.Paint(model); // frame 1: cursor parked at row 0
        writer.GetStringBuilder().Clear();

        model.LiveWindowRows = [PlainLine("A"), PlainLine("B"), PlainLine("C")];
        model.NewlyCommittedRows = [];
        renderer.Paint(model); // frame 2: _lastRestingRow = 0 → stepsUp = 0

        string output = writer.ToString();
        // No cursor-up of any magnitude before ESC[0J.
        int eraseIdx = output.IndexOf(_eraseEos, StringComparison.Ordinal);
        Assert.True(eraseIdx >= 0, "ESC[0J must be present");
        string beforeErase = output[..eraseIdx];
        Assert.DoesNotContain("\x1b[1A", beforeErase, StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b[2A", beforeErase, StringComparison.Ordinal);
        // Content still repainted.
        Assert.Contains("A", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondFrameWithCaretAtMidRowMovesUpByCaretRow()
    {
        // Visible caret on row 1 of a 3-row region: _lastRestingRow = 1.
        // Frame 2 must emit ESC[1A (not ESC[2A) before ESC[0J.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("r0"), PlainLine("r1"), PlainLine("r2")];
        model.CaretPosition = (1, 0);
        model.IsCursorVisible = true;

        renderer.Paint(model); // frame 1: cursor parked at row 1
        writer.GetStringBuilder().Clear();

        model.LiveWindowRows = [PlainLine("A"), PlainLine("B"), PlainLine("C")];
        model.NewlyCommittedRows = [];
        renderer.Paint(model); // frame 2: _lastRestingRow = 1 → stepsUp = 1

        string output = writer.ToString();
        int eraseIdx = output.IndexOf(_eraseEos, StringComparison.Ordinal);
        Assert.True(eraseIdx >= 0, "ESC[0J must be present");
        string beforeErase = output[..eraseIdx];
        // Exactly ESC[1A, not ESC[2A.
        Assert.Contains("\x1b[1A", beforeErase, StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b[2A", beforeErase, StringComparison.Ordinal);
        Assert.Contains("A", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondFrameWithHiddenCursorMovesUpByHeightMinusOne()
    {
        // Hidden cursor: ParkCursor does not reposition, so cursor rests on the last row.
        // _lastRestingRow = totalRows - 1 = 2. Frame 2 must emit ESC[2A.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("r0"), PlainLine("r1"), PlainLine("r2")];
        model.IsCursorVisible = false;

        renderer.Paint(model); // frame 1: cursor on last row (hidden)
        writer.GetStringBuilder().Clear();

        model.LiveWindowRows = [PlainLine("A"), PlainLine("B"), PlainLine("C")];
        model.NewlyCommittedRows = [];
        renderer.Paint(model); // frame 2

        string output = writer.ToString();
        int eraseIdx = output.IndexOf(_eraseEos, StringComparison.Ordinal);
        Assert.True(eraseIdx >= 0, "ESC[0J must be present");
        string beforeErase = output[..eraseIdx];
        Assert.Contains("\x1b[2A", beforeErase, StringComparison.Ordinal);
        Assert.Contains("A", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondFrameContainsEraseEosAndNewContent()
    {
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("old-row0"), PlainLine("old-row1")];
        renderer.Paint(model);
        writer.GetStringBuilder().Clear();

        model.LiveWindowRows = [PlainLine("new-row0"), PlainLine("new-row1")];
        renderer.Paint(model);

        string output = writer.ToString();
        // Clear sequence present.
        Assert.Contains(_eraseEos, output, StringComparison.Ordinal);
        // New content present.
        Assert.Contains("new-row0", output, StringComparison.Ordinal);
        Assert.Contains("new-row1", output, StringComparison.Ordinal);
        // Old content not in this frame's output.
        Assert.DoesNotContain("old-row", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitRowsEmittedAboveRegionWithTrailingNewline()
    {
        // When NewlyCommittedRows is non-empty the committed rows must be emitted
        // (as frozen native-scrollback lines) before the ESC[0J clear.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("live0"), PlainLine("live1")];
        renderer.Paint(model); // frame 1
        writer.GetStringBuilder().Clear();

        // Frame 2: commit one row.
        model.NewlyCommittedRows = [PlainLine("committed-row")];
        model.LiveWindowRows = [PlainLine("live1-updated")];
        renderer.Paint(model);

        string output = writer.ToString();

        // Committed row text must appear.
        Assert.Contains("committed-row", output, StringComparison.Ordinal);

        // committed-row must appear BEFORE the ESC[0J clear.
        int commitIdx = output.IndexOf("committed-row", StringComparison.Ordinal);
        int clearIdx = output.IndexOf(_eraseEos, StringComparison.Ordinal);
        Assert.True(commitIdx < clearIdx,
            "committed row must be emitted before the region clear (ESC[0J)");

        // The live content follows after the clear.
        int liveIdx = output.IndexOf("live1-updated", StringComparison.Ordinal);
        Assert.True(liveIdx > clearIdx,
            "live content must follow the region clear");
    }

    [Fact]
    public void FrozenRowNeverRewrittenInSubsequentFrames()
    {
        // After a row is committed in frame 2, frames 3+ must not emit its text again.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("live0"), PlainLine("live1")];
        renderer.Paint(model); // frame 1

        // Frame 2: commit "live0".
        model.NewlyCommittedRows = [PlainLine("live0")];
        model.LiveWindowRows = [PlainLine("live1")];
        renderer.Paint(model);

        // Frame 3: committed list cleared; model only has live1.
        model.NewlyCommittedRows = [];
        model.LiveWindowRows = [PlainLine("live1"), PlainLine("live2")];
        writer.GetStringBuilder().Clear(); // only capture frame-3 output

        renderer.Paint(model);
        string output = writer.ToString();

        // "live0" must not appear in frame 3's output — it is frozen.
        Assert.DoesNotContain("live0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnchorMovesDownByCommittedCount()
    {
        // Frame 1 paints 3 rows → cursor rests 2 rows below anchor.
        // Frame 2 commits 1 row → it emits ESC[nA (move to anchor), prints committed
        // row + \n (pushing anchor down by 1), then ESC[0J, then 2 live rows.
        // Frame 3: previous painted height = 2 (frame 2 painted 2 live rows),
        // move-up = 2-1 = 1. Verify frame 3 emits ESC[1A (not ESC[2A).
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("r0"), PlainLine("r1"), PlainLine("r2")];
        renderer.Paint(model); // frame 1 — _lastPaintedHeight = 3

        model.NewlyCommittedRows = [PlainLine("frozen0")];
        model.LiveWindowRows = [PlainLine("r1"), PlainLine("r2")];
        renderer.Paint(model); // frame 2 — _lastPaintedHeight = 2
        writer.GetStringBuilder().Clear();

        model.NewlyCommittedRows = [];
        model.LiveWindowRows = [PlainLine("r1-v2"), PlainLine("r2-v2")];
        renderer.Paint(model); // frame 3

        string output = writer.ToString();
        // Frame 3: previous region had 2 rows → move up 1.
        Assert.Contains(CursorUp(1), output, StringComparison.Ordinal);
        // Must NOT move up 2 (that would intrude into frozen area).
        Assert.DoesNotContain(CursorUp(2), output, StringComparison.Ordinal);
    }

    [Fact]
    public void RegionGrowsCorrectlyMoveUpReflectsNewHeight()
    {
        // Frame 1: 1 row.  Frame 2: 3 rows.  Frame 3: verify move-up = 2 (3-1).
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("only")];
        renderer.Paint(model); // frame 1 — _lastPaintedHeight = 1

        model.LiveWindowRows = [PlainLine("r0"), PlainLine("r1"), PlainLine("r2")];
        renderer.Paint(model); // frame 2 — _lastPaintedHeight = 3
        writer.GetStringBuilder().Clear();

        model.LiveWindowRows = [PlainLine("x0"), PlainLine("x1"), PlainLine("x2")];
        renderer.Paint(model); // frame 3

        string output = writer.ToString();
        // Frame 3 should move up 2 (3 rows in frame 2 → 3-1 = 2 steps up).
        Assert.Contains(CursorUp(2), output, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleRowRegionSecondFrameNoMoveUp()
    {
        // When _lastPaintedHeight == 1 the cursor is already at the anchor row.
        // ESC[0A is never emitted; the move-up is skipped (stepsUp == 0).
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("one")];
        renderer.Paint(model); // frame 1
        writer.GetStringBuilder().Clear();

        model.LiveWindowRows = [PlainLine("two")];
        renderer.Paint(model); // frame 2

        string output = writer.ToString();
        // No cursor-up sequence — stepsUp = 1 - 1 = 0.
        Assert.DoesNotContain("\x1b[0A", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b[1A", output, StringComparison.Ordinal);
        // But ESC[0J clear must still be present.
        Assert.Contains(_eraseEos, output, StringComparison.Ordinal);
        Assert.Contains("two", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleCommittedRowsEmittedInOrder()
    {
        // Two rows committed in one frame must both appear before ESC[0J, in order.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [PlainLine("a"), PlainLine("b"), PlainLine("c")];
        renderer.Paint(model);
        writer.GetStringBuilder().Clear();

        model.NewlyCommittedRows = [PlainLine("first-commit"), PlainLine("second-commit")];
        model.LiveWindowRows = [PlainLine("c")];
        renderer.Paint(model);

        string output = writer.ToString();
        int firstIdx = output.IndexOf("first-commit", StringComparison.Ordinal);
        int secondIdx = output.IndexOf("second-commit", StringComparison.Ordinal);
        int clearIdx = output.IndexOf(_eraseEos, StringComparison.Ordinal);

        Assert.True(firstIdx >= 0, "first-commit not found");
        Assert.True(secondIdx > firstIdx, "second-commit must follow first-commit");
        Assert.True(clearIdx > secondIdx, "ESC[0J must follow committed rows");
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    // ── Test double ──────────────────────────────────────────────────────────

    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }
}
