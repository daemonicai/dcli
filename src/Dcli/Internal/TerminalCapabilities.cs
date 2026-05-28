namespace Dcli.Internal;

/// <summary>
/// Optional terminal capabilities detected from the environment.
/// All flags default to <see langword="false"/> (conservative — no capability assumed).
/// </summary>
internal readonly record struct TerminalCapabilities
{
    /// <summary>
    /// <see langword="true"/> when the terminal advertises 24-bit truecolor via
    /// <c>COLORTERM=truecolor</c> or <c>COLORTERM=24bit</c>. When <see langword="false"/>,
    /// <see cref="Dcli.Internal.RenderLoop.SgrTranslator"/> downgrades RGB colors to the
    /// nearest xterm 256-indexed palette entry.
    /// </summary>
    public bool HasTruecolor { get; init; }

    /// <summary>
    /// <see langword="true"/> when the terminal is known to support the synchronized-output
    /// DEC private mode (<c>ESC[?2026h…l</c>). Recorded for diagnostics; the fence is
    /// emitted unconditionally — terminals that do not support the sequence ignore it.
    /// </summary>
    public bool HasSynchronizedOutput { get; init; }

    /// <summary>
    /// <see langword="true"/> when the locale suggests East-Asian CJK content and
    /// Unicode "ambiguous-width" characters should be treated as two cells wide.
    /// Recorded for future use; the wcwidth table is unchanged in v1.
    /// </summary>
    public bool TreatAmbiguousAsWide { get; init; }

    /// <summary>
    /// Conservative default: all capabilities absent. Used by headless test rigs and any
    /// context where the environment has not been probed.
    /// </summary>
    internal static readonly TerminalCapabilities Default;
}
