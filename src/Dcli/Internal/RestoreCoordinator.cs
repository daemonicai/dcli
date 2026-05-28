using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dcli.Internal;

/// <summary>
/// Wires a last-resort restoration net around an <see cref="IRawModeSession"/>:
/// <list type="bullet">
///   <item>On POSIX: SIGINT, SIGTERM, SIGQUIT → restore terminal, then re-raise the signal so
///     the process terminates with the correct exit status.</item>
///   <item>On POSIX: SIGCONT → re-apply raw mode (resume after suspend left the tty cooked).</item>
///   <item>All platforms: <c>AppDomain.CurrentDomain.ProcessExit</c> → restore terminal.</item>
/// </list>
/// <para>
/// This class is internal and testable: the <see cref="IRawModeSession"/> is injected, so
/// tests substitute a <see cref="RecordingRawModeSession"/> and drive the observable actions
/// (<see cref="SimulateTerminateSignal"/> / <see cref="SimulateContinueSignal"/> /
/// <see cref="OnProcessExit"/>) directly without needing to construct real OS signal contexts.
/// </para>
/// <para>
/// Idempotency: all paths converge on <see cref="IRawModeSession.Restore"/>, which is itself
/// idempotent, so concurrent invocations from multiple handlers are safe.
/// </para>
/// </summary>
internal sealed class RestoreCoordinator : IDisposable
{
    private readonly IRawModeSession _session;
    private readonly List<IDisposable> _registrations = [];
    private int _disposed;

    // Wired after loop creation (SetHaltAction). When set, signal-driven restore halts the
    // loop first — so the loop's finally emits the ANSI restore on the single-writer thread —
    // before falling back to the idempotent session restore below.
    private volatile Action? _haltLoop;

    private RestoreCoordinator(IRawModeSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Wires the loop-halt action called on every terminate-signal path so the loop thread
    /// emits the ANSI restore sequence before the termios restore occurs.
    /// Must be called once, after the loop is created, before any signal can fire.
    /// The supplied action must be idempotent.
    /// </summary>
    internal void SetHaltAction(Action haltLoop)
    {
        ArgumentNullException.ThrowIfNull(haltLoop);
        _haltLoop = haltLoop;
    }

    /// <summary>
    /// Creates and wires a coordinator for <paramref name="session"/>, registering all signal
    /// handlers and the ProcessExit hook.
    /// </summary>
    internal static RestoreCoordinator Wire(IRawModeSession session)
    {
        RestoreCoordinator coord = new(session);
        coord.Register();
        return coord;
    }

    private void Register()
    {
        // AppDomain.ProcessExit fires on normal exit and Environment.Exit; always registered.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // POSIX-only signals.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            RegisterPosixSignals();
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private void RegisterPosixSignals()
    {
        // Terminate signals: restore terminal then let the process die with the default disposition.
        // Cancel=false (the default) allows the signal to propagate so the exit status is correct.
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, OnTerminateSignal));
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnTerminateSignal));
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGQUIT, OnTerminateSignal));

        // SIGCONT: resume from an externally-driven kill -STOP/-CONT may have left the tty cooked.
        // dcli does not install a SIGTSTP handler (ISIG is cleared, so Ctrl-Z does not generate it);
        // Reapply() handles the case where an external agent suspended and resumed the process.
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGCONT, OnContinueSignal));
    }

    // ─── OS signal callbacks ─────────────────────────────────────────────────

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private void OnTerminateSignal(PosixSignalContext context)
    {
        // context.Cancel remains false; signal propagates → process dies.
        SimulateTerminateSignal();
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private void OnContinueSignal(PosixSignalContext context)
    {
        context.Cancel = true;  // We fully handle SIGCONT; do not re-raise.
        SimulateContinueSignal();
    }

    // ─── Testable action methods ─────────────────────────────────────────────
    // These are the unit-testable equivalents of the OS callbacks.
    // Tests call them directly; the OS callbacks above delegate to them.

    /// <summary>
    /// Halts the loop (so its <c>finally</c> emits the ANSI restore on the single-writer thread)
    /// then restores the terminal session — the action taken on SIGINT/SIGTERM/SIGQUIT.
    /// Exposed for unit-testing the handler logic without a real OS signal.
    /// </summary>
    internal void SimulateTerminateSignal()
    {
        // Halting the loop causes its finally block to run EmitRestoreSequence() on the
        // loop thread (the single stdout writer), preserving the single-writer discipline.
        // _haltLoop is null only in tests that construct RestoreCoordinator directly without
        // wiring a loop — those tests exercise termios restore only, which is sufficient.
        _haltLoop?.Invoke();

        // Idempotent fallback: covers the case where _haltLoop was not wired (bare tests)
        // and ensures termios is restored even if the loop halt races with another exit path.
        _session.Restore();
    }

    /// <summary>
    /// Re-applies raw mode — the action taken on SIGCONT.
    /// Exposed for unit-testing the handler logic without a real OS signal.
    /// </summary>
    internal void SimulateContinueSignal() => _session.Reapply();

    // ─── ProcessExit handler ─────────────────────────────────────────────────

    /// <summary>
    /// Called by <c>AppDomain.ProcessExit</c>. Restores the terminal.
    /// </summary>
    internal void OnProcessExit(object? sender, EventArgs e) => _session.Restore();

    // ─── Dispose ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Deregisters all signal/ProcessExit hooks and calls <see cref="IRawModeSession.Restore"/>.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _session.Restore();

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        foreach (IDisposable reg in _registrations)
            reg.Dispose();

        _registrations.Clear();
    }
}
