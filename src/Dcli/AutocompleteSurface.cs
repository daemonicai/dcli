using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;

namespace Dcli;

/// <summary>
/// Consumer-facing surface for showing and hiding the autocomplete dropdown overlay.
/// </summary>
/// <remarks>
/// <para>
/// All methods post fire-and-forget <see cref="Internal.RenderLoop.ILoopCommand"/>s; they
/// return before the command is applied or a frame is painted.
/// </para>
/// <para>
/// <strong>Overlay priority:</strong> <see cref="Show"/> is a no-op when a modal overlay (dialog)
/// is currently active — modal overlays suppress autocomplete.
/// <see cref="Hide"/> is safe to call at any time; it clears the overlay only when the active
/// overlay is the autocomplete (not a dialog).
/// </para>
/// <para>
/// <strong>Thread safety:</strong> all methods are safe to call from any thread.
/// </para>
/// </remarks>
public sealed class AutocompleteSurface : IAutocomplete
{
    private readonly LoopEngine _loop;

    internal AutocompleteSurface(LoopEngine loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
    }

    /// <summary>
    /// Shows the autocomplete dropdown with the given candidates.
    /// No-op when a modal overlay is active.
    /// </summary>
    /// <param name="candidates">
    /// The candidates to display, ordered by preference. An empty list is valid (renders
    /// nothing; accepting on an empty list is a safe no-op).
    /// </param>
    public void Show(IReadOnlyList<AutocompleteCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _loop.Post(new ShowAutocompleteCommand(candidates));
    }

    /// <summary>
    /// Hides the autocomplete dropdown without modifying the input buffer.
    /// No-op when the active overlay is not an autocomplete.
    /// </summary>
    public void Hide()
    {
        _loop.Post(new HideAutocompleteCommand());
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    private sealed class ShowAutocompleteCommand : ILoopCommand
    {
        private readonly IReadOnlyList<AutocompleteCandidate> _candidates;

        internal ShowAutocompleteCommand(IReadOnlyList<AutocompleteCandidate> candidates) =>
            _candidates = candidates;

        void ILoopCommand.Apply(RenderModel model)
        {
            // Construct a fresh Autocomplete overlay bound to the current editor instance.
            // ShowAutocomplete is a no-op when a modal overlay is already active.
            Autocomplete overlay = new(model.FixedRegion.Editor);
            overlay.Show(_candidates);
            model.ShowAutocomplete(overlay);
            model.MarkDirty();
        }
    }

    private sealed class HideAutocompleteCommand : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            // Clear only when the active overlay is an Autocomplete — not a dialog.
            // The overlay's own Hide()/IsDismissed sweeping happens on the next key event,
            // which is too late for an explicit programmatic Hide(); clear directly here.
            if (model.ActiveOverlay is Autocomplete)
            {
                model.ClearOverlay();
                model.MarkDirty();
            }
        }
    }
}
