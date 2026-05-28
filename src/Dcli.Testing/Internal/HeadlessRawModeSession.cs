using Dcli.Internal;

namespace Dcli.Testing.Internal;

/// <summary>
/// A no-op <see cref="IRawModeSession"/> for headless tests.
/// Raw-mode entry and restoration are skipped; lifecycle methods are safe no-ops.
/// </summary>
internal sealed class HeadlessRawModeSession : IRawModeSession
{
    /// <inheritdoc/>
    public void Restore() { }

    /// <inheritdoc/>
    public void Reapply() { }

    /// <inheritdoc/>
    public void Dispose() { }
}
