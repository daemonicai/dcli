using System.Text;
using Dcli.Internal;
using Dcli.Internal.RenderLoop;
using Dcli.Testing.Internal;

namespace Dcli.Testing;

/// <summary>
/// A <see cref="ITerminal"/> harness that runs the real render engine, input parser, and
/// fixed-region composer against in-memory OS edges — no tty required.
/// </summary>
/// <remarks>
/// <para>
/// Obtain an instance via <see cref="StartAsync"/>. All scripting methods
/// (<see cref="Feed"/>, <see cref="SendKey"/>, <see cref="Type"/>, <see cref="Paste"/>,
/// <see cref="Resize"/>) post events through the same channels the production path uses;
/// they never mutate model state directly.
/// </para>
/// <para>
/// Call <see cref="SettleAsync"/> after posting input to drain all pending work and wait for
/// exactly one coalesced frame before asserting state.
/// </para>
/// </remarks>
public sealed class HeadlessTerminal : IAsyncDisposable
{
    private readonly Terminal _terminal;
    private readonly ScriptedInputByteSource _byteSource;
    private readonly ScriptedResizeWatcher _resizeWatcher;
    private readonly FixedSizeSource _sizeSource;
    // Held here so the CA2000 analyzer sees ownership; Terminal also disposes it via IRawModeSession.
    private readonly HeadlessRawModeSession _session;

    private HeadlessTerminal(
        Terminal terminal,
        HeadlessRawModeSession session,
        ScriptedInputByteSource byteSource,
        ScriptedResizeWatcher resizeWatcher,
        FixedSizeSource sizeSource,
        VirtualClock clock,
        InMemoryOutputSink sink)
    {
        _terminal = terminal;
        _session = session;
        _byteSource = byteSource;
        _resizeWatcher = resizeWatcher;
        _sizeSource = sizeSource;
        Clock = clock;
        Sink = sink;
    }

    /// <summary>
    /// The real <see cref="ITerminal"/> facade. Use this to drive scrollback, input, status,
    /// autocomplete, and dialogs exactly as production consumers do.
    /// </summary>
    public ITerminal Terminal => _terminal;

    /// <summary>
    /// The virtual clock controlling the render loop's cadence.
    /// Call <see cref="VirtualClock.Advance"/> to trigger throttled paints in cadence tests.
    /// </summary>
    public VirtualClock Clock { get; }

    /// <summary>
    /// The in-memory output sink. Exposes <see cref="InMemoryOutputSink.LastModel"/> and
    /// <see cref="InMemoryOutputSink.PaintCount"/> for frame-level assertions.
    /// </summary>
    /// <remarks>
    /// Internal: consumers assert through <see cref="SettleAsync"/>, <see cref="Snapshot"/>,
    /// and the public <see cref="Terminal"/> surface. Direct sink access is for advanced
    /// assertions only.
    /// </remarks>
    internal InMemoryOutputSink Sink { get; }

    /// <summary>
    /// Returns an immutable snapshot of the most recently painted frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always call <see cref="SettleAsync"/> before reading this property to ensure the loop
    /// has painted the latest state.
    /// </para>
    /// <para>
    /// If no paint has occurred yet (before the first <see cref="SettleAsync"/>), returns an
    /// empty snapshot: size <c>(0, 0)</c>, empty row lists, no caret, no overlay. Does not throw.
    /// </para>
    /// <para>
    /// The snapshot is immutable — captured at the moment of property access. Subsequent
    /// paints do not mutate previously captured snapshots.
    /// </para>
    /// </remarks>
    public FrameSnapshot Snapshot
    {
        get
        {
            RenderModel? model = Sink.LastModel;
            return model is null ? FrameSnapshot.Empty : FrameSnapshot.Capture(model);
        }
    }

    // ── Factory ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and starts a headless terminal with the given options.
    /// </summary>
    /// <param name="options">
    /// Configuration; pass <see langword="null"/> or <c>new HeadlessTerminalOptions()</c> for defaults.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the start-up sequence.</param>
    /// <returns>A live <see cref="HeadlessTerminal"/> handle.</returns>
    public static Task<HeadlessTerminal> StartAsync(
        HeadlessTerminalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new HeadlessTerminalOptions();
        cancellationToken.ThrowIfCancellationRequested();

        HeadlessRawModeSession session = new();
        RestoreCoordinator? coordinator = null;
        bool success = false;
        try
        {
            coordinator = RestoreCoordinator.Wire(session);

            ScriptedInputByteSource byteSource = new();
            ScriptedResizeWatcher resizeWatcher = new();
            FixedSizeSource sizeSource = new(options.InitialColumns, options.InitialRows);
            InMemoryOutputSink sink = new();
            VirtualClock clock = options.Clock ?? new VirtualClock(TimeSpan.Zero);

            Dcli.Terminal dcliTerminal = Dcli.Terminal.StartCore(
                session,
                coordinator,
                resizeWatcher,
                byteSource,
                clock,
                sink,
                sizeSource,
                options.MinFrameInterval,
                options.MaxFixedHeight);

            // session and coordinator ownership transfers to harness (and via harness to Terminal).
            HeadlessTerminal harness = new(dcliTerminal, session, byteSource, resizeWatcher, sizeSource, clock, sink);
            success = true;
            return Task.FromResult(harness);
        }
        finally
        {
            // Dispose only on failure; on success, harness owns session and coordinator via Terminal.
            if (!success)
            {
                coordinator?.Dispose();
                session.Dispose();
            }
        }
    }

    // ── Scripting methods ────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues raw bytes into the scripted input source.
    /// The real VT input parser will decode them on the input-reader thread.
    /// Call <see cref="SettleAsync"/> afterwards to wait for the resulting events to reach the loop.
    /// </summary>
    /// <param name="bytes">The raw bytes to feed through the parser.</param>
    public void Feed(ReadOnlySpan<byte> bytes) => _byteSource.Enqueue(bytes);

    /// <summary>
    /// Posts a synthesised key event directly to the render loop's inbound channel,
    /// bypassing the VT parser. The event is processed on the loop thread.
    /// </summary>
    /// <param name="key">The key event to inject.</param>
    public void SendKey(KeyEvent key) => _terminal.Loop.InputWriter.TryWrite(key);

    /// <summary>
    /// Posts one <see cref="KeyEvent"/> per rune in <paramref name="text"/> to the render loop.
    /// Each character is synthesised as <c>KeyCode.FromRune(rune)</c> with
    /// <see cref="Modifiers.None"/>, exactly as if the user typed the text character-by-character.
    /// </summary>
    /// <remarks>
    /// For named keys (Enter, Tab, arrows, etc.) prefer <see cref="SendKey"/>.
    /// <c>Type</c> emits <see cref="KeyCode.FromRune"/> (<c>KeyCodeKind.Char</c>)
    /// events only and cannot produce named-key events.
    /// </remarks>
    /// <param name="text">The text to type.</param>
    public void Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (Rune rune in text.EnumerateRunes())
            _terminal.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(rune), Modifiers.None));
    }

    /// <summary>
    /// Posts a single <see cref="PasteEvent"/> to the render loop as if the user had used
    /// bracketed-paste mode to paste <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The pasted text.</param>
    public void Paste(string text) => _terminal.Loop.InputWriter.TryWrite(new PasteEvent(text));

    /// <summary>
    /// Updates the reported terminal size and fires the scripted resize watcher, which posts
    /// a <see cref="ResizeEvent"/> to the render loop's inbound channel — the same path that
    /// POSIX SIGWINCH uses in production.
    /// </summary>
    /// <param name="columns">New terminal width in columns.</param>
    /// <param name="rows">New terminal height in rows.</param>
    public void Resize(int columns, int rows)
    {
        _sizeSource.SetSize(columns, rows);
        _resizeWatcher.Fire(columns, rows);
    }

    // ── Settle ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Drains all currently-pending inbound work and waits for exactly one coalesced frame
    /// (if the model is dirty). Completes deterministically without advancing wall-clock time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settlement is fully deterministic for event-level scripting — <see cref="SendKey"/>,
    /// <see cref="Type"/>, <see cref="Paste"/>, and <see cref="Resize"/> post directly to the
    /// render loop's inbound channel and are visible after a single <c>SettleAsync</c>.
    /// </para>
    /// <para>
    /// <see cref="Feed"/> enqueues raw bytes that the independent InputReader thread decodes
    /// asynchronously. Callers may need to call <c>SettleAsync</c> twice after a large feed:
    /// once to let the InputReader drain the byte queue, and once to let the resulting
    /// <see cref="InputEvent"/>s reach the render loop.
    /// </para>
    /// <para>
    /// Delegates to <see cref="LoopEngine.SettleAsync"/> on the underlying loop engine.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public Task SettleAsync(CancellationToken cancellationToken = default) =>
        _terminal.Loop.SettleAsync(cancellationToken);

    // ── Dispose ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _terminal.DisposeAsync();
}
