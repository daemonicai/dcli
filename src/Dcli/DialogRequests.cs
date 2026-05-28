namespace Dcli;

/// <summary>
/// Parameters for <see cref="Terminal.SelectAsync"/> — a single-select list dialog.
/// </summary>
/// <param name="Items">The list items to display. An empty list is allowed; Submit on an
/// empty list returns <see cref="DialogOutcome.Submitted"/> with value <c>-1</c>.</param>
/// <param name="Title">Optional leading title row rendered above the list.</param>
public sealed record SelectRequest(IReadOnlyList<Line> Items, Line? Title = null);

/// <summary>
/// Parameters for <see cref="Terminal.MultiSelectAsync"/> — a multi-select list dialog where
/// Space toggles individual items.
/// </summary>
/// <param name="Items">The list items to display.</param>
/// <param name="Title">Optional leading title row rendered above the list.</param>
public sealed record MultiSelectRequest(IReadOnlyList<Line> Items, Line? Title = null);

/// <summary>
/// Parameters for <see cref="Terminal.ChoiceAsync"/> — a single-select choice dialog.
/// Semantically equivalent to <see cref="SelectRequest"/>; the separate type is provided for
/// caller clarity when presenting mutually-exclusive options with an explanatory prompt.
/// </summary>
/// <param name="Options">The choice options to display.</param>
/// <param name="Prompt">Optional leading prompt row rendered above the options.</param>
public sealed record ChoiceRequest(IReadOnlyList<Line> Options, Line? Prompt = null);

/// <summary>
/// Parameters for <see cref="Terminal.InputAsync"/> — a free-text entry dialog.
/// </summary>
/// <param name="Prompt">
/// Optional leading prompt row rendered above the input field. When <see langword="null"/>,
/// no prompt row is shown and the full overlay budget goes to the input field.
/// </param>
/// <param name="Default">
/// Optional pre-filled text. The caret starts at the end of this text.
/// When <see langword="null"/> or empty, the field starts blank.
/// </param>
/// <param name="IsSecret">
/// When <see langword="true"/>, each character is replaced by a bullet glyph (U+2022 •)
/// in the rendered overlay, preserving column-width arithmetic. The <see cref="DialogResult{T}.Value"/>
/// always carries the real (unmasked) entered text.
/// </param>
public sealed record InputRequest(Line? Prompt = null, string? Default = null, bool IsSecret = false);
