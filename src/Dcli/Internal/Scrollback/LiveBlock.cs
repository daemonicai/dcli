using System.Text;

namespace Dcli.Internal.Scrollback;

/// <summary>
/// A mutable line-object whose content grows via <see cref="AppendText"/> or is replaced
/// wholesale by <see cref="SetContent"/>, and is frozen by <see cref="Commit"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Thread discipline:</strong> all methods are called exclusively on the render loop
/// thread (via <see cref="RenderLoop.ILoopCommand"/> apply). No synchronization is required.
/// </para>
/// <para>
/// While live the block renders its current accumulated text. After <see cref="Commit"/> the
/// object is removed from the live list; its final rendered rows were already flushed into the
/// commit queue and the object itself may be dropped.
/// </para>
/// <para>
/// A committed block that has not been replaced still renders its accumulated text; a block
/// on which <see cref="SetContent"/> was called renders the replacement lines.
/// </para>
/// </remarks>
internal sealed class LiveBlock : ILineObject
{
    // When _replacedLines is non-null, Render() uses those instead of _text.
    private readonly StringBuilder _text = new();
    private IReadOnlyList<Line>? _replacedLines;

    /// <summary>Whether <see cref="Commit"/> has been called.</summary>
    internal bool IsCommitted { get; private set; }

    /// <summary>
    /// Appends <paramref name="text"/> to the live tail.
    /// Ignored after commit (no-op; guards against out-of-order commands).
    /// </summary>
    internal void AppendText(string text)
    {
        if (IsCommitted || text.Length == 0)
            return;

        // SetContent replaces wholesale; subsequent AppendText is not meaningful.
        // Honour the contract: once replaced, appends are discarded.
        if (_replacedLines is not null)
            return;

        _text.Append(text);
    }

    /// <summary>
    /// Replaces the entire content with <paramref name="lines"/>.
    /// After this call <see cref="AppendText"/> is a no-op.
    /// Ignored after commit.
    /// </summary>
    internal void SetContent(IReadOnlyList<Line> lines)
    {
        if (IsCommitted)
            return;

        ArgumentNullException.ThrowIfNull(lines);
        _replacedLines = lines;
    }

    /// <summary>
    /// Marks the block as committed. The caller (<see cref="ScrollbackModel"/>) is
    /// responsible for removing it from the live list and emitting its rendered rows.
    /// </summary>
    internal void Commit() => IsCommitted = true;

    /// <inheritdoc/>
    public IReadOnlyList<Line> Render(int width)
    {
        if (_replacedLines is not null)
        {
            if (_replacedLines.Count == 0)
                return [new Line(Array.Empty<Segment>())];

            List<Line> result = [];
            foreach (Line logicalLine in _replacedLines)
                result.AddRange(LineWrapper.Wrap(logicalLine, width));
            return result;
        }

        // Render the accumulated text as a single logical line of one plain segment.
        string txt = _text.ToString();
        Line line = txt.Length == 0
            ? new Line(Array.Empty<Segment>())
            : new Line([new Segment(txt)]);
        return LineWrapper.Wrap(line, width);
    }
}
