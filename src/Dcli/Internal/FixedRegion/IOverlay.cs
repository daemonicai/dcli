namespace Dcli.Internal.FixedRegion;

// ── OverlayPlacement ──────────────────────────────────────────────────────────

/// <summary>
/// Where in the fixed region an overlay is rendered relative to the input line.
/// </summary>
internal enum OverlayPlacement
{
    /// <summary>The overlay renders above the input line (e.g. a dialog).</summary>
    AboveInput,

    /// <summary>The overlay renders below the input line (e.g. autocomplete suggestions).</summary>
    BelowInput,
}

// ── OverlayCloseKind ──────────────────────────────────────────────────────────

/// <summary>
/// How an overlay was closed; surfaced by <see cref="Dialog.CloseRequest"/> and consumed by §12
/// to resolve the awaitable dialog result.
/// </summary>
internal enum OverlayCloseKind
{
    /// <summary>The user confirmed (Enter).</summary>
    Submit,

    /// <summary>
    /// The user navigated back (Backspace at position zero before any movement,
    /// when <see cref="Dialog"/> was constructed with <c>allowBack: true</c>).
    /// </summary>
    Back,

    /// <summary>The user cancelled (Escape).</summary>
    Cancel,
}

// ── IOverlay ──────────────────────────────────────────────────────────────────

/// <summary>
/// Front of the key intercept chain; contributes rows to the fixed region.
/// </summary>
/// <remarks>
/// The active overlay receives keys before the input editor. It either consumes a key
/// (returns <see langword="true"/> from <see cref="HandleKey"/>) or passes it on
/// (returns <see langword="false"/>). At most one overlay is active at a time
/// (<c>OverlayState</c> invariant, enforced in B-ii).
/// </remarks>
internal interface IOverlay
{
    /// <summary>Where the overlay is slotted relative to the input line.</summary>
    OverlayPlacement Placement { get; }

    /// <summary>
    /// When <see langword="true"/> the composer tells the painter to hide the hardware cursor.
    /// Only <see langword="true"/> while a modal <see cref="Dialog"/> is active.
    /// </summary>
    bool HidesCursor { get; }

    /// <summary>
    /// Viewport row cap set by the composer from the height budget.
    /// Forwarded directly to the hosted <see cref="ScrollableList.MaxRows"/>.
    /// </summary>
    int MaxRows { get; set; }

    /// <summary>
    /// <see langword="true"/> once the overlay should be removed from the model.
    /// For <see cref="Autocomplete"/> this is <c>!IsVisible</c>; for <see cref="Dialog"/> this is
    /// <c>CloseRequest is not null</c>.
    /// </summary>
    bool IsDismissed { get; }

    /// <summary>
    /// Attempts to handle a key press.
    /// </summary>
    /// <param name="key">The key event.</param>
    /// <returns>
    /// <see langword="true"/> if the overlay consumed the key and the event must not be forwarded;
    /// <see langword="false"/> if the key falls through to the next handler in the chain.
    /// </returns>
    bool HandleKey(KeyEvent key);

    /// <summary>
    /// Attempts to handle a paste event.
    /// </summary>
    /// <param name="text">The pasted text.</param>
    /// <returns>
    /// <see langword="true"/> if the overlay consumed the paste and the event must not be forwarded
    /// to the base editor; <see langword="false"/> if the paste falls through to the next handler.
    /// </returns>
    bool HandlePaste(string text);

    /// <summary>
    /// Renders the overlay rows at the given terminal width.
    /// Delegates to the hosted <see cref="ScrollableList.Render"/>.
    /// </summary>
    /// <param name="width">Terminal width in columns (forwarded as-is to the list).</param>
    /// <returns>The rows to slot into the fixed region; empty when nothing to show.</returns>
    IReadOnlyList<Line> Render(int width);

    /// <summary>
    /// The overlay's own caret position within its rendered rows (zero-based row and column),
    /// or <see langword="null"/> when the overlay does not own the hardware cursor.
    /// When non-<see langword="null"/> and the overlay was rendered this frame, the composer
    /// parks the hardware cursor here instead of at the main input editor.
    /// </summary>
    (int Row, int Col)? CaretInOverlay { get; }
}

// ── IModalOverlay ─────────────────────────────────────────────────────────────

/// <summary>
/// An <see cref="IOverlay"/> that has a deterministic dismissal signal: a
/// <see cref="CloseRequest"/> that is non-<see langword="null"/> once the overlay is closed
/// (Enter → Submit, Escape → Cancel). The loop's dismiss hook reads this to invoke the
/// awaitable completion and clear the overlay.
/// </summary>
internal interface IModalOverlay : IOverlay
{
    /// <summary>
    /// How the overlay was closed, or <see langword="null"/> while still open.
    /// </summary>
    OverlayCloseKind? CloseRequest { get; }
}
