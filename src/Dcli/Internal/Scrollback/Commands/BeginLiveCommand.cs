using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback.Commands;

/// <summary>
/// Begins a new live block in the scrollback model and captures the resulting
/// <see cref="LiveBlock"/> so the caller can issue subsequent mutation commands.
/// </summary>
/// <remarks>
/// The caller captures the live block reference via a callback supplied at construction,
/// which is invoked synchronously on the loop thread during <see cref="ILoopCommand.Apply"/>.
/// </remarks>
internal sealed class BeginLiveCommand : ILoopCommand
{
    private readonly ScrollbackModel _scrollback;
    private readonly Action<LiveBlock> _capture;

    internal BeginLiveCommand(ScrollbackModel scrollback, Action<LiveBlock> capture)
    {
        ArgumentNullException.ThrowIfNull(scrollback);
        ArgumentNullException.ThrowIfNull(capture);
        _scrollback = scrollback;
        _capture = capture;
    }

    void ILoopCommand.Apply(RenderModel model)
    {
        LiveBlock block = _scrollback.BeginLive(model);
        _capture(block);
    }
}
