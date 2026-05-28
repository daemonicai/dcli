namespace Dcli;

/// <summary>
/// Discriminated representation of a terminal color.
/// Three representations are supported: the 16 named ANSI colors, a 256-palette index, and 24-bit RGB.
/// Construct via the static factory methods <see cref="Named"/>, <see cref="FromIndex"/>, and <see cref="FromRgb"/>.
/// </summary>
public readonly record struct Color
{
    /// <summary>
    /// The 16 standard ANSI named colors (8 base + 8 bright).
    /// </summary>
    public enum AnsiColor
    {
        /// <summary>ANSI color 0 — black.</summary>
        Black = 0,

        /// <summary>ANSI color 1 — red.</summary>
        Red = 1,

        /// <summary>ANSI color 2 — green.</summary>
        Green = 2,

        /// <summary>ANSI color 3 — yellow.</summary>
        Yellow = 3,

        /// <summary>ANSI color 4 — blue.</summary>
        Blue = 4,

        /// <summary>ANSI color 5 — magenta.</summary>
        Magenta = 5,

        /// <summary>ANSI color 6 — cyan.</summary>
        Cyan = 6,

        /// <summary>ANSI color 7 — white (light gray).</summary>
        White = 7,

        /// <summary>ANSI color 8 — bright black (dark gray).</summary>
        BrightBlack = 8,

        /// <summary>ANSI color 9 — bright red.</summary>
        BrightRed = 9,

        /// <summary>ANSI color 10 — bright green.</summary>
        BrightGreen = 10,

        /// <summary>ANSI color 11 — bright yellow.</summary>
        BrightYellow = 11,

        /// <summary>ANSI color 12 — bright blue.</summary>
        BrightBlue = 12,

        /// <summary>ANSI color 13 — bright magenta.</summary>
        BrightMagenta = 13,

        /// <summary>ANSI color 14 — bright cyan.</summary>
        BrightCyan = 14,

        /// <summary>ANSI color 15 — bright white.</summary>
        BrightWhite = 15,
    }

    /// <summary>Identifies which color representation is stored.</summary>
    public enum ColorKind
    {
        /// <summary>One of the 16 named ANSI colors.</summary>
        Named,

        /// <summary>An index into the 256-color palette.</summary>
        Indexed,

        /// <summary>A 24-bit RGB truecolor value.</summary>
        Rgb,
    }

    private readonly ColorKind _kind;
    private readonly int _value; // Named→(int)AnsiColor, Indexed→byte, Rgb→(r<<16)|(g<<8)|b

    private Color(ColorKind kind, int value)
    {
        _kind = kind;
        _value = value;
    }

    /// <summary>Which representation this color uses.</summary>
    public ColorKind Kind => _kind;

    /// <summary>
    /// Returns a color from the 16 named ANSI palette.
    /// </summary>
    /// <param name="color">The named ANSI color.</param>
    /// <returns>A <see cref="Color"/> with <see cref="ColorKind.Named"/> kind.</returns>
    public static Color Named(AnsiColor color) => new(ColorKind.Named, (int)color);

    /// <summary>
    /// Returns a color from the 256-color terminal palette (indices 0–255).
    /// </summary>
    /// <param name="index">The palette index (0–255).</param>
    /// <returns>A <see cref="Color"/> with <see cref="ColorKind.Indexed"/> kind.</returns>
    public static Color FromIndex(byte index) => new(ColorKind.Indexed, index);

    /// <summary>
    /// Returns a 24-bit truecolor value from red, green, and blue components.
    /// </summary>
    /// <param name="r">Red component (0–255).</param>
    /// <param name="g">Green component (0–255).</param>
    /// <param name="b">Blue component (0–255).</param>
    /// <returns>A <see cref="Color"/> with <see cref="ColorKind.Rgb"/> kind.</returns>
    public static Color FromRgb(byte r, byte g, byte b) => new(ColorKind.Rgb, (r << 16) | (g << 8) | b);

    /// <summary>
    /// For <see cref="ColorKind.Named"/> colors, returns the <see cref="AnsiColor"/> value.
    /// Throws <see cref="InvalidOperationException"/> for other kinds.
    /// </summary>
    public AnsiColor NamedValue =>
        _kind == ColorKind.Named
            ? (AnsiColor)_value
            : throw new InvalidOperationException($"Color kind is {_kind}, not Named.");

    /// <summary>
    /// For <see cref="ColorKind.Indexed"/> colors, returns the palette index (0–255).
    /// Throws <see cref="InvalidOperationException"/> for other kinds.
    /// </summary>
    public byte IndexValue =>
        _kind == ColorKind.Indexed
            ? (byte)_value
            : throw new InvalidOperationException($"Color kind is {_kind}, not Indexed.");

    /// <summary>
    /// For <see cref="ColorKind.Rgb"/> colors, returns the red component (0–255).
    /// Throws <see cref="InvalidOperationException"/> for other kinds.
    /// </summary>
    public byte R =>
        _kind == ColorKind.Rgb
            ? (byte)((_value >> 16) & 0xFF)
            : throw new InvalidOperationException($"Color kind is {_kind}, not Rgb.");

    /// <summary>
    /// For <see cref="ColorKind.Rgb"/> colors, returns the green component (0–255).
    /// Throws <see cref="InvalidOperationException"/> for other kinds.
    /// </summary>
    public byte G =>
        _kind == ColorKind.Rgb
            ? (byte)((_value >> 8) & 0xFF)
            : throw new InvalidOperationException($"Color kind is {_kind}, not Rgb.");

    /// <summary>
    /// For <see cref="ColorKind.Rgb"/> colors, returns the blue component (0–255).
    /// Throws <see cref="InvalidOperationException"/> for other kinds.
    /// </summary>
    public byte B =>
        _kind == ColorKind.Rgb
            ? (byte)(_value & 0xFF)
            : throw new InvalidOperationException($"Color kind is {_kind}, not Rgb.");

    /// <inheritdoc/>
    public override string ToString() => _kind switch
    {
        ColorKind.Named => $"Named({(AnsiColor)_value})",
        ColorKind.Indexed => $"Index({_value})",
        ColorKind.Rgb => $"Rgb({(_value >> 16) & 0xFF},{(_value >> 8) & 0xFF},{_value & 0xFF})",
        _ => "Unknown",
    };
}
