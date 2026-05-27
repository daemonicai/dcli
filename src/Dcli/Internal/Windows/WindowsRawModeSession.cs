using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dcli.Internal.Windows;

/// <summary>
/// Windows raw-mode session. Saves the current console input/output modes, applies raw + VT mode,
/// and restores on <see cref="Dispose"/> or <see cref="Restore"/>.
/// Thread-safe and idempotent via lock-free CAS (same as the POSIX implementation).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRawModeSession : IRawModeSession
{
    private int _restoreFlag;

    private readonly nint _stdinHandle;
    private readonly nint _stdoutHandle;
    private readonly uint _savedInputMode;
    private readonly uint _savedOutputMode;

    private WindowsRawModeSession(nint stdinHandle, nint stdoutHandle, uint savedInput, uint savedOutput)
    {
        _stdinHandle = stdinHandle;
        _stdoutHandle = stdoutHandle;
        _savedInputMode = savedInput;
        _savedOutputMode = savedOutput;
    }

    /// <summary>
    /// Captures the current console modes and enters raw + VT mode.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A console API call failed.
    /// </exception>
    [SupportedOSPlatform("windows")]
    internal static WindowsRawModeSession Enter()
    {
        nint stdinHandle = ConsoleMode.GetStdHandle(ConsoleMode.STD_INPUT_HANDLE);
        nint stdoutHandle = ConsoleMode.GetStdHandle(ConsoleMode.STD_OUTPUT_HANDLE);

        if (stdinHandle == ConsoleMode.INVALID_HANDLE_VALUE || stdoutHandle == ConsoleMode.INVALID_HANDLE_VALUE)
            throw new InvalidOperationException($"GetStdHandle failed (error={Marshal.GetLastPInvokeError()}).");

        if (!ConsoleMode.GetConsoleMode(stdinHandle, out uint savedInput))
            throw new InvalidOperationException($"GetConsoleMode (stdin) failed (error={Marshal.GetLastPInvokeError()}).");

        if (!ConsoleMode.GetConsoleMode(stdoutHandle, out uint savedOutput))
            throw new InvalidOperationException($"GetConsoleMode (stdout) failed (error={Marshal.GetLastPInvokeError()}).");

        // Raw input: clear cooked/echo/signal processing; enable VT input.
        uint rawInput = savedInput
                      & ~(ConsoleMode.ENABLE_LINE_INPUT | ConsoleMode.ENABLE_ECHO_INPUT | ConsoleMode.ENABLE_PROCESSED_INPUT)
                      | ConsoleMode.ENABLE_VIRTUAL_TERMINAL_INPUT;

        if (!ConsoleMode.SetConsoleMode(stdinHandle, rawInput))
            throw new InvalidOperationException($"SetConsoleMode (stdin raw) failed (error={Marshal.GetLastPInvokeError()}).");

        // VT output: ensure the console interprets VT sequences we emit.
        uint rawOutput = savedOutput | ConsoleMode.ENABLE_VIRTUAL_TERMINAL_PROCESSING;

        if (!ConsoleMode.SetConsoleMode(stdoutHandle, rawOutput))
        {
            // Roll back input change before throwing.
            ConsoleMode.SetConsoleMode(stdinHandle, savedInput);
            throw new InvalidOperationException($"SetConsoleMode (stdout VT) failed (error={Marshal.GetLastPInvokeError()}).");
        }

        return new WindowsRawModeSession(stdinHandle, stdoutHandle, savedInput, savedOutput);
    }

    /// <inheritdoc/>
    public void Restore()
    {
        if (Interlocked.Exchange(ref _restoreFlag, 1) != 0)
            return;

        // Best-effort: ignore errors; we may be in ProcessExit.
        ConsoleMode.SetConsoleMode(_stdinHandle, _savedInputMode);
        ConsoleMode.SetConsoleMode(_stdoutHandle, _savedOutputMode);
    }

    /// <inheritdoc/>
    public void Reapply()
    {
        // Re-enter raw mode after resume. Only relevant on POSIX (SIGCONT); on Windows there is no
        // equivalent suspend/resume mechanism that would cooked the console, so this is a no-op.
        // It is required by the interface contract and safe to call.
    }

    /// <inheritdoc/>
    public void Dispose() => Restore();
}
