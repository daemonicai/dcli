using Dcli.Internal.RenderLoop;

namespace Dcli.Testing.Internal;

/// <summary>
/// An in-memory <see cref="IOutputSink"/> for headless tests.
/// Captures the most-recently painted <see cref="RenderModel"/> and counts paint calls.
/// </summary>
internal sealed class InMemoryOutputSink : IOutputSink
{
    private volatile RenderModel? _lastModel;
    private int _paintCount;

    /// <summary>The most-recently painted model, or <see langword="null"/> before the first paint.</summary>
    internal RenderModel? LastModel => _lastModel;

    /// <summary>Total number of <see cref="Paint"/> calls received.</summary>
    internal int PaintCount => Volatile.Read(ref _paintCount);

    /// <inheritdoc/>
    public void Paint(RenderModel model)
    {
        _lastModel = model;
        Interlocked.Increment(ref _paintCount);
    }

    /// <inheritdoc/>
    /// <remarks>No-op: headless tests do not write to a real terminal.</remarks>
    public void EmitRestoreSequence() { }
}
