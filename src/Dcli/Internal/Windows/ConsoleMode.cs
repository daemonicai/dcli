// Windows Console Mode P/Invoke.
//
// Constants sourced from:
//   https://learn.microsoft.com/en-us/windows/console/setconsolemode
//   (Windows Console API documentation, retrieved 2025)
//
// Input mode flags (applied to stdin handle):
//   ENABLE_VIRTUAL_TERMINAL_INPUT (0x0200) — causes the console to emit VT input sequences
//   ENABLE_LINE_INPUT             (0x0002) — cooked/line-buffered input (clear this)
//   ENABLE_ECHO_INPUT             (0x0004) — echo typed characters (clear this)
//   ENABLE_PROCESSED_INPUT        (0x0001) — process CTRL+C as signal (clear this)
//
// Output mode flags (applied to stdout handle):
//   ENABLE_VIRTUAL_TERMINAL_PROCESSING (0x0004) — interpret VT output sequences (set this)

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dcli.Internal.Windows;

[SupportedOSPlatform("windows")]
internal static partial class ConsoleMode
{
    // Input mode flags
    internal const uint ENABLE_PROCESSED_INPUT = 0x0001U;
    internal const uint ENABLE_LINE_INPUT = 0x0002U;
    internal const uint ENABLE_ECHO_INPUT = 0x0004U;
    internal const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200U;

    // Output mode flags
    internal const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004U;

    // Standard handle identifiers
    internal const int STD_INPUT_HANDLE = -10;
    internal const int STD_OUTPUT_HANDLE = -11;

    // Invalid handle sentinel
    internal static readonly nint INVALID_HANDLE_VALUE = new(-1);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GetStdHandle(int nStdHandle);
}
