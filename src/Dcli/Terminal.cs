using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dcli.Internal;
using Dcli.Internal.FixedRegion;
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
public sealed class Terminal : ITerminal
{
    private readonly IRawModeSession _session;
    private readonly RestoreCoordinator _coordinator;
    private readonly IResizeWatcher _resizeWatcher;
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
        IResizeWatcher resizeWatcher,
        InputReader inputReader,
        LoopEngine loop)
    {
        _session = session;
        _coordinator = coordinator;
        _resizeWatcher = resizeWatcher;
        _inputReader = inputReader;
        _loop = loop;
        Scrollback = new ScrollbackSurface(loop);
        Input = new InputSurface(loop);
        Status = new StatusSurface(loop);
        InputPreamble = new InputPreambleSurface(loop);
        Autocomplete = new AutocompleteSurface(loop);
    }

    // ── Internal surface (tests) ────────────────────────────────────────────

    /// <summary>
    /// The underlying loop engine. Exposed for headless tests that need to post commands
    /// (e.g. to trigger a paint and observe loop-fault propagation). Not part of the public API.
    /// </summary>
    internal LoopEngine Loop => _loop;

    /// <summary>
    /// The restore coordinator. Exposed for tests that need to simulate signal-driven exit
    /// (e.g. to verify the full Option C wiring: signal → halt loop → ANSI restore → termios).
    /// Not part of the public API.
    /// </summary>
    internal RestoreCoordinator Coordinator => _coordinator;

    // ── Public surface ──────────────────────────────────────────────────────

    /// <summary>
    /// Scrollback content surface: append lines, live blocks, and collapsibles.
    /// </summary>
    /// <remarks>
    /// Posts are fire-and-forget; all writes are applied on the render-loop thread.
    /// </remarks>
    public IScrollback Scrollback { get; }

    /// <summary>
    /// Input editor surface: programmatic <see cref="IInput.SetText"/> and
    /// <see cref="IInput.Clear"/>. Does not emit <see cref="InputChanged"/>.
    /// </summary>
    public IInput Input { get; }

    /// <summary>
    /// Status bar surface: set the sacred status rows at the bottom of the fixed region.
    /// </summary>
    public IStatus Status { get; }

    /// <summary>
    /// Input preamble surface: set styled rows rendered directly above the input editor.
    /// </summary>
    public IInputPreamble InputPreamble { get; }

    /// <summary>
    /// Autocomplete overlay surface: show and hide the completion dropdown.
    /// </summary>
    public IAutocomplete Autocomplete { get; }

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

    // ── Awaitable dialogs (§12) ─────────────────────────────────────────────

    /// <summary>
    /// Opens a single-select list dialog and awaits the user's choice.
    /// </summary>
    /// <param name="req">The request describing the items and optional title.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>. If already
    /// cancelled before the dialog opens, returns immediately with <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the zero-based selected index, or
    /// <see cref="DialogOutcome.Cancelled"/>. When the item list is empty and the user submits,
    /// <see cref="DialogResult{T}.Value"/> is <c>-1</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="req"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A dialog is already active.</exception>
    public Task<DialogResult<int>> SelectAsync(
        SelectRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        Dialog dialog = new(multiSelect: false, modal: true, typeToFilter: false, title: req.Title, allowBack: req.AllowBack);
        dialog.List.SetItems(req.Items);
        return OpenModalAsync<int>(
            dialog,
            () => new DialogResult<int>(DialogOutcome.Submitted, dialog.List.SelectedIndex),
            cancellationToken);
    }

    /// <summary>
    /// Opens a multi-select list dialog and awaits the user's selection.
    /// </summary>
    /// <param name="req">The request describing the items and optional title.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the checked indices in ascending order, or
    /// <see cref="DialogOutcome.Cancelled"/> with an empty array.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="req"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A dialog is already active.</exception>
    public Task<DialogResult<int[]>> MultiSelectAsync(
        MultiSelectRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        Dialog dialog = new(multiSelect: true, modal: true, typeToFilter: false, title: req.Title, allowBack: req.AllowBack);
        dialog.List.SetItems(req.Items);
        return OpenModalAsync<int[]>(
            dialog,
            () => new DialogResult<int[]>(DialogOutcome.Submitted, [.. dialog.List.CheckedIndices]),
            cancellationToken);
    }

    /// <summary>
    /// Opens a single-select choice dialog and awaits the user's choice.
    /// Semantically equivalent to <see cref="SelectAsync"/> with an optional prompt row.
    /// </summary>
    /// <param name="req">The request describing the options and optional prompt.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the zero-based selected index, or
    /// <see cref="DialogOutcome.Cancelled"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="req"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A dialog is already active.</exception>
    public Task<DialogResult<int>> ChoiceAsync(
        ChoiceRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        Dialog dialog = new(multiSelect: false, modal: true, typeToFilter: false, title: req.Prompt, allowBack: req.AllowBack);
        dialog.List.SetItems(req.Options);
        return OpenModalAsync<int>(
            dialog,
            () => new DialogResult<int>(DialogOutcome.Submitted, dialog.List.SelectedIndex),
            cancellationToken);
    }

    /// <summary>
    /// Opens a free-text input dialog and awaits the user's entry.
    /// </summary>
    /// <param name="req">The request describing the optional prompt, default text, and masking.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>. If already
    /// cancelled before the dialog opens, returns immediately with <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the entered text (possibly empty), or
    /// <see cref="DialogOutcome.Cancelled"/> when the user presses Escape or the token fires.
    /// When <see cref="InputRequest.IsSecret"/> is <see langword="true"/>, the rendered overlay
    /// shows mask glyphs but <see cref="DialogResult{T}.Value"/> always carries the real text.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="req"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A dialog is already active.</exception>
    public Task<DialogResult<string>> InputAsync(
        InputRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        InputDialog dialog = new(req.Prompt, req.Default, req.IsSecret, req.AllowBack);
        return OpenModalAsync<string>(
            dialog,
            () => new DialogResult<string>(DialogOutcome.Submitted, dialog.Text),
            cancellationToken);
    }

    /// <summary>
    /// Core modal-open helper: creates a TCS, posts an <see cref="OpenDialogCommand"/>,
    /// wires cancellation, and returns the awaitable task.
    /// </summary>
    /// <typeparam name="T">The result value type.</typeparam>
    /// <param name="overlay">The prepared modal overlay.</param>
    /// <param name="buildResult">
    /// Parameterless factory called on the loop thread when the overlay is submitted.
    /// The closure captures the overlay and reads its state (e.g. text, selection index).
    /// Only called on <see cref="OverlayCloseKind.Submit"/>; <see cref="OverlayCloseKind.Back"/>
    /// and <see cref="OverlayCloseKind.Cancel"/> produce default-value results with the
    /// corresponding <see cref="DialogOutcome"/>.
    /// </param>
    /// <param name="cancellationToken">External cancellation token.</param>
    private Task<DialogResult<T>> OpenModalAsync<T>(
        IModalOverlay overlay,
        Func<DialogResult<T>> buildResult,
        CancellationToken cancellationToken)
    {
        // Fast path: already cancelled — never open the dialog.
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(new DialogResult<T>(DialogOutcome.Cancelled, default!));

        TaskCompletionSource<DialogResult<T>> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The registration handle: we need to dispose it once the task completes to avoid leaks.
        // Use a holder so the lambda can capture and dispose it.
        CancellationTokenRegistration[] registrationHolder = new CancellationTokenRegistration[1];

        Action completion = () =>
        {
            // Running on the loop thread. Read overlay state and complete the TCS.
            DialogResult<T> result = overlay.CloseRequest switch
            {
                OverlayCloseKind.Submit => buildResult(),
                OverlayCloseKind.Back => new DialogResult<T>(DialogOutcome.Back, default!),
                _ => new DialogResult<T>(DialogOutcome.Cancelled, default!),
            };
            tcs.TrySetResult(result);
            // Dispose the CT registration to prevent a stale cancel command from posting later.
            registrationHolder[0].Dispose();
        };

        Action reject = () =>
        {
            // Running on the loop thread. Fault the TCS — caller gets InvalidOperationException.
            tcs.TrySetException(new InvalidOperationException("A dialog is already active."));
            registrationHolder[0].Dispose();
        };

        // Post a cancel command when the token fires. The command checks object identity so
        // it is a no-op if the overlay has already been dismissed naturally.
        if (cancellationToken.CanBeCanceled)
        {
            Action cancelCompletion = () =>
            {
                // Running on the loop thread (from CancelDialogCommand.Apply).
                tcs.TrySetResult(new DialogResult<T>(DialogOutcome.Cancelled, default!));
                registrationHolder[0].Dispose();
            };

            registrationHolder[0] = cancellationToken.Register(
                () => _loop.Post(new CancelDialogCommand(overlay, cancelCompletion)),
                useSynchronizationContext: false);
        }

        _loop.Post(new OpenDialogCommand(overlay, completion, reject));
        return tcs.Task;
    }

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

        // Detect optional capabilities (truecolor, synchronized-output, ambiguous-width) after
        // the VT gate passes. The gate runs first; capabilities are the "extras" layer.
        TerminalCapabilities capabilities = TerminalCapabilityDetector.DetectCapabilities(capabilityInputs);

        // Last-resort restore net for signals + ProcessExit (§4). Registered before the loop
        // starts so no window exists between raw-mode entry and signal coverage.
        RestoreCoordinator coordinator = RestoreCoordinator.Wire(session);

        IInputByteSource byteSource = CreatePlatformInputByteSource();

        IResizeWatcher resizeWatcher = CreatePlatformResizeWatcher(sizeSource);

        // UTF-8, no BOM, no auto-flush — one explicit Flush() per frame keeps each
        // synchronized-output fenced frame written as a single kernel write.
        StreamWriter stdoutWriter = new(
            Console.OpenStandardOutput(),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 65536,
            leaveOpen: false);
        IOutputSink sink = new VtFrameRenderer(stdoutWriter, capabilities);

        Terminal terminal = StartCore(
            session,
            coordinator,
            resizeWatcher,
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
        IResizeWatcher resizeWatcher,
        IInputByteSource byteSource,
        IClock clock,
        IOutputSink sink,
        ITerminalSizeSource sizeSource,
        TimeSpan minFrameInterval,
        int? maxFixedHeight = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(resizeWatcher);
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

        // Wire the loop halt into the coordinator so that signal-driven restore halts the
        // loop first (causing its finally to emit the ANSI restore on the single-writer thread)
        // before the coordinator calls the idempotent session restore.
        coordinator.SetHaltAction(loop.Dispose);

        // Wire resize watcher → loop's inbound channel (SIGWINCH callback posts ResizeEvent).
        // Capture InputWriter once to avoid capturing the whole loop in the closure.
        ChannelWriter<InputEvent> inputWriter = loop.InputWriter;
        resizeWatcher.Start((cols, rows) => inputWriter.TryWrite(new ResizeEvent(cols, rows)));

        // Wire input reader → loop's inbound channel.
        InputReader inputReader = new(byteSource, loop.InputWriter);

        return new Terminal(session, coordinator, resizeWatcher, inputReader, loop);
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

        // Stop the resize watcher first so it stops posting once the loop is shutting down.
        _resizeWatcher.Dispose();

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
    private static IResizeWatcher CreatePlatformResizeWatcher(ITerminalSizeSource sizeSource)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return new PosixResizeWatcher(sizeSource);

        if (OperatingSystem.IsWindows())
            return new WindowsResizeWatcher();

        // Unknown platform; §4 will have thrown before this, but satisfy the compiler.
        return new NoopResizeWatcher();
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
