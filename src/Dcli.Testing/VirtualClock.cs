using Dcli.Internal.RenderLoop;

namespace Dcli.Testing;

/// <summary>
/// A controllable clock for cadence tests.
/// Time never advances unless <see cref="Advance"/> is called explicitly, so tests exercise
/// throttle and coalescing logic without wall-clock sleeps.
/// </summary>
/// <remarks>
/// All public members are thread-safe. Waiters whose deadline has been passed are completed
/// outside the lock to prevent re-entrant deadlock if a continuation calls back into the clock.
/// </remarks>
public sealed class VirtualClock : IClock
{
    private readonly object _lock = new();
    private TimeSpan _now;
    private readonly List<(TimeSpan Deadline, TaskCompletionSource Tcs, CancellationTokenSource LinkedCts)> _waiters = [];

    /// <summary>
    /// Initialises the clock at the specified instant (defaults to <see cref="TimeSpan.Zero"/>).
    /// </summary>
    /// <param name="initial">The starting time value.</param>
    public VirtualClock(TimeSpan? initial = null) => _now = initial ?? TimeSpan.Zero;

    /// <inheritdoc/>
    public TimeSpan Now
    {
        get { lock (_lock) { return _now; } }
    }

    /// <summary>
    /// Advances the clock by <paramref name="by"/> and completes all waiters whose deadline
    /// is at or before the new time.
    /// </summary>
    /// <param name="by">The amount of time to advance. Must be non-negative.</param>
    public void Advance(TimeSpan by)
    {
        List<(TaskCompletionSource Tcs, CancellationTokenSource LinkedCts)> toComplete = [];
        lock (_lock)
        {
            _now += by;
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Deadline <= _now)
                {
                    toComplete.Add((_waiters[i].Tcs, _waiters[i].LinkedCts));
                    _waiters.RemoveAt(i);
                }
            }
        }
        // Complete outside the lock: a continuation that re-enters the clock must not deadlock.
        foreach ((TaskCompletionSource tcs, CancellationTokenSource linkedCts) in toComplete)
        {
            tcs.TrySetResult();
            linkedCts.Dispose();
        }
    }

    /// <inheritdoc/>
    public Task WaitUntilAsync(TimeSpan deadline, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (deadline <= _now)
                return Task.CompletedTask;
        }

        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        linkedCts.Token.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            tcs,
            useSynchronizationContext: false);

        lock (_lock)
        {
            if (deadline <= _now)
            {
                linkedCts.Dispose();
                return Task.CompletedTask;
            }
            _waiters.Add((deadline, tcs, linkedCts));
        }
        return tcs.Task;
    }
}
