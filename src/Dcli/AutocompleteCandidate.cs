namespace Dcli;

/// <summary>
/// A single completion candidate offered to the user by the autocomplete overlay.
/// </summary>
/// <param name="InsertText">
/// The text applied to the input buffer when this candidate is accepted.
/// Applied via whole-buffer replace: the entire input buffer is replaced with this value and
/// the caret is moved to the end. Span-replace (replacing only the typed prefix) is not
/// supported in this release.
/// </param>
/// <param name="Display">
/// The styled row rendered in the dropdown list. This is the consumer's presentation of
/// the candidate; <c>dcli</c> never interprets its text.
/// </param>
public sealed record AutocompleteCandidate(string InsertText, Line Display);
