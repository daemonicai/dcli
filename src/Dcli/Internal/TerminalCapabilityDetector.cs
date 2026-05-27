using System.Runtime.InteropServices;

namespace Dcli.Internal;

/// <summary>
/// Checks whether the current environment supports VT terminal sequences.
/// <para>
/// Scope: this type performs the VT-or-fail gate only. Truecolor, synchronized-output, and
/// unicode-width capability detection belong to §13.3 and are out of scope here.
/// </para>
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
/// Inputs used by <see cref="TerminalCapabilityDetector.EnsureVtCapable"/> for capability
/// detection. Inject an explicit instance in tests to avoid reading the real process environment.
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

    /// <summary>
    /// Reads capability inputs from the real process environment. Used on the production path.
    /// </summary>
    internal static CapabilityInputs FromEnvironment()
    {
        bool supported = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                      || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                      || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        return new CapabilityInputs
        {
            TermVariable = Environment.GetEnvironmentVariable("TERM"),
            IsStdinTty = !Console.IsInputRedirected,
            IsStdoutTty = !Console.IsOutputRedirected,
            IsSupportedPlatform = supported,
        };
    }
}
