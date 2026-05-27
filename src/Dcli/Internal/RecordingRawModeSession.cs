namespace Dcli.Internal;

/// <summary>
/// A test-double <see cref="IRawModeSession"/> that records calls to <see cref="Restore"/> and
/// <see cref="Reapply"/> without touching the OS. Used to verify the restore coordinator's
/// wiring logic without a real tty.
/// </summary>
internal sealed class RecordingRawModeSession : IRawModeSession
{
    private int _restoreCallCount;
    private int _effectiveRestoreCount;
    private int _reapplyCallCount;

    /// <summary>
    /// Total number of times <see cref="Restore"/> was invoked (raw call count, including
    /// redundant calls). Use <see cref="EffectiveRestoreCount"/> to assert the CAS-idempotent
    /// "one real restore" invariant.
    /// </summary>
    internal int RestoreCallCount => Volatile.Read(ref _restoreCallCount);

    /// <summary>
    /// Number of times <see cref="Restore"/> had a real effect (i.e. was the first call).
    /// Mirrors the CAS-idempotency of <see cref="Posix.PosixRawModeSession"/>: only the first
    /// Restore call counts; all subsequent calls are no-ops.
    /// </summary>
    internal int EffectiveRestoreCount => Volatile.Read(ref _effectiveRestoreCount);

    /// <summary>Total number of times <see cref="Reapply"/> was invoked.</summary>
    internal int ReapplyCallCount => Volatile.Read(ref _reapplyCallCount);

    /// <inheritdoc/>
    public void Restore()
    {
        Interlocked.Increment(ref _restoreCallCount);
        // Mirror real CAS-idempotency: only the first call is "effective".
        Interlocked.CompareExchange(ref _effectiveRestoreCount, 1, 0);
    }

    /// <inheritdoc/>
    public void Reapply() => Interlocked.Increment(ref _reapplyCallCount);

    /// <inheritdoc/>
    public void Dispose() => Restore();
}
