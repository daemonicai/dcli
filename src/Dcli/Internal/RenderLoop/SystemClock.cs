using System.Diagnostics;

namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Real-time <see cref="IClock"/> implementation backed by <see cref="Stopwatch"/>.
/// </summary>
internal sealed class SystemClock : IClock
{
    private static readonly Stopwatch _epoch = Stopwatch.StartNew();

    /// <inheritdoc/>
    public TimeSpan Now => _epoch.Elapsed;

    /// <inheritdoc/>
    public async Task WaitUntilAsync(TimeSpan deadline, CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadline - Now;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }
}
