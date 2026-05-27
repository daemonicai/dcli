using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Scrollback.Commands;

/// <summary>
/// Requests expansion of a <see cref="Collapsible"/>. Handles both the normal (fits live
/// window) and oversized (reprints into flow) cases via
/// <see cref="ScrollbackModel.ExpandCollapsible"/>.
/// </summary>
/// <remarks>
/// If the collapsible has already committed (dropped from the live list) this command is a
/// clean no-op: <see cref="ScrollbackModel.ExpandCollapsible"/> skips objects not present in
/// the live list.
/// </remarks>
internal sealed class ExpandCollapsibleCommand : ILoopCommand
{
    private readonly ScrollbackModel _scrollback;
    private readonly Collapsible _collapsible;

    internal ExpandCollapsibleCommand(ScrollbackModel scrollback, Collapsible collapsible)
    {
        ArgumentNullException.ThrowIfNull(scrollback);
        ArgumentNullException.ThrowIfNull(collapsible);
        _scrollback = scrollback;
        _collapsible = collapsible;
    }

    void ILoopCommand.Apply(RenderModel model) =>
        _scrollback.ExpandCollapsible(_collapsible, model);
}
