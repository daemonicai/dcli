namespace Dcli;

/// <summary>
/// A single completion candidate offered to the user by the autocomplete overlay.
/// </summary>
/// <param name="InsertText">
/// The text applied to the input buffer when this candidate is accepted.
/// Applied via <c>TextBuffer.SetText</c>, which replaces the entire buffer and places the
/// caret at the end (whole-buffer replace). A span-replace refinement — so only the typed
/// prefix is replaced rather than the full buffer — is a documented gap for a later §12 pass.
/// </param>
/// <param name="Display">
/// The styled row rendered in the dropdown list. This is the consumer's presentation of
/// the candidate; <c>dcli</c> never interprets its text.
/// </param>
public sealed record AutocompleteCandidate(string InsertText, Line Display);
