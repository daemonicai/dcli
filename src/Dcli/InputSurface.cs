using Dcli.Internal.RenderLoop;

namespace Dcli;

/// <summary>
/// Consumer-facing surface for programmatic control of the input editor.
/// </summary>
/// <remarks>
/// <para>
/// All methods post fire-and-forget <see cref="Internal.RenderLoop.ILoopCommand"/>s; they
/// return before the command is applied or a frame is painted.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> all methods are safe to call from any thread.
/// </para>
/// <para>
/// <strong>No <c>InputChanged</c> on programmatic mutation:</strong> <see cref="SetText"/>,
/// <see cref="Clear"/>, and <see cref="SetPrompt(Line)"/> do NOT emit <see cref="InputChanged"/>.
/// That event is reserved for user-driven edits so that a consumer reacting to
/// <see cref="InputChanged"/> (e.g. to update autocomplete candidates) cannot trigger a feedback
/// loop from its own programmatic writes.
/// </para>
/// <para>
/// <strong>Documented gaps:</strong>
/// <list type="bullet">
///   <item><c>ReadOnly</c> — preventing user edits — is similarly deferred.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class InputSurface : IInput
{
    private readonly LoopEngine _loop;

    internal InputSurface(LoopEngine loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
    }

    /// <summary>
    /// Replaces the entire input buffer with <paramref name="text"/> and moves the caret to
    /// the end. Does not emit <see cref="InputChanged"/>.
    /// </summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _loop.Post(new SetTextCommand(text));
    }

    /// <summary>
    /// Clears the input buffer and moves the caret to position 0.
    /// Does not emit <see cref="InputChanged"/>.
    /// </summary>
    public void Clear()
    {
        _loop.Post(new ClearCommand());
    }

    /// <summary>
    /// Sets the prompt prefix rendered immediately before the editable region on the first visual row.
    /// An empty line clears the prompt and renders no prefix.
    /// Does not emit <see cref="InputChanged"/>.
    /// </summary>
    public void SetPrompt(Line line)
    {
        _loop.Post(new SetPromptCommand(line));
    }

    /// <summary>
    /// Sets the prompt prefix to a plain-text string.
    /// Null or empty clears the prompt and renders no prefix.
    /// Does not emit <see cref="InputChanged"/>.
    /// </summary>
    /// <param name="text">The plain text to use as the prompt prefix. Null or empty clears the prompt.</param>
    public void SetPrompt(string text)
    {
        Line line = string.IsNullOrEmpty(text) ? new Line([]) : Line.FromText(text);
        _loop.Post(new SetPromptCommand(line));
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    private sealed class SetTextCommand : ILoopCommand
    {
        private readonly string _text;

        internal SetTextCommand(string text) => _text = text;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.FixedRegion.Editor.SetText(_text);
            model.MarkDirty();
        }
    }

    private sealed class ClearCommand : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            model.FixedRegion.Editor.Clear();
            model.MarkDirty();
        }
    }

    private sealed class SetPromptCommand : ILoopCommand
    {
        private readonly Line _line;

        internal SetPromptCommand(Line line) => _line = line;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.FixedRegion.Editor.SetPrompt(_line);
            model.MarkDirty();
        }
    }
}
