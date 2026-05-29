namespace Dcli.Internal.Scrollback;

/// <summary>
/// A one-way collapsible line-object: starts collapsed (showing a summary line), can expand
/// exactly once to reveal hidden lines, and can never re-collapse.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One-way / monotonic height:</strong> once <see cref="Expand"/> is called the block
/// is permanently expanded. A second call to <see cref="Expand"/> is a no-op. There is no
/// collapse operation — the spec forbids re-collapse.
/// </para>
/// <para>
/// <strong>Incremental append:</strong> while still collapsed-and-live, callers may append
/// lines to the hidden set via <see cref="AppendHidden"/>. The caller
/// (<see cref="ScrollbackModel"/>) guarantees this is invoked only while the collapsible is
/// collapsed and present in the live list.
/// </para>
/// <para>
/// <strong>Oversized expansion:</strong> if the expanded height would exceed the live-window
/// cap the expansion is handled by <see cref="ScrollbackModel.ExpandCollapsible"/> instead
/// of being performed inline. The collapsible stays a collapsed marker in the live list and
/// the hidden lines are emitted into the committed flow exactly once.
/// </para>
/// <para>
/// <strong>Freeze-collapsed-at-horizon:</strong> a collapsible that commits while still
/// collapsed is dropped from <c>_liveObjects</c>. Any subsequent <see cref="Expand"/> call
/// is a no-op because the caller no longer holds a reference that is present in the live list.
/// The frozen summary rows that already committed into native scrollback are unaffected.
/// </para>
/// <para>
/// <strong>Thread discipline:</strong> all methods run exclusively on the render loop thread.
/// No synchronization is required.
/// </para>
/// </remarks>
internal sealed class Collapsible : ILineObject
{
    private readonly Line _summary;
    // Owned mutable copy — callers cannot mutate the original list after construction.
    private readonly List<Line> _hiddenLines;

    /// <summary>Whether <see cref="Expand"/> has been called (successfully).</summary>
    internal bool IsExpanded { get; private set; }

    /// <summary>
    /// Initialises a new <see cref="Collapsible"/> in the collapsed state.
    /// </summary>
    /// <param name="summary">The single summary line shown while collapsed.</param>
    /// <param name="hiddenLines">The lines revealed on expansion. Copied at construction so
    /// off-thread mutations to the original list cannot affect this object.</param>
    internal Collapsible(Line summary, IReadOnlyList<Line> hiddenLines)
    {
        ArgumentNullException.ThrowIfNull(hiddenLines);
        _summary = summary;
        _hiddenLines = hiddenLines.ToList();
    }

    /// <summary>
    /// Appends <paramref name="line"/> to the hidden-line set.
    /// </summary>
    /// <remarks>
    /// The caller (<see cref="ScrollbackModel"/>) guarantees this is invoked only while the
    /// block is collapsed and present in the live list. Guards live in the model, not here.
    /// </remarks>
    internal void AppendHidden(Line line)
    {
        _hiddenLines.Add(line);
    }

    /// <summary>
    /// Marks this collapsible as expanded. Idempotent — a second call has no effect.
    /// </summary>
    internal void Expand()
    {
        // One-way: false → true only. A second call leaves IsExpanded = true unchanged.
        IsExpanded = true;
    }

    /// <summary>
    /// Returns the visual rows produced by the hidden lines at <paramref name="width"/>,
    /// without mutating expansion state. Used by the oversized-expansion path.
    /// </summary>
    internal IReadOnlyList<Line> GetHiddenRows(int width)
    {
        List<Line> result = [];
        foreach (Line line in _hiddenLines)
            result.AddRange(LineWrapper.Wrap(line, width));
        return result;
    }

    /// <summary>
    /// Returns the number of visual rows the hidden lines would produce at
    /// <paramref name="width"/>, without mutating expansion state.
    /// </summary>
    internal int MeasureHiddenRows(int width)
    {
        int count = 0;
        foreach (Line line in _hiddenLines)
            count += LineWrapper.Wrap(line, width).Count;
        return count;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Line> Render(int width)
    {
        if (!IsExpanded)
            return LineWrapper.Wrap(_summary, width);

        // Expanded: summary rows followed by the hidden lines' rows.
        List<Line> result = [];
        result.AddRange(LineWrapper.Wrap(_summary, width));
        foreach (Line line in _hiddenLines)
            result.AddRange(LineWrapper.Wrap(line, width));
        return result;
    }
}
