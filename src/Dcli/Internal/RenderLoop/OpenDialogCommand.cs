using Dcli.Internal.FixedRegion;

namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Loop command that opens a modal overlay and registers a parameterless completion delegate.
/// </summary>
/// <remarks>
/// <para>
/// The command carries a prepared <see cref="IModalOverlay"/> and a closure-captured
/// parameterless <see cref="Action"/> that captures the overlay reference, reads its outcome on
/// the loop thread, and completes the caller's <see cref="TaskCompletionSource{T}"/>. The
/// generic TCS is captured by the closure so the inbound channel remains untyped.
/// </para>
/// <para>
/// <strong>Reject rule:</strong> if any <see cref="IModalOverlay"/> is already active when this
/// command applies, the call's task is faulted with <see cref="InvalidOperationException"/>.
/// An active <see cref="Autocomplete"/> is suppressed (replaced) — modal overlays take priority.
/// </para>
/// </remarks>
internal sealed class OpenDialogCommand : ILoopCommand
{
    private readonly IModalOverlay _overlay;
    private readonly Action _completion;
    private readonly Action _reject;

    /// <summary>
    /// Initialises the command.
    /// </summary>
    /// <param name="overlay">The prepared modal overlay.</param>
    /// <param name="completion">
    /// Parameterless delegate invoked on the loop thread when the overlay is dismissed.
    /// The closure captures the overlay and reads its outcome to complete the caller's TCS.
    /// </param>
    /// <param name="reject">
    /// Delegate invoked when an <see cref="IModalOverlay"/> is already active. Faults the
    /// caller's TCS with <see cref="InvalidOperationException"/>.
    /// </param>
    internal OpenDialogCommand(IModalOverlay overlay, Action completion, Action reject)
    {
        _overlay = overlay;
        _completion = completion;
        _reject = reject;
    }

    /// <inheritdoc/>
    void ILoopCommand.Apply(RenderModel model)
    {
        if (model.ActiveOverlay is IModalOverlay)
        {
            // A modal overlay is already active — reject the new open request.
            _reject();
            return;
        }

        // ShowModal unconditionally assigns ActiveOverlay, suppressing any active Autocomplete.
        model.ShowModal(_overlay, _completion);

        // Seed the real terminal width into InputDialog so that width-dependent key handling
        // (Home/End/Up/Down) is correct for keys arriving in the same batch as this open command,
        // before the first Render call has a chance to update _lastWidth.
        if (_overlay is InputDialog inputDialog)
            inputDialog.SeedWidth(model.Columns);

        model.MarkDirty();
    }
}

/// <summary>
/// Loop command that cancels the active modal overlay if it is still the expected one.
/// </summary>
/// <remarks>
/// Posted by a <see cref="CancellationToken"/> registration when the token is cancelled while
/// a modal overlay is open. If the overlay has already been dismissed (naturally or by another
/// cancel), this is a no-op.
/// </remarks>
internal sealed class CancelDialogCommand : ILoopCommand
{
    private readonly IModalOverlay _expectedOverlay;
    private readonly Action _cancelCompletion;

    /// <summary>
    /// Initialises the command.
    /// </summary>
    /// <param name="expectedOverlay">
    /// The modal overlay this cancel is intended for. If the active overlay is a different object,
    /// the command is a no-op (another overlay may have opened after this one closed).
    /// </param>
    /// <param name="cancelCompletion">
    /// Action that completes the caller's TCS with <see cref="DialogOutcome.Cancelled"/> and
    /// disposes the CancellationToken registration to prevent leaks. Invoked on the loop thread.
    /// </param>
    internal CancelDialogCommand(IModalOverlay expectedOverlay, Action cancelCompletion)
    {
        _expectedOverlay = expectedOverlay;
        _cancelCompletion = cancelCompletion;
    }

    /// <inheritdoc/>
    void ILoopCommand.Apply(RenderModel model)
    {
        // Only act if the exact overlay we were created for is still the active overlay.
        if (!ReferenceEquals(model.ActiveOverlay, _expectedOverlay))
            return;

        // Invoke the completion (completes TCS with Cancelled) before clearing.
        _cancelCompletion();
        model.ClearOverlay();
        model.MarkDirty();
    }
}
