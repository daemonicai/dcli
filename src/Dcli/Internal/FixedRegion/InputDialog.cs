using System.Text;

namespace Dcli.Internal.FixedRegion;

/// <summary>
/// A free-text input dialog overlay rendered above the input line.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the list-based <see cref="Dialog"/>, this overlay hosts a <see cref="TextBuffer"/> and
/// exposes a full single-line or multi-line text entry experience. The cursor is placed at the
/// dialog's own caret via <see cref="CaretInOverlay"/> — not hidden the way a select dialog hides
/// it (see Decision 8 / <see cref="HidesCursor"/>).
/// </para>
/// <para>
/// When <see cref="IsSecret"/> is <see langword="true"/>, each character in the rendered rows is
/// replaced by the bullet mask glyph U+2022 (•). The width of each glyph is preserved so that
/// <see cref="CaretInOverlay"/> remains correct without any offset arithmetic.
/// </para>
/// <para>Thread safety is the caller's responsibility; the render-loop thread owns this in normal use.</para>
/// </remarks>
internal sealed class InputDialog : IModalOverlay
{
    private readonly TextBuffer _buffer;
    private readonly bool _isSecret;
    // True once the user makes any buffer-mutating keystroke (insert, Backspace, Delete).
    // Sticky: never reset to false after being set. Used so that masking semantics are
    // consistent: _userEdited=false means the buffer still holds the seeded Default exactly
    // as constructed.
    private bool _userEdited;
    private int _maxRows = 10;
    // Cached from the last Render call (or seeded by SeedWidth on the loop thread at open time)
    // so HandleKey has a correct width for Home/End/Up/Down before the first paint.
    // The value is 0 until seeded; SeedWidth is called by OpenDialogCommand.Apply so width-
    // dependent navigation in the first key batch uses the real terminal width, not a guess.
    private int _lastWidth;
    private (int Row, int Col)? _lastCaret;

    /// <summary>
    /// Initialises a new <see cref="InputDialog"/>.
    /// </summary>
    /// <param name="prompt">Optional preamble lines rendered above the text field.</param>
    /// <param name="default">Optional pre-filled text; the caret starts at its end.</param>
    /// <param name="isSecret">When <see langword="true"/>, rendered characters are masked.</param>
    internal InputDialog(IReadOnlyList<Line>? prompt, string? @default, bool isSecret)
    {
        Prompt = prompt;
        _isSecret = isSecret;
        _buffer = new TextBuffer();
        if (!string.IsNullOrEmpty(@default))
            _buffer.SetText(@default);
    }

    // ── IOverlay ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public OverlayPlacement Placement => OverlayPlacement.AboveInput;

    /// <inheritdoc/>
    /// <remarks>
    /// The input dialog is the focus — the hardware cursor is placed at its own caret.
    /// <see langword="false"/> here; cursor position comes from <see cref="CaretInOverlay"/>.
    /// </remarks>
    public bool HidesCursor => false;

    /// <inheritdoc/>
    public int MaxRows
    {
        get => _maxRows;
        set => _maxRows = value;
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
    ///   <item><description>Printable rune (≥ U+0020, ≠ U+007F) → insert into buffer; consumed.</description></item>
    ///   <item><description><c>Backspace</c> → delete before caret; consumed.</description></item>
    ///   <item><description><c>Delete</c> → delete after caret; consumed.</description></item>
    ///   <item><description><c>Left</c> / <c>Right</c> → move caret; consumed.</description></item>
    ///   <item><description><c>Home</c> / <c>End</c> → move to visual row start/end; consumed.</description></item>
    ///   <item><description><c>Up</c> / <c>Down</c> → move caret up/down one visual row; consumed.</description></item>
    ///   <item><description>Any other key → modal catch-all; consumed (Tab, Ctrl/Alt, etc.).</description></item>
    /// </list>
    /// Cancellation is via Escape or the CancellationToken, never Ctrl+C.
    /// </remarks>
    public bool HandleKey(KeyEvent key)
    {
        int width = _lastWidth;

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

        // Keys with Ctrl or Alt fall to the modal catch-all — they must not insert text or trigger
        // editing operations. Mirrors the gate in LoopEngine.RouteEditorKey. Shift is allowed
        // because terminals deliver shifted printable runes as ordinary UnicodeScalar events.
        if ((key.Modifiers & (Modifiers.Ctrl | Modifiers.Alt)) != Modifiers.None)
            return true; // consumed by modal catch-all

        // 3. Printable rune (Ctrl/Alt already excluded above)
        if (key.Code.Kind == KeyCode.KeyCodeKind.UnicodeScalar)
        {
            Rune r = key.Code.RuneValue;
            if (r.Value >= 0x20 && r.Value != 0x7F)
            {
                _userEdited = true;
                _buffer.Insert(r);
                return true;
            }
        }

        // 4–8. Named navigation keys
        if (key.Code.Kind == KeyCode.KeyCodeKind.Named)
        {
            switch (key.Code.NamedValue)
            {
                case NamedKey.Backspace:
                    _userEdited = true;
                    _buffer.Backspace();
                    return true;

                case NamedKey.Delete:
                    _userEdited = true;
                    _buffer.Delete();
                    return true;

                case NamedKey.Left:
                    _buffer.MoveLeft();
                    return true;

                case NamedKey.Right:
                    _buffer.MoveRight();
                    return true;

                case NamedKey.Home:
                    _buffer.MoveHome(width);
                    return true;

                case NamedKey.End:
                    _buffer.MoveEnd(width);
                    return true;

                case NamedKey.Up:
                    _buffer.MoveUp(width);
                    return true;

                case NamedKey.Down:
                    _buffer.MoveDown(width);
                    return true;
            }
        }

        // 9. Modal catch-all — consume everything else (Tab, Ctrl/Alt combos, unknown keys).
        return true;
    }

    /// <inheritdoc/>
    public bool HandlePaste(string text)
    {
        _userEdited = true;
        _buffer.Insert(text);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When <see cref="Prompt"/> is non-<see langword="null"/> and non-empty, its lines are
    /// prepended above the text field rows. When <see cref="IsSecret"/> is <see langword="true"/>,
    /// each Rune in the visible buffer rows is replaced by one mask glyph per display column
    /// occupied by that Rune, so <see cref="CaretInOverlay"/> needs no offset correction.
    /// </remarks>
    public IReadOnlyList<Line> Render(int width)
    {
        _lastWidth = width;

        int promptRows = Prompt?.Count ?? 0;
        int bufferAllotment = Math.Max(1, _maxRows - promptRows);

        RenderResult r = _buffer.Render(width, allottedHeight: bufferAllotment);
        IReadOnlyList<Line> bufferRows = _isSecret ? MaskRows(r.VisibleRows) : r.VisibleRows;

        // Caret within the assembled rows: offset by prompt row count.
        _lastCaret = (r.CaretPosition.Row + promptRows, r.CaretPosition.Col);

        // Assemble and truncate to MaxRows.
        int totalCount = promptRows + bufferRows.Count;
        Line[] result = new Line[Math.Min(totalCount, _maxRows)];
        int idx = 0;

        for (int i = 0; i < promptRows && idx < result.Length; i++)
            result[idx++] = Prompt![i];

        for (int i = 0; i < bufferRows.Count && idx < result.Length; i++)
            result[idx++] = bufferRows[i];

        return result;
    }

    // ── IModalOverlay ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public OverlayCloseKind? CloseRequest { get; private set; }

    // ── IOverlay ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the prompt-offset caret from the last <see cref="Render"/> call,
    /// or <see langword="null"/> before the first render.
    /// </remarks>
    public (int Row, int Col)? CaretInOverlay => _lastCaret;

    // ── Internal surface ─────────────────────────────────────────────────────

    /// <summary>
    /// Optional preamble lines displayed above the input field. Set via the constructor.
    /// When non-<see langword="null"/> and non-empty, <see cref="Render"/> prepends all lines
    /// and reserves a row for each of them in the <see cref="MaxRows"/> budget.
    /// </summary>
    internal IReadOnlyList<Line>? Prompt { get; }

    /// <summary>Whether the input is secret (masked in the rendered rows).</summary>
    internal bool IsSecret => _isSecret;

    /// <summary>The real (unmasked) text currently in the buffer.</summary>
    internal string Text => _buffer.Text;

    /// <summary>
    /// <see langword="true"/> once the user has made any buffer-mutating keystroke
    /// (insert, Backspace, Delete). Sticky — never reset after being set.
    /// </summary>
    internal bool UserEdited => _userEdited;

    /// <summary>
    /// Seeds the cached width used by width-dependent <see cref="HandleKey"/> operations
    /// (Home, End, Up, Down) before the first <see cref="Render"/> call.
    /// Called by the open-dialog loop command on the loop thread immediately after the overlay
    /// is shown, so the real terminal width is available from the first key batch.
    /// </summary>
    internal void SeedWidth(int width) => _lastWidth = width;

    // ── Secret masking ────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces every Rune in each row with one bullet glyph per display column it occupies.
    /// This preserves column-width arithmetic so <see cref="CaretInOverlay"/> remains correct.
    /// </summary>
    private static Line[] MaskRows(IReadOnlyList<Line> rows)
    {
        Line[] masked = new Line[rows.Count];
        for (int i = 0; i < rows.Count; i++)
            masked[i] = MaskLine(rows[i]);
        return masked;
    }

    private static Line MaskLine(Line line)
    {
        // Rebuild segment list: each Rune → DisplayWidth(rune) copies of the mask glyph.
        List<Segment> maskedSegments = new(line.Segments.Count);
        foreach (Segment seg in line.Segments)
        {
            if (string.IsNullOrEmpty(seg.Text))
            {
                maskedSegments.Add(seg);
                continue;
            }

            int totalCols = 0;
            foreach (Rune r in seg.Text.EnumerateRunes())
                totalCols += DisplayWidth.Measure(r);

            string maskedText = new string('•', totalCols);
            maskedSegments.Add(new Segment(maskedText, seg.Style));
        }
        return new Line(maskedSegments);
    }
}
