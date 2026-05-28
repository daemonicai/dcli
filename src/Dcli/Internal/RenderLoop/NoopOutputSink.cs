namespace Dcli.Internal.RenderLoop;

/// <summary>
/// A no-op <see cref="IOutputSink"/> used as the real stdout placeholder until §8
/// implements actual escape-sequence painting.
/// </summary>
/// <remarks>
/// Swapped out in §8 for the real frame renderer. Writing nothing is safe — it prevents
/// garbage being emitted to a real terminal while the painting layer is not yet built.
/// </remarks>
internal sealed class NoopOutputSink : IOutputSink
{
    /// <inheritdoc/>
    public void Paint(RenderModel model)
    {
        // §8 replaces this with real VT frame painting.
    }

    /// <inheritdoc/>
    public void EmitRestoreSequence()
    {
        // No-op: this sink writes nothing to stdout.
    }
}
