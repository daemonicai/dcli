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
