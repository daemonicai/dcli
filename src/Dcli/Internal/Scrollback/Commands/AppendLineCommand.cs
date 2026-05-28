using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback.Commands;

/// <summary>
/// Appends a single styled <see cref="Line"/> to the scrollback live window.
/// </summary>
internal sealed class AppendLineCommand : ILoopCommand
{
    private readonly ScrollbackModel _scrollback;
    private readonly Line _line;

    internal AppendLineCommand(ScrollbackModel scrollback, Line line)
    {
        ArgumentNullException.ThrowIfNull(scrollback);
        ArgumentNullException.ThrowIfNull(line);
        _scrollback = scrollback;
        _line = line;
    }

    void ILoopCommand.Apply(RenderModel model) =>
        _scrollback.Append(new TextBlock(_line), model);
}
