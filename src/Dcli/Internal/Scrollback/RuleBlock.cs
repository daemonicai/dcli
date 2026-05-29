namespace Dcli.Internal.Scrollback;

/// <summary>
/// An immutable line-object that renders as a horizontal rule spanning the full content width.
/// </summary>
/// <remarks>
/// <para>
/// Width is resolved at paint time — <see cref="Render"/> receives the current live-window
/// content width (in columns) and returns a single <see cref="Line"/> of U+2500 BOX DRAWINGS
/// LIGHT HORIZONTAL characters. This means the rule expands or contracts correctly after a
/// terminal resize without any stored fixed width.
/// </para>
/// <para>
/// Like <see cref="TextBlock"/>, this block performs no wrapping — a single row is always
/// returned regardless of width.
/// </para>
/// </remarks>
internal sealed class RuleBlock : ILineObject
{
    /// <inheritdoc/>
    public IReadOnlyList<Line> Render(int width)
    {
        int w = Math.Max(1, width);
        Segment rule = new(new string('─', w));
        return [new Line([rule])];
    }
}
