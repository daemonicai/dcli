using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback.Commands;

/// <summary>
/// Freezes a live block: marks it committed and triggers a repaint so the final content
/// is emitted into the live window before the block drains into native scrollback.
/// </summary>
internal sealed class CommitLiveBlockCommand : ILoopCommand
{
    private readonly ScrollbackModel _scrollback;
    private readonly LiveBlock _block;

    internal CommitLiveBlockCommand(ScrollbackModel scrollback, LiveBlock block)
    {
        ArgumentNullException.ThrowIfNull(scrollback);
        ArgumentNullException.ThrowIfNull(block);
        _scrollback = scrollback;
        _block = block;
    }

    void ILoopCommand.Apply(RenderModel model) =>
        _scrollback.CommitLiveBlock(_block, model);
}
