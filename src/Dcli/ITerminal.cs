using System.Threading.Channels;

namespace Dcli;

// ─────────────────────────────────────────────────────────────────────────────
// Sub-surface interfaces
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Consumer-facing interface for appending content to the scrollback live window.
/// </summary>
/// <remarks>
/// All methods are fire-and-forget; they post commands and return before the render loop
/// applies them or paints a frame.
/// </remarks>
public interface IScrollback
{
    /// <summary>
    /// Appends a single styled line to the scrollback live window.
    /// </summary>
    /// <param name="line">The line to append.</param>
    void Append(Line line);

    /// <summary>
    /// Appends a plain-text line to the scrollback live window.
    /// Shorthand equivalent to <c>Append(Line.FromText(text))</c>.
    /// </summary>
    /// <param name="text">The plain text to append as a single unstyled line.</param>
    void Append(string text);

    /// <summary>
    /// Begins a new live block in the scrollback, returning a handle for incremental mutation.
    /// </summary>
    /// <returns>A handle whose methods post fire-and-forget commands to the render loop.</returns>
    ILiveBlock BeginLive();

    /// <summary>
    /// Begins a new collapsible block, returning a handle for one-time expansion.
    /// </summary>
    /// <param name="summary">The summary line shown while the block is collapsed.</param>
    /// <param name="hiddenLines">The lines revealed when the block is expanded.</param>
    /// <returns>A handle whose <see cref="ICollapsible.Expand"/> posts a command to the render loop.</returns>
    ICollapsible BeginCollapsible(Line summary, IReadOnlyList<Line> hiddenLines);
}

/// <summary>
/// Consumer-facing interface for programmatic control of the input editor.
/// </summary>
/// <remarks>
/// Methods do not emit <see cref="InputChanged"/> — that event is reserved for user-driven edits.
/// </remarks>
public interface IInput
{
    /// <summary>
    /// Replaces the entire input buffer with <paramref name="text"/> and moves the caret to the end.
    /// Does not emit <see cref="InputChanged"/>.
    /// </summary>
    /// <param name="text">The text to place in the editor.</param>
    void SetText(string text);

    /// <summary>
    /// Clears the input buffer and moves the caret to position 0.
    /// Does not emit <see cref="InputChanged"/>.
    /// </summary>
    void Clear();
}

/// <summary>
/// Consumer-facing interface for setting the status bar rows at the bottom of the fixed region.
/// </summary>
/// <remarks>
/// The status bar is always fully rendered regardless of height budget — it is never truncated.
/// </remarks>
public interface IStatus
{
    /// <summary>
    /// Replaces the status bar content with the given rows.
    /// An empty argument list clears the status bar.
    /// </summary>
    /// <param name="rows">The rows to display in the status bar.</param>
    void SetRows(params Line[] rows);

    /// <summary>
    /// Replaces the status bar content with the given rows.
    /// Passing an empty list clears the status bar.
    /// </summary>
    /// <param name="rows">The rows to display in the status bar.</param>
    void SetRows(IReadOnlyList<Line> rows);
}

/// <summary>
/// Consumer-facing interface for showing and hiding the autocomplete dropdown overlay.
/// </summary>
/// <remarks>
/// <see cref="Show"/> is a no-op when a modal overlay (dialog) is currently active.
/// <see cref="Hide"/> clears the overlay only when the active overlay is the autocomplete.
/// </remarks>
public interface IAutocomplete
{
    /// <summary>
    /// Shows the autocomplete dropdown with the given candidates.
    /// No-op when a modal overlay is active.
    /// </summary>
    /// <param name="candidates">The candidates to display, ordered by preference.</param>
    void Show(IReadOnlyList<AutocompleteCandidate> candidates);

    /// <summary>
    /// Hides the autocomplete dropdown without modifying the input buffer.
    /// No-op when the active overlay is not an autocomplete.
    /// </summary>
    void Hide();
}

// ─────────────────────────────────────────────────────────────────────────────
// Top-level façade interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The dcli terminal façade — the root abstraction a consumer depends on.
/// </summary>
/// <remarks>
/// <para>
/// The concrete implementation is <see cref="Terminal"/>; obtain one via
/// <see cref="Terminal.StartAsync(TerminalOptions, CancellationToken)"/>. Consumer code should
/// depend only on this interface so that tests can substitute a hand-written fake without
/// a real terminal, raw-mode session, or static state to reset.
/// </para>
/// <para>
/// <strong>No static singletons:</strong> all state is per-instance. A test that creates a fake
/// <see cref="ITerminal"/> starts from a clean slate with no global bookkeeping.
/// </para>
/// </remarks>
public interface ITerminal : IAsyncDisposable
{
    /// <summary>
    /// Scrollback content surface: append lines, live blocks, and collapsibles.
    /// </summary>
    IScrollback Scrollback { get; }

    /// <summary>
    /// Input editor surface: programmatic text control.
    /// </summary>
    IInput Input { get; }

    /// <summary>
    /// Status bar surface: set the sacred status rows at the bottom of the fixed region.
    /// </summary>
    IStatus Status { get; }

    /// <summary>
    /// Autocomplete overlay surface: show and hide the completion dropdown.
    /// </summary>
    IAutocomplete Autocomplete { get; }

    /// <summary>
    /// The outbound terminal event stream.
    /// </summary>
    /// <remarks>
    /// Drain this channel on a separate consumer thread or task. A consumer that never reads
    /// accumulates events but does not stall the render loop.
    /// </remarks>
    ChannelReader<TerminalEvent> Events { get; }

    /// <summary>
    /// Returns the current terminal size. May return a fixed or mocked value in test fakes.
    /// </summary>
    /// <returns>A tuple of <c>(Columns, Rows)</c> representing the terminal dimensions.</returns>
    (int Columns, int Rows) GetTerminalSize();

    /// <summary>
    /// Opens a single-select list dialog and awaits the user's choice.
    /// </summary>
    /// <param name="req">The request describing the items and optional title.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the zero-based selected index, or
    /// <see cref="DialogOutcome.Cancelled"/>.
    /// </returns>
    Task<DialogResult<int>> SelectAsync(SelectRequest req, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a multi-select list dialog and awaits the user's selection.
    /// </summary>
    /// <param name="req">The request describing the items and optional title.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the checked indices in ascending order, or
    /// <see cref="DialogOutcome.Cancelled"/> with an empty array.
    /// </returns>
    Task<DialogResult<int[]>> MultiSelectAsync(MultiSelectRequest req, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a single-select choice dialog and awaits the user's choice.
    /// </summary>
    /// <param name="req">The request describing the options and optional prompt.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the zero-based selected index, or
    /// <see cref="DialogOutcome.Cancelled"/>.
    /// </returns>
    Task<DialogResult<int>> ChoiceAsync(ChoiceRequest req, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a free-text input dialog and awaits the user's entry.
    /// </summary>
    /// <param name="req">The request describing the optional prompt, default text, and masking.</param>
    /// <param name="cancellationToken">
    /// Cancels the dialog and returns <see cref="DialogOutcome.Cancelled"/>.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Submitted"/> with the entered text (possibly empty), or
    /// <see cref="DialogOutcome.Cancelled"/>.
    /// </returns>
    Task<DialogResult<string>> InputAsync(InputRequest req, CancellationToken cancellationToken = default);
}
