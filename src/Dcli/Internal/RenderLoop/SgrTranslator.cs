using System.Text;

namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Translates a <see cref="Style"/> to an ANSI SGR escape sequence string.
/// </summary>
/// <remarks>
/// This is a pure stateless helper — no I/O, no allocation beyond the returned string.
/// Callers: <see cref="VtFrameRenderer"/> on the loop thread; unit tests directly.
/// </remarks>
internal static class SgrTranslator
{
    // Named-color offset tables (base + bright).
    // Named colors 0–7 → 30–37 fg / 40–47 bg.
    // Named colors 8–15 → 90–97 fg / 100–107 bg.
    private const int _fgBase = 30;
    private const int _bgBase = 40;
    private const int _fgBrightBase = 90;
    private const int _bgBrightBase = 100;

    /// <summary>
    /// Returns an SGR reset sequence (<c>ESC[0m</c>).
    /// </summary>
    internal const string Reset = "\x1b[0m";

    /// <summary>
    /// Appends the opening SGR codes for <paramref name="style"/> to <paramref name="sb"/>.
    /// Writes nothing when the style is entirely default (no-op fast path).
    /// </summary>
    /// <param name="style">The style to translate.</param>
    /// <param name="sb">Destination builder.</param>
    /// <returns>
    /// <see langword="true"/> if any SGR codes were appended (caller should emit
    /// <see cref="Reset"/> after the styled run); <see langword="false"/> when the style is
    /// the default (no SGR emitted, no reset needed).
    /// </returns>
    internal static bool AppendOpenSgr(Style style, StringBuilder sb)
    {
        // Fast path: nothing to encode.
        if (style.Foreground is null && style.Background is null && style.Format == Format.None)
            return false;

        sb.Append("\x1b[");

        bool needSemi = false;

        // Format flags — emit in a defined order so sequences are deterministic.
        if (style.Format.HasFlag(Format.Bold)) { AppendCode(sb, 1, ref needSemi); }
        if (style.Format.HasFlag(Format.Dim)) { AppendCode(sb, 2, ref needSemi); }
        if (style.Format.HasFlag(Format.Italic)) { AppendCode(sb, 3, ref needSemi); }
        if (style.Format.HasFlag(Format.Underline)) { AppendCode(sb, 4, ref needSemi); }
        if (style.Format.HasFlag(Format.Reverse)) { AppendCode(sb, 7, ref needSemi); }
        if (style.Format.HasFlag(Format.Strikethrough)) { AppendCode(sb, 9, ref needSemi); }

        // Foreground color.
        if (style.Foreground.HasValue)
            AppendColorCode(sb, style.Foreground.Value, isForeground: true, ref needSemi);

        // Background color.
        if (style.Background.HasValue)
            AppendColorCode(sb, style.Background.Value, isForeground: false, ref needSemi);

        sb.Append('m');
        return true;
    }

    /// <summary>
    /// Returns the complete SGR opening sequence for <paramref name="style"/>, or an empty string
    /// when the style is the default. Convenience wrapper around <see cref="AppendOpenSgr"/>.
    /// </summary>
    internal static string ToOpenSgr(Style style)
    {
        StringBuilder sb = new();
        AppendOpenSgr(style, sb);
        return sb.ToString();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void AppendCode(StringBuilder sb, int code, ref bool needSemi)
    {
        if (needSemi) sb.Append(';');
        sb.Append(code);
        needSemi = true;
    }

    private static void AppendColorCode(StringBuilder sb, Color color, bool isForeground, ref bool needSemi)
    {
        switch (color.Kind)
        {
            case Color.ColorKind.Named:
                {
                    int idx = (int)color.NamedValue;
                    int code = idx < 8
                        ? (isForeground ? _fgBase : _bgBase) + idx
                        : (isForeground ? _fgBrightBase : _bgBrightBase) + (idx - 8);
                    AppendCode(sb, code, ref needSemi);
                    break;
                }
            case Color.ColorKind.Indexed:
                {
                    // 256-color: ESC[38;5;n m  or  ESC[48;5;n m
                    if (needSemi) sb.Append(';');
                    sb.Append(isForeground ? "38;5;" : "48;5;");
                    sb.Append(color.IndexValue);
                    needSemi = true;
                    break;
                }
            case Color.ColorKind.Rgb:
                {
                    // Truecolor: ESC[38;2;r;g;b m  or  ESC[48;2;r;g;b m
                    if (needSemi) sb.Append(';');
                    sb.Append(isForeground ? "38;2;" : "48;2;");
                    sb.Append(color.R); sb.Append(';');
                    sb.Append(color.G); sb.Append(';');
                    sb.Append(color.B);
                    needSemi = true;
                    break;
                }
        }
    }
}
