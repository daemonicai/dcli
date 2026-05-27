namespace Dcli;

/// <summary>
/// Text formatting attributes that can be applied to a <see cref="Segment"/>.
/// Values are power-of-two so they can be combined with bitwise OR.
/// </summary>
[Flags]
public enum Format
{
    /// <summary>No formatting attributes.</summary>
    None = 0,

    /// <summary>Bold / increased intensity.</summary>
    Bold = 1,

    /// <summary>Italic.</summary>
    Italic = 2,

    /// <summary>Underline.</summary>
    Underline = 4,

    /// <summary>Dim / decreased intensity (faint).</summary>
    Dim = 8,

    /// <summary>Reverse video: swap foreground and background colors.</summary>
    Reverse = 16,

    /// <summary>Strikethrough (crossed-out text).</summary>
    Strikethrough = 32,
}
