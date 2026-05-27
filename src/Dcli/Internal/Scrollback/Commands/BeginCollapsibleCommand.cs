using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback.Commands;

/// <summary>
/// Creates a new <see cref="Collapsible"/>, appends it to the scrollback live window,
/// and delivers the reference to the caller via a callback.
/// </summary>
/// <remarks>
/// The caller captures the collapsible reference via a callback supplied at construction,
/// which is invoked synchronously on the loop thread during <see cref="ILoopCommand.Apply"/>.
/// </remarks>
internal sealed class BeginCollapsibleCommand : ILoopCommand
{
    private readonly ScrollbackModel _scrollback;
    private readonly Line _summary;
    private readonly IReadOnlyList<Line> _hiddenLines;
    private readonly Action<Collapsible> _capture;

    internal BeginCollapsibleCommand(
        ScrollbackModel scrollback,
        Line summary,
        IReadOnlyList<Line> hiddenLines,
        Action<Collapsible> capture)
    {
        ArgumentNullException.ThrowIfNull(scrollback);
        ArgumentNullException.ThrowIfNull(hiddenLines);
        ArgumentNullException.ThrowIfNull(capture);
        _scrollback = scrollback;
        _summary = summary;
        _hiddenLines = hiddenLines;
        _capture = capture;
    }

    void ILoopCommand.Apply(RenderModel model)
    {
        Collapsible collapsible = _scrollback.BeginCollapsible(_summary, _hiddenLines, model);
        _capture(collapsible);
    }
}
