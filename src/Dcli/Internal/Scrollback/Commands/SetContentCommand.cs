using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback.Commands;

/// <summary>
/// Replaces a live block's content wholesale with the given lines.
/// </summary>
internal sealed class SetContentCommand : ILoopCommand
{
    private readonly LiveBlock _block;
    private readonly IReadOnlyList<Line> _lines;

    internal SetContentCommand(LiveBlock block, IReadOnlyList<Line> lines)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(lines);
        _block = block;
        _lines = lines;
    }

    void ILoopCommand.Apply(RenderModel model)
    {
        _block.SetContent(_lines);
        model.MarkDirty();
    }
}
