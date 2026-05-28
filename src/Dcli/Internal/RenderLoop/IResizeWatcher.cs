namespace Dcli.Internal.RenderLoop;

/// <summary>
/// An OS edge that notifies callers when the terminal window size changes.
/// </summary>
/// <remarks>
/// <para>
/// The callback supplied to <see cref="Start"/> may fire on a POSIX signal-handler thread.
/// It must only perform thread-safe work — in production, posting a
/// <see cref="Dcli.ResizeEvent"/> to the loop's inbound channel via
/// <c>loop.InputWriter.TryWrite</c> (an unbounded channel write is safe from any thread).
/// </para>
/// <para>
/// Callers must dispose the watcher to deregister the OS hook and ensure no callback fires
/// after disposal.
/// </para>
/// </remarks>
internal interface IResizeWatcher : IDisposable
{
    /// <summary>
    /// Arms the OS resize hook and registers <paramref name="onResize"/> as the callback.
    /// The callback receives <c>(columns, rows)</c> for the new terminal size.
    /// </summary>
    /// <param name="onResize">
    /// Called on every terminal resize. May be invoked on a signal-handler thread; must be
    /// thread-safe and must not block.
    /// </param>
    void Start(Action<int, int> onResize);
}

/// <summary>
/// A no-op <see cref="IResizeWatcher"/> for headless tests and unknown-platform fallback.
/// Never fires the callback.
/// </summary>
internal sealed class NoopResizeWatcher : IResizeWatcher
{
    /// <inheritdoc/>
    public void Start(Action<int, int> onResize) { }

    /// <inheritdoc/>
    public void Dispose() { }
}
