namespace Dcli.Terminal;

/// <summary>
/// Represents an OS raw-mode session: the tty is in raw mode while this object is alive, and
/// <see cref="Restore"/> returns it to its previous state.
/// <para>
/// Implementations must be idempotent and thread-safe: calling <see cref="Restore"/> (or
/// <see cref="IDisposable.Dispose"/>) more than once — including concurrently from a signal
/// handler, <c>ProcessExit</c>, and <see cref="IDisposable.Dispose"/> — must be safe and must
/// not double-restore the terminal.
/// </para>
/// <para>
/// This interface is the seam used by the headless test harness (§15) to substitute a no-op
/// session without touching the render loop or input pipeline.
/// </para>
/// </summary>
public interface IRawModeSession : IDisposable
{
    /// <summary>
    /// Restores the terminal to the state captured before raw mode was entered.
    /// Idempotent: safe to call from multiple threads or multiple times.
    /// </summary>
    void Restore();

    /// <summary>
    /// Re-applies raw mode. Called after SIGCONT (process resumed from suspension)
    /// which may have left the tty in cooked mode.
    /// </summary>
    void Reapply();
}
