using Dcli.Internal.RenderLoop;
using Dcli.Internal.Scrollback;

namespace Dcli;

// ─────────────────────────────────────────────────────────────────────────────
// Public handle interfaces
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A handle to a live (mutable) scrollback block that is still visible in the live window.
/// All methods post fire-and-forget commands; they are applied on the render-loop thread.
/// </summary>
/// <remarks>
/// Obtain a handle via <see cref="ScrollbackSurface.BeginLive"/>.
/// The underlying <c>LiveBlock</c> object is pre-created on the calling thread and is only
/// <em>mutated</em> on the loop thread via commands. The handle merely carries the reference
/// into command constructors. This is the one permitted exception to the "no off-thread model
/// access" rule: the object is constructed off-thread but written exclusively on the loop thread.
/// </remarks>
public interface ILiveBlock
{
    /// <summary>
    /// Appends <paramref name="text"/> to the block's accumulation buffer.
    /// No-op after <see cref="Commit"/>. No-op if <see cref="SetContent"/> was already called.
    /// </summary>
    void AppendText(string text);

    /// <summary>
    /// Replaces the block's content wholesale with <paramref name="lines"/>.
    /// After this call <see cref="AppendText"/> is a no-op.
    /// No-op after <see cref="Commit"/>.
    /// </summary>
    void SetContent(IReadOnlyList<Line> lines);

    /// <summary>
    /// Freezes the block and removes it from the live window on the next paint.
    /// Its final rendered rows flow into native scrollback.
    /// </summary>
    void Commit();
}

/// <summary>
/// A handle to a collapsible scrollback block.
/// All methods post fire-and-forget commands; they are applied on the render-loop thread.
/// </summary>
/// <remarks>
/// Obtain a handle via <see cref="ScrollbackSurface.BeginCollapsible"/>.
/// The underlying <c>Collapsible</c> object is pre-created on the calling thread with
/// immutable <c>summary</c> and <c>hiddenLines</c>; only the loop thread mutates its expanded
/// state. Incremental append to the hidden-line list after construction is a documented gap —
/// the model stores hidden lines as an immutable snapshot at construction time.
/// </remarks>
public interface ICollapsible
{
    /// <summary>
    /// Expands the collapsible to reveal its hidden lines. One-way and idempotent.
    /// If already expanded or the block has scrolled past the commit horizon, this is a no-op.
    /// An oversized expansion reprints the hidden lines into native scrollback flow instead of
    /// expanding in-place.
    /// </summary>
    void Expand();
}

// ─────────────────────────────────────────────────────────────────────────────
// Façade surface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Consumer-facing surface for appending content to the scrollback live window.
/// </summary>
/// <remarks>
/// <para>
/// All methods post fire-and-forget <see cref="Internal.RenderLoop.ILoopCommand"/>s; they
/// return before the command is applied or a frame is painted.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> all methods are safe to call from any thread; internally
/// they post to the loop's inbound channel. The render-loop thread is the sole mutator of
/// scrollback state.
/// </para>
/// </remarks>
public sealed class ScrollbackSurface : IScrollback
{
    private readonly LoopEngine _loop;

    internal ScrollbackSurface(LoopEngine loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
    }

    /// <summary>
    /// Appends a single styled line to the scrollback live window.
    /// </summary>
    public void Append(Line line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _loop.Post(new AppendToScrollbackCommand(line));
    }

    /// <summary>
    /// Appends a plain-text line to the scrollback live window.
    /// </summary>
    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Append(Line.FromText(text));
    }

    /// <summary>
    /// Appends a horizontal rule to the scrollback live window.
    /// </summary>
    public void AppendRule()
    {
        _loop.Post(new AppendRuleToScrollbackCommand());
    }

    /// <summary>
    /// Begins a new live block in the scrollback, returning a handle for incremental mutation.
    /// </summary>
    /// <remarks>
    /// The underlying <c>LiveBlock</c> is pre-created on the calling thread and inserted into
    /// the live list via an enqueued command. Callers may immediately use the returned handle —
    /// there is no race window because handle methods also enqueue commands that are applied
    /// in FIFO order after the insert command.
    /// </remarks>
    public ILiveBlock BeginLive()
    {
        LiveBlock block = new();
        _loop.Post(new InsertLiveBlockCommand(block));
        return new LiveBlockHandle(_loop, block);
    }

    /// <summary>
    /// Begins a new collapsible block, returning a handle for one-time expansion.
    /// </summary>
    /// <param name="summary">The summary line shown while the block is collapsed.</param>
    /// <param name="hiddenLines">The lines revealed when the block is expanded.</param>
    /// <remarks>
    /// Hidden lines are captured as an immutable snapshot at construction time. Incremental
    /// append to the hidden-line list after the handle is created is a documented gap for a
    /// future refinement.
    /// </remarks>
    public ICollapsible BeginCollapsible(Line summary, IReadOnlyList<Line> hiddenLines)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(hiddenLines);
        Collapsible collapsible = new(summary, hiddenLines);
        _loop.Post(new InsertCollapsibleCommand(collapsible));
        return new CollapsibleHandle(_loop, collapsible);
    }

    // ── Internal handle implementations ───────────────────────────────────────

    private sealed class LiveBlockHandle : ILiveBlock
    {
        private readonly LoopEngine _loop;
        private readonly LiveBlock _block;

        internal LiveBlockHandle(LoopEngine loop, LiveBlock block)
        {
            _loop = loop;
            _block = block;
        }

        public void AppendText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            _loop.Post(new AppendTextToLiveBlockCommand(_block, text));
        }

        public void SetContent(IReadOnlyList<Line> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);
            _loop.Post(new SetLiveBlockContentCommand(_block, lines));
        }

        public void Commit()
        {
            _loop.Post(new CommitLiveBlockFacadeCommand(_block));
        }
    }

    private sealed class CollapsibleHandle : ICollapsible
    {
        private readonly LoopEngine _loop;
        private readonly Collapsible _collapsible;

        internal CollapsibleHandle(LoopEngine loop, Collapsible collapsible)
        {
            _loop = loop;
            _collapsible = collapsible;
        }

        public void Expand()
        {
            _loop.Post(new ExpandCollapsibleFacadeCommand(_collapsible));
        }
    }

    // ── Façade commands (obtain scrollback from model.Scrollback in Apply) ────

    private sealed class AppendToScrollbackCommand : ILoopCommand
    {
        private readonly Line _line;

        internal AppendToScrollbackCommand(Line line) => _line = line;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.Scrollback.Append(new TextBlock(_line), model);
            model.MarkDirty();
        }
    }

    private sealed class AppendRuleToScrollbackCommand : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            model.Scrollback.Append(new RuleBlock(), model);
            model.MarkDirty();
        }
    }

    private sealed class InsertLiveBlockCommand : ILoopCommand
    {
        private readonly LiveBlock _block;

        internal InsertLiveBlockCommand(LiveBlock block) => _block = block;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.Scrollback.Append(_block, model);
            model.MarkDirty();
        }
    }

    private sealed class InsertCollapsibleCommand : ILoopCommand
    {
        private readonly Collapsible _collapsible;

        internal InsertCollapsibleCommand(Collapsible collapsible) => _collapsible = collapsible;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.Scrollback.Append(_collapsible, model);
            model.MarkDirty();
        }
    }

    private sealed class AppendTextToLiveBlockCommand : ILoopCommand
    {
        private readonly LiveBlock _block;
        private readonly string _text;

        internal AppendTextToLiveBlockCommand(LiveBlock block, string text)
        {
            _block = block;
            _text = text;
        }

        void ILoopCommand.Apply(RenderModel model)
        {
            _block.AppendText(_text);
            model.MarkDirty();
        }
    }

    private sealed class SetLiveBlockContentCommand : ILoopCommand
    {
        private readonly LiveBlock _block;
        private readonly IReadOnlyList<Line> _lines;

        internal SetLiveBlockContentCommand(LiveBlock block, IReadOnlyList<Line> lines)
        {
            _block = block;
            _lines = lines;
        }

        void ILoopCommand.Apply(RenderModel model)
        {
            _block.SetContent(_lines);
            model.MarkDirty();
        }
    }

    private sealed class CommitLiveBlockFacadeCommand : ILoopCommand
    {
        private readonly LiveBlock _block;

        internal CommitLiveBlockFacadeCommand(LiveBlock block) => _block = block;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.Scrollback.CommitLiveBlock(_block, model);
        }
    }

    private sealed class ExpandCollapsibleFacadeCommand : ILoopCommand
    {
        private readonly Collapsible _collapsible;

        internal ExpandCollapsibleFacadeCommand(Collapsible collapsible) => _collapsible = collapsible;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.Scrollback.ExpandCollapsible(_collapsible, model);
        }
    }
}
