namespace Dcli.Terminal;

/// <summary>
/// A test-double <see cref="IRawModeSession"/> that records calls to <see cref="Restore"/> and
/// <see cref="Reapply"/> without touching the OS. Used to verify the restore coordinator's
/// wiring logic without a real tty.
/// </summary>
internal sealed class RecordingRawModeSession : IRawModeSession
{
    private int _restoreCallCount;
    private int _reapplyCallCount;

    /// <summary>Total number of times <see cref="Restore"/> was invoked.</summary>
    internal int RestoreCallCount => Volatile.Read(ref _restoreCallCount);

    /// <summary>Total number of times <see cref="Reapply"/> was invoked.</summary>
    internal int ReapplyCallCount => Volatile.Read(ref _reapplyCallCount);

    /// <inheritdoc/>
    public void Restore() => Interlocked.Increment(ref _restoreCallCount);

    /// <inheritdoc/>
    public void Reapply() => Interlocked.Increment(ref _reapplyCallCount);

    /// <inheritdoc/>
    public void Dispose() => Restore();
}
