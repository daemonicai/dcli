namespace Dcli;

/// <summary>
/// Configuration options for <see cref="Terminal.StartAsync(TerminalOptions, CancellationToken)"/>.
/// </summary>
public sealed class TerminalOptions
{
    /// <summary>
    /// Maximum height in rows of the fixed interactive region pinned at the bottom of the
    /// terminal. <see langword="null"/> means "no explicit cap".
    /// </summary>
    /// <remarks>
    /// The fixed region includes the caret line, status bar, and any overlay (dialog /
    /// autocomplete). When <see langword="null"/>, the render loop applies an internal default
    /// based on the terminal height (clamped to a minimum of 8 rows).
    /// </remarks>
    public int? MaxFixedHeight { get; init; }

    /// <summary>
    /// Minimum interval between successive rendered frames, in milliseconds.
    /// Defaults to <c>16</c> (≈ 60 fps ceiling). Increase for lower-frequency rendering;
    /// decrease only in tests or benchmark scenarios.
    /// </summary>
    public int MinFrameIntervalMs { get; init; } = 16;
}
