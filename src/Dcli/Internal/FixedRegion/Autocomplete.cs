namespace Dcli.Internal.FixedRegion;

/// <summary>
/// Autocomplete dropdown rendered below the input line.
/// </summary>
/// <remarks>
/// <para>
/// The overlay is driven by the consumer: call <see cref="Show"/> to present a candidate list
/// and <see cref="Hide"/> to dismiss it. While visible, <see cref="HandleKey"/> intercepts
/// navigation and acceptance keys; all other keys fall through to the input editor.
/// </para>
/// <para>
/// <strong>Accept semantics:</strong> when the user confirms a candidate (Enter or Tab),
/// its <see cref="AutocompleteCandidate.InsertText"/> is applied to the <see cref="TextBuffer"/>
/// via <see cref="TextBuffer.SetText"/>, which replaces the entire buffer and moves the caret
/// to the end. This is a whole-buffer replace — the spec scenario and slash-command-style
/// completion require the candidate to supply the full replacement text. §12 may later
/// refine this to a span-replace; if so, flag it as a behavioural change.
/// </para>
/// <para>
/// The hardware cursor stays at the input caret while this overlay is active
/// (<see cref="HidesCursor"/> is always <see langword="false"/>); selection is shown by
/// <see cref="ScrollableList"/>'s reverse-video highlight.
/// </para>
/// <para>Thread safety is the caller's responsibility; the render-loop thread owns this in normal use.</para>
/// </remarks>
internal sealed class Autocomplete : IOverlay
{
    // ── State ─────────────────────────────────────────────────────────────────

    private readonly TextBuffer _buffer;
    private readonly ScrollableList _list;
    private List<AutocompleteCandidate> _candidates = [];
    private bool _visible;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="Autocomplete"/> overlay.
    /// </summary>
    /// <param name="buffer">
    /// The input buffer to which accepted candidate insert-text is applied.
    /// </param>
    internal Autocomplete(TextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _list = new ScrollableList(multiSelect: false);
    }

    // ── IOverlay ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public OverlayPlacement Placement => OverlayPlacement.BelowInput;

    /// <inheritdoc/>
    public bool HidesCursor => false;

    /// <inheritdoc/>
    public int MaxRows
    {
        get => _list.MaxRows;
        set => _list.MaxRows = value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> when the overlay is not currently visible (after <see cref="Hide"/>
    /// or before the first <see cref="Show"/> call).
    /// </remarks>
    public bool IsDismissed => !_visible;

    /// <inheritdoc/>
    /// Autocomplete uses selection highlight; it does not own the hardware cursor.
    public (int Row, int Col)? CaretInOverlay => null;

    /// <inheritdoc/>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><c>↑</c> — moves selection up; consumed.</description></item>
    ///   <item><description><c>↓</c> — moves selection down; consumed.</description></item>
    ///   <item><description><c>Enter</c> / <c>Tab</c> — accepts selected candidate (applies InsertText, hides); consumed.</description></item>
    ///   <item><description><c>Escape</c> — hides without touching the buffer; consumed.</description></item>
    ///   <item><description>All other keys — fall through (returns <see langword="false"/>).</description></item>
    /// </list>
    /// </remarks>
    public bool HandleKey(KeyEvent key)
    {
        if (key.Code.Kind != KeyCode.KeyCodeKind.Named)
            return false;

        switch (key.Code.NamedValue)
        {
            case NamedKey.Up:
                _list.MoveUp();
                return true;

            case NamedKey.Down:
                _list.MoveDown();
                return true;

            case NamedKey.Enter:
            case NamedKey.Tab:
                Accept();
                return true;

            case NamedKey.Escape:
                Hide();
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Line> Render(int width) => _list.Render(width);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Presents the given candidates in the dropdown.
    /// Resets the list selection to the first item. Showing an empty candidate list is valid
    /// (renders nothing; an Accept on an empty list is a safe no-op).
    /// </summary>
    /// <param name="candidates">The candidates to display, ordered by preference.</param>
    internal void Show(IReadOnlyList<AutocompleteCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        // Defensive copy: caller may mutate the original list; the Accept lookup must stay consistent.
        _candidates = candidates.ToList();
        _list.SetItems(_candidates.Select(c => c.Display).ToList());
        _visible = true;
    }

    /// <summary>
    /// Hides the dropdown without modifying the input buffer.
    /// </summary>
    internal void Hide()
    {
        _visible = false;
    }

    // ── Internal accessors ───────────────────────────────────────────────────

    /// <summary>
    /// Zero-based index of the currently selected candidate, or <c>-1</c> when the list is empty.
    /// Exposed for <c>Dcli.Testing</c> snapshot construction.
    /// </summary>
    internal int SelectedIndex => _list.SelectedIndex;

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the selected candidate's <see cref="AutocompleteCandidate.InsertText"/> to the
    /// buffer and hides the overlay. No-op when the list is empty.
    /// </summary>
    private void Accept()
    {
        int index = _list.SelectedIndex;
        if (index >= 0 && index < _candidates.Count)
            _buffer.SetText(_candidates[index].InsertText);

        Hide();
    }
}
