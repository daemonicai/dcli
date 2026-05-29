using System.Collections.ObjectModel;

namespace Dcli;

/// <summary>
/// An immutable, ordered list of <see cref="Segment"/>s that together form one logical line of
/// styled text.
/// <para>
/// <b>Equality:</b> two <see cref="Line"/> instances are equal when they contain the same segments
/// in the same order (structural/sequence equality). The default <c>record</c> equality for
/// <c>IReadOnlyList&lt;Segment&gt;</c> compares by reference; this type overrides
/// <see cref="Equals(Line?)"/> and <see cref="GetHashCode"/> to provide sequence equality instead,
/// so that unit tests and golden-frame comparisons behave as expected.
/// </para>
/// </summary>
/// <param name="Segments">The ordered list of segments. Must not be <see langword="null"/>.</param>
public sealed record Line(IReadOnlyList<Segment> Segments)
{
    /// <summary>
    /// Initializes a new <see cref="Line"/> wrapping a copy of the provided sequence so that
    /// callers cannot mutate the list after construction.
    /// </summary>
    /// <param name="segments">The segments to store.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="segments"/> is <see langword="null"/>.
    /// </exception>
    public Line(IEnumerable<Segment> segments)
        : this(new ReadOnlyCollection<Segment>(
            (segments ?? throw new ArgumentNullException(nameof(segments))).ToList()))
    {
    }

    /// <summary>
    /// Canonical short form for a label-only <see cref="Line"/> — wraps a single plain-text
    /// string in one <see cref="Segment"/> with an optional <see cref="Dcli.Style"/>.
    /// <para>
    /// Use this factory when the entire line is a single unstyled (or uniformly styled) string.
    /// For multi-segment lines use <see cref="LineBuilder"/> instead.
    /// </para>
    /// <para>
    /// <b>No implicit conversion from <see langword="string"/> to <see cref="Line"/> is defined.</b>
    /// This is a deliberate API choice: an implicit conversion would let callers accidentally pass
    /// a bare <see langword="string"/> where a styled <see cref="Line"/> was intended, silently
    /// discarding any styling. Use <c>Line.FromText(s)</c> explicitly at every call site.
    /// </para>
    /// </summary>
    /// <param name="text">The text content. Must not be <see langword="null"/>.</param>
    /// <param name="style">
    /// The style to apply to the segment. When <see langword="null"/> the segment carries
    /// <c>default(Style)</c> (terminal defaults, no formatting attributes).
    /// </param>
    /// <returns>
    /// A <see cref="Line"/> containing a single <see cref="Segment"/> with the given
    /// <paramref name="text"/> and resolved style.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static Line FromText(string text, Style? style = null) =>
        new(new[] { new Segment(text ?? throw new ArgumentNullException(nameof(text)), style ?? default) });

    /// <summary>
    /// Shorthand for a single <b>bold</b> line — equivalent to
    /// <c>Line.FromText(text, new Style(Format: Format.Bold))</c>.
    /// <para>
    /// The text is routed through the ordinary sanitizing <see cref="Segment"/> constructor:
    /// control/escape bytes are stripped (or replaced) exactly as they would be under
    /// <see cref="FromText"/>.
    /// </para>
    /// <para>
    /// Only four single-style shorthands exist on <see cref="Line"/>: <see cref="Bold"/>,
    /// <see cref="Dim"/>, <see cref="Fg"/>, and <see cref="Bg"/>. There is deliberately no
    /// <c>Line.Italic</c>, <c>Line.Underline</c>, <c>Line.Reverse</c>,
    /// <c>Line.Strikethrough</c>, or <c>Line.Raw</c> factory. <see cref="Segment.Raw"/> and
    /// <see cref="LineBuilder"/> remain the only verbatim/escape seams.
    /// No implicit <see langword="string"/>→<see cref="Line"/> conversion is defined.
    /// </para>
    /// </summary>
    /// <param name="text">The text content. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A <see cref="Line"/> containing a single bold <see cref="Segment"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static Line Bold(string text) =>
        FromText(text, new Style(Format: Format.Bold));

    /// <summary>
    /// Shorthand for a single <b>dim</b> line — equivalent to
    /// <c>Line.FromText(text, new Style(Format: Format.Dim))</c>.
    /// <para>
    /// The text is routed through the ordinary sanitizing <see cref="Segment"/> constructor:
    /// control/escape bytes are stripped (or replaced) exactly as they would be under
    /// <see cref="FromText"/>.
    /// </para>
    /// <para>
    /// Only four single-style shorthands exist on <see cref="Line"/>: <see cref="Bold"/>,
    /// <see cref="Dim"/>, <see cref="Fg"/>, and <see cref="Bg"/>. There is deliberately no
    /// <c>Line.Italic</c>, <c>Line.Underline</c>, <c>Line.Reverse</c>,
    /// <c>Line.Strikethrough</c>, or <c>Line.Raw</c> factory. <see cref="Segment.Raw"/> and
    /// <see cref="LineBuilder"/> remain the only verbatim/escape seams.
    /// No implicit <see langword="string"/>→<see cref="Line"/> conversion is defined.
    /// </para>
    /// </summary>
    /// <param name="text">The text content. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A <see cref="Line"/> containing a single dim <see cref="Segment"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static Line Dim(string text) =>
        FromText(text, new Style(Format: Format.Dim));

    /// <summary>
    /// Shorthand for a single foreground-colored line — equivalent to
    /// <c>Line.FromText(text, new Style(Foreground: foreground))</c>.
    /// No format flags are set; <see cref="Style.Format"/> is <see cref="Format.None"/>.
    /// <para>
    /// The text is routed through the ordinary sanitizing <see cref="Segment"/> constructor:
    /// control/escape bytes are stripped (or replaced) exactly as they would be under
    /// <see cref="FromText"/>.
    /// </para>
    /// <para>
    /// Only four single-style shorthands exist on <see cref="Line"/>: <see cref="Bold"/>,
    /// <see cref="Dim"/>, <see cref="Fg"/>, and <see cref="Bg"/>. There is deliberately no
    /// <c>Line.Italic</c>, <c>Line.Underline</c>, <c>Line.Reverse</c>,
    /// <c>Line.Strikethrough</c>, or <c>Line.Raw</c> factory. <see cref="Segment.Raw"/> and
    /// <see cref="LineBuilder"/> remain the only verbatim/escape seams.
    /// No implicit <see langword="string"/>→<see cref="Line"/> conversion is defined.
    /// </para>
    /// </summary>
    /// <param name="text">The text content. Must not be <see langword="null"/>.</param>
    /// <param name="foreground">The foreground color to apply.</param>
    /// <returns>
    /// A <see cref="Line"/> containing a single foreground-colored <see cref="Segment"/>
    /// with <see cref="Format.None"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static Line Fg(string text, Color foreground) =>
        FromText(text, new Style(Foreground: foreground));

    /// <summary>
    /// Shorthand for a single background-colored line — equivalent to
    /// <c>Line.FromText(text, new Style(Background: background))</c>.
    /// No format flags are set; <see cref="Style.Format"/> is <see cref="Format.None"/>.
    /// <para>
    /// The text is routed through the ordinary sanitizing <see cref="Segment"/> constructor:
    /// control/escape bytes are stripped (or replaced) exactly as they would be under
    /// <see cref="FromText"/>.
    /// </para>
    /// <para>
    /// Only four single-style shorthands exist on <see cref="Line"/>: <see cref="Bold"/>,
    /// <see cref="Dim"/>, <see cref="Fg"/>, and <see cref="Bg"/>. There is deliberately no
    /// <c>Line.Italic</c>, <c>Line.Underline</c>, <c>Line.Reverse</c>,
    /// <c>Line.Strikethrough</c>, or <c>Line.Raw</c> factory. <see cref="Segment.Raw"/> and
    /// <see cref="LineBuilder"/> remain the only verbatim/escape seams.
    /// No implicit <see langword="string"/>→<see cref="Line"/> conversion is defined.
    /// </para>
    /// </summary>
    /// <param name="text">The text content. Must not be <see langword="null"/>.</param>
    /// <param name="background">The background color to apply.</param>
    /// <returns>
    /// A <see cref="Line"/> containing a single background-colored <see cref="Segment"/>
    /// with <see cref="Format.None"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static Line Bg(string text, Color background) =>
        FromText(text, new Style(Background: background));

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="other"/> contains exactly the same
    /// segments in the same order.
    /// </summary>
    public bool Equals(Line? other) =>
        other is not null && Segments.SequenceEqual(other.Segments);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (Segment segment in Segments)
        {
            hash.Add(segment);
        }
        return hash.ToHashCode();
    }
}
