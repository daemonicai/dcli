namespace Dcli.Internal.FixedRegion;

/// <summary>
/// Owns the preamble row(s) rendered directly above the input editor.
/// </summary>
/// <remarks>
/// <para>
/// A consumer sets <see cref="Rows"/> to replace the displayed content. Setting to an empty list
/// means no preamble rows are rendered. All rows are painted verbatim — no wrapping is applied.
/// </para>
/// <para>
/// Unlike the status bar, the preamble is not sacred: when the height budget is tight, preamble
/// rows are truncated before the editor loses its last visible row.
/// </para>
/// </remarks>
internal sealed class PreambleLine
{
    /// <summary>
    /// The preamble rows to display directly above the input editor. May be empty (no preamble rendered).
    /// Set by the consumer; read by <see cref="FixedRegionComposer"/> during composition.
    /// </summary>
    internal IReadOnlyList<Line> Rows { get; set; } = [];
}
