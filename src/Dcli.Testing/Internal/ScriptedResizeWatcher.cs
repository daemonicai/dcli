using Dcli.Internal.RenderLoop;

namespace Dcli.Testing.Internal;

/// <summary>
/// A controllable <see cref="IResizeWatcher"/> for headless tests.
/// Stores the callback registered via <see cref="Start"/> and exposes <see cref="Fire"/>
/// to simulate a terminal resize event.
/// </summary>
internal sealed class ScriptedResizeWatcher : IResizeWatcher
{
    private volatile Action<int, int>? _callback;

    /// <inheritdoc/>
    public void Start(Action<int, int> onResize) => _callback = onResize;

    /// <summary>
    /// Simulates a terminal resize by invoking the registered callback.
    /// Safe to call from any thread; no-op before <see cref="Start"/> is called.
    /// </summary>
    internal void Fire(int columns, int rows) => _callback?.Invoke(columns, rows);

    /// <inheritdoc/>
    /// <remarks>
    /// No-op. The callback is retained so <see cref="Fire"/> can still be invoked after the
    /// <see cref="Terminal"/> disposes the watcher during its own disposal. Any <see cref="Fire"/>
    /// call after dispose is a silent no-op in practice because the loop's inbound channel is
    /// already closed and the posted resize event is silently dropped.
    /// </remarks>
    public void Dispose() { }
}
