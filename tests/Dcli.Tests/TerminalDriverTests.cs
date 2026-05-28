using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dcli.Internal;
using Dcli.Internal.Posix;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Automated tests for Section 4: terminal driver — capability detection (4.3) and
/// guaranteed-restore wiring (4.4).
/// <para>
/// Raw-mode entry itself cannot be unit-tested without a real tty. Those behaviours are
/// covered by the manual harness (task 4.5 / samples/Dcli.RawModeHarness).
/// </para>
/// </summary>
public sealed class TerminalDriverTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Termios struct layout guards (platform-independent Marshal.SizeOf checks)
    //
    // These tests run on any OS — Marshal.SizeOf measures the managed struct layout,
    // which must match the native struct layout on the corresponding platform.
    //
    // Expected sizes (verified against SDK headers):
    //   TermiosMac  (macOS): 4×ulong(32) + 20×byte(20) + 4 pad + 2×ulong(16) = 72 bytes
    //   TermiosLinux (Linux): 4×uint(16) + byte(1) + 32×byte(32) + 3 pad + 2×uint(8) = 60 bytes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [SupportedOSPlatform("macos")]
    public void TermiosMacSizeIs72Bytes()
    {
        // macOS struct termios: 4 × tcflag_t(ulong,8) + c_cc[20] + implicit pad(4) + 2 × speed_t(ulong,8)
        // Guards against a spurious c_line byte (Linux-only) being added, which would corrupt VMIN/VTIME offsets.
        Assert.Equal(72, Marshal.SizeOf<TermiosMac>());
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void TermiosLinuxSizeIs60Bytes()
    {
        // Linux struct termios: 4 × tcflag_t(uint,4) + c_line(1) + c_cc[32] + implicit pad(3) + 2 × speed_t(uint,4)
        Assert.Equal(60, Marshal.SizeOf<TermiosLinux>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4.3 Capability detection
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CapabilityDetectorVtCapableDoesNotThrow()
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = "xterm-256color",
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
        };

        // Should not throw.
        TerminalCapabilityDetector.EnsureVtCapable(inputs);
    }

    [Fact]
    public void CapabilityDetectorDumbTermThrows()
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = "dumb",
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
        };

        TerminalNotSupportedException ex =
            Assert.Throws<TerminalNotSupportedException>(() => TerminalCapabilityDetector.EnsureVtCapable(inputs));

        Assert.Contains("TERM=dumb", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapabilityDetectorEmptyTermThrows()
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = "",
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
        };

        TerminalNotSupportedException ex =
            Assert.Throws<TerminalNotSupportedException>(() => TerminalCapabilityDetector.EnsureVtCapable(inputs));

        Assert.Contains("TERM is not set", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapabilityDetectorNullTermThrows()
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = null,
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
        };

        TerminalNotSupportedException ex =
            Assert.Throws<TerminalNotSupportedException>(() => TerminalCapabilityDetector.EnsureVtCapable(inputs));

        Assert.Contains("TERM is not set", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapabilityDetectorStdinNotTtyThrows()
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = "xterm-256color",
            IsStdinTty = false,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
        };

        TerminalNotSupportedException ex =
            Assert.Throws<TerminalNotSupportedException>(() => TerminalCapabilityDetector.EnsureVtCapable(inputs));

        Assert.Contains("stdin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapabilityDetectorStdoutNotTtyThrows()
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = "xterm-256color",
            IsStdinTty = true,
            IsStdoutTty = false,
            IsSupportedPlatform = true,
        };

        TerminalNotSupportedException ex =
            Assert.Throws<TerminalNotSupportedException>(() => TerminalCapabilityDetector.EnsureVtCapable(inputs));

        Assert.Contains("stdout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapabilityDetectorUnsupportedPlatformThrows()
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = "xterm-256color",
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = false,
        };

        Assert.Throws<TerminalNotSupportedException>(() => TerminalCapabilityDetector.EnsureVtCapable(inputs));
    }

    [Theory]
    [InlineData("xterm")]
    [InlineData("xterm-256color")]
    [InlineData("xterm-kitty")]
    [InlineData("screen")]
    [InlineData("tmux-256color")]
    [InlineData("rxvt-unicode")]
    public void CapabilityDetectorKnownVtTermsDoNotThrow(string term)
    {
        CapabilityInputs inputs = new()
        {
            TermVariable = term,
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
        };

        TerminalCapabilityDetector.EnsureVtCapable(inputs); // must not throw
    }

    [Fact]
    public void TerminalNotSupportedExceptionIsPublic()
    {
        // The exception is documented as public so consumers can catch it.
        Assert.True(typeof(TerminalNotSupportedException).IsPublic);
    }

    [Fact]
    public void TerminalNotSupportedExceptionMessageConstructorSetsMessage()
    {
        TerminalNotSupportedException ex = new("test message");
        Assert.Equal("test message", ex.Message);
    }

    [Fact]
    public void TerminalNotSupportedExceptionInnerExceptionConstructorSetsInner()
    {
        InvalidOperationException inner = new("inner");
        TerminalNotSupportedException ex = new("outer", inner);
        Assert.Same(inner, ex.InnerException);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4.4 Guaranteed restore — wiring logic (via RecordingRawModeSession)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DisposeRestoresSession()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        coord.Dispose();

        Assert.Equal(1, fake.RestoreCallCount);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        coord.Dispose();
        coord.Dispose();
        coord.Dispose();

        // RestoreCoordinator.Dispose is idempotent via CAS; only one Restore reaches the session.
        Assert.Equal(1, fake.RestoreCallCount);
    }

    [Fact]
    public void SessionRestoreRecordsEachCall()
    {
        // RecordingRawModeSession.Restore records every call (it is not itself idempotent).
        using RecordingRawModeSession fake = new();
        fake.Restore();
        fake.Restore();
        fake.Restore();
        Assert.Equal(3, fake.RestoreCallCount);
    }

    [Fact]
    public void SimulateTerminateSignalRestoresSession()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        coord.SimulateTerminateSignal();

        Assert.Equal(1, fake.RestoreCallCount);
    }

    [Fact]
    public void SimulateTerminateSignalAfterDisposeCallsRestoreAgain()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        coord.Dispose(); // Restore called once here.
        coord.SimulateTerminateSignal(); // Session's Restore called a second time (handler always delegates).

        Assert.Equal(2, fake.RestoreCallCount);
    }

    [Fact]
    public void SimulateContinueSignalReappliesRawMode()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        coord.SimulateContinueSignal();

        Assert.Equal(1, fake.ReapplyCallCount);
        Assert.Equal(0, fake.RestoreCallCount);
    }

    [Fact]
    public void OnProcessExitRestoresSession()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        coord.OnProcessExit(sender: null, EventArgs.Empty);

        Assert.Equal(1, fake.RestoreCallCount);
    }

    [Fact]
    public void SessionDisposeCallsRestore()
    {
        using RecordingRawModeSession fake = new();
        ((IDisposable)fake).Dispose();
        Assert.Equal(1, fake.RestoreCallCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SetHaltAction wiring: SimulateTerminateSignal must invoke the halt action
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a halt action is registered via SetHaltAction, SimulateTerminateSignal must invoke
    /// it before calling session.Restore. This is the unit-level contract for the wiring that
    /// Terminal.StartCore establishes: coordinator.SetHaltAction(loop.Dispose).
    /// </summary>
    [Fact]
    public void SimulateTerminateSignalInvokesSetHaltActionThenRestores()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        int haltCallCount = 0;
        coord.SetHaltAction(() => Interlocked.Increment(ref haltCallCount));

        coord.SimulateTerminateSignal();

        // The halt action must have been invoked.
        Assert.Equal(1, Volatile.Read(ref haltCallCount));
        // Session restore must also have been called.
        Assert.Equal(1, fake.RestoreCallCount);
    }

    /// <summary>
    /// Removing SetHaltAction wiring (by never calling SetHaltAction) means the halt action is
    /// not invoked — but session.Restore still is. This is the regression baseline: if Terminal
    /// never calls coordinator.SetHaltAction(loop.Dispose), the loop does not stop on signal.
    /// </summary>
    [Fact]
    public void SimulateTerminateSignalWithNoHaltActionStillRestoresSession()
    {
        using RecordingRawModeSession fake = new();
        using RestoreCoordinator coord = RestoreCoordinator.Wire(fake);

        // No SetHaltAction call — mimics the broken wiring scenario.
        coord.SimulateTerminateSignal();

        Assert.Equal(1, fake.RestoreCallCount);
    }
}
