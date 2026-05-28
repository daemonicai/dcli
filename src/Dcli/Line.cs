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
