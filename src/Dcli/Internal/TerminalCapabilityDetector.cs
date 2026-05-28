using System.Runtime.InteropServices;

namespace Dcli.Internal;

/// <summary>
/// Checks whether the current environment supports VT terminal sequences, and detects
/// optional capabilities (truecolor, synchronized output, East-Asian ambiguous width).
/// </summary>
internal static class TerminalCapabilityDetector
{
    /// <summary>
    /// Throws <see cref="TerminalNotSupportedException"/> if the environment does not meet the
    /// minimum requirements for dcli to operate.
    /// </summary>
    /// <param name="inputs">
    /// Environment inputs used for detection. Pass <see langword="null"/> to read from the
    /// real process environment (production path). Pass an explicit value in tests.
    /// </param>
    /// <exception cref="TerminalNotSupportedException">
    /// The process is not attached to a tty, or the terminal does not support VT sequences.
    /// </exception>
    internal static void EnsureVtCapable(CapabilityInputs? inputs = null)
    {
        CapabilityInputs env = inputs ?? CapabilityInputs.FromEnvironment();
        Check(env);
    }

    /// <summary>
    /// Detects optional terminal capabilities from the environment.
    /// </summary>
    /// <param name="inputs">
    /// Environment inputs. Pass <see langword="null"/> to read from the real process environment
    /// (production path). Pass an explicit value in tests.
    /// </param>
    /// <returns>A <see cref="TerminalCapabilities"/> value reflecting what the terminal advertises.</returns>
    internal static TerminalCapabilities DetectCapabilities(CapabilityInputs? inputs = null)
    {
        CapabilityInputs env = inputs ?? CapabilityInputs.FromEnvironment();

        bool hasTruecolor = env.ColorTermVariable is not null &&
            (env.ColorTermVariable.Equals("truecolor", StringComparison.OrdinalIgnoreCase) ||
             env.ColorTermVariable.Equals("24bit", StringComparison.OrdinalIgnoreCase));

        // Terminals that don't recognise ESC[?2026h…l ignore it harmlessly (already emitted
        // unconditionally by VtFrameRenderer). False negatives here are harmless.
        string termProg = env.TermProgramVariable ?? string.Empty;
        string term = env.TermVariable ?? string.Empty;
        bool hasSyncOutput =
            termProg.Equals("WezTerm", StringComparison.OrdinalIgnoreCase) ||
            termProg.Equals("iTerm.app", StringComparison.OrdinalIgnoreCase) ||
            termProg.Equals("vscode", StringComparison.OrdinalIgnoreCase) ||
            termProg.Equals("WarpTerminal", StringComparison.OrdinalIgnoreCase) ||
            termProg.Equals("ghostty", StringComparison.OrdinalIgnoreCase) ||
            termProg.Equals("kitty", StringComparison.OrdinalIgnoreCase) ||
            termProg.Equals("foot", StringComparison.OrdinalIgnoreCase) ||
            term.StartsWith("alacritty", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("xterm-kitty", StringComparison.OrdinalIgnoreCase) ||
            env.IsWindows;

        string? lang = env.LangVariable;
        bool ambiguousWide = lang is not null &&
            (lang.Contains("zh", StringComparison.OrdinalIgnoreCase) ||
             lang.Contains("ja", StringComparison.OrdinalIgnoreCase) ||
             lang.Contains("ko", StringComparison.OrdinalIgnoreCase));

        return new TerminalCapabilities
        {
            HasTruecolor = hasTruecolor,
            HasSynchronizedOutput = hasSyncOutput,
            TreatAmbiguousAsWide = ambiguousWide,
        };
    }

    private static void Check(CapabilityInputs env)
    {
        if (!env.IsSupportedPlatform)
        {
            throw new TerminalNotSupportedException(
                "dcli requires Linux, macOS, or Windows. Current platform is not supported.");
        }

        if (!env.IsStdinTty)
        {
            throw new TerminalNotSupportedException(
                "dcli requires stdin to be an interactive terminal (tty). " +
                "stdin is currently redirected to a file or pipe.");
        }

        if (!env.IsStdoutTty)
        {
            throw new TerminalNotSupportedException(
                "dcli requires stdout to be an interactive terminal (tty). " +
                "stdout is currently redirected to a file or pipe.");
        }

        string term = env.TermVariable ?? string.Empty;
        if (string.IsNullOrEmpty(term) || string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase))
        {
            string detail = string.IsNullOrEmpty(term)
                ? "TERM is not set"
                : $"TERM={term}";
            throw new TerminalNotSupportedException(
                $"dcli requires a VT-capable terminal. {detail} indicates a terminal that does " +
                "not support VT control sequences.");
        }
    }
}

/// <summary>
/// Inputs used by <see cref="TerminalCapabilityDetector"/> for VT and capability detection.
/// Inject an explicit instance in tests to avoid reading the real process environment.
/// </summary>
internal sealed class CapabilityInputs
{
    /// <summary>The value of the <c>TERM</c> environment variable, or <see langword="null"/>.</summary>
    public string? TermVariable { get; init; }

    /// <summary>Whether stdin is an interactive tty.</summary>
    public bool IsStdinTty { get; init; }

    /// <summary>Whether stdout is an interactive tty.</summary>
    public bool IsStdoutTty { get; init; }

    /// <summary>Whether the current platform is supported (Linux, macOS, or Windows).</summary>
    public bool IsSupportedPlatform { get; init; }

    /// <summary>The value of the <c>COLORTERM</c> environment variable, or <see langword="null"/>.</summary>
    public string? ColorTermVariable { get; init; }

    /// <summary>The value of the <c>TERM_PROGRAM</c> environment variable, or <see langword="null"/>.</summary>
    public string? TermProgramVariable { get; init; }

    /// <summary>
    /// The locale string to use for East-Asian ambiguous-width detection.
    /// Set to <c>LANG</c> when available; falls back to <c>LC_ALL</c> when <c>LANG</c> is null.
    /// </summary>
    public string? LangVariable { get; init; }

    /// <summary>Whether the process is running on Windows.</summary>
    public bool IsWindows { get; init; }

    /// <summary>
    /// Reads capability inputs from the real process environment. Used on the production path.
    /// </summary>
    internal static CapabilityInputs FromEnvironment()
    {
        bool supported = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                      || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                      || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        string? lang = Environment.GetEnvironmentVariable("LANG")
                    ?? Environment.GetEnvironmentVariable("LC_ALL");

        return new CapabilityInputs
        {
            TermVariable = Environment.GetEnvironmentVariable("TERM"),
            IsStdinTty = !Console.IsInputRedirected,
            IsStdoutTty = !Console.IsOutputRedirected,
            IsSupportedPlatform = supported,
            ColorTermVariable = Environment.GetEnvironmentVariable("COLORTERM"),
            TermProgramVariable = Environment.GetEnvironmentVariable("TERM_PROGRAM"),
            LangVariable = lang,
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
        };
    }
}
