namespace Dcli;

/// <summary>
/// The outcome of an awaitable dialog — how the user closed it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Submitted"/> and <see cref="Cancelled"/> are produced by all v1 list-based dialogs.
/// <see cref="Back"/> is produced by <see cref="Terminal.SelectAsync"/> and
/// <see cref="Terminal.ChoiceAsync"/> when the request has <c>AllowBack = true</c> and the user
/// presses Backspace before moving the selection cursor.
/// </para>
/// </remarks>
public enum DialogOutcome
{
    /// <summary>The user confirmed the selection (Enter).</summary>
    Submitted,

    /// <summary>
    /// The user pressed Backspace to navigate back in a wizard flow.
    /// Produced by <see cref="Terminal.SelectAsync"/> and <see cref="Terminal.ChoiceAsync"/>
    /// when the request has <c>AllowBack = true</c> and the user presses Backspace before
    /// moving the selection cursor (and before entering any filter text when type-to-filter
    /// is active). <see cref="DialogResult{T}.Value"/> is <see langword="default"/> when this
    /// outcome is returned.
    /// </summary>
    Back,

    /// <summary>The user dismissed without confirming (Escape or cancellation token).</summary>
    Cancelled,
}

/// <summary>
/// The result of an awaitable dialog, combining the <see cref="DialogOutcome"/> with a typed
/// value that is valid when <see cref="Outcome"/> is <see cref="DialogOutcome.Submitted"/>.
/// </summary>
/// <typeparam name="T">
/// The type of the selected value: <see cref="int"/> for single-select dialogs,
/// <see cref="int"/>[] for multi-select.
/// </typeparam>
/// <param name="Outcome">How the dialog was closed.</param>
/// <param name="Value">
/// The selected value when <see cref="Outcome"/> is <see cref="DialogOutcome.Submitted"/>;
/// <see langword="default"/> otherwise.
/// </param>
public readonly record struct DialogResult<T>(DialogOutcome Outcome, T Value);
