namespace Dcli.Testing;

/// <summary>
/// Configuration for <see cref="HeadlessTerminal.StartAsync"/>.
/// All properties have defaults suitable for most tests.
/// </summary>
public sealed class HeadlessTerminalOptions
{
    /// <summary>
    /// Initial terminal width in columns. Defaults to <c>80</c>.
    /// </summary>
    public int InitialColumns { get; init; } = 80;

    /// <summary>
    /// Initial terminal height in rows. Defaults to <c>24</c>.
    /// </summary>
    public int InitialRows { get; init; } = 24;

    /// <summary>
    /// Minimum interval between successive paints.
    /// Defaults to <see cref="TimeSpan.Zero"/> so every <see cref="HeadlessTerminal.SettleAsync"/>
    /// call produces a frame without virtual-time advancement.
    /// Set to a non-zero value for cadence tests that use <see cref="VirtualClock.Advance"/>.
    /// </summary>
    public TimeSpan MinFrameInterval { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Optional cap on the fixed-region height in rows.
    /// <see langword="null"/> (the default) lets the render model use its built-in 50 % heuristic.
    /// </summary>
    public int? MaxFixedHeight { get; init; }

    /// <summary>
    /// Optional pre-constructed <see cref="VirtualClock"/> to use.
    /// When <see langword="null"/> (the default), <see cref="HeadlessTerminal.StartAsync"/> creates
    /// a fresh clock starting at <see cref="TimeSpan.Zero"/>.
    /// Providing a clock lets the caller share a single clock across multiple harness instances.
    /// </summary>
    public VirtualClock? Clock { get; init; }
}
