using Dcli.Internal;

namespace Dcli;

/// <summary>
/// An immutable run of text with a single <see cref="Dcli.Style"/> applied uniformly across it.
/// <para>
/// Text is <b>sanitized at construction</b>: control bytes (C0, C1, DEL, ESC) are stripped or
/// replaced according to the <c>DCLI_SANITIZE_MODE</c> environment variable (default: strip).
/// Whitespace controls (<c>\t \n \r \v \f</c>) are normalized to a single space <c>U+0020</c>.
/// A string such as <c>"[bold]"</c> is literal content — bracket/markup syntax is never
/// interpreted.
/// </para>
/// <para>
/// To carry verbatim bytes (e.g. renderer-generated SGR sequences) use
/// <see cref="Raw(string, Style)"/>. That path bypasses sanitization; the caller owns terminal
/// integrity.
/// </para>
/// </summary>
public record Segment
{
    /// <summary>
    /// The sanitized text content. Control bytes have been neutralized; the stored text is safe
    /// to emit directly to the terminal.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The style applied uniformly across <see cref="Text"/>.
    /// Defaults to <c>default(Style)</c> (terminal defaults, no formatting attributes).
    /// </summary>
    public Style Style { get; }

    /// <summary>
    /// <see langword="true"/> when this segment was created via <see cref="Raw(string, Style)"/>
    /// and therefore carries unsanitized bytes. <see langword="false"/> for all ordinarily
    /// constructed segments. Participates in value equality so that
    /// <c>Segment.Raw("x") != new Segment("x")</c>.
    /// </summary>
    internal bool IsRaw { get; }

    /// <summary>
    /// Initializes a new <see cref="Segment"/> with sanitized text.
    /// Control bytes are stripped (or replaced) according to <c>DCLI_SANITIZE_MODE</c>;
    /// whitespace controls are normalized to <c>U+0020</c>.
    /// </summary>
    /// <param name="Text">
    /// The text content. May be empty; must not be <see langword="null"/>.
    /// </param>
    /// <param name="Style">
    /// The style to apply. Defaults to <c>default(Style)</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="Text"/> is <see langword="null"/>.
    /// </exception>
    public Segment(string Text, Style Style = default)
    {
        this.Text = TextSanitizer.Apply(Text ?? throw new ArgumentNullException(nameof(Text)));
        this.Style = Style;
        // IsRaw defaults to false
    }

    // Private verbatim constructor — used only by Raw().
    private Segment(string text, Style style, bool raw)
    {
        Text = text;   // already trusted — caller owns terminal integrity
        Style = style;
        IsRaw = raw;
    }

    /// <summary>
    /// Creates a <see cref="Segment"/> that carries <paramref name="text"/> <b>verbatim</b>,
    /// bypassing sanitization. Use only for internally-generated, audited sequences (e.g.
    /// renderer-owned SGR strings). Consumer-supplied strings must never take this path.
    /// </summary>
    /// <param name="text">
    /// The verbatim text. Control bytes reach the terminal as-is; the caller owns terminal
    /// integrity. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="style">
    /// The style to apply. Defaults to <c>default(Style)</c>.
    /// </param>
    /// <returns>
    /// A <see cref="Segment"/> with <see cref="IsRaw"/> set to <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static Segment Raw(string text, Style style = default) =>
        new(text ?? throw new ArgumentNullException(nameof(text)), style, raw: true);
}
