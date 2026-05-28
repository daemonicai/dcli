using System.Runtime.Versioning;
using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Windows;

/// <summary>
/// Windows implementation of <see cref="IResizeWatcher"/>.
/// </summary>
/// <remarks>
/// Windows runtime delivery of resize is deferred to §14.1 (consistent with the §6.1 Windows
/// input stub). This compile-only stub keeps the cross-platform build green; the size source
/// returns 80×24 until §14.1 wires <c>WINDOW_BUFFER_SIZE_EVENT</c> polling.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsResizeWatcher : IResizeWatcher
{
    /// <inheritdoc/>
    public void Start(Action<int, int> onResize) { }

    /// <inheritdoc/>
    public void Dispose() { }
}
