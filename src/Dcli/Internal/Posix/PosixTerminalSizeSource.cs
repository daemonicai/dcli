using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Posix;

/// <summary>
/// Queries the terminal size via <c>ioctl(TIOCGWINSZ)</c>.
/// Returns an 80×24 fallback when the query fails (e.g. in CI without a tty).
/// </summary>
/// <remarks>
/// This performs only the initial one-shot size read used by §7.3's snapshot.
/// SIGWINCH-driven live resize belongs to §13 and is out of scope here.
/// </remarks>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class PosixTerminalSizeSource : ITerminalSizeSource
{
    // Fallback dimensions used when ioctl fails.
    private const int _fallbackColumns = 80;
    private const int _fallbackRows = 24;

    /// <inheritdoc/>
    public (int Columns, int Rows) GetSize()
    {
        WinSize ws = default;
        int ret = Ioctl.GetWinSize(1 /* STDOUT_FILENO */, ref ws);

        if (ret == 0 && ws.Columns > 0 && ws.Rows > 0)
            return (ws.Columns, ws.Rows);

        return (_fallbackColumns, _fallbackRows);
    }
}

/// <summary>
/// Layout of the <c>winsize</c> struct from <c>&lt;sys/ioctl.h&gt;</c>.
/// Same layout on macOS and Linux.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WinSize
{
    public ushort Rows;
    public ushort Columns;
    public ushort XPixel;
    public ushort YPixel;
}

/// <summary>
/// <c>ioctl(2)</c> P/Invoke used to query terminal window size.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static partial class Ioctl
{
    // TIOCGWINSZ constant:
    //   macOS:  0x40087468  (from /usr/include/sys/ioctl.h)
    //   Linux:  0x00005413  (from asm-generic/ioctls.h)
    private static readonly uint _tiocgWinSz =
        OperatingSystem.IsMacOS() ? 0x40087468u : 0x00005413u;

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int ioctl_winsize(int fd, uint request, WinSize* ws);

    /// <summary>Queries the terminal size for file descriptor <paramref name="fd"/>.</summary>
    internal static unsafe int GetWinSize(int fd, ref WinSize ws)
    {
        fixed (WinSize* ptr = &ws)
        {
            return ioctl_winsize(fd, _tiocgWinSz, ptr);
        }
    }
}
