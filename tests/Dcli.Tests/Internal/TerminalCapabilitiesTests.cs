using Dcli.Internal;
using Xunit;

namespace Dcli.Tests.Internal;

/// <summary>
/// §13.4 (Chunk B) — Capability detection tests.
/// All cases inject <see cref="CapabilityInputs"/> directly so no real environment
/// variables are read.
/// </summary>
public sealed class TerminalCapabilitiesTests
{
    // ── Shared VT-capable base inputs ─────────────────────────────────────────
    private static CapabilityInputs Base(
        string? colorTerm = null,
        string? termProgram = null,
        string? term = "xterm-256color",
        string? lang = null,
        bool isWindows = false) =>
        new CapabilityInputs
        {
            TermVariable = term,
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
            ColorTermVariable = colorTerm,
            TermProgramVariable = termProgram,
            LangVariable = lang,
            IsWindows = isWindows,
        };

    // ── 13.4-cap-a  Truecolor detection ──────────────────────────────────────

    [Fact]
    public void TruecolorDetectedWhenColortermIsTruecolor()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(colorTerm: "truecolor"));
        Assert.True(caps.HasTruecolor);
    }

    [Fact]
    public void TruecolorDetectedWhenColortermIsTruecolorUpperCase()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(colorTerm: "TRUECOLOR"));
        Assert.True(caps.HasTruecolor);
    }

    [Fact]
    public void TruecolorDetectedWhenColortermIs24bit()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(colorTerm: "24bit"));
        Assert.True(caps.HasTruecolor);
    }

    [Fact]
    public void TruecolorDetectedWhenColortermIs24bitUpperCase()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(colorTerm: "24BIT"));
        Assert.True(caps.HasTruecolor);
    }

    [Fact]
    public void TruecolorNotDetectedWhenColortermIsUnset()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(colorTerm: null));
        Assert.False(caps.HasTruecolor);
    }

    [Fact]
    public void TruecolorNotDetectedWhenColortermIs256color()
    {
        // COLORTERM=256color is sometimes set but does NOT indicate truecolor.
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(colorTerm: "256color"));
        Assert.False(caps.HasTruecolor);
    }

    // ── 13.4-cap-b  Synchronized-output detection ────────────────────────────

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsWezTerm()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "WezTerm"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsWezTermLowerCase()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "wezterm"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsITerm()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "iTerm.app"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsVscode()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "vscode"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsWarpTerminal()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "WarpTerminal"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsGhostty()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "ghostty"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsKitty()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "kitty"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermProgramIsFoot()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(termProgram: "foot"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermIsXtermKitty()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(term: "xterm-kitty"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermStartsWithAlacrittyDirect()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(term: "alacritty-direct"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenTermIsAlacrittyExact()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(term: "alacritty"));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputDetectedWhenIsWindows()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(
            Base(termProgram: null, term: "xterm-256color", isWindows: true));
        Assert.True(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputNotDetectedWhenTerminalIsUnknown()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(
            Base(termProgram: null, term: "xterm-256color", isWindows: false));
        Assert.False(caps.HasSynchronizedOutput);
    }

    [Fact]
    public void SyncOutputNotDetectedWhenNothingIsSet()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(
            Base(termProgram: null, term: null, isWindows: false));
        Assert.False(caps.HasSynchronizedOutput);
    }

    // ── 13.4-cap-c  East-Asian ambiguous-wide detection ──────────────────────

    [Fact]
    public void AmbiguousWideDetectedWhenLangIsJapanese()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(lang: "ja_JP.UTF-8"));
        Assert.True(caps.TreatAmbiguousAsWide);
    }

    [Fact]
    public void AmbiguousWideDetectedWhenLangIsChinese()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(lang: "zh_CN.UTF-8"));
        Assert.True(caps.TreatAmbiguousAsWide);
    }

    [Fact]
    public void AmbiguousWideDetectedWhenLangIsKorean()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(lang: "ko_KR.UTF-8"));
        Assert.True(caps.TreatAmbiguousAsWide);
    }

    [Fact]
    public void AmbiguousWideNotDetectedWhenLangIsEnglish()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(lang: "en_US.UTF-8"));
        Assert.False(caps.TreatAmbiguousAsWide);
    }

    [Fact]
    public void AmbiguousWideDetectedWhenLangIsNullAndLcAllIsJapanese()
    {
        // LangVariable carries the LC_ALL fallback when LANG is null.
        CapabilityInputs inputs = new CapabilityInputs
        {
            TermVariable = "xterm-256color",
            IsStdinTty = true,
            IsStdoutTty = true,
            IsSupportedPlatform = true,
            LangVariable = "ja_JP.UTF-8",
        };
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(inputs);
        Assert.True(caps.TreatAmbiguousAsWide);
    }

    [Fact]
    public void AmbiguousWideNotDetectedWhenBothLangAndLcAllAreNull()
    {
        TerminalCapabilities caps = TerminalCapabilityDetector.DetectCapabilities(Base(lang: null));
        Assert.False(caps.TreatAmbiguousAsWide);
    }

    // ── Default value ─────────────────────────────────────────────────────────

    [Fact]
    public void DefaultCapabilitiesHaveAllFlagsFalse()
    {
        TerminalCapabilities def = TerminalCapabilities.Default;
        Assert.False(def.HasTruecolor);
        Assert.False(def.HasSynchronizedOutput);
        Assert.False(def.TreatAmbiguousAsWide);
    }
}
