// POSIX termios P/Invoke layer.
//
// Separate constant sets and struct layouts are defined per platform because macOS/BSD and Linux
// use incompatible representations:
//
//   macOS (BSD): tcflag_t = ulong (64-bit), speed_t = ulong (64-bit), NCCS = 20
//                VMIN=16, VTIME=17
//                Source: /usr/include/sys/termios.h (XNU / LLVM libc headers, macOS 14+)
//
//   Linux:       tcflag_t = uint (32-bit), speed_t = uint (32-bit), NCCS = 32
//                VMIN=6,  VTIME=5
//                Source: <asm-generic/termbits.h> (Linux kernel headers, identical across arches
//                except SPARC; all arches supported by .NET use the generic values)
//
// P/Invoke targets "libc" which resolves to libSystem.B.dylib on macOS and libc.so.6 on Linux.
// Both export tcgetattr/tcsetattr with the same C prototype; only the struct layout differs.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dcli.Internal.Posix;

// ─── macOS / BSD ─────────────────────────────────────────────────────────────

/// <summary>macOS <c>termios</c> struct (tcflag_t = ulong, NCCS = 20).</summary>
/// <remarks>
/// Layout (verified against /usr/include/sys/termios.h, macOS 14+ / XNU):
///   offset  0: c_iflag  (ulong, 8 bytes)
///   offset  8: c_oflag  (ulong, 8 bytes)
///   offset 16: c_cflag  (ulong, 8 bytes)
///   offset 24: c_lflag  (ulong, 8 bytes)
///   offset 32: c_cc[0..19] (20 × byte)   — NO c_line field; macOS/BSD omits it
///   offset 52: padding  (4 bytes, implicit for 8-byte alignment of speed fields)
///   offset 56: c_ispeed (speed_t = ulong, 8 bytes)
///   offset 64: c_ospeed (speed_t = ulong, 8 bytes)
///   total: 72 bytes
/// The Linux glibc/musl struct genuinely has c_line after c_lflag; macOS/BSD does not.
/// </remarks>
[SupportedOSPlatform("macos")]
[StructLayout(LayoutKind.Sequential)]
internal struct TermiosMac
{
    // c_iflag / c_oflag / c_cflag / c_lflag are ulong on macOS (tcflag_t = unsigned long = 8 bytes)
    public ulong c_iflag;
    public ulong c_oflag;
    public ulong c_cflag;
    public ulong c_lflag;

    // c_cc[NCCS] where NCCS=20 on macOS — no c_line gap before this array
    public byte cc0, cc1, cc2, cc3, cc4;
    public byte cc5, cc6, cc7, cc8, cc9;
    public byte cc10, cc11, cc12, cc13, cc14;
    public byte cc15, cc16, cc17, cc18, cc19;

    // speed_t = unsigned long on macOS (8 bytes each); do not simplify to uint.
    public ulong c_ispeed;
    public ulong c_ospeed;

    // VMIN=16, VTIME=17 on macOS
    internal byte VMIN { readonly get => cc16; set => cc16 = value; }
    internal byte VTIME { readonly get => cc17; set => cc17 = value; }
}

/// <summary>macOS termios flag constants.</summary>
[SupportedOSPlatform("macos")]
internal static class TermiosMacFlags
{
    // c_iflag bits (Source: macOS /usr/include/sys/termios.h)
    internal const ulong ICRNL = 0x0100UL;  // map CR to NL on input
    internal const ulong IXON = 0x0200UL;  // enable output flow control

    // c_lflag bits (Source: macOS /usr/include/sys/termios.h)
    internal const ulong ISIG = 0x0080UL;  // enable signals (INTR, QUIT, SUSP)
    internal const ulong ICANON = 0x0100UL;  // canonical mode
    internal const ulong ECHO = 0x0008UL;  // echo input characters
    internal const ulong ECHOE = 0x0002UL;  // echo ERASE as BS-SP-BS
    internal const ulong ECHOK = 0x0004UL;  // echo KILL
    internal const ulong ECHONL = 0x0010UL;  // echo NL
    internal const ulong IEXTEN = 0x0400UL;  // enable extended processing (e.g. DISCARD, LNEXT)

    internal const int TCSAFLUSH = 2;
    internal const int STDIN_FILENO = 0;
}

// ─── Linux ───────────────────────────────────────────────────────────────────

/// <summary>Linux <c>termios</c> struct (tcflag_t = uint, NCCS = 32).</summary>
[SupportedOSPlatform("linux")]
[StructLayout(LayoutKind.Sequential)]
internal struct TermiosLinux
{
    public uint c_iflag;
    public uint c_oflag;
    public uint c_cflag;
    public uint c_lflag;

    public byte c_line;  // line discipline

    // c_cc[NCCS] where NCCS=32 on Linux
    public byte cc0, cc1, cc2, cc3, cc4, cc5, cc6, cc7;
    public byte cc8, cc9, cc10, cc11, cc12, cc13, cc14, cc15;
    public byte cc16, cc17, cc18, cc19, cc20, cc21, cc22, cc23;
    public byte cc24, cc25, cc26, cc27, cc28, cc29, cc30, cc31;

    public uint c_ispeed;
    public uint c_ospeed;

    // VMIN=6, VTIME=5 on Linux (asm-generic/termbits.h)
    internal byte VMIN { readonly get => cc6; set => cc6 = value; }
    internal byte VTIME { readonly get => cc5; set => cc5 = value; }
}

/// <summary>Linux termios flag constants.</summary>
[SupportedOSPlatform("linux")]
internal static class TermiosLinuxFlags
{
    // c_iflag bits (Source: asm-generic/termbits.h)
    internal const uint ICRNL = 0x0100U;  // map CR to NL on input
    internal const uint IXON = 0x0400U;  // enable output flow control

    // c_lflag bits (Source: asm-generic/termbits.h)
    internal const uint ISIG = 0x0001U;  // enable signals
    internal const uint ICANON = 0x0002U;  // canonical mode
    internal const uint ECHO = 0x0008U;  // echo input characters
    internal const uint ECHOE = 0x0010U;  // echo erase as BS-SP-BS
    internal const uint ECHOK = 0x0020U;  // echo kill
    internal const uint ECHONL = 0x0040U;  // echo NL
    internal const uint IEXTEN = 0x8000U;  // extended processing

    internal const int TCSAFLUSH = 2;
    internal const int STDIN_FILENO = 0;
}

// ─── P/Invoke declarations ───────────────────────────────────────────────────

[SupportedOSPlatform("linux")]
internal static partial class TermiosLinuxInterop
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    internal static partial int tcgetattr(int fd, out TermiosLinux termios);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    internal static partial int tcsetattr(int fd, int optionalActions, in TermiosLinux termios);
}

[SupportedOSPlatform("macos")]
internal static partial class TermiosMacInterop
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    internal static partial int tcgetattr(int fd, out TermiosMac termios);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    internal static partial int tcsetattr(int fd, int optionalActions, in TermiosMac termios);
}

/// <summary>
/// POSIX <c>read(2)</c> — shared across macOS and Linux; the prototype is identical.
/// Used by the raw-mode harness to read stdin without going through the managed stream layer,
/// which misinterprets a 0-byte timeout return (VMIN=0/VTIME&gt;0) as end-of-stream.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static partial class PosixReadInterop
{
    internal const int EINTR = 4;

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    internal static unsafe partial nint read(int fd, byte* buf, nuint count);
}
