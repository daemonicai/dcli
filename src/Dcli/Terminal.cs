using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dcli.Internal;
using Dcli.Internal.Input;
using Dcli.Internal.Posix;
using Dcli.Internal.RenderLoop;
using Dcli.Internal.Windows;

namespace Dcli;

/// <summary>
/// The dcli terminal lifecycle handle.
/// </summary>
/// <remarks>
/// <para>
/// Obtain an instance via <see cref="StartAsync(TerminalOptions, CancellationToken)"/>. The handle wires together raw mode,
/// the input reader, and the render loop. Dispose to stop the loop, join all threads, and
/// restore the terminal.
/// </para>
/// <para>
/// <strong>Restore guarantee:</strong> restoration occurs on <em>every</em> exit path —
/// normal <see cref="DisposeAsync"/>, an unhandled exception on the loop thread (via the
/// loop's <c>finally</c> block), and external signals or <c>ProcessExit</c> (via the
/// <see cref="RestoreCoordinator"/>). All paths converge on the idempotent
/// <see cref="IRawModeSession.Restore"/>, so no double-restore can occur.
/// </para>
/// </remarks>
public sealed class Terminal : IAsyncDisposable
{
    private readonly IRawModeSession _session;
    private readonly RestoreCoordinator _coordinator;
    private readonly InputReader _inputReader;
    private readonly LoopEngine _loop;

    // DisposeAsync blocks the calling thread on bounded Thread.Join calls (loop ≤2 s,
    // reader ≤500 ms) by design — await-using callers should expect thread blocking, not
    // a truly asynchronous yield. IAsyncDisposable is exposed for the await-using pattern.
    private int _disposed;

    // Captured loop fault: set by DisposeAsync after joining the loop thread so that
    // DisposeAsync rethrows and the caller learns the loop crashed.
    private volatile Exception? _loopFault;

    private Terminal(
        IRawModeSession session,
        RestoreCoordinator coordinator,
        InputReader inputReader,
        LoopEngine loop)
    {
        _session = session;
        _coordinator = coordinator;
        _inputReader = inputReader;
        _loop = loop;
    }

    // ── Internal surface (tests) ────────────────────────────────────────────

    /// <summary>
    /// The underlying loop engine. Exposed for headless tests that need to post commands
    /// (e.g. to trigger a paint and observe loop-fault propagation). Not part of the public API.
    /// </summary>
    internal LoopEngine Loop => _loop;

    // ── Public surface ──────────────────────────────────────────────────────

    /// <summary>
    /// The outbound terminal event stream.
    /// </summary>
    /// <remarks>
    /// Drain this channel on a separate consumer thread or task. A consumer that never reads
    /// accumulates events but does not stall the render loop.
    /// </remarks>
    public ChannelReader<TerminalEvent> Events => _loop.OutboundEvents;

    /// <summary>
    /// Returns the last-known terminal size from the volatile snapshot. Does not round-trip
    /// to the render loop thread (Decision 10: snapshot read).
    /// </summary>
    public (int Columns, int Rows) GetTerminalSize() => _loop.GetTerminalSize();

    // ── Factory ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Enters raw mode and starts the render loop, returning a handle whose
    /// <see cref="DisposeAsync"/> stops the loop and restores the terminal.
    /// </summary>
    /// <param name="options">Configuration; pass <c>new TerminalOptions()</c> for defaults.</param>
    /// <param name="cancellationToken">Token that cancels the start-up sequence.</param>
    /// <returns>A live <see cref="Terminal"/> handle.</returns>
    /// <exception cref="TerminalNotSupportedException">
    /// The environment does not support VT sequences (non-tty stdout/stdin, <c>TERM=dumb</c>,
    /// or unsupported platform).
    /// </exception>
    public static Task<Terminal> StartAsync(
        TerminalOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return StartAsync(options, capabilityInputs: null, cancellationToken);
    }

    /// <summary>
    /// Overload accepting injected <see cref="CapabilityInputs"/> for testing the capability gate
    /// without a real tty. Internal — the public overload reads from the real environment.
    /// </summary>
    internal static Task<Terminal> StartAsync(
        TerminalOptions options,
        CapabilityInputs? capabilityInputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        ITerminalSizeSource sizeSource = CreatePlatformSizeSource();

        // Capability gate + raw mode (§4). Throws TerminalNotSupportedException if not VT-capable.
        IRawModeSession session = RawModeSession.Enter(capabilityInputs);

        // Last-resort restore net for signals + ProcessExit (§4). Registered before the loop
        // starts so no window exists between raw-mode entry and signal coverage.
        RestoreCoordinator coordinator = RestoreCoordinator.Wire(session);

        IInputByteSource byteSource = CreatePlatformInputByteSource();
        IOutputSink sink = new NoopOutputSink();

        Terminal terminal = StartCore(
            session,
            coordinator,
            byteSource,
            new SystemClock(),
            sink,
            sizeSource,
            TimeSpan.FromMilliseconds(options.MinFrameIntervalMs),
            options.MaxFixedHeight);

        return Task.FromResult(terminal);
    }

    /// <summary>
    /// Internal start path for headless testing: accepts injected edges rather than real OS
    /// resources. The <paramref name="session"/> is wired into the loop's <c>finally</c> and
    /// into the <see cref="RestoreCoordinator"/> to cover all exit paths.
    /// </summary>
    internal static Terminal StartCore(
        IRawModeSession session,
        RestoreCoordinator coordinator,
        IInputByteSource byteSource,
        IClock clock,
        IOutputSink sink,
        ITerminalSizeSource sizeSource,
        TimeSpan minFrameInterval,
        int? maxFixedHeight = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(byteSource);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(sizeSource);

        // Wire the loop: pass the session's Restore as the loop-finally hook so that an
        // unhandled exception on the loop thread still restores. The coordinator covers
        // signals/ProcessExit; Dispose covers normal shutdown. All three converge on the
        // same CAS-idempotent Restore.
        LoopEngine loop = new(sizeSource, clock, sink, minFrameInterval,
            restoreOnExit: session.Restore, maxFixedHeight: maxFixedHeight);

        // Wire input reader → loop's inbound channel.
        InputReader inputReader = new(byteSource, loop.InputWriter);

        return new Terminal(session, coordinator, inputReader, loop);
    }

    // ── Dispose ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stops the render loop and input reader, joins their threads, and restores the terminal.
    /// Idempotent: safe to call more than once. Rethrows any fatal exception that caused the
    /// render loop to crash (observable via the faulted <see cref="LoopEngine.LoopTerminated"/>
    /// task); the terminal is always restored before the exception is rethrown.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            // Rethrow a previously-captured loop fault on repeated calls too, so the caller
            // always sees the crash regardless of which DisposeAsync call they await.
            Exception? fault = _loopFault;
            if (fault is not null)
                return ValueTask.FromException(fault);
            return ValueTask.CompletedTask;
        }

        // Stop the loop first (cancels CTS, completes channel) then stop the reader.
        // Both Dispose() calls block for a short join timeout.
        _loop.Dispose();
        _inputReader.Dispose();

        // Deregister signal/ProcessExit hooks and call Restore (idempotent).
        _coordinator.Dispose();

        // Dispose the raw-mode session (also calls Restore; idempotent).
        _session.Dispose();

        // After the loop thread has joined, inspect whether it faulted.
        // LoopTerminated is already completed at this point (the thread joined above).
        Task loopTask = _loop.LoopTerminated;
        if (loopTask.IsFaulted)
        {
            Exception fault = loopTask.Exception!.GetBaseException();
            _loopFault = fault;
            return ValueTask.FromException(fault);
        }

        return ValueTask.CompletedTask;
    }

    // ── Platform wiring helpers ─────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ITerminalSizeSource CreatePlatformSizeSource()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return new PosixTerminalSizeSource();

        if (OperatingSystem.IsWindows())
            return new WindowsTerminalSizeSource();

        // Fallback: unknown platform; §4 will have thrown TerminalNotSupportedException before
        // we reach this, but return a safe default so the compiler is satisfied.
        return new FixedFallbackSizeSource();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IInputByteSource CreatePlatformInputByteSource()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return new PosixInputByteSource();

        if (OperatingSystem.IsWindows())
            return new WindowsInputByteSource();

        throw new PlatformNotSupportedException("dcli only supports Linux, macOS, and Windows.");
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>Safe 80×24 fallback used when the platform is unknown (§4 rejects it first).</summary>
    private sealed class FixedFallbackSizeSource : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (80, 24);
    }
}
