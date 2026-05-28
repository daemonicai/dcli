using System.Threading.Channels;
using Dcli.Internal.Input;
using Xunit;

namespace Dcli.Tests.Terminal.Input;

/// <summary>
/// Headless tests for §6: InputReader thread, IInputByteSource edge, and Ctrl+C handling.
/// All tests run without a real tty by injecting a scripted <see cref="FakeInputByteSource"/>.
/// </summary>
public sealed class InputReaderTests
{
    // ─── Fake byte source ─────────────────────────────────────────────────────

    /// <summary>
    /// A scripted implementation of <see cref="IInputByteSource"/> for use in tests.
    /// Chunks of bytes are enqueued; a 0-byte chunk represents a timed-read timeout.
    /// After all chunks are consumed each subsequent Read returns 0 (timeout) indefinitely.
    /// </summary>
    private sealed class FakeInputByteSource : IInputByteSource
    {
        private readonly Queue<byte[]> _chunks = new();

        internal void Enqueue(params byte[] chunk) => _chunks.Enqueue(chunk);

        /// <summary>Enqueues a 0-byte chunk to simulate a VMIN=0/VTIME=1 timeout.</summary>
        internal void EnqueueTimeout() => _chunks.Enqueue([]);

        public int Read(Span<byte> buffer)
        {
            if (_chunks.Count == 0)
                return 0; // simulate idle timeout

            byte[] chunk = _chunks.Dequeue();
            if (chunk.Length == 0)
                return 0; // explicit timeout

            int count = Math.Min(chunk.Length, buffer.Length);
            chunk.AsSpan(0, count).CopyTo(buffer);
            return count;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a reader fed by <paramref name="source"/>, collects events until
    /// <paramref name="expectedEventCount"/> are received or <paramref name="timeoutMs"/>
    /// elapses, then stops the reader. Returns all collected events in order.
    /// </summary>
    private static List<InputEvent> RunReader(
        FakeInputByteSource source,
        int expectedEventCount,
        int timeoutMs = 2000)
    {
        Channel<InputEvent> channel = Channel.CreateUnbounded<InputEvent>();
        using InputReader reader = new(source, channel.Writer);

        List<InputEvent> events = new();
        using CancellationTokenSource cts = new(timeoutMs);
        try
        {
            while (events.Count < expectedEventCount)
            {
                if (channel.Reader.TryRead(out InputEvent? ev))
                    events.Add(ev);
                else if (cts.IsCancellationRequested)
                    break;
                else
                    Thread.Sleep(1);
            }
        }
        catch (OperationCanceledException) { }

        return events;
    }

    private static KeyEvent AssertKey(InputEvent ev) =>
        Assert.IsType<KeyEvent>(ev);

    // ─── §6.1 — basic byte-chunk → events ────────────────────────────────────

    [Fact]
    public void SingleAsciiByteProducesKeyEvent()
    {
        FakeInputByteSource source = new();
        source.Enqueue((byte)'a');
        source.EnqueueTimeout(); // allow loop to deliver

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.FromRune(new System.Text.Rune('a')), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    [Fact]
    public void MultipleAsciiChunkProducesEventsInOrder()
    {
        FakeInputByteSource source = new();
        source.Enqueue((byte)'h', (byte)'i');
        source.EnqueueTimeout();

        List<InputEvent> events = RunReader(source, expectedEventCount: 2);

        Assert.Equal(2, events.Count);
        Assert.Equal(KeyCode.FromRune(new System.Text.Rune('h')), AssertKey(events[0]).Code);
        Assert.Equal(KeyCode.FromRune(new System.Text.Rune('i')), AssertKey(events[1]).Code);
    }

    // ─── Multi-byte UTF-8 split across reads ─────────────────────────────────

    [Fact]
    public void MultiByteUtf8SplitAcrossReadsProducesOneEvent()
    {
        // U+00E9 LATIN SMALL LETTER E WITH ACUTE = 0xC3 0xA9 (2 bytes)
        FakeInputByteSource source = new();
        source.Enqueue(0xC3);           // lead byte arrives in first read
        source.Enqueue(0xA9);           // continuation byte in second read
        source.EnqueueTimeout();

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.FromRune(new System.Text.Rune('é')), ke.Code);
    }

    [Fact]
    public void CsiSequenceSplitAcrossReadsProducesOneArrowEvent()
    {
        // Up-arrow: ESC [ A split across two reads
        FakeInputByteSource source = new();
        source.Enqueue(0x1B, (byte)'['); // ESC [ in first read
        source.Enqueue((byte)'A');        // A in second read
        source.EnqueueTimeout();

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    // ─── ESC-disambiguation via Flush ─────────────────────────────────────────

    [Fact]
    public void BareEscFollowedByTimeoutProducesEscapeEvent()
    {
        // Lone ESC + timeout → Named(Escape)
        FakeInputByteSource source = new();
        source.Enqueue(0x1B);
        source.EnqueueTimeout(); // timeout triggers Flush because HasPendingEscape

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.Named(NamedKey.Escape), ke.Code);
    }

    [Fact]
    public void EscFollowedImmediatelyByBracketAProducesUpNotEscape()
    {
        // ESC arrives, then [ A arrives before any timeout — must be Up, not Escape.
        FakeInputByteSource source = new();
        source.Enqueue(0x1B);           // ESC
        source.Enqueue((byte)'[', (byte)'A'); // [ A follows immediately (no timeout between)
        source.EnqueueTimeout();

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
    }

    [Fact]
    public void PartialCsiFollowedByTimeoutDoesNotEmitSpuriousEscape()
    {
        // ESC [ is partial CSI (not bare ESC). A timeout must NOT flush it.
        // Then A completes the sequence → one Up event.
        FakeInputByteSource source = new();
        source.Enqueue(0x1B, (byte)'['); // partial CSI
        source.EnqueueTimeout();          // timeout — HasPendingEscape is false (state=CsiParam)
        source.Enqueue((byte)'A');        // completes CSI to Up
        source.EnqueueTimeout();

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        // Exactly one Up event — no spurious Escape
        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.Named(NamedKey.Up), ke.Code);
    }

    [Fact]
    public void PartialUtf8FollowedByTimeoutDoesNotTruncateScalar()
    {
        // U+4E2D CJK UNIFIED IDEOGRAPH 中 = 0xE4 0xB8 0xAD (3-byte UTF-8 sequence).
        // The lead byte arrives in one read, then a 100 ms timeout fires, then the two
        // continuation bytes arrive in the next read.
        // The timeout must NOT flush the partial scalar as a spurious Escape because the
        // parser is in the UTF-8 state, not the bare-ESC state (HasPendingEscape is false).
        // Exactly one KeyEvent carrying Char(中) must be emitted.
        FakeInputByteSource source = new();
        source.Enqueue(0xE4);              // lead byte of 中
        source.EnqueueTimeout();           // simulated VTIME=1 timeout between bytes
        source.Enqueue(0xB8, 0xAD);        // continuation bytes completing 中
        source.EnqueueTimeout();

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.FromRune(new System.Text.Rune('中')), ke.Code);
        Assert.Equal(Modifiers.None, ke.Modifiers);
    }

    // ─── §6.2 — Ctrl+C must not terminate ────────────────────────────────────

    [Fact]
    public void CtrlCByteProducesKeyEventNotTermination()
    {
        // 0x03 = Ctrl+C. ISIG is cleared in raw mode, so it arrives as a byte.
        // The reader must post it and the process must keep running.
        FakeInputByteSource source = new();
        source.Enqueue(0x03);
        source.EnqueueTimeout();

        List<InputEvent> events = RunReader(source, expectedEventCount: 1);

        Assert.Single(events);
        KeyEvent ke = AssertKey(events[0]);
        Assert.Equal(KeyCode.FromRune(new System.Text.Rune('c')), ke.Code);
        Assert.Equal(Modifiers.Ctrl, ke.Modifiers);
        // If we reach here, the process was not terminated — test passes.
    }

    // ─── Clean shutdown ───────────────────────────────────────────────────────

    [Fact]
    public void StopCausesThreadToExitPromptly()
    {
        // Reader with an infinite-idle source (all timeouts).
        // Stop() must return within ~500 ms.
        FakeInputByteSource source = new(); // no enqueued data → all reads return 0

        Channel<InputEvent> channel = Channel.CreateUnbounded<InputEvent>();
        using InputReader reader = new(source, channel.Writer);

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        reader.Stop();
        sw.Stop();

        // The read loop wakes every ~100 ms (VTIME=1). Allow 600 ms to be safe.
        Assert.True(sw.ElapsedMilliseconds < 600,
            $"Stop() took {sw.ElapsedMilliseconds} ms — expected < 600 ms.");
    }

    [Fact]
    public void DisposeCausesThreadToExitPromptly()
    {
        FakeInputByteSource source = new();

        Channel<InputEvent> channel = Channel.CreateUnbounded<InputEvent>();

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        using (InputReader _ = new(source, channel.Writer))
        {
            // Dispose immediately.
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 600,
            $"Dispose() took {sw.ElapsedMilliseconds} ms — expected < 600 ms.");
    }
}
