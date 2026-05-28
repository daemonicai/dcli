using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Posix;

/// <summary>
/// POSIX implementation of <see cref="IResizeWatcher"/>: registers for <c>SIGWINCH</c> via
/// <see cref="PosixSignalRegistration"/> and delivers resize events to the caller-supplied callback.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class PosixResizeWatcher : IResizeWatcher
{
    private readonly ITerminalSizeSource _sizeSource;
    private PosixSignalRegistration? _registration;
    private int _disposed;

    internal PosixResizeWatcher(ITerminalSizeSource sizeSource)
    {
        ArgumentNullException.ThrowIfNull(sizeSource);
        _sizeSource = sizeSource;
    }

    /// <inheritdoc/>
    public void Start(Action<int, int> onResize)
    {
        ArgumentNullException.ThrowIfNull(onResize);

        _registration = PosixSignalRegistration.Create(PosixSignal.SIGWINCH,
            context =>
            {
                // We handle SIGWINCH fully; do not re-raise to the runtime.
                context.Cancel = true;
                (int cols, int rows) = _sizeSource.GetSize();
                onResize(cols, rows);
            });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _registration?.Dispose();
        _registration = null;
    }
}
