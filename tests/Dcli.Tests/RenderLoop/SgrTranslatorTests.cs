using System.Text;
using Dcli.Internal;
using Dcli.Internal.RenderLoop;
using Xunit;

namespace Dcli.Tests.RenderLoop;

/// <summary>
/// §13.4 (Chunk B) — SgrTranslator truecolor and 256-indexed downgrade tests.
/// </summary>
public sealed class SgrTranslatorTests
{
    private static SgrTranslator MakeSgr(bool hasTruecolor) =>
        new(new TerminalCapabilities { HasTruecolor = hasTruecolor });

    /// <summary>Emits a foreground SGR and returns only the code portion (between ESC[ and m).</summary>
    private static string FgSgr(SgrTranslator sgr, byte r, byte g, byte b)
    {
        StringBuilder sb = new();
        sgr.AppendOpenSgr(new Style(Foreground: Color.FromRgb(r, g, b)), sb);
        string s = sb.ToString();
        // Strip ESC[ prefix and trailing m.
        return s.Length > 3 ? s[2..^1] : s;
    }

    private static string BgSgr(SgrTranslator sgr, byte r, byte g, byte b)
    {
        StringBuilder sb = new();
        sgr.AppendOpenSgr(new Style(Background: Color.FromRgb(r, g, b)), sb);
        string s = sb.ToString();
        return s.Length > 3 ? s[2..^1] : s;
    }

    // ── 13.4-sgr-a  HasTruecolor=true emits 24-bit sequences ─────────────────

    [Fact]
    public void WithTruecolorRgbForegroundEmits24bitSequence()
    {
        SgrTranslator sgr = MakeSgr(hasTruecolor: true);
        // (255,0,0) → ESC[38;2;255;0;0m
        Assert.Equal("38;2;255;0;0", FgSgr(sgr, 255, 0, 0));
    }

    [Fact]
    public void WithTruecolorArbitraryRgbEmits24bitForeground()
    {
        SgrTranslator sgr = MakeSgr(hasTruecolor: true);
        // (10,20,30) — preserves original VtFrameRendererTests SGR assertion
        Assert.Equal("38;2;10;20;30", FgSgr(sgr, 10, 20, 30));
    }

    // ── 13.4-sgr-b  HasTruecolor=false downgrades to 256-indexed ─────────────

    [Fact]
    public void WithoutTruecolorRedDowngradesToCubeIndex196()
    {
        // (255,0,0) → cube(5,0,0) = 16 + 36*5 = 196
        SgrTranslator sgr = MakeSgr(hasTruecolor: false);
        Assert.Equal("38;5;196", FgSgr(sgr, 255, 0, 0));
    }

    [Fact]
    public void WithoutTruecolorWhiteDowngradesToCubeIndex231()
    {
        // (255,255,255) → cube(5,5,5) = 16 + 180 + 30 + 5 = 231
        SgrTranslator sgr = MakeSgr(hasTruecolor: false);
        Assert.Equal("38;5;231", FgSgr(sgr, 255, 255, 255));
    }

    [Fact]
    public void WithoutTruecolorBlackDowngradesToCubeIndex16()
    {
        // (0,0,0) → cube(0,0,0) = 16; grey ramp dist = 3*64 = 192 → cube wins
        SgrTranslator sgr = MakeSgr(hasTruecolor: false);
        Assert.Equal("38;5;16", FgSgr(sgr, 0, 0, 0));
    }

    [Fact]
    public void WithoutTruecolorMidGreyDowngradesToGreyRamp244()
    {
        // (128,128,128): cube nearest = (135,135,135) → dist = 3*49 = 147
        // grey ramp: level = 12 → grey = 8 + 12*10 = 128 → dist = 0 → grey wins
        // index = 232 + 12 = 244
        SgrTranslator sgr = MakeSgr(hasTruecolor: false);
        Assert.Equal("38;5;244", FgSgr(sgr, 128, 128, 128));
    }

    [Fact]
    public void WithoutTruecolorBlackBackgroundDowngradesToIndex16()
    {
        // Background variant: ESC[48;5;16m
        SgrTranslator sgr = MakeSgr(hasTruecolor: false);
        Assert.Equal("48;5;16", BgSgr(sgr, 0, 0, 0));
    }

    // ── NearestXterm256 pure-function matrix ──────────────────────────────────

    [Theory]
    [InlineData(255, 0, 0, 196)]       // pure red → cube (5,0,0)
    [InlineData(255, 255, 255, 231)]   // white → cube (5,5,5)
    [InlineData(0, 0, 0, 16)]          // black → cube (0,0,0)
    [InlineData(128, 128, 128, 244)]   // mid-grey → grey ramp level 12
    [InlineData(0, 255, 0, 46)]        // pure green → cube (0,5,0) = 16+30 = 46
    [InlineData(0, 0, 255, 21)]        // pure blue → cube (0,0,5) = 16+5 = 21
    public void NearestXterm256MapsKnownColorsToExpectedIndex(byte r, byte g, byte b, int expectedIndex)
    {
        int actual = SgrTranslator.NearestXterm256(r, g, b);
        Assert.Equal(expectedIndex, actual);
    }
}
