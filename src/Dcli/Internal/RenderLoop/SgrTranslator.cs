using System.Text;

namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Translates a <see cref="Style"/> to an ANSI SGR escape sequence string.
/// </summary>
/// <remarks>
/// Callers: <see cref="VtFrameRenderer"/> on the loop thread; unit tests directly.
/// When <see cref="TerminalCapabilities.HasTruecolor"/> is <see langword="false"/>, RGB colors
/// are downgraded to the nearest xterm 256-indexed palette entry.
/// </remarks>
internal sealed class SgrTranslator
{
    // Named-color offset tables (base + bright).
    // Named colors 0–7 → 30–37 fg / 40–47 bg.
    // Named colors 8–15 → 90–97 fg / 100–107 bg.
    private const int _fgBase = 30;
    private const int _bgBase = 40;
    private const int _fgBrightBase = 90;
    private const int _bgBrightBase = 100;

    // xterm 256-color cube: indices 16–231 are a 6×6×6 RGB cube.
    // Cube-level RGB values at each of the 6 steps:
    private static readonly int[] _cubeSteps = [0, 95, 135, 175, 215, 255];

    private readonly TerminalCapabilities _capabilities;

    /// <summary>Returns an SGR reset sequence (<c>ESC[0m</c>).</summary>
    internal const string Reset = "\x1b[0m";

    /// <summary>
    /// Initialises the translator with the given capabilities.
    /// </summary>
    /// <param name="capabilities">
    /// Capabilities detected from the environment. Pass <see cref="TerminalCapabilities.Default"/>
    /// in headless tests.
    /// </param>
    internal SgrTranslator(TerminalCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

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
    internal bool AppendOpenSgr(Style style, StringBuilder sb)
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
    internal string ToOpenSgr(Style style)
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

    private void AppendColorCode(StringBuilder sb, Color color, bool isForeground, ref bool needSemi)
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
                    if (_capabilities.HasTruecolor)
                    {
                        // Truecolor: ESC[38;2;r;g;b m  or  ESC[48;2;r;g;b m
                        if (needSemi) sb.Append(';');
                        sb.Append(isForeground ? "38;2;" : "48;2;");
                        sb.Append(color.R); sb.Append(';');
                        sb.Append(color.G); sb.Append(';');
                        sb.Append(color.B);
                        needSemi = true;
                    }
                    else
                    {
                        // Downgrade to nearest xterm 256-indexed color.
                        int idx = NearestXterm256(color.R, color.G, color.B);
                        if (needSemi) sb.Append(';');
                        sb.Append(isForeground ? "38;5;" : "48;5;");
                        sb.Append(idx);
                        needSemi = true;
                    }
                    break;
                }
        }
    }

    /// <summary>
    /// Maps an RGB triple to the nearest xterm 256-palette index (16–255).
    /// Compares the best match from the 6×6×6 cube (indices 16–231) against
    /// the 24-level greyscale ramp (indices 232–255) and returns the closer one.
    /// </summary>
    internal static int NearestXterm256(byte r, byte g, byte b)
    {
        // ── 6×6×6 cube (indices 16–231) ──────────────────────────────────────
        int cr = NearestCubeIndex(r);
        int cg = NearestCubeIndex(g);
        int cb = NearestCubeIndex(b);
        int cubeIndex = 16 + 36 * cr + 6 * cg + cb;

        int cubeDist = SquaredDist(r, g, b, _cubeSteps[cr], _cubeSteps[cg], _cubeSteps[cb]);

        // ── 24-level greyscale ramp (indices 232–255): grey = 8 + i*10 ───────
        // Find the nearest grey level.
        int greyLevel = NearestGreyLevel(r, g, b);
        int greyValue = 8 + greyLevel * 10;
        int greyIndex = 232 + greyLevel;

        int greyDist = SquaredDist(r, g, b, greyValue, greyValue, greyValue);

        return greyDist <= cubeDist ? greyIndex : cubeIndex;
    }

    private static int NearestCubeIndex(int channel)
    {
        // Snap to the nearest of the 6 cube step values.
        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < _cubeSteps.Length; i++)
        {
            int d = Math.Abs(channel - _cubeSteps[i]);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private static int NearestGreyLevel(int r, int g, int b)
    {
        // The grey ramp uses perceptual average for the target grey.
        int avg = (r + g + b) / 3;
        // grey(i) = 8 + i*10, for i in [0,23], so i = clamp(round((avg - 8) / 10), 0, 23).
        int level = (avg - 8 + 5) / 10; // round-half-up
        return Math.Clamp(level, 0, 23);
    }

    private static int SquaredDist(int r, int g, int b, int pr, int pg, int pb)
    {
        int dr = r - pr;
        int dg = g - pg;
        int db = b - pb;
        return dr * dr + dg * dg + db * db;
    }
}
