using System.Globalization;
using System.Text;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;

namespace Dcli.Testing;

// ── Overlay descriptor ────────────────────────────────────────────────────────

/// <summary>
/// The kind of overlay active in a <see cref="FrameSnapshot"/>.
/// </summary>
public enum OverlayKind
{
    /// <summary>No overlay is active.</summary>
    None,
    /// <summary>An autocomplete dropdown is active.</summary>
    Autocomplete,
    /// <summary>A single-select or multi-select dialog is active.</summary>
    Dialog,
    /// <summary>An input-text dialog is active.</summary>
    Input,
}

/// <summary>
/// A lightweight descriptor for the active overlay in a <see cref="FrameSnapshot"/>.
/// </summary>
/// <remarks>
/// For list-style overlays (<see cref="OverlayKind.Autocomplete"/>, <see cref="OverlayKind.Dialog"/>)
/// <see cref="SelectedIndex"/> is the zero-based selected row, or <c>-1</c> when the list is empty.
/// For <see cref="OverlayKind.Input"/> overlays <see cref="InputText"/> is the current buffer text
/// when <see cref="IsSecret"/> is <see langword="false"/>; <see langword="null"/> when secret.
/// All other fields are <see langword="null"/> / <c>0</c> when not applicable.
/// </remarks>
public sealed record OverlayDescriptor
{
    /// <summary>The kind of active overlay.</summary>
    public required OverlayKind Kind { get; init; }

    /// <summary>
    /// Selected item index (zero-based) for <see cref="OverlayKind.Autocomplete"/> and
    /// <see cref="OverlayKind.Dialog"/> overlays. <c>-1</c> when the list is empty or the overlay
    /// kind does not use a list.
    /// </summary>
    public int SelectedIndex { get; init; } = -1;

    /// <summary>
    /// Number of visible list rows at snapshot time. <c>0</c> when not applicable.
    /// </summary>
    public int VisibleRowCount { get; init; }

    /// <summary>
    /// Current buffer text for <see cref="OverlayKind.Input"/> overlays when
    /// <see cref="IsSecret"/> is <see langword="false"/>; otherwise <see langword="null"/>.
    /// <see langword="null"/> for all other overlay kinds.
    /// </summary>
    public string? InputText { get; init; }

    /// <summary>
    /// <see langword="true"/> when the input overlay is in secret (password) mode.
    /// Always <see langword="false"/> for non-input overlay kinds.
    /// </summary>
    public bool IsSecret { get; init; }

    /// <summary>The singleton descriptor for <see cref="OverlayKind.None"/>.</summary>
    internal static readonly OverlayDescriptor NoOverlay = new() { Kind = OverlayKind.None };
}

// ── FrameSnapshot ─────────────────────────────────────────────────────────────

/// <summary>
/// An immutable snapshot of the logical frame produced by the most recent paint.
/// </summary>
/// <remarks>
/// <para>
/// Obtain a snapshot via <see cref="HeadlessTerminal.Snapshot"/> after calling
/// <see cref="HeadlessTerminal.SettleAsync"/>. The snapshot is captured at the moment of
/// property access; subsequent paints do not mutate it.
/// </para>
/// <para>
/// Style information is stored in the <see cref="Line"/> and <see cref="Segment"/> objects but
/// stripped when producing the human-readable string via <see cref="FrameSnapshotPrinter.PrettyPrint"/>.
/// Golden-frame assertions should use <see cref="FrameSnapshotPrinter.PrettyPrint"/> so that
/// tests assert structure rather than raw ANSI codes.
/// </para>
/// <para>
/// When no paint has occurred yet (pre-paint), <see cref="HeadlessTerminal.Snapshot"/> returns
/// an empty snapshot: <see cref="Size"/> is <c>(0, 0)</c>, all row lists are empty, <see cref="Caret"/>
/// is <see langword="null"/>, and <see cref="Overlay"/> is <see cref="OverlayKind.None"/>.
/// </para>
/// </remarks>
public sealed record FrameSnapshot
{
    /// <summary>
    /// The live-window visual rows at the time of the snapshot.
    /// These are the rows above the fixed region that have not yet been committed to scrollback.
    /// </summary>
    public required IReadOnlyList<Line> LiveWindowRows { get; init; }

    /// <summary>
    /// The fixed-region rows (input editor, status line, overlays) at the time of the snapshot.
    /// </summary>
    public required IReadOnlyList<Line> FixedRegionRows { get; init; }

    /// <summary>
    /// Caret position in the rendered frame (zero-based row and column), or <see langword="null"/>
    /// when no caret is placed (cursor hidden or modal dialog active).
    /// </summary>
    public required (int Row, int Col)? Caret { get; init; }

    /// <summary>Whether the hardware cursor is visible at end of frame.</summary>
    public required bool IsCursorVisible { get; init; }

    /// <summary>Terminal size at the time of the snapshot.</summary>
    public required (int Columns, int Rows) Size { get; init; }

    /// <summary>
    /// Rows that were committed to scrollback (crossed the commit horizon) on this frame.
    /// Empty when no rows were committed.
    /// </summary>
    public required IReadOnlyList<Line> NewlyCommittedRows { get; init; }

    /// <summary>Descriptor for the active overlay, or <see cref="OverlayKind.None"/> when none.</summary>
    public required OverlayDescriptor Overlay { get; init; }

    // ── Internal factory ──────────────────────────────────────────────────────

    /// <summary>The empty snapshot returned before the first paint.</summary>
    internal static readonly FrameSnapshot Empty = new()
    {
        LiveWindowRows = [],
        FixedRegionRows = [],
        Caret = null,
        IsCursorVisible = true,
        Size = (0, 0),
        NewlyCommittedRows = [],
        Overlay = OverlayDescriptor.NoOverlay,
    };

    /// <summary>
    /// Captures an immutable snapshot from the render model produced by the most recent paint.
    /// </summary>
    /// <param name="model">The model to snapshot; must not be <see langword="null"/>.</param>
    internal static FrameSnapshot Capture(RenderModel model)
    {
        // Defensive copies: future paints mutate the model's lists in-place. We snapshot by
        // allocating new arrays so the snapshot's row lists are stable after capture.
        Line[] liveRows = [.. model.LiveWindowRows];
        Line[] fixedRows = [.. model.FixedRegionRows];
        Line[] committed = [.. model.NewlyCommittedRows];

        return new FrameSnapshot
        {
            LiveWindowRows = liveRows,
            FixedRegionRows = fixedRows,
            Caret = model.CaretPosition,
            IsCursorVisible = model.IsCursorVisible,
            Size = (model.Columns, model.Rows),
            NewlyCommittedRows = committed,
            Overlay = BuildOverlay(model.ActiveOverlay, model.Columns),
        };
    }

    private static OverlayDescriptor BuildOverlay(IOverlay? overlay, int terminalWidth)
    {
        if (overlay is null)
            return OverlayDescriptor.NoOverlay;

        if (overlay is Autocomplete autocomplete)
        {
            IReadOnlyList<Line> rows = autocomplete.Render(terminalWidth);
            return new OverlayDescriptor
            {
                Kind = OverlayKind.Autocomplete,
                SelectedIndex = autocomplete.SelectedIndex,
                VisibleRowCount = rows.Count,
            };
        }

        if (overlay is Dialog dialog)
        {
            IReadOnlyList<Line> rows = dialog.Render(terminalWidth);
            return new OverlayDescriptor
            {
                Kind = OverlayKind.Dialog,
                SelectedIndex = dialog.List.SelectedIndex,
                VisibleRowCount = rows.Count,
            };
        }

        if (overlay is InputDialog inputDialog)
        {
            IReadOnlyList<Line> rows = inputDialog.Render(terminalWidth);
            return new OverlayDescriptor
            {
                Kind = OverlayKind.Input,
                VisibleRowCount = rows.Count,
                IsSecret = inputDialog.IsSecret,
                InputText = inputDialog.IsSecret ? null : inputDialog.Text,
            };
        }

        // Unknown overlay type — expose as None rather than throwing.
        return OverlayDescriptor.NoOverlay;
    }
}

// ── FrameSnapshotPrinter ──────────────────────────────────────────────────────

/// <summary>
/// Produces a human-readable, style-stripped representation of a <see cref="FrameSnapshot"/>
/// suitable for golden-frame assertions.
/// </summary>
/// <remarks>
/// <para>
/// The output format is stable and intentionally simple — ASCII borders, row indices, and plain
/// text only. Style information (colours, bold, italic) is stripped so golden strings remain
/// readable and diff-friendly. No ANSI codes appear in the output.
/// </para>
/// <para>
/// Format overview:
/// <list type="bullet">
///   <item><description>Any newly-committed rows are printed first, one per line, with a
///   <c>[committed N]</c> prefix.</description></item>
///   <item><description>A bordered box follows. Live-window rows are numbered from 0 upward.
///   A <c>--- horizon ---</c> line separates the live window from the fixed region.
///   Fixed-region rows continue the numbering.</description></item>
///   <item><description>A summary line at the end of the box records caret position and overlay
///   state.</description></item>
/// </list>
/// </para>
/// </remarks>
public static class FrameSnapshotPrinter
{
    /// <summary>
    /// Renders <paramref name="snapshot"/> as a bordered ASCII frame string.
    /// The output format is stable and suitable for golden-frame test assertions.
    /// </summary>
    /// <param name="snapshot">The snapshot to render.</param>
    /// <returns>A multi-line string representing the frame.</returns>
    public static string PrettyPrint(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Determine box width from the terminal size recorded in the snapshot.
        // Minimum 20 columns so the summary line is always readable.
        int contentWidth = Math.Max(snapshot.Size.Columns, 20);
        int innerWidth = contentWidth - 2; // box inner width (between '|' borders)

        StringBuilder sb = new();

        // ── Newly committed rows ──────────────────────────────────────────────
        for (int i = 0; i < snapshot.NewlyCommittedRows.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"[committed {i}] ");
            sb.AppendLine(ExtractText(snapshot.NewlyCommittedRows[i]));
        }

        // ── Box top border ────────────────────────────────────────────────────
        sb.Append('+');
        sb.Append('-', contentWidth);
        sb.AppendLine("+");

        // ── Live-window rows ──────────────────────────────────────────────────
        int rowIdx = 0;
        foreach (Line row in snapshot.LiveWindowRows)
        {
            AppendRow(sb, rowIdx, row, innerWidth);
            rowIdx++;
        }

        // ── Horizon line ──────────────────────────────────────────────────────
        if (snapshot.FixedRegionRows.Count > 0)
        {
            string horizonLabel = "--- horizon ---";
            int horizonPad = contentWidth - horizonLabel.Length;
            int leftPad = horizonPad / 2;
            int rightPad = horizonPad - leftPad;
            sb.Append('|');
            sb.Append(' ', leftPad);
            sb.Append(horizonLabel);
            sb.Append(' ', rightPad);
            sb.AppendLine("|");

            // ── Fixed-region rows ─────────────────────────────────────────────
            foreach (Line row in snapshot.FixedRegionRows)
            {
                AppendRow(sb, rowIdx, row, innerWidth);
                rowIdx++;
            }
        }

        // ── Summary lines (caret + overlay) — not width-constrained so they're always readable ──
        string caretStr = snapshot.Caret is { } pos
            ? string.Create(CultureInfo.InvariantCulture,
                $"Caret: (r={pos.Row}, c={pos.Col}, visible={snapshot.IsCursorVisible})")
            : "Caret: hidden";
        string overlayStr = BuildOverlayStr(snapshot.Overlay);
        // Each summary line: "| <label>|" — unbounded width for readability.
        sb.Append("| ");
        sb.Append(caretStr);
        sb.AppendLine();
        sb.Append("| ");
        sb.Append(overlayStr);
        sb.AppendLine();

        // ── Box bottom border ─────────────────────────────────────────────────
        sb.Append('+');
        sb.Append('-', contentWidth);
        sb.Append('+');

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowIdx, Line row, int innerWidth)
    {
        string text = ExtractText(row);
        // Prefix: " NNN | " (7 chars)
        string prefix = string.Create(CultureInfo.InvariantCulture, $" {rowIdx,3} | ");
        int textWidth = innerWidth - prefix.Length;
        string displayText = textWidth > 0 && text.Length > textWidth
            ? text[..textWidth]
            : text;
        // Pad to fill inner width
        int totalInner = prefix.Length + displayText.Length;
        int pad = innerWidth - totalInner;

        sb.Append('|');
        sb.Append(prefix);
        sb.Append(displayText);
        if (pad > 0)
            sb.Append(' ', pad);
        sb.AppendLine("|");
    }

    private static string ExtractText(Line line)
    {
        if (line.Segments.Count == 0)
            return string.Empty;

        StringBuilder sb = new();
        foreach (Segment seg in line.Segments)
            sb.Append(seg.Text);
        return sb.ToString();
    }

    private static string BuildOverlayStr(OverlayDescriptor overlay) =>
        overlay.Kind switch
        {
            OverlayKind.None => "Overlay: None",
            OverlayKind.Autocomplete => string.Create(CultureInfo.InvariantCulture,
                $"Overlay: Autocomplete(selected={overlay.SelectedIndex}, rows={overlay.VisibleRowCount})"),
            OverlayKind.Dialog => string.Create(CultureInfo.InvariantCulture,
                $"Overlay: Dialog(selected={overlay.SelectedIndex}, rows={overlay.VisibleRowCount})"),
            OverlayKind.Input when overlay.IsSecret => string.Create(CultureInfo.InvariantCulture,
                $"Overlay: Input(secret, rows={overlay.VisibleRowCount})"),
            OverlayKind.Input => string.Create(CultureInfo.InvariantCulture,
                $"Overlay: Input(text=\"{overlay.InputText}\", rows={overlay.VisibleRowCount})"),
            _ => "Overlay: None",
        };
}
