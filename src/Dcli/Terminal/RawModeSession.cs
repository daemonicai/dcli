using Dcli.Terminal.Posix;
using Dcli.Terminal.Windows;

namespace Dcli.Terminal;

/// <summary>
/// Factory that creates the platform-appropriate <see cref="IRawModeSession"/>.
/// </summary>
internal static class RawModeSession
{
    /// <summary>
    /// Runs capability detection and enters raw mode, returning an <see cref="IRawModeSession"/>
    /// that restores the terminal on dispose.
    /// </summary>
    /// <param name="inputs">
    /// Override capability inputs (for tests). Pass <see langword="null"/> to read from the
    /// real environment.
    /// </param>
    /// <returns>A live raw-mode session.</returns>
    /// <exception cref="TerminalNotSupportedException">
    /// The terminal does not meet the minimum VT requirements.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">
    /// The current platform is not supported.
    /// </exception>
    internal static IRawModeSession Enter(CapabilityInputs? inputs = null)
    {
        TerminalCapabilityDetector.EnsureVtCapable(inputs);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return PosixRawModeSession.Enter();

        if (OperatingSystem.IsWindows())
            return WindowsRawModeSession.Enter();

        throw new PlatformNotSupportedException("dcli only supports Linux, macOS, and Windows.");
    }
}
