using System.Globalization;
using System.Text;

namespace Dcli.Internal.FixedRegion;

/// <summary>
/// A dialog overlay rendered above the input line.
/// </summary>
/// <remarks>
/// <para>
/// The dialog is a slot that hosts an interactive <see cref="ScrollableList"/>. It does not
/// contain title or border chrome in v1 — the consumer maps a request to its list contents
/// via the <see cref="List"/> property before showing the overlay.
/// </para>
/// <para>
/// The dialog is closed (and <see cref="IsDismissed"/> becomes <see langword="true"/>) when
/// <see cref="CloseRequest"/> is set by an Enter (<see cref="OverlayCloseKind.Submit"/>) or
/// Escape (<see cref="OverlayCloseKind.Cancel"/>) key. §12 consumes <see cref="CloseRequest"/>
/// to resolve the awaitable dialog result.
/// </para>
/// <para>
/// <strong>Type-to-filter:</strong> when <see cref="TypeToFilter"/> is <see langword="true"/>,
/// printable character keys append to <see cref="FilterText"/> and Backspace trims it. The
/// actual item filtering is consumer-driven (§12) — the dialog records only the raw filter
/// string so that <c>dcli</c> stays protocol-free and does not interpret item text semantics.
/// </para>
/// <para>Thread safety is the caller's responsibility; the render-loop thread owns this in normal use.</para>
/// </remarks>
internal sealed class Dialog : IModalOverlay
{
    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="Dialog"/>.
    /// </summary>
    /// <param name="multiSelect">
    /// When <see langword="true"/>, Space toggles the current item in and out of
    /// <see cref="ScrollableList.CheckedIndices"/>; the list shows check markers.
    /// </param>
    /// <param name="modal">
    /// When <see langword="true"/> (the default), the dialog consumes all keys that are not
    /// handled by an earlier rule, preventing them from reaching the input editor. Also hides
    /// the hardware cursor (<see cref="HidesCursor"/> returns <see langword="true"/>).
    /// </param>
    /// <param name="typeToFilter">
    /// When <see langword="true"/>, printable keys append to <see cref="FilterText"/> and
    /// Backspace trims it. When <see langword="false"/> (the default), those keys are handled
    /// by the modal fall-through rule or fall through to the input editor.
    /// </param>
    /// <param name="title">
    /// Optional leading row rendered above the list. When non-<see langword="null"/>, one row
    /// of the <see cref="MaxRows"/> budget is reserved for it, and <c>List.MaxRows</c> is set to
    /// at most <c>MaxRows - 1</c> so the total output never exceeds the budget.
    /// </param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, Backspace closes the dialog with
    /// <see cref="OverlayCloseKind.Back"/> provided the user has not yet moved the selection
    /// cursor and the filter text is empty. Defaults to <see langword="false"/>.
    /// </param>
    internal Dialog(bool multiSelect = false, bool modal = true, bool typeToFilter = false, Line? title = null, bool allowBack = false)
    {
        Modal = modal;
        TypeToFilter = typeToFilter;
        Title = title;
        _allowBack = allowBack;
        List = new ScrollableList(multiSelect);
    }

    // ── IOverlay ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public OverlayPlacement Placement => OverlayPlacement.AboveInput;

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> while <see cref="Modal"/> is <see langword="true"/> and the dialog
    /// has not yet been dismissed. Non-modal dialogs never hide the cursor.
    /// </remarks>
    public bool HidesCursor => Modal;

    /// <inheritdoc/>
    /// <remarks>
    /// The setter stores the raw budget and forwards to <see cref="ScrollableList.MaxRows"/>
    /// (minus one when a <see cref="Title"/> is present, as a hint; <see cref="Render"/> always
    /// truncates the combined output to at most <see cref="_maxRows"/> rows regardless).
    /// </remarks>
    public int MaxRows
    {
        get => _maxRows;
        set
        {
            _maxRows = value;
            // Give the list as much of the budget as possible. When Title is present, one row
            // is consumed by it; the list gets the remainder (≥ 0, clamped to 1 by ScrollableList).
            List.MaxRows = Title is null ? value : Math.Max(1, value - 1);
        }
    }

    /// <inheritdoc/>
    /// <remarks><see langword="true"/> once <see cref="CloseRequest"/> is set.</remarks>
    public bool IsDismissed => CloseRequest is not null;

    /// <inheritdoc/>
    /// <remarks>
    /// Key handling precedence (first match wins):
    /// <list type="number">
    ///   <item><description><c>Enter</c> → <see cref="CloseRequest"/> = Submit; consumed.</description></item>
    ///   <item><description><c>Escape</c> → <see cref="CloseRequest"/> = Cancel; consumed.</description></item>
    ///   <item><description><c>Backspace</c> when constructed with <c>allowBack: true</c>, the user
    ///   has not yet moved the selection cursor, and <see cref="FilterText"/> is empty →
    ///   <see cref="CloseRequest"/> = Back; consumed. (Does not fire when the cursor has moved or
    ///   filter text is present, so it cannot mask the type-to-filter Backspace-trim behaviour.)</description></item>
    ///   <item><description><c>↑</c> / <c>↓</c> → navigate the list (sets the internal moved flag); consumed.</description></item>
    ///   <item><description>Space (U+0020) when <see cref="ScrollableList.MultiSelect"/> → toggle current; consumed.</description></item>
    ///   <item><description>Printable rune (≥ U+0020, ≠ U+007F) when <see cref="TypeToFilter"/> → append to <see cref="FilterText"/>; consumed. Backspace → trim <see cref="FilterText"/>; consumed.</description></item>
    ///   <item><description>Any remaining key when <see cref="Modal"/> → consumed (modal catch-all).</description></item>
    ///   <item><description>Otherwise → <see langword="false"/> (falls through to the input editor).</description></item>
    /// </list>
    /// </remarks>
    public bool HandleKey(KeyEvent key)
    {
        // 1. Enter → Submit
        if (key.Code.Kind == KeyCode.KeyCodeKind.Named && key.Code.NamedValue == NamedKey.Enter)
        {
            CloseRequest = OverlayCloseKind.Submit;
            return true;
        }

        // 2. Escape → Cancel
        if (key.Code.Kind == KeyCode.KeyCodeKind.Named && key.Code.NamedValue == NamedKey.Escape)
        {
            CloseRequest = OverlayCloseKind.Cancel;
            return true;
        }

        // 3. Backspace-Back: fires only when AllowBack is true, the user has not yet moved the
        //    selection cursor, and no filter text has been accumulated. The _filterText guard
        //    ensures this branch cannot mask the type-to-filter Backspace-trim path (rule 5).
        if (key.Code.Kind == KeyCode.KeyCodeKind.Named && key.Code.NamedValue == NamedKey.Backspace &&
            _allowBack && !_hasMoved && _filterText.Length == 0)
        {
            CloseRequest = OverlayCloseKind.Back;
            return true;
        }

        // 4. Arrow navigation (sets _hasMoved so the Back branch above is no longer eligible)
        if (key.Code.Kind == KeyCode.KeyCodeKind.Named && key.Code.NamedValue == NamedKey.Up)
        {
            _hasMoved = true;
            List.MoveUp();
            return true;
        }
        if (key.Code.Kind == KeyCode.KeyCodeKind.Named && key.Code.NamedValue == NamedKey.Down)
        {
            _hasMoved = true;
            List.MoveDown();
            return true;
        }

        // 5. Space → toggle when multi-select (takes precedence over type-to-filter for space)
        if (key.Code.Kind == KeyCode.KeyCodeKind.UnicodeScalar &&
            key.Code.RuneValue.Value == ' ' &&
            List.MultiSelect)
        {
            List.ToggleCurrent();
            return true;
        }

        // 6. Type-to-filter: printable runes and Backspace
        if (TypeToFilter)
        {
            if (key.Code.Kind == KeyCode.KeyCodeKind.Named && key.Code.NamedValue == NamedKey.Backspace)
            {
                TrimFilterText();
                return true;
            }

            if (key.Code.Kind == KeyCode.KeyCodeKind.UnicodeScalar)
            {
                Rune r = key.Code.RuneValue;
                // Printable: rune >= U+0020 and not DEL (U+007F)
                if (r.Value >= 0x20 && r.Value != 0x7F)
                {
                    _filterText.Append(r.ToString());
                    return true;
                }
            }
        }

        // 7. Modal catch-all: consume everything so the input editor gets nothing
        if (Modal)
            return true;

        // 8. Non-modal fall-through
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When <see cref="Title"/> is set, it is prepended as the first row before the list rows.
    /// The total row count never exceeds <see cref="MaxRows"/>: the title is emitted first and
    /// list rows fill whatever budget remains (possibly zero when <see cref="MaxRows"/> is 1).
    /// </remarks>
    public IReadOnlyList<Line> Render(int width)
    {
        IReadOnlyList<Line> listRows = List.Render(width);
        if (Title is null)
            return listRows;

        // Budget: title occupies row 0; list gets up to (MaxRows - 1) additional rows.
        int listBudget = Math.Max(0, _maxRows - 1);
        int listCount = Math.Min(listRows.Count, listBudget);
        Line[] result = new Line[1 + listCount];
        result[0] = Title;
        for (int i = 0; i < listCount; i++)
            result[i + 1] = listRows[i];
        return result;
    }

    // ── Public properties ─────────────────────────────────────────────────────

    /// <summary>The hosted interactive list; configure items before displaying the dialog.</summary>
    internal ScrollableList List { get; }

    /// <summary>
    /// Optional title row displayed above the list. Set via the constructor.
    /// When non-<see langword="null"/>, <see cref="Render"/> prepends it and
    /// <see cref="MaxRows"/> reserves one row for it.
    /// </summary>
    internal Line? Title { get; }

    /// <summary>Whether the dialog is modal (consumes all keys, hides the cursor).</summary>
    internal bool Modal { get; }

    /// <summary>Whether printable keys are routed to <see cref="FilterText"/>.</summary>
    internal bool TypeToFilter { get; }

    /// <summary>
    /// The current filter string entered by the user.
    /// Starts empty; only updated when <see cref="TypeToFilter"/> is <see langword="true"/>.
    /// The actual item filtering is consumer-driven (§12); this property records only the raw text.
    /// </summary>
    internal string FilterText => _filterText.ToString();

    /// <summary>
    /// How the dialog was closed, or <see langword="null"/> while still open.
    /// Set by Enter (<see cref="OverlayCloseKind.Submit"/>) or Escape (<see cref="OverlayCloseKind.Cancel"/>).
    /// Consumed by §12 to resolve the awaitable dialog result.
    /// </summary>
    public OverlayCloseKind? CloseRequest { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The list dialog never owns the hardware cursor; it uses selection highlight instead.
    /// </remarks>
    public (int Row, int Col)? CaretInOverlay => null;

    // ── Private ───────────────────────────────────────────────────────────────

    private int _maxRows = 10; // default matches ScrollableList default
    private readonly StringBuilder _filterText = new();
    private readonly bool _allowBack;

    // Set to true on the first ↑/↓ press; once moved, Backspace-Back is no longer eligible.
    private bool _hasMoved;

    /// <summary>
    /// Removes the last grapheme cluster (text element) from <see cref="FilterText"/>.
    /// No-op when the filter string is empty.
    /// </summary>
    private void TrimFilterText()
    {
        if (_filterText.Length == 0)
            return;

        string current = _filterText.ToString();
        // Find the last grapheme cluster boundary using StringInfo.
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(current);
        int lastStart = 0;
        while (enumerator.MoveNext())
            lastStart = enumerator.ElementIndex;

        _filterText.Remove(lastStart, _filterText.Length - lastStart);
    }
}
