using Dcli.Internal.FixedRegion;
using Dcli.Internal.Scrollback;

namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Mutable UI state owned exclusively by the render loop thread.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Thread discipline:</strong> this object must only ever be read or written on the
/// render loop thread — the single dedicated thread running <see cref="LoopEngine"/>.
/// No locks are needed because the single-writer loop is the only mutator.
/// </para>
/// <para>
/// <strong>Paint-state fields</strong> (<see cref="LiveWindowRows"/>, <see cref="FixedRegionRows"/>,
/// <see cref="CaretPosition"/>, <see cref="IsCursorVisible"/>, <see cref="NewlyCommittedRows"/>):
/// populated by §9 (scrollback) and §10 (fixed region). §8's <see cref="VtFrameRenderer"/> reads
/// them directly — the painter emits whatever the model holds. Tests set them directly to produce
/// deterministic golden frames.
/// </para>
/// <para>
/// <strong>Dirty flag:</strong> the loop sets <see cref="IsDirty"/> to <see langword="true"/>
/// whenever any state changes (command applied, input event processed). The flag is cleared
/// after each paint. A frame is only produced when dirty AND the minimum frame interval has
/// elapsed, giving the throttled-coalescing cadence.
/// </para>
/// <para>
/// <strong>Terminal size snapshot:</strong> <see cref="Columns"/> and <see cref="Rows"/> are
/// read by consumers via a volatile snapshot (Decision 10 — "value reads served from snapshot,
/// not round-trip"). The snapshot is initialised from the injected
/// <see cref="ITerminalSizeSource"/> and updated by resize messages. Resize events flow in
/// via <c>PosixResizeWatcher</c> (§13.1).
/// </para>
/// <para>
/// <strong>MaxFixedHeight:</strong> recorded from <see cref="TerminalOptions.MaxFixedHeight"/>
/// but not yet enforced. §10 will apply the fixed-region MaxHeight budget
/// (<c>clamp(appSet ?? 50%, 8, rows)</c>) when the fixed-region painter is implemented.
/// </para>
/// </remarks>
internal sealed class RenderModel
{
    /// <summary>
    /// Initialises the render model with the terminal dimensions reported by
    /// <paramref name="sizeSource"/>.
    /// </summary>
    /// <param name="sizeSource">Provides the initial terminal dimensions.</param>
    /// <param name="maxFixedHeight">
    /// Optional consumer-supplied cap on the fixed-region height (rows). Recorded here;
    /// §10's painter will enforce it.
    /// </param>
    internal RenderModel(ITerminalSizeSource sizeSource, int? maxFixedHeight = null)
    {
        ArgumentNullException.ThrowIfNull(sizeSource);
        (Columns, Rows) = sizeSource.GetSize();
        MaxFixedHeight = maxFixedHeight;
    }

    /// <summary>
    /// Whether any mutation has occurred since the last paint. Cleared by
    /// <see cref="ClearDirty"/>; set by callers applying commands or input events.
    /// </summary>
    internal bool IsDirty { get; private set; }

    /// <summary>Current terminal width in columns (from the latest size snapshot).</summary>
    internal int Columns { get; private set; }

    /// <summary>Current terminal height in rows (from the latest size snapshot).</summary>
    internal int Rows { get; private set; }

    /// <summary>
    /// Consumer-supplied cap on the fixed-region height in rows.
    /// <see langword="null"/> means no explicit cap. Recorded but not yet enforced;
    /// §10 will apply the full <c>clamp(appSet ?? 50%, 8, rows)</c> budget.
    /// </summary>
    internal int? MaxFixedHeight { get; }

    // ── Paint-state (loop-thread-owned, no locks) ────────────────────────────
    // Populated by §9 (scrollback) and §10 (fixed region).
    // The painter reads them verbatim; §8 tests set them directly.

    /// <summary>
    /// Pre-wrapped visual rows for the live window, already fitted to <see cref="Columns"/>.
    /// Each row is a list of styled segments. §9 fills this; painter emits them as-is.
    /// </summary>
    internal IReadOnlyList<Line> LiveWindowRows { get; set; } = [];

    /// <summary>
    /// Pre-composed rows for the fixed region (input bar, status line, overlays).
    /// Each row is a list of styled segments. §10/§11 fill this; painter emits them as-is.
    /// </summary>
    internal IReadOnlyList<Line> FixedRegionRows { get; set; } = [];

    /// <summary>
    /// Desired hardware-cursor position within the frame (zero-based, relative to the first
    /// row of the frame). <see langword="null"/> defers to <see cref="IsCursorVisible"/>.
    /// §10 sets this to the input-editor caret.
    /// </summary>
    internal (int Row, int Col)? CaretPosition { get; set; }

    /// <summary>
    /// Whether the hardware cursor should be visible at end of frame.
    /// <see langword="true"/> (default) = place cursor at <see cref="CaretPosition"/> and show it.
    /// <see langword="false"/> = hide cursor (modal-dialog case). §11 drives this via
    /// <see cref="ActiveOverlay"/>.<see cref="IOverlay.HidesCursor"/>.
    /// </summary>
    internal bool IsCursorVisible { get; set; } = true;

    /// <summary>
    /// Rows that became committed (scrolled above the anchor) this frame.
    /// §9's <see cref="ScrollbackModel.PrePaint"/> fills this before each paint;
    /// <see cref="ScrollbackModel.ClearCommitted"/> resets it to empty after the frame so
    /// committed rows are emitted exactly once and never rewritten.
    /// </summary>
    internal IReadOnlyList<Line> NewlyCommittedRows { get; set; } = [];

    /// <summary>
    /// The scrollback model (§9). Owned by the render loop; called from the loop's
    /// apply→paint cycle via <see cref="ScrollbackModel.PrePaint"/> and
    /// <see cref="ScrollbackModel.ClearCommitted"/>.
    /// </summary>
    internal ScrollbackModel Scrollback { get; } = new();

    /// <summary>
    /// The fixed-region composer (§10). Populates <see cref="FixedRegionRows"/> and
    /// <see cref="EditorCaretLocal"/> each paint cycle, before scrollback PrePaint.
    /// </summary>
    internal FixedRegionComposer FixedRegion { get; } = new(new TextBuffer(), new PreambleLine(), new StatusLine());

    /// <summary>
    /// The editor-relative caret position set by <see cref="FixedRegionComposer.Compose"/>:
    /// (row within the visible input rows, display col). <see langword="null"/> when the caret
    /// is hidden (degenerate tiny-terminal or modal dialog — §11). The loop thread offsets the
    /// row by <see cref="LiveWindowRows"/>.Count after scrollback PrePaint to produce
    /// <see cref="CaretPosition"/>.
    /// </summary>
    internal (int Row, int Col)? EditorCaretLocal { get; set; }

    // ── Active overlay (§11) ──────────────────────────────────────────────────

    /// <summary>
    /// The currently active overlay, or <see langword="null"/> when none is shown.
    /// At most one overlay is active at a time (the OverlayState invariant).
    /// <see cref="FixedRegionComposer.Compose"/> slots this into the fixed region each frame.
    /// </summary>
    internal IOverlay? ActiveOverlay { get; private set; }

    /// <summary>
    /// Shows an autocomplete overlay. No-op when a modal overlay (<see cref="IModalOverlay"/>) is
    /// already active (modal takes priority — opening autocomplete under a modal dialog would
    /// violate the single-active-overlay invariant and the modal-suppresses-autocomplete rule).
    /// </summary>
    internal void ShowAutocomplete(Autocomplete autocomplete)
    {
        if (ActiveOverlay is IModalOverlay)
            return;
        ActiveOverlay = autocomplete;
    }

    /// <summary>
    /// Shows a modal overlay and registers its parameterless completion delegate.
    /// Suppresses any active autocomplete (modal takes priority).
    /// </summary>
    /// <param name="overlay">The modal overlay to show.</param>
    /// <param name="completion">
    /// Called on the loop thread when the overlay is dismissed. The closure captures the overlay
    /// reference and reads its state (<see cref="IModalOverlay.CloseRequest"/> and any other
    /// overlay-specific fields) to complete the caller's <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/>.
    /// </param>
    internal void ShowModal(IModalOverlay overlay, Action? completion = null)
    {
        ActiveOverlay = overlay;
        PendingModalCompletion = completion;
    }

    /// <summary>
    /// The loop-thread-owned completion delegate for the currently active modal overlay.
    /// Set by <see cref="ShowModal"/> alongside the overlay; cleared by <see cref="ClearOverlay"/>.
    /// Invoked by the loop's dismiss hook before clearing the overlay.
    /// </summary>
    internal Action? PendingModalCompletion { get; private set; }

    /// <summary>
    /// Clears the active overlay and its pending completion delegate.
    /// Called by the loop after <see cref="IOverlay.IsDismissed"/> becomes <see langword="true"/>
    /// (autocomplete hidden, modal overlay Enter/Escape, or external token cancellation).
    /// </summary>
    internal void ClearOverlay()
    {
        ActiveOverlay = null;
        PendingModalCompletion = null;
    }

    // ── Dirtyness ─────────────────────────────────────────────────────────────

    /// <summary>Marks the model as dirty so the loop will produce a frame.</summary>
    internal void MarkDirty() => IsDirty = true;

    /// <summary>Clears the dirty flag after a frame has been painted.</summary>
    internal void ClearDirty() => IsDirty = false;

    /// <summary>Updates the terminal size in the model.</summary>
    internal void UpdateSize(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
    }
}
