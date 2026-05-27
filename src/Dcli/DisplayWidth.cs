using System.Globalization;
using System.Text;

namespace Dcli;

/// <summary>
/// Per-Unicode-scalar display-width measurement (wcwidth semantics).
/// <para>
/// Rules applied, in priority order:
/// <list type="number">
///   <item>Control characters (U+0000–U+001F, U+007F) → 0 (not stripped; just not counted as columns).</item>
///   <item>Zero-width characters (ZWSP U+200B, ZWNJ U+200C, ZWJ U+200D, BOM U+FEFF, SHY U+00AD,
///     and scalars whose <see cref="UnicodeCategory"/> is <see cref="UnicodeCategory.NonSpacingMark"/>,
///     <see cref="UnicodeCategory.EnclosingMark"/>, or <see cref="UnicodeCategory.Format"/>) → 0.</item>
///   <item>East-Asian Wide / Fullwidth scalars (see <see cref="IsWide"/>) → 2.</item>
///   <item>Tab (U+0009) → advances to the next multiple of <see cref="TabWidth"/> from column 0.
///     <see cref="Measure(string)"/> assumes a start column of 0; use
///     <see cref="MeasureWithColumn"/> when tabs appear mid-line.</item>
///   <item>Everything else → 1.</item>
/// </list>
/// </para>
/// <para>
/// This type is internal: it is a rendering primitive, not a committed public API surface.
/// Callers within the assembly (and the test project via InternalsVisibleTo) may use it freely.
/// </para>
/// </summary>
internal static class DisplayWidth
{
    /// <summary>Number of columns a tab advances to (next multiple of this value).</summary>
    internal const int TabWidth = 8;

    /// <summary>
    /// Returns the display width (in terminal columns) of a single Unicode scalar value.
    /// Tab is measured as if it starts at column 0 (width = <see cref="TabWidth"/>).
    /// For tab handling relative to a known column, use <see cref="MeasureWithColumn"/>.
    /// </summary>
    internal static int Measure(Rune rune)
    {
        int value = rune.Value;

        // Control characters
        if (value < 0x0020 || value == 0x007F)
        {
            if (value == 0x0009) // TAB
            {
                // Start-column-0 assumption: expands to full TabWidth.
                return TabWidth;
            }
            return 0;
        }

        // Explicitly zero-width scalars
        if (value is 0x00AD or 0x200B or 0x200C or 0x200D or 0xFEFF)
            return 0;

        // Combining marks and format category characters
        UnicodeCategory cat = Rune.GetUnicodeCategory(rune);
        if (cat is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format)
            return 0;

        // East-Asian Wide / Fullwidth
        if (IsWide(value))
            return 2;

        return 1;
    }

    /// <summary>
    /// Returns the display width of <paramref name="rune"/> given that it starts at
    /// <paramref name="column"/>. Only differs from <see cref="Measure(Rune)"/> for tab characters.
    /// </summary>
    internal static int MeasureWithColumn(Rune rune, int column)
    {
        if (rune.Value == 0x0009) // TAB
        {
            int next = (column / TabWidth + 1) * TabWidth;
            return next - column;
        }
        return Measure(rune);
    }

    /// <summary>
    /// Returns the total display width of <paramref name="text"/> in terminal columns.
    /// Tab characters are expanded assuming a start column of 0; for accurate tab handling
    /// mid-line use <see cref="MeasureWithColumn"/> per scalar.
    /// </summary>
    internal static int Measure(string text)
    {
        int width = 0;
        int col = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int w = MeasureWithColumn(rune, col);
            width += w;
            col += w;
        }
        return width;
    }

    // -----------------------------------------------------------------------------------------
    // East-Asian Width table
    //
    // Source: Unicode Standard Annex #11 "East Asian Width"
    // (https://www.unicode.org/reports/tr11/) and Markus Kuhn's wcwidth reference implementation.
    // Covers Unicode 15.1 Wide (W) and Fullwidth (F) ranges.  Narrow (Na), Ambiguous (A),
    // Halfwidth (H), and Neutral (N) all map to width 1 here — the correct choice for modern
    // Unicode-aware terminals that treat ambiguous as narrow.
    //
    // Ranges are stored as (inclusive_low, inclusive_high) pairs in a flat sorted int array.
    // -----------------------------------------------------------------------------------------

    private static ReadOnlySpan<int> WideRanges =>
    [
        // Fullwidth Latin / punctuation / etc. (F)
        0x1100, 0x115F,   // Hangul Jamo
        0x2329, 0x232A,   // Angle brackets (legacy)
        0x2E80, 0x303E,   // CJK Radicals, Kangxi, CJK Symbols & Punctuation
        0x3041, 0x33BF,   // Hiragana, Katakana, Bopomofo, Hangul Compatibility Jamo, Kanbun, Bopomofo Extended, Katakana Phonetic Extensions, Enclosed CJK, CJK Compatibility
        0x33FF, 0x33FF,   // CJK Compatibility
        0x3400, 0x4DBF,   // CJK Unified Ideographs Extension A
        0x4E00, 0x9FFF,   // CJK Unified Ideographs
        0xA000, 0xA4CF,   // Yi
        0xA960, 0xA97F,   // Hangul Jamo Extended-A
        0xAC00, 0xD7AF,   // Hangul Syllables
        0xF900, 0xFAFF,   // CJK Compatibility Ideographs
        0xFE10, 0xFE19,   // Vertical forms
        0xFE30, 0xFE4F,   // CJK Compatibility Forms, Small Form Variants
        0xFF01, 0xFF60,   // Fullwidth Forms
        0xFFE0, 0xFFE6,   // Fullwidth Signs
        // Supplementary Multilingual Plane
        0x1B000, 0x1B12F, // Kana Supplement, Extended-A
        0x1B130, 0x1B16F, // Kana Extended-B  (Unicode 15)
        0x1B170, 0x1B2FF, // Nushu
        0x1F004, 0x1F004, // Mahjong tile (wide emoji)
        0x1F0CF, 0x1F0CF, // Playing card black joker
        0x1F100, 0x1F10A, // Enclosed Alphanumeric Supplement (wide)
        0x1F110, 0x1F12D, // Enclosed Alphanumeric Supplement
        0x1F130, 0x1F169, // Enclosed Alphanumeric Supplement
        0x1F170, 0x1F1AC, // Enclosed Alphanumeric Supplement
        0x1F1E0, 0x1F1FF, // Regional Indicator Symbols (treated as wide for width = 2 per scalar)
        0x1F200, 0x1F2FF, // Enclosed Ideographic Supplement
        0x1F300, 0x1F64F, // Misc Symbols and Pictographs, Emoticons
        0x1F680, 0x1F6FF, // Transport and Map Symbols
        0x1F700, 0x1F77F, // Alchemical Symbols (some wide)
        0x1F780, 0x1F7FF, // Geometric Shapes Extended
        0x1F800, 0x1F8FF, // Supplemental Arrows-C
        0x1F900, 0x1F9FF, // Supplemental Symbols and Pictographs
        0x1FA00, 0x1FA6F, // Chess Symbols
        0x1FA70, 0x1FAFF, // Symbols and Pictographs Extended-A
        0x20000, 0x2FFFD, // CJK Unified Ideographs Extension B–F, CJK Compatibility Ideographs Supplement
        0x30000, 0x3FFFD, // CJK Unified Ideographs Extension G–H (Unicode 13+)
    ];

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="value"/> is an East-Asian Wide or
    /// Fullwidth codepoint that occupies two terminal columns.
    /// </summary>
    internal static bool IsWide(int value)
    {
        ReadOnlySpan<int> ranges = WideRanges;
        // Binary search over pair-indexed ranges.
        int lo = 0;
        int hi = ranges.Length / 2 - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int low = ranges[mid * 2];
            int high = ranges[mid * 2 + 1];
            if (value < low)
                hi = mid - 1;
            else if (value > high)
                lo = mid + 1;
            else
                return true;
        }
        return false;
    }
}
