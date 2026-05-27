// Dcli.RawModeHarness — manual verification of §4 (raw-mode session and guaranteed restore).
//
// Run this in a real terminal (macOS/Linux). It is NOT an automated test — it exists so a human
// can confirm the four behaviours that cannot be unit-tested without a real tty:
//
//   CHECK 1 — Entry:           after starting, local echo is OFF and input is byte-wise.
//   CHECK 2 — Normal exit:     pressing 'q' exits; the shell is back to normal (echo on, cooked).
//   CHECK 3 — Exception exit:  pressing 'x' throws; the shell is still restored.
//   CHECK 4 — Signal restore:  sending SIGTERM/SIGINT restores the terminal.
//
// Windows: the Windows path compiles but runtime verification of raw-mode entry is deferred to
// §14.1 cross-platform validation (the Windows CI runner has no interactive console). The harness
// will detect that stdout is not a tty and exit gracefully.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dcli;
using Dcli.Internal;
using Dcli.Internal.Posix;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("=== Dcli.RawModeHarness --- Section 4 manual check ===");
Console.WriteLine();
Console.WriteLine("This harness puts the terminal in raw mode and lets you verify:");
Console.WriteLine("  CHECK 1  Echo is OFF: type a few chars -- they should NOT appear.");
Console.WriteLine("           Ctrl+C should NOT kill the process (arrives as bytes 0x03).");
Console.WriteLine("           Arrow keys should NOT move a cursor (arrive as ESC sequences).");
Console.WriteLine();
Console.WriteLine("  CHECK 2  Press 'q' -> normal exit. Verify: shell echo works after exit.");
Console.WriteLine("  CHECK 3  Press 'x' -> simulated exception. Shell should still be restored.");
Console.WriteLine("  CHECK 4  In another terminal, run:");
Console.WriteLine($"             kill -TERM {Environment.ProcessId}");
Console.WriteLine("           Then verify the original shell is back to normal.");
Console.WriteLine();
Console.WriteLine("Press ENTER to enter raw mode...");
Console.ReadLine();

// Verify the terminal is capable before entering raw mode.
try
{
    TerminalCapabilityDetector.EnsureVtCapable();
}
catch (TerminalNotSupportedException ex)
{
    Console.WriteLine($"[SKIP] Terminal not supported: {ex.Message}");
    Console.WriteLine("       This is expected when stdout is not a tty (e.g. CI, pipe).");
    return;
}

Console.WriteLine("[OK] Terminal is VT-capable. Entering raw mode NOW...");
Console.WriteLine();

using var session = RawModeSession.Enter();
using var coord = RestoreCoordinator.Wire(session);

Console.Write("[RAW MODE ACTIVE] Bytes received: ");

int col = "[RAW MODE ACTIVE] Bytes received: ".Length;
bool running = true;

if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
{
    RunPosixLoop();
}
else
{
    Console.Write("\r\n[SKIP] Non-POSIX platform: stdin read loop deferred to §14.1.\r\n");
}

Console.Write("\r\n[DONE] Harness exited normally. Shell should be back to cooked mode.\r\n");

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
void RunPosixLoop()
{
    // Use a direct POSIX read(2) syscall instead of the managed Console stream.
    //
    // With VMIN=0 / VTIME=1 (set by RawModeSession), the kernel returns 0 from read() when the
    // 100ms timeout expires with no bytes available. The managed Stream.ReadByte() path treats
    // that 0-byte return as end-of-stream (returns -1), causing the loop to exit immediately on
    // the first timeout tick. The direct syscall lets us distinguish:
    //   n > 0  → bytes available; process them.
    //   n == 0 → timeout, no data; keep waiting (the entire fix).
    //   n < 0  → error; EINTR means retry, anything else is fatal.
    unsafe
    {
        byte buf = 0;
        while (running)
        {
            nint n = PosixReadInterop.read(0, &buf, 1);

            if (n == 0)
            {
                // Timed out (VMIN=0 / VTIME=1): no data this 100ms window — keep polling.
                continue;
            }

            if (n < 0)
            {
                int err = Marshal.GetLastPInvokeError();
                if (err == PosixReadInterop.EINTR)
                    continue;  // interrupted by a signal; retry

                Console.Write($"\r\n[ERROR] read() returned {n}, errno={err}. Exiting.\r\n");
                break;
            }

            // n == 1: got a byte.
            string desc = buf switch
            {
                0x03 => "[Ctrl+C=0x03]",
                0x1B => "[ESC=0x1B]",
                0x0D => "[CR=0x0D]",
                0x0A => "[LF=0x0A]",
                _ => $"[0x{buf:X2}={(buf >= 32 && buf < 127 ? (char)buf : '?')}]",
            };

            Console.Write(desc);
            col += desc.Length;
            if (col > 70)
            {
                Console.Write("\r\n");
                col = 0;
            }

            if (buf == (byte)'q')
            {
                Console.Write("\r\n[CHECK 2] 'q' pressed -- normal exit follows. Shell should restore.\r\n");
                running = false;
            }
            else if (buf == (byte)'x')
            {
                Console.Write("\r\n[CHECK 3] 'x' pressed -- throwing exception now...\r\n");
                // Dispose (via using) runs before the exception propagates, restoring the terminal.
                throw new InvalidOperationException(
                    "Simulated exception from harness (CHECK 3). Terminal should be restored.");
            }
        }
    }
}
