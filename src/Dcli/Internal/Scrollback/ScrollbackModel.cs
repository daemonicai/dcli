using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback;

/// <summary>
/// Owns the flat list of live line-objects and the commit-horizon / overflow logic (§9.2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Thread discipline:</strong> all methods run exclusively on the render loop thread.
/// No synchronization is required.
/// </para>
/// <para>
/// <strong>Live window:</strong> the set of line-objects whose visual rows populate
/// <see cref="RenderModel.LiveWindowRows"/>. The window is bounded:
/// <c>maxHeight = Rows − fixedRegionHeight</c>. When rendering the live list would exceed
/// <c>maxHeight</c>, the topmost objects are committed (their rows are moved to
/// <see cref="RenderModel.NewlyCommittedRows"/>) until the window fits. Committed objects are
/// then removed from <see cref="_liveObjects"/> and may be garbage collected.
/// </para>
/// <para>
/// <strong>Per-frame lifecycle for <see cref="RenderModel.NewlyCommittedRows"/>:</strong>
/// <list type="number">
///   <item>Commands append objects and mark the model dirty.</item>
///   <item>Just before painting (<see cref="PrePaint"/>), the live list is rendered at the
///     current width; overflow objects are committed into <see cref="_pendingCommittedRows"/>.</item>
///   <item><see cref="PrePaint"/> sets <see cref="RenderModel.NewlyCommittedRows"/> from the
///     pending buffer and sets <see cref="RenderModel.LiveWindowRows"/> from the remaining
///     live objects.</item>
///   <item>After paint, <see cref="ClearCommitted"/> resets
///     <see cref="RenderModel.NewlyCommittedRows"/> to empty so the rows are never emitted
///     a second time.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class ScrollbackModel
{
    // Ordered flat list of live line-objects. Index 0 = oldest (topmost on screen).
    private readonly List<ILineObject> _liveObjects = [];

    // Rows from objects that committed this frame, waiting to be handed to the painter once.
    private List<Line>? _pendingCommittedRows;

    /// <summary>
    /// Appends a line-object to the live tail. Marks the model dirty.
    /// </summary>
    internal void Append(ILineObject obj, RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(obj);
        _liveObjects.Add(obj);
        model.MarkDirty();
    }

    /// <summary>
    /// Begins a new live block, appends it to the live tail, and returns it for mutation.
    /// </summary>
    internal LiveBlock BeginLive(RenderModel model)
    {
        LiveBlock block = new();
        _liveObjects.Add(block);
        model.MarkDirty();
        return block;
    }

    /// <summary>
    /// Commits a live block: freezes it and marks the model dirty so the next pre-paint
    /// pass sweeps it out of the live list into the commit queue.
    /// </summary>
    /// <remarks>
    /// The sweep runs in the same <see cref="PrePaint"/> call that follows, so committed
    /// rows reach <see cref="RenderModel.NewlyCommittedRows"/> in that same frame — there
    /// is no one-frame latency.
    /// </remarks>
    internal void CommitLiveBlock(LiveBlock block, RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.Commit();
        // Eagerly allocate the pending buffer so PrePaint never hits the null-branch.
        _pendingCommittedRows ??= [];
        model.MarkDirty();
    }

    /// <summary>
    /// Creates a new <see cref="Collapsible"/>, appends it to the live tail, and returns it.
    /// </summary>
    internal Collapsible BeginCollapsible(Line summary, IReadOnlyList<Line> hiddenLines, RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(hiddenLines);
        Collapsible collapsible = new(summary, hiddenLines);
        _liveObjects.Add(collapsible);
        model.MarkDirty();
        return collapsible;
    }

    /// <summary>
    /// Appends <paramref name="line"/> to the hidden-line set of a <see cref="Collapsible"/>
    /// that is still collapsed-and-live. No-op if the collapsible has already expanded or
    /// frozen past the commit horizon (mirroring the guard precedence of
    /// <see cref="ExpandCollapsible"/>).
    /// </summary>
    internal void AppendToCollapsible(Collapsible collapsible, Line line, RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(collapsible);
        ArgumentNullException.ThrowIfNull(line);

        // Freeze-collapsed-at-horizon: already committed → not in live list → no-op.
        if (!_liveObjects.Contains(collapsible))
            return;

        // Already expanded → no-op.
        if (collapsible.IsExpanded)
            return;

        collapsible.AppendHidden(line);
        model.MarkDirty();
    }

    /// <summary>
    /// Expands a <see cref="Collapsible"/> that is still in the live list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Freeze-collapsed-at-horizon takes precedence:</strong> if the collapsible has
    /// already committed (dropped from <see cref="_liveObjects"/>), this is a clean no-op —
    /// the frozen summary rows already in native scrollback are untouched.
    /// </para>
    /// <para>
    /// <strong>Oversized expansion:</strong> if expanding would make the block taller than the
    /// live-window cap (<c>Rows − fixedRegionHeight</c>), the hidden lines are emitted directly
    /// into <see cref="_pendingCommittedRows"/> (flowing into native scrollback on the next
    /// paint) and the block is left collapsed as a summary marker in the live list.
    /// If the expanded height fits, the block is marked expanded and stays live/re-renderable.
    /// </para>
    /// </remarks>
    internal void ExpandCollapsible(Collapsible collapsible, RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(collapsible);

        // Freeze-collapsed-at-horizon: already committed → not in live list → no-op.
        if (!_liveObjects.Contains(collapsible))
            return;

        // Already expanded → idempotent no-op.
        if (collapsible.IsExpanded)
            return;

        int width = model.Columns;
        int maxHeight = ComputeMaxHeight(model);

        // Measure expanded height without mutating state: summary rows + hidden rows.
        int summaryRowCount = collapsible.Render(width).Count; // collapsed render = summary rows
        int expandedRowCount = summaryRowCount + collapsible.MeasureHiddenRows(width);

        if (expandedRowCount <= maxHeight)
        {
            // Normal path: mark expanded; next PrePaint re-renders the full content live.
            collapsible.Expand();
            model.MarkDirty();
        }
        else
        {
            // Oversized path: reprint hidden lines into committed flow; block stays a collapsed
            // summary marker (do NOT call collapsible.Expand()).
            _pendingCommittedRows ??= [];
            _pendingCommittedRows.AddRange(collapsible.GetHiddenRows(width));
            model.MarkDirty();
        }
    }

    /// <summary>
    /// Called by the loop thread immediately before handing the model to the painter.
    /// Renders the live list, applies the commit horizon, and populates
    /// <see cref="RenderModel.LiveWindowRows"/> and <see cref="RenderModel.NewlyCommittedRows"/>.
    /// </summary>
    internal void PrePaint(RenderModel model)
    {
        int width = model.Columns;
        int maxHeight = ComputeMaxHeight(model);

        // ── Step 1: sweep committed LiveBlocks from the front ────────────────
        // A LiveBlock that has been committed is frozen — treat it as ordinary content
        // that should drain forward. We eagerly drain it here so it hits the commit
        // queue quickly rather than lingering in the live list across many frames.
        // Non-LiveBlock objects (TextBlock, Collapsible) are drained only when they overflow.
        int sweepEnd = 0;
        while (sweepEnd < _liveObjects.Count && _liveObjects[sweepEnd] is LiveBlock { IsCommitted: true })
            sweepEnd++;

        // ── Step 2: render all remaining live objects to measure total height ─
        // Build a parallel array of rendered rows so we don't call Render() twice.
        List<(ILineObject Obj, IReadOnlyList<Line> Rows)> rendered = [];
        rendered.Capacity = _liveObjects.Count;

        for (int i = 0; i < _liveObjects.Count; i++)
            rendered.Add((_liveObjects[i], _liveObjects[i].Render(width)));

        // ── Step 3: commit from the front until the window fits ───────────────
        // First, eagerly commit all swept committed blocks (index 0 .. sweepEnd-1).
        // Then, commit additional from the front until height ≤ maxHeight.
        _pendingCommittedRows ??= [];

        int totalRows = rendered.Sum(r => r.Rows.Count);
        int commitCount = 0;

        // Drain the pre-swept committed blocks first.
        while (commitCount < sweepEnd && commitCount < rendered.Count)
        {
            _pendingCommittedRows.AddRange(rendered[commitCount].Rows);
            totalRows -= rendered[commitCount].Rows.Count;
            commitCount++;
        }

        // Then drain additional objects until we fit within maxHeight.
        // maxHeight == 0 means a zero-height live window — all live rows commit.
        while (totalRows > maxHeight && commitCount < rendered.Count)
        {
            _pendingCommittedRows.AddRange(rendered[commitCount].Rows);
            totalRows -= rendered[commitCount].Rows.Count;
            commitCount++;
        }

        // Remove the committed objects from the live list (drop from memory).
        if (commitCount > 0)
            _liveObjects.RemoveRange(0, commitCount);

        // ── Step 4: populate paint-state ──────────────────────────────────────
        model.NewlyCommittedRows = _pendingCommittedRows.Count > 0
            ? _pendingCommittedRows
            : (IReadOnlyList<Line>)[];

        // Build live window from remaining rendered rows (already computed above).
        List<Line> liveRows = [];
        for (int i = commitCount; i < rendered.Count; i++)
            liveRows.AddRange(rendered[i].Rows);

        model.LiveWindowRows = liveRows;
    }

    /// <summary>
    /// Called by the loop thread after each paint to reset <see cref="RenderModel.NewlyCommittedRows"/>
    /// so committed rows are never emitted a second time.
    /// </summary>
    internal void ClearCommitted(RenderModel model)
    {
        _pendingCommittedRows?.Clear();
        model.NewlyCommittedRows = [];
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes the live window height cap: <c>max(0, Rows − fixedRegionHeight)</c>.
    /// <c>fixedRegionHeight</c> is the current row count of
    /// <see cref="RenderModel.FixedRegionRows"/> (§10 populates this; §9 reads it).
    /// </summary>
    private static int ComputeMaxHeight(RenderModel model)
    {
        int fixedHeight = model.FixedRegionRows.Count;
        return Math.Max(0, model.Rows - fixedHeight);
    }
}
