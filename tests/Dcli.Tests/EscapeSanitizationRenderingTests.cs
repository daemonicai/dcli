using Dcli.Internal;
using Dcli.Internal.RenderLoop;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// §4 (vt-escape-sanitization) — end-to-end rendering safety tests.
/// These tests assert the byte stream emitted by <see cref="VtFrameRenderer"/> to a
/// <see cref="StringWriter"/>, mirroring the harness from <see cref="VtFrameRendererTests"/>.
/// They prove the security guarantee at the emit level: consumer-supplied ESC bytes must
/// not reach the writer except when carried by <see cref="Segment.Raw"/>.
/// </summary>
public sealed class EscapeSanitizationRenderingTests
{
    // ── Known renderer-owned VT sequences ────────────────────────────────────
    // Use \u001B (4-digit Unicode escape) for ESC so C# does not greedily parse
    // the following hex digit into the escape (e.g. \x1bB would become U+01BB).
    private const string _syncStart = "\u001B[?2026h";
    private const string _syncEnd = "\u001B[?2026l";
    private const string _eraseEol = "\u001B[K";
    private const string _eraseToEos = "\u001B[0J";
    private const string _cursorHide = "\u001B[?25l";
    private const string _cursorShow = "\u001B[?25h";
    private const char _esc = '\u001B';

    // ── Helpers (mirror VtFrameRendererTests setup exactly) ──────────────────

    private static (VtFrameRenderer renderer, StringWriter writer, RenderModel model) MakeRenderer(
        int cols = 80, int rows = 24)
    {
        StringWriter w = new();
        VtFrameRenderer r = new(w);
        RenderModel m = new(new FixedSizeSource(cols, rows));
        return (r, w, m);
    }

    private static Line L(params Segment[] segments) => new(segments);

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="needle"/> in <paramref name="haystack"/>.
    /// </summary>
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

    /// <summary>
    /// Returns the set of ESC-led substrings present in <paramref name="output"/> that are NOT
    /// among the known renderer-owned sequences. Used to detect unexpected consumer ESC leakage.
    /// </summary>
    private static IEnumerable<string> UnknownEscSequences(string output)
    {
        // Renderer-owned ESC sequences (exact strings the renderer may emit).
        HashSet<string> knownPrefixes =
        [
            _syncStart,           // ESC[?2026h
            _syncEnd,             // ESC[?2026l
            _eraseEol,            // ESC[K
            _eraseToEos,          // ESC[0J
            _cursorHide,          // ESC[?25l
            _cursorShow,          // ESC[?25h
            "[0m",          // SGR reset
            "[1m",          // SGR bold
            "[4m",          // SGR underline
            "[",            // generic CSI prefix — covers cursor moves ESC[nA, ESC[nG etc.
            "O",            // SS3 (cursor key, not emitted by renderer but covers it)
        ];

        int i = 0;
        while (i < output.Length)
        {
            if (output[i] != _esc)
            {
                i++;
                continue;
            }

            // Found an ESC: extract the sequence up to a plausible terminator or 16 chars.
            int end = Math.Min(i + 16, output.Length);
            string candidate = output.Substring(i, end - i);

            bool isKnown = knownPrefixes.Any(k => candidate.StartsWith(k, StringComparison.Ordinal));
            if (!isKnown)
                yield return candidate;

            i++;
        }
    }

    // ── §4.1 — Consumer ESC/CSI/OSC/newline does not reach the emitted bytes ──

    [Fact]
    public void ConsumerCsiInContentRowIsAbsentFromEmittedBytes()
    {
        // Consumer text with an embedded CSI erase-display sequence.
        // Use string concatenation to avoid greedy \x hex parse (ESC = U+001B).
        string consumerText = "before" + "" + "[2Jafter";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(new Segment(consumerText))];
        renderer.Paint(model);

        string output = writer.ToString();

        // The de-ESC'd literal "[2J" must appear as plain text (strip mode removes ESC).
        // Under strip mode: ESC is removed, "[2J" remains as literal characters.
        Assert.Contains("[2J", output, StringComparison.Ordinal);

        // The consumer's ESC must not be followed by "[2J" in the emitted bytes —
        // i.e. the renderer must not have emitted the consumer's CSI verbatim.
        Assert.DoesNotContain("[2J", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsumerOscInContentRowIsAbsentFromEmittedBytes()
    {
        // Consumer text with an embedded OSC set-title sequence.
        // BEL = U+0007; OSC = ESC + ']'.
        string consumerText = "label" + "" + "]0;title" + "" + "end";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(new Segment(consumerText))];
        renderer.Paint(model);

        string output = writer.ToString();

        // Under strip mode: ESC is removed, the rest is literal.
        Assert.DoesNotContain("]0;title", output, StringComparison.Ordinal);
        // The text around the control is still present.
        Assert.Contains("label", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsumerNewlineInContentRowNormalizedToSpace()
    {
        // Newlines in consumer text are normalized to space; the raw LF must not appear
        // as a structural newline in the content payload (only the renderer's own \n is allowed).
        const string consumerText = "line1\nline2";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(new Segment(consumerText))];
        renderer.Paint(model);

        string output = writer.ToString();

        // The segment text has the newline normalized to a space.
        // "line1 line2" (with a space) must be present, not a raw LF in the content area.
        Assert.Contains("line1 line2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EmittedBytesContainNoUnknownEscSequencesWithConsumerControlText()
    {
        // Build content from text containing CSI, OSC, and a standalone ESC.
        // Build via concatenation to avoid greedy \x parsing.
        string esc = "";
        string bel = "";
        string consumerText = "A" + esc + "[2J" + "B" + esc + "]0;evil" + bel + "C" + esc + "D";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(new Segment(consumerText))];
        renderer.Paint(model);

        string output = writer.ToString();
        List<string> unknownEscs = UnknownEscSequences(output).ToList();

        Assert.True(
            unknownEscs.Count == 0,
            $"Unexpected ESC-led sequences in emitted bytes: {string.Join(", ", unknownEscs.Select(s => s.Replace("", "<ESC>", StringComparison.Ordinal)))}");
    }

    // ── §4.2 — Sync-fence defeat test (the headline security test) ────────────

    [Fact]
    public void ConsumerSyncFenceCloseInContentDoesNotAddExtraFenceClose()
    {
        // Consumer tries to inject a premature sync-fence close.
        // After sanitization (strip mode) the ESC is removed, leaving literal "[?2026l"
        // as plain text. The emitted stream must contain exactly one _syncEnd (the renderer's own)
        // and exactly one _syncStart.
        string attackText = "safe" + "" + "[?2026lunsafe";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(new Segment(attackText))];
        renderer.Paint(model);

        string output = writer.ToString();

        int syncStartCount = CountOccurrences(output, _syncStart);
        int syncEndCount = CountOccurrences(output, _syncEnd);

        Assert.Equal(1, syncStartCount);
        Assert.Equal(1, syncEndCount);
    }

    [Fact]
    public void RawSegmentWithSyncFenceCloseProducesTwoFenceCloses()
    {
        // Segment.Raw is the legitimate bypass — Raw("...") carries bytes verbatim.
        // With a Raw segment containing _syncEnd, the count becomes 2:
        // one from the consumer's Raw and one from the renderer's own fence close.
        // This test proves the previous test is actually sensitive to the bypass.
        string rawPayload = _syncEnd;  // "[?2026l"
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(Segment.Raw(rawPayload))];
        renderer.Paint(model);

        string output = writer.ToString();

        int syncEndCount = CountOccurrences(output, _syncEnd);
        Assert.Equal(2, syncEndCount);  // one from Raw, one from renderer's own close
    }

    [Fact]
    public void ConsumerSyncFenceOpenInContentDoesNotAddExtraFenceOpen()
    {
        // Symmetric test: consumer tries to inject an extra fence open.
        string attackText = "x" + "" + "[?2026hy";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(new Segment(attackText))];
        renderer.Paint(model);

        string output = writer.ToString();

        int syncStartCount = CountOccurrences(output, _syncStart);
        Assert.Equal(1, syncStartCount);
    }

    [Fact]
    public void RawSegmentWithSyncFenceOpenProducesTwoFenceOpens()
    {
        // Symmetric Raw bypass proof for the open fence.
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(Segment.Raw(_syncStart))];
        renderer.Paint(model);

        string output = writer.ToString();

        int syncStartCount = CountOccurrences(output, _syncStart);
        Assert.Equal(2, syncStartCount);
    }

    // ── §4.3 — Raw passthrough and width consistency ──────────────────────────

    [Fact]
    public void RawSegmentReachesEmittedBytesByteForByte()
    {
        // Segment.Raw("...") must appear verbatim in the emitted stream.
        string rawSgr = "[31mred[0m";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(Segment.Raw(rawSgr))];
        renderer.Paint(model);

        string output = writer.ToString();
        Assert.Contains(rawSgr, output, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizedSegmentEscBytesAbsentFromEmittedBytes()
    {
        // Contrast: new Segment("...") strips the ESC — the emitted stream must NOT
        // contain the ESC bytes that were in the consumer input.
        string consumerInput = "[31mred[0m";
        var (renderer, writer, model) = MakeRenderer();
        model.LiveWindowRows = [L(new Segment(consumerInput))];
        renderer.Paint(model);

        string output = writer.ToString();

        // "[31mred[0m" (without ESC) must be present as plain text.
        Assert.Contains("[31mred[0m", output, StringComparison.Ordinal);

        // But "[31m" as a real SGR sequence must NOT be present
        // (only the renderer's own SGR for any Style-driven formatting would be).
        // Since we used default(Style), no renderer SGR is emitted either.
        Assert.DoesNotContain("[31m", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WidthConsistencyStripModeStoredTextMatchesDisplayWidth()
    {
        // In strip mode, Class-A bytes are removed. The stored segment text and its
        // DisplayWidth must be consistent (no phantom zero-width control bytes).
        string input = "hello" + "" + "[31mworld";
        string sanitized = TextSanitizer.Apply(input, SanitizeMode.Strip);
        Segment seg = new(input);

        // Stored text must equal what TextSanitizer.Apply produces in strip mode.
        Assert.Equal(sanitized, seg.Text);
        // DisplayWidth of the stored text must match the measured width of sanitized string.
        Assert.Equal(DisplayWidth.Measure(sanitized), DisplayWidth.Measure(seg.Text));
        // No ESC in stored text.
        Assert.DoesNotContain(_esc, seg.Text);
    }

    [Fact]
    public void WidthConsistencyReplaceModeStoredTextMatchesDisplayWidth()
    {
        // In replace mode, Class-A bytes become visible glyphs. The stored segment text
        // from TextSanitizer.Apply(strip) differs from replace output, but replace-mode
        // sanitized text also has consistent width.
        // Build via concatenation to avoid greedy \x hex parsing (\x1bB would be U+01BB).
        string input = "A" + _esc + "B";  // "A" + ESC + "B"
        string sanitizedReplace = TextSanitizer.Apply(input, SanitizeMode.Replace);
        string sanitizedStrip = TextSanitizer.Apply(input, SanitizeMode.Strip);

        // Strip: ESC removed → "AB" → width 2.
        Assert.Equal(2, DisplayWidth.Measure(sanitizedStrip));

        // Replace: ESC → a visible glyph (U+241B SYMBOL FOR ESCAPE); the replacement string
        // is longer than strip (replace kept one extra character; strip discarded it).
        Assert.True(sanitizedReplace.Length > sanitizedStrip.Length,
            "Replace mode must keep one glyph per stripped control; string length must exceed strip result.");
        // Replace glyphs (U+2400+ Control Pictures) are width-1; display width must equal string length.
        Assert.Equal(sanitizedReplace.Length, DisplayWidth.Measure(sanitizedReplace));

        // Replace output contains no ESC and no raw control bytes.
        Assert.DoesNotContain('', sanitizedReplace);

        // Each sanitized output has no remaining Class-A bytes (fast path returns same instance on re-apply).
        Assert.Same(sanitizedReplace, TextSanitizer.Apply(sanitizedReplace, SanitizeMode.Replace));
        Assert.Same(sanitizedStrip, TextSanitizer.Apply(sanitizedStrip, SanitizeMode.Strip));
    }

    [Fact]
    public void WidthConsistencySampleWithMultipleControlsBothModes()
    {
        // Sample with several control bytes including CSI fragments.
        // Build via concatenation to avoid greedy \x parsing.
        string input = "ok" + "" + "[2Jtext\nmore";
        string strip = TextSanitizer.Apply(input, SanitizeMode.Strip);
        string replace = TextSanitizer.Apply(input, SanitizeMode.Replace);

        // Strip: ESC removed; "[2J" and the newline (→ space) remain as literal chars.
        // "ok" + "[2J" + "text" + " " + "more" = 15 chars, all width-1 ASCII.
        Assert.Equal(DisplayWidth.Measure(strip), strip.Length);

        // Replace: ESC is replaced by a glyph and newline (Class B) → space.
        // The replace output must be longer than strip output (one extra glyph for ESC).
        Assert.True(replace.Length > strip.Length,
            "Replace mode keeps a glyph per control; replace output must be longer than strip.");
        // Replace glyphs (U+2400+ Control Pictures) are width-1; display width must equal string length.
        Assert.Equal(replace.Length, DisplayWidth.Measure(replace));

        // Both results contain no ESC.
        Assert.DoesNotContain('', strip);
        Assert.DoesNotContain('', replace);
    }

    // ── Infrastructure ────────────────────────────────────────────────────────

    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }
}
