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
    private long _outputLengthAtRestore = -1;

    /// <summary>
    /// Optional delegate that returns the current byte position of the output stream.
    /// When set, <see cref="Restore"/> snapshots the stream position so tests can assert
    /// that the ANSI restore sequence was emitted BEFORE termios restore was called.
    /// </summary>
    internal Func<long>? GetOutputLength { get; set; }

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

    /// <summary>
    /// The output stream length (in characters) at the moment the first <see cref="Restore"/>
    /// was called, or -1 if <see cref="GetOutputLength"/> was not set. Used to assert ordering:
    /// ANSI bytes must have been written before termios restore.
    /// </summary>
    internal long OutputLengthAtRestore => Volatile.Read(ref _outputLengthAtRestore);

    /// <inheritdoc/>
    public void Restore()
    {
        // Snapshot the output length on the FIRST call to Restore (before any idempotency guard
        // so the snapshot reflects the position when restore was first triggered).
        long snapshot = GetOutputLength?.Invoke() ?? -1;
        Interlocked.CompareExchange(ref _outputLengthAtRestore, snapshot, -1);

        Interlocked.Increment(ref _restoreCallCount);
        // Mirror real CAS-idempotency: only the first call is "effective".
        Interlocked.CompareExchange(ref _effectiveRestoreCount, 1, 0);
    }

    /// <inheritdoc/>
    public void Reapply() => Interlocked.Increment(ref _reapplyCallCount);

    /// <inheritdoc/>
    public void Dispose() => Restore();
}
