namespace Dcli.Internal.FixedRegion;

/// <summary>
/// A single candidate offered to the user by <see cref="Autocomplete"/>.
/// </summary>
/// <param name="InsertText">
/// The text applied to the input buffer when this candidate is accepted.
/// Applied via <see cref="TextBuffer.SetText"/>, which replaces the entire buffer and
/// places the caret at the end. (§12 may refine this to a span-replace; that would be a
/// deviation from the current whole-buffer-replace behaviour.)
/// </param>
/// <param name="Display">
/// The styled row rendered in the dropdown list. This is the consumer's presentation of
/// the candidate; <c>dcli</c> never interprets its text.
/// </param>
internal sealed record AutocompleteCandidate(string InsertText, Line Display);
