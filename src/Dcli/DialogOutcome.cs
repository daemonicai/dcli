namespace Dcli;

/// <summary>
/// The outcome of an awaitable dialog — how the user closed it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Submitted"/> and <see cref="Cancelled"/> are produced by the v1 list-based dialogs
/// (<see cref="Terminal.SelectAsync"/>, <see cref="Terminal.MultiSelectAsync"/>,
/// <see cref="Terminal.ChoiceAsync"/>). <see cref="Back"/> exists for API/wizard-flow
/// compatibility but is not produced by any v1 dialog (no key maps to it in this release).
/// </para>
/// </remarks>
public enum DialogOutcome
{
    /// <summary>The user confirmed the selection (Enter).</summary>
    Submitted,

    /// <summary>
    /// Reserved for wizard-flow "go back" semantics. Not produced by any v1 dialog.
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
