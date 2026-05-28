namespace Dcli;

/// <summary>
/// Styling attributes for a <see cref="Segment"/>: optional foreground color, optional background
/// color, and zero or more <see cref="Format"/> flags.
/// <para>
/// <c>default(Style)</c> (equivalently <c>new Style()</c>) means: inherit terminal default colors
/// and no formatting attributes — identical to <c>new Style(null, null, Format.None)</c>.
/// </para>
/// </summary>
/// <param name="Foreground">
/// Foreground color, or <see langword="null"/> to use the terminal default.
/// </param>
/// <param name="Background">
/// Background color, or <see langword="null"/> to use the terminal default.
/// </param>
/// <param name="Format">
/// Text-formatting attributes to apply. Defaults to <see cref="Format.None"/>.
/// </param>
public readonly record struct Style(
    Color? Foreground = null,
    Color? Background = null,
    Format Format = Format.None);
