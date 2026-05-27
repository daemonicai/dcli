using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dcli.Terminal.Posix;

/// <summary>
/// POSIX raw-mode session. Captures the current <c>termios</c> state, applies raw mode,
/// and restores the original state on <see cref="Dispose"/> or <see cref="Restore"/>.
/// Thread-safe and idempotent: restoration via <see cref="Restore"/> is a lock-free CAS so
/// concurrent calls from Dispose, a signal handler, and ProcessExit are safe.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class PosixRawModeSession : IRawModeSession
{
    // 0 = active, 1 = restored
    private int _restoreFlag;

    // Only one of these is populated, selected at construction time by platform.
    private readonly TermiosMac? _savedMac;
    private readonly TermiosLinux? _savedLinux;

    private PosixRawModeSession(TermiosMac saved)
    {
        _savedMac = saved;
        _savedLinux = null;
    }

    private PosixRawModeSession(TermiosLinux saved)
    {
        _savedMac = null;
        _savedLinux = saved;
    }

    /// <summary>
    /// Captures the current termios state and enters raw mode.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The underlying <c>tcgetattr</c> or <c>tcsetattr</c> syscall failed.
    /// </exception>
    internal static PosixRawModeSession Enter()
    {
        if (OperatingSystem.IsMacOS())
            return EnterMac();
        if (OperatingSystem.IsLinux())
            return EnterLinux();

        throw new PlatformNotSupportedException("PosixRawModeSession is only supported on macOS and Linux.");
    }

    [SupportedOSPlatform("macos")]
    private static PosixRawModeSession EnterMac()
    {
        int rc = TermiosMacInterop.tcgetattr(TermiosMacFlags.STDIN_FILENO, out TermiosMac original);
        if (rc != 0)
            throw new InvalidOperationException($"tcgetattr failed (errno={Marshal.GetLastPInvokeError()}).");

        TermiosMac raw = original;

        // Clear input flags: stop translating CR→NL (ICRNL) and output flow control (IXON).
        raw.c_iflag &= ~(TermiosMacFlags.ICRNL | TermiosMacFlags.IXON);

        // Clear local flags: no canonical mode, no echo, no signals (ISIG), no extended (IEXTEN).
        raw.c_lflag &= ~(TermiosMacFlags.ICANON | TermiosMacFlags.ECHO | TermiosMacFlags.ECHOE
                       | TermiosMacFlags.ECHOK | TermiosMacFlags.ECHONL | TermiosMacFlags.ISIG
                       | TermiosMacFlags.IEXTEN);

        // Non-blocking timed reads: return immediately if no bytes, or after VTIME tenths-of-seconds.
        raw.VMIN = 0;
        raw.VTIME = 1;  // 100 ms timeout per read

        rc = TermiosMacInterop.tcsetattr(TermiosMacFlags.STDIN_FILENO, TermiosMacFlags.TCSAFLUSH, in raw);
        if (rc != 0)
            throw new InvalidOperationException($"tcsetattr failed (errno={Marshal.GetLastPInvokeError()}).");

        return new PosixRawModeSession(original);
    }

    [SupportedOSPlatform("linux")]
    private static PosixRawModeSession EnterLinux()
    {
        int rc = TermiosLinuxInterop.tcgetattr(TermiosLinuxFlags.STDIN_FILENO, out TermiosLinux original);
        if (rc != 0)
            throw new InvalidOperationException($"tcgetattr failed (errno={Marshal.GetLastPInvokeError()}).");

        TermiosLinux raw = original;

        raw.c_iflag &= ~(TermiosLinuxFlags.ICRNL | TermiosLinuxFlags.IXON);
        raw.c_lflag &= ~(TermiosLinuxFlags.ICANON | TermiosLinuxFlags.ECHO | TermiosLinuxFlags.ECHOE
                       | TermiosLinuxFlags.ECHOK | TermiosLinuxFlags.ECHONL | TermiosLinuxFlags.ISIG
                       | TermiosLinuxFlags.IEXTEN);

        raw.VMIN = 0;
        raw.VTIME = 1;

        rc = TermiosLinuxInterop.tcsetattr(TermiosLinuxFlags.STDIN_FILENO, TermiosLinuxFlags.TCSAFLUSH, in raw);
        if (rc != 0)
            throw new InvalidOperationException($"tcsetattr failed (errno={Marshal.GetLastPInvokeError()}).");

        return new PosixRawModeSession(original);
    }

    /// <inheritdoc/>
    public void Restore()
    {
        // CAS: only the first caller proceeds; subsequent calls are no-ops.
        if (Interlocked.Exchange(ref _restoreFlag, 1) != 0)
            return;

        if (OperatingSystem.IsMacOS() && _savedMac.HasValue)
            RestoreMac(_savedMac.Value);
        else if (OperatingSystem.IsLinux() && _savedLinux.HasValue)
            RestoreLinux(_savedLinux.Value);
    }

    [SupportedOSPlatform("macos")]
    private static void RestoreMac(TermiosMac saved)
    {
        // Best-effort: we are possibly in a signal handler or finalizer; ignore errors.
        TermiosMacInterop.tcsetattr(TermiosMacFlags.STDIN_FILENO, TermiosMacFlags.TCSAFLUSH, in saved);
    }

    [SupportedOSPlatform("linux")]
    private static void RestoreLinux(TermiosLinux saved)
    {
        TermiosLinuxInterop.tcsetattr(TermiosLinuxFlags.STDIN_FILENO, TermiosLinuxFlags.TCSAFLUSH, in saved);
    }

    /// <inheritdoc/>
    public void Reapply()
    {
        // Re-enter raw mode after SIGCONT (resume from suspend may have left tty cooked).
        // dcli does not own suspend: ISIG is cleared so SIGTSTP is not sent by keypress, and no
        // SIGTSTP handler is installed. Reapply() exists for an externally-driven kill -STOP/-CONT.
        // Only re-apply if we have not yet been restored.
        if (Volatile.Read(ref _restoreFlag) != 0)
            return;

        if (OperatingSystem.IsMacOS())
            ReapplyMac();
        else if (OperatingSystem.IsLinux())
            ReapplyLinux();
    }

    [SupportedOSPlatform("macos")]
    private static void ReapplyMac()
    {
        if (TermiosMacInterop.tcgetattr(TermiosMacFlags.STDIN_FILENO, out TermiosMac current) != 0)
            return;

        current.c_iflag &= ~(TermiosMacFlags.ICRNL | TermiosMacFlags.IXON);
        current.c_lflag &= ~(TermiosMacFlags.ICANON | TermiosMacFlags.ECHO | TermiosMacFlags.ECHOE
                           | TermiosMacFlags.ECHOK | TermiosMacFlags.ECHONL | TermiosMacFlags.ISIG
                           | TermiosMacFlags.IEXTEN);
        current.VMIN = 0;
        current.VTIME = 1;
        TermiosMacInterop.tcsetattr(TermiosMacFlags.STDIN_FILENO, TermiosMacFlags.TCSAFLUSH, in current);
    }

    [SupportedOSPlatform("linux")]
    private static void ReapplyLinux()
    {
        if (TermiosLinuxInterop.tcgetattr(TermiosLinuxFlags.STDIN_FILENO, out TermiosLinux current) != 0)
            return;

        current.c_iflag &= ~(TermiosLinuxFlags.ICRNL | TermiosLinuxFlags.IXON);
        current.c_lflag &= ~(TermiosLinuxFlags.ICANON | TermiosLinuxFlags.ECHO | TermiosLinuxFlags.ECHOE
                           | TermiosLinuxFlags.ECHOK | TermiosLinuxFlags.ECHONL | TermiosLinuxFlags.ISIG
                           | TermiosLinuxFlags.IEXTEN);
        current.VMIN = 0;
        current.VTIME = 1;
        TermiosLinuxInterop.tcsetattr(TermiosLinuxFlags.STDIN_FILENO, TermiosLinuxFlags.TCSAFLUSH, in current);
    }

    /// <inheritdoc/>
    public void Dispose() => Restore();
}
