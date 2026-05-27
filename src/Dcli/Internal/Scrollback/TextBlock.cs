namespace Dcli.Internal.Scrollback;

/// <summary>
/// An immutable line-object consisting of one or more logical <see cref="Line"/>s.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="TextBlock"/> is constructed once and never mutated. Width-aware wrapping
/// and display-width accounting are fully delegated to <see cref="LineWrapper"/> and
/// <see cref="DisplayWidth"/> (§3); no wrapping logic lives here.
/// </para>
/// <para>
/// A block with zero logical lines renders as a single empty visual row so the paint-state
/// always has at least one entry (consistent with <see cref="LineWrapper"/> behaviour for
/// empty lines).
/// </para>
/// </remarks>
internal sealed class TextBlock : ILineObject
{
    private readonly List<Line> _logicalLines;

    /// <summary>
    /// Initialises a <see cref="TextBlock"/> from a sequence of logical lines.
    /// </summary>
    internal TextBlock(IEnumerable<Line> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _logicalLines = lines.ToList();
    }

    /// <summary>
    /// Initialises a <see cref="TextBlock"/> from a single logical line.
    /// </summary>
    internal TextBlock(Line line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _logicalLines = [line];
    }

    /// <inheritdoc/>
    public IReadOnlyList<Line> Render(int width)
    {
        if (_logicalLines.Count == 0)
            return [new Line(Array.Empty<Segment>())];

        List<Line> result = [];
        foreach (Line logicalLine in _logicalLines)
        {
            IReadOnlyList<Line> wrapped = LineWrapper.Wrap(logicalLine, width);
            result.AddRange(wrapped);
        }
        return result;
    }
}
