namespace Dcli.Internal.Scrollback;

/// <summary>
/// A single element in the flat scrollback list.
/// </summary>
/// <remarks>
/// Implementations: <see cref="TextBlock"/> (this chunk), <c>Collapsible</c> (Chunk B).
/// <see cref="Render"/> is called only while the object is live (in the live window);
/// frozen objects were rendered once and may be dropped from memory.
/// </remarks>
internal interface ILineObject
{
    /// <summary>
    /// Renders this line-object into visual rows at the given terminal width.
    /// </summary>
    /// <param name="width">
    /// Display column width. Values &lt;= 0 are handled by <see cref="LineWrapper"/> (clamped to 1).
    /// </param>
    /// <returns>
    /// One or more visual rows. Never empty. Each row is a <see cref="Line"/> of
    /// styled <see cref="Segment"/>s, already fitted to <paramref name="width"/> columns.
    /// </returns>
    IReadOnlyList<Line> Render(int width);
}
