using System.Threading.Channels;

namespace Dcli.Internal.Input;

/// <summary>
/// Runs a dedicated long-running thread that reads bytes from an <see cref="IInputByteSource"/>,
/// feeds them to a <see cref="VtInputParser"/>, and posts decoded <see cref="InputEvent"/>s to
/// an injected sink.
/// </summary>
/// <remarks>
/// <para>
/// The reader is the <em>single producer</em> of input events for the render loop. It owns no
/// UI state and never writes to stdout.
/// </para>
/// <para>
/// The underlying byte source is assumed to be configured with <c>VMIN=0/VTIME=1</c>
/// semantics (or equivalent): each <see cref="IInputByteSource.Read"/> call returns in at most
/// ~100 ms. A 0-byte return means "timed out, no data" — the loop checks the shutdown flag and
/// continues. This 100 ms cadence is the ESC-disambiguation window: when a bare ESC is the only
/// pending byte at timeout, <see cref="VtInputParser.Flush"/> resolves it to
/// <see cref="NamedKey.Escape"/>. The ~10 idle wake-ups/sec are accepted for v1.
/// </para>
/// <para>
/// The sink is a <see cref="ChannelWriter{T}"/> so §7 (the render loop) can plug in its
/// inbound channel by wrapping the posted events as needed. Tests inject a plain
/// <c>Channel&lt;InputEvent&gt;</c>.
/// </para>
/// <para>
/// Ctrl+C arrives as byte 0x03 (ISIG is cleared in raw mode) and is decoded to
/// <see cref="KeyEvent"/>(<see cref="KeyCode.FromRune"/>('c'), <see cref="Modifiers.Ctrl"/>)
/// like any other key. The library never terminates or interrupts the process on Ctrl+C.
/// </para>
/// </remarks>
internal sealed class InputReader : IDisposable
{
    private readonly IInputByteSource _source;
    private readonly ChannelWriter<InputEvent> _sink;
    private readonly VtInputParser _parser = new();
    private readonly Thread _thread;

    // Set by Stop()/Dispose() to signal the read loop to exit.
    private volatile bool _stopping;

    // Read buffer: 4 KB is more than enough for any single read burst.
    private const int _bufferSize = 4096;

    /// <summary>
    /// Initialises the reader and starts the dedicated input thread.
    /// The thread is a background thread so it does not prevent process exit.
    /// </summary>
    /// <param name="source">The OS-level byte source (real tty fd or test fake).</param>
    /// <param name="sink">
    /// Channel writer to post decoded <see cref="InputEvent"/>s to.
    /// §7 wires this to its inbound message channel.
    /// </param>
    internal InputReader(IInputByteSource source, ChannelWriter<InputEvent> sink)
    {
        _source = source;
        _sink = sink;

        _thread = new Thread(RunLoop)
        {
            Name = "dcli-input-reader",
            IsBackground = true,
        };
        _thread.Start();
    }

    /// <summary>
    /// Signals the read loop to stop and waits for the thread to exit.
    /// Blocks for at most one read-timeout interval (~100 ms) plus a small margin.
    /// </summary>
    internal void Stop()
    {
        _stopping = true;
        // The loop wakes every ~100 ms on a timed-read timeout, so joining with a
        // generous timeout is safe. 500 ms leaves plenty of headroom without hanging.
        // A false return from Join means the byte source is not honouring its VTIME=1
        // timeout contract. In that case the background thread is abandoned: it is marked
        // IsBackground so it will not prevent process exit, and blocking Dispose
        // indefinitely would be worse than leaking an unresponsive read.
        _thread.Join(millisecondsTimeout: 500);
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    // ─── Read loop ───────────────────────────────────────────────────────────

    private void RunLoop()
    {
        // TODO (future): replace the timed-read loop with poll(2)/WaitForMultipleObjects
        // so the thread sleeps with zero CPU when the terminal is idle instead of waking
        // ~10 times/sec. The VMIN=0/VTIME=1 contract is the v1 ESC-disambiguation mechanism.

        byte[] buf = new byte[_bufferSize];
        List<InputEvent> events = new(capacity: 16);

        while (!_stopping)
        {
            int n = _source.Read(buf.AsSpan());

            if (n < 0)
            {
                // Fatal read error — the tty has gone away (e.g. SSH disconnect).
                // Stop the loop; §7/§13 handle process lifecycle.
                break;
            }

            if (n > 0)
            {
                events.Clear();
                _parser.Feed(buf.AsSpan(0, n), events);
                PostEvents(events);
            }
            else
            {
                // 0-byte timed-read timeout. Flush a bare ESC if one is pending.
                // Do NOT flush for partial CSI/UTF-8 — those are sequences split across
                // the 100 ms boundary and will be completed by the next read.
                if (_parser.HasPendingEscape)
                {
                    events.Clear();
                    _parser.Flush(events);
                    PostEvents(events);
                }
            }
        }
    }

    private void PostEvents(List<InputEvent> events)
    {
        foreach (InputEvent ev in events)
        {
            // Decision 10: the inbound channel is unbounded for v1, so TryWrite always
            // succeeds and no keystroke or paste is ever silently dropped. If a future
            // version switches to a bounded channel it must adopt an explicit backpressure
            // strategy (block or coalesce) rather than calling TryWrite and discarding the
            // return value.
            _sink.TryWrite(ev);
        }
    }
}
