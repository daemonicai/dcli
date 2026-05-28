namespace Dcli.Internal.FixedRegion;

/// <summary>
/// Owns the status row(s) that are always shown at the bottom of the fixed region.
/// </summary>
/// <remarks>
/// <para>
/// The status line is <em>sacred</em> (Decision 8): it is always fully rendered regardless of the
/// height budget. When the budget is too small to show both the input editor and the status, the
/// input shrinks (or disappears) while the status remains visible.
/// </para>
/// <para>
/// A consumer sets <see cref="Rows"/> to replace the displayed content. Setting to an empty list
/// means no status row is rendered. All rows are painted verbatim — no wrapping is applied.
/// </para>
/// </remarks>
internal sealed class StatusLine
{
    /// <summary>
    /// The status rows to display. May be empty (no status rendered).
    /// Set by the consumer; read by <see cref="FixedRegionComposer"/> during composition.
    /// </summary>
    internal IReadOnlyList<Line> Rows { get; set; } = [];
}
