using System.Collections.Concurrent;
using Dcli.Internal.Input;

namespace Dcli.Testing.Internal;

/// <summary>
/// A thread-safe byte queue that implements <see cref="IInputByteSource"/> for headless tests.
/// </summary>
/// <remarks>
/// <para>
/// Respects the VMIN=0/VTIME=1 contract: when the queue is empty <see cref="Read"/> returns 0
/// (timed-out, no data) rather than blocking or returning -1.
/// </para>
/// <para>
/// <see cref="Enqueue"/> may be called from the test thread while <see cref="Read"/> runs on
/// the <c>dcli-input-reader</c> thread; <see cref="ConcurrentQueue{T}"/> provides the required
/// thread-safety.
/// </para>
/// </remarks>
internal sealed class ScriptedInputByteSource : IInputByteSource
{
    private readonly ConcurrentQueue<byte> _queue = new();

    /// <summary>
    /// Enqueues bytes to be delivered by the next <see cref="Read"/> call.
    /// Safe to call from any thread.
    /// </summary>
    internal void Enqueue(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
            _queue.Enqueue(b);
    }

    /// <inheritdoc/>
    public int Read(Span<byte> buffer)
    {
        int count = 0;
        while (count < buffer.Length && _queue.TryDequeue(out byte b))
            buffer[count++] = b;
        return count; // 0 means "no data this poll" — not EOF
    }
}
