using System.Text;

namespace Dcli.Internal;

/// <summary>
/// Controls how Class-A bytes (control/escape characters) are handled.
/// Class-B bytes (whitespace controls) are always replaced with a single space
/// regardless of mode.
/// </summary>
internal enum SanitizeMode
{
    /// <summary>Remove Class-A bytes entirely.</summary>
    Strip,

    /// <summary>
    /// Replace Class-A bytes with a visible width-1 glyph:
    /// C0 controls map to Control Pictures (U+2400+c); DEL maps to U+2421;
    /// C1 bytes map to U+FFFD.
    /// </summary>
    Replace,
}

/// <summary>
/// Strips or replaces control and escape characters in consumer-supplied text
/// before it enters the render pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Class A — control/escape (mode-controlled):</b>
/// <c>0x00–0x08</c>, <c>0x0E–0x1F</c> (includes ESC <c>0x1B</c>),
/// <c>DEL 0x7F</c>, and C1 range <c>0x80–0x9F</c>.
/// In <see cref="SanitizeMode.Strip"/> mode (default) these bytes are removed.
/// In <see cref="SanitizeMode.Replace"/> mode they become visible glyphs:
/// a C0 control <c>c</c> → <c>U+2400+c</c> (Control Picture); DEL → U+2421; C1 → U+FFFD.
/// </para>
/// <para>
/// <b>Class B — whitespace controls (always → U+0020):</b>
/// TAB <c>0x09</c>, LF <c>0x0A</c>, VT <c>0x0B</c>, FF <c>0x0C</c>, CR <c>0x0D</c>.
/// The mode setting does not affect Class B.
/// </para>
/// <para>
/// <b>Fast path:</b> when the input contains no Class-A or Class-B bytes,
/// the same string instance is returned (zero allocation).
/// </para>
/// <para>
/// <b>Idempotence:</b> sanitized output contains no Class-A or Class-B bytes,
/// so a second call always hits the fast path and returns the same instance.
/// </para>
/// </remarks>
internal static class TextSanitizer
{
    /// <summary>
    /// The sanitize mode read once from the <c>DCLI_SANITIZE_MODE</c> environment variable
    /// (case-insensitive; <c>replace</c> selects replace mode; anything else → strip).
    /// </summary>
    internal static readonly SanitizeMode DefaultMode = ReadDefaultMode();

    private static SanitizeMode ReadDefaultMode()
    {
        string? value = Environment.GetEnvironmentVariable("DCLI_SANITIZE_MODE");
        if (string.IsNullOrEmpty(value))
            return SanitizeMode.Strip;

        return value.Equals("replace", StringComparison.OrdinalIgnoreCase)
            ? SanitizeMode.Replace
            : SanitizeMode.Strip;
    }

    /// <summary>
    /// Sanitizes <paramref name="text"/> using <see cref="DefaultMode"/>.
    /// Returns the same instance when no Class-A or Class-B bytes are present.
    /// </summary>
    /// <param name="text">The text to sanitize.</param>
    /// <returns>Sanitized text, or the original instance if no changes were needed.</returns>
    internal static string Apply(string text) => Apply(text, DefaultMode);

    /// <summary>
    /// Sanitizes <paramref name="text"/> using an explicit <paramref name="mode"/>.
    /// Use this overload in tests to avoid mutating the process environment.
    /// Returns the same instance when no Class-A or Class-B bytes are present.
    /// </summary>
    /// <param name="text">The text to sanitize.</param>
    /// <param name="mode">The sanitize mode to apply to Class-A bytes.</param>
    /// <returns>Sanitized text, or the original instance if no changes were needed.</returns>
    internal static string Apply(string text, SanitizeMode mode)
    {
        // Fast path: scan for the first byte that needs action.
        int firstDirty = FindFirstDirty(text);
        if (firstDirty < 0)
            return text;

        // Rebuild from scratch, copying clean chars and neutralizing dirty ones.
        // Capacity estimate: same length (strip can only shrink; replace is 1-for-1).
        StringBuilder sb = new(text.Length);

        // Copy the clean prefix verbatim.
        sb.Append(text, 0, firstDirty);

        // Process the rest character-by-character.
        // Surrogate pairs (emoji, supplementary planes) are all ≥ U+10000 and are
        // represented as two UTF-16 code units (both > 0x9F), so they are never
        // Class-A or Class-B and both halves are copied intact.
        for (int i = firstDirty; i < text.Length; i++)
        {
            char c = text[i];

            if (IsClassB(c))
            {
                sb.Append(' ');
            }
            else if (IsClassA(c))
            {
                if (mode == SanitizeMode.Replace)
                    sb.Append(ClassAGlyph(c));
                // strip: emit nothing
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Returns the index of the first Class-A or Class-B char, or -1.</summary>
    private static int FindFirstDirty(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (IsClassA(c) || IsClassB(c))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Class A: C0 controls excluding whitespace controls (0x00–0x08, 0x0E–0x1F),
    /// DEL (0x7F), and C1 controls (0x80–0x9F).
    /// </summary>
    private static bool IsClassA(char c) =>
        (c <= 0x08) ||
        (c >= 0x0E && c <= 0x1F) ||
        (c == 0x7F) ||
        (c >= 0x80 && c <= 0x9F);

    /// <summary>
    /// Class B: TAB (0x09), LF (0x0A), VT (0x0B), FF (0x0C), CR (0x0D).
    /// Always replaced with a single space.
    /// </summary>
    private static bool IsClassB(char c) => c >= 0x09 && c <= 0x0D;

    /// <summary>
    /// Maps a Class-A character to its replace-mode glyph.
    /// Reachable C0 set: <c>0x00–0x08</c> and <c>0x0E–0x1F</c> (the whitespace
    /// controls <c>0x09–0x0D</c> are Class B and never reach this method).
    /// C0 → Control Picture U+2400+c; DEL (0x7F) → U+2421; C1 (0x80–0x9F) → U+FFFD.
    /// </summary>
    private static char ClassAGlyph(char c)
    {
        if (c <= 0x1F)
            return (char)(0x2400 + c);   // Control Pictures block
        if (c == 0x7F)
            return '␡';             // ␡ DELETE symbol
        return '�';                 // C1: replacement character
    }
}
