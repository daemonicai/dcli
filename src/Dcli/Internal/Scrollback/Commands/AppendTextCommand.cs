using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback.Commands;

/// <summary>
/// Appends plain text to a live block's accumulation buffer.
/// </summary>
internal sealed class AppendTextCommand : ILoopCommand
{
    private readonly LiveBlock _block;
    private readonly string _text;

    internal AppendTextCommand(LiveBlock block, string text)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(text);
        _block = block;
        _text = text;
    }

    void ILoopCommand.Apply(RenderModel model)
    {
        _block.AppendText(_text);
        model.MarkDirty();
    }
}
