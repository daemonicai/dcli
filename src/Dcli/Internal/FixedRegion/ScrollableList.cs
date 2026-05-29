namespace Dcli.Internal.FixedRegion;

/// <summary>
/// A bounded, scrollable, optionally multi-select list component.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Selection highlight:</strong> the selected row is indicated by <see cref="Format.Reverse"/>
/// applied to every segment in that row, preserving existing foreground, background, and other format
/// flags (Decision 8 — reverse video, never the hardware cursor). The selected row is additionally
/// padded to <c>width</c> columns with a reverse-styled space so the highlight reads as a full-width
/// bar; non-selected rows are not padded.
/// </para>
/// <para>
/// <strong>One item = one visual row.</strong> Each item is a <see cref="Line"/>; it is truncated
/// (not wrapped) to the first width-aware row produced by <see cref="LineWrapper.Wrap"/>. Multi-row
/// item wrapping is not supported — this keeps selection navigation and auto-scroll item-indexed and
/// predictable.
/// </para>
/// <para>
/// <strong>Viewport + auto-scroll:</strong> maintains a private <c>_top</c> index. On
/// <see cref="MoveUp"/> / <see cref="MoveDown"/> the viewport scrolls by the minimum amount needed
/// to keep <see cref="SelectedIndex"/> visible ("scroll just enough" — same anchoring as
/// <see cref="TextBuffer"/>).
/// </para>
/// <para>
/// <strong>Multi-select marker:</strong> when <see cref="MultiSelect"/> is <see langword="true"/>,
/// every rendered row is prefixed with <c>"[x] "</c> or <c>"[ ] "</c> (4 columns each, equal width)
/// before the item content is truncated to <c>width</c>.
/// </para>
/// <para>
/// Thread safety is the caller's responsibility; the render-loop thread owns this in normal use.
/// </para>
/// </remarks>
internal sealed class ScrollableList
{
    // ── State ────────────────────────────────────────────────────────────────────────────────

    private IReadOnlyList<Line> _items = [];
    private int _selectedIndex = -1;
    private int _top;
    private int _maxRows = 10;

    // Sorted ascending set of checked indices; null when !MultiSelect.
    private readonly SortedSet<int>? _checkedIndices;

    // Marker text — widths must be equal.
    private const string _checkedMarker = "[x] ";
    private const string _uncheckedMarker = "[ ] ";

    // ── Construction ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes a new <see cref="ScrollableList"/>.
    /// </summary>
    /// <param name="multiSelect">
    /// When <see langword="true"/>, <see cref="ToggleCurrent"/> adds/removes items from
    /// <see cref="CheckedIndices"/> and every row is prefixed with a checked/unchecked marker.
    /// When <see langword="false"/>, <see cref="ToggleCurrent"/> is a no-op and no markers are shown.
    /// </param>
    internal ScrollableList(bool multiSelect = false)
    {
        MultiSelect = multiSelect;
        _checkedIndices = multiSelect ? [] : null;
    }

    // ── Configuration ────────────────────────────────────────────────────────────────────────

    /// <summary>Whether multi-select was enabled at construction (fixed).</summary>
    internal bool MultiSelect { get; }

    /// <summary>
    /// Maximum number of item rows shown in the viewport. Values below 1 are treated as 1.
    /// Defaults to 10.
    /// </summary>
    internal int MaxRows
    {
        get => _maxRows;
        set => _maxRows = Math.Max(1, value);
    }

    // ── Query ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Number of items in the list.</summary>
    internal int Count => _items.Count;

    /// <summary>
    /// Zero-based index of the currently selected item, or <c>-1</c> when the list is empty.
    /// </summary>
    internal int SelectedIndex => _selectedIndex;

    /// <summary>
    /// The set of checked item indices in ascending order.
    /// Always empty when <see cref="MultiSelect"/> is <see langword="false"/>.
    /// Returns a snapshot — mutations to the returned collection do not affect the list.
    /// </summary>
    internal IReadOnlyCollection<int> CheckedIndices =>
        _checkedIndices?.ToArray() ?? [];

    // ── Mutation ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the item list. Resets <see cref="SelectedIndex"/> to <c>0</c> (or <c>-1</c> when
    /// empty), scrolls the viewport back to the top, and clears <see cref="CheckedIndices"/>.
    /// </summary>
    /// <param name="items">The new items to display.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="items"/> is <see langword="null"/>.
    /// </exception>
    internal void SetItems(IReadOnlyList<Line> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        _selectedIndex = items.Count > 0 ? 0 : -1;
        _top = 0;
        _checkedIndices?.Clear();
    }

    /// <summary>
    /// Moves the selection one item up. No-op when at index 0 or when the list is empty
    /// (no wrap-around).
    /// </summary>
    internal void MoveUp()
    {
        if (_selectedIndex <= 0)
            return;

        _selectedIndex--;
        ScrollToKeepSelectionVisible();
    }

    /// <summary>
    /// Moves the selection one item down. No-op when at the last item or when the list is empty
    /// (no wrap-around).
    /// </summary>
    internal void MoveDown()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _items.Count - 1)
            return;

        _selectedIndex++;
        ScrollToKeepSelectionVisible();
    }

    /// <summary>
    /// Toggles whether the currently selected item is in <see cref="CheckedIndices"/>.
    /// No-op when <see cref="MultiSelect"/> is <see langword="false"/> or when the list is empty.
    /// </summary>
    internal void ToggleCurrent()
    {
        if (_checkedIndices is null || _selectedIndex < 0)
            return;

        if (!_checkedIndices.Remove(_selectedIndex))
            _checkedIndices.Add(_selectedIndex);
    }

    // ── Rendering ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces at most <see cref="MaxRows"/> visual rows for the current viewport.
    /// </summary>
    /// <param name="width">
    /// Available display columns. Values below 1 are treated as 1.
    /// </param>
    /// <returns>
    /// An ordered list of <see cref="Line"/>s — one per visible item. Empty when the list is empty.
    /// </returns>
    internal IReadOnlyList<Line> Render(int width)
    {
        if (width < 1)
            width = 1;

        if (_items.Count == 0)
            return [];

        // Reconcile the viewport anchor against the current MaxRows/Count/SelectedIndex.
        // MaxRows may have changed between renders (height-budget squeeze/expand) without
        // going through MoveUp/MoveDown, leaving _top stale and causing overruns or
        // dropped-selection bugs. ScrollToKeepSelectionVisible is the single source of
        // truth for the _top clamp.
        ScrollToKeepSelectionVisible();

        int visibleCount = Math.Min(_maxRows, _items.Count);
        int end = _top + visibleCount;

        Line[] result = new Line[visibleCount];

        for (int i = _top; i < end; i++)
        {
            bool isSelected = i == _selectedIndex;
            bool isChecked = _checkedIndices?.Contains(i) ?? false;

            Line row = BuildRow(_items[i], isSelected, isChecked, width);
            result[i - _top] = row;
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts <c>_top</c> by the minimum scroll needed to keep <see cref="SelectedIndex"/>
    /// within the viewport <c>[_top, _top + MaxRows)</c>.
    /// </summary>
    private void ScrollToKeepSelectionVisible()
    {
        if (_selectedIndex < _top)
        {
            _top = _selectedIndex;
        }
        else if (_selectedIndex >= _top + _maxRows)
        {
            _top = _selectedIndex - _maxRows + 1;
        }

        // Clamp _top so we never show a partial bottom window when items allow full one.
        int maxTop = Math.Max(0, _items.Count - _maxRows);
        _top = Math.Min(_top, maxTop);
    }

    /// <summary>
    /// Builds a single rendered row for an item.
    /// </summary>
    /// <remarks>
    /// When <see cref="MultiSelect"/>, prefixes the row with a <c>"[x] "</c> / <c>"[ ] "</c>
    /// marker (4 equal-width columns) before truncating to <paramref name="width"/>. If the item
    /// is selected, every segment has <see cref="Format.Reverse"/> ORed into its style, and
    /// a trailing reverse-styled space segment pads out to <paramref name="width"/> columns.
    /// </remarks>
    private Line BuildRow(Line item, bool isSelected, bool isChecked, int width)
    {
        // Prefix the item with the multi-select marker if needed.
        Line source;
        if (MultiSelect)
        {
            string marker = isChecked ? _checkedMarker : _uncheckedMarker;
            // Prepend marker as first segment, followed by all item segments.
            List<Segment> markerPlusItem = new(item.Segments.Count + 1)
            {
                new Segment(marker)
            };
            markerPlusItem.AddRange(item.Segments);
            source = new Line(markerPlusItem);
        }
        else
        {
            source = item;
        }

        // Truncate to the first width-aware row.
        Line truncated = LineWrapper.Wrap(source, width)[0];

        if (!isSelected)
            return truncated;

        // Apply Format.Reverse to every segment.
        Segment[] reversed = new Segment[truncated.Segments.Count];
        int col = 0;
        for (int i = 0; i < truncated.Segments.Count; i++)
        {
            Segment seg = truncated.Segments[i];
            Style reversedStyle = seg.Style with { Format = seg.Style.Format | Format.Reverse };
            // Text is already sanitized; constructing a new Segment re-sanitizes via the fast path (no-op).
            reversed[i] = seg.IsRaw
                ? Segment.Raw(seg.Text, reversedStyle)
                : new Segment(seg.Text, reversedStyle);
            col += DisplayWidth.Measure(seg.Text);
        }

        // Pad selected row to full width with a reverse-styled space so the highlight bar fills
        // the entire width. The §8 painter full-clears the region, so non-selected rows need
        // no padding.
        if (col < width)
        {
            Segment[] withPad = new Segment[reversed.Length + 1];
            reversed.CopyTo(withPad, 0);
            withPad[reversed.Length] = new Segment(
                new string(' ', width - col),
                new Style(Format: Format.Reverse));
            return new Line(withPad);
        }

        return new Line(reversed);
    }
}
