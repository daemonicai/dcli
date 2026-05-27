namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Abstracts wall-clock access so the render loop's cadence (frame-rate cap, idle detection)
/// can be exercised deterministically in tests without real time passing.
/// </summary>
/// <remarks>
/// The real implementation delegates to <see cref="System.Diagnostics.Stopwatch"/> /
/// <c>Task.Delay</c>. The virtual-clock test implementation
/// lets tests advance time explicitly so throttle / coalescing / idle scenarios run
/// without any wall-clock sleeps.
/// </remarks>
internal interface IClock
{
    /// <summary>Returns the current instant as a <see cref="TimeSpan"/> since an arbitrary epoch.</summary>
    TimeSpan Now { get; }

    /// <summary>
    /// Returns a <see cref="System.Threading.Tasks.Task"/> that completes at <paramref name="deadline"/>,
    /// or immediately if <paramref name="deadline"/> is already in the past.
    /// </summary>
    /// <param name="deadline">The target instant (same epoch as <see cref="Now"/>).</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task WaitUntilAsync(TimeSpan deadline, CancellationToken cancellationToken);
}
