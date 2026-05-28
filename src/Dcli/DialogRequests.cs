namespace Dcli;

/// <summary>
/// Parameters for <see cref="Terminal.SelectAsync"/> — a single-select list dialog.
/// </summary>
/// <param name="Items">The list items to display. An empty list is allowed; Submit on an
/// empty list returns <see cref="DialogOutcome.Submitted"/> with value <c>-1</c>.</param>
/// <param name="Title">Optional leading title row rendered above the list.</param>
/// <param name="AllowBack">
/// When <see langword="true"/>, pressing Backspace before moving the selection cursor (and
/// before entering any filter text) closes the dialog with <see cref="DialogOutcome.Back"/>.
/// Intended for wizard flows where the user can step backwards. Defaults to
/// <see langword="false"/>; existing callers are unaffected.
/// </param>
public sealed record SelectRequest(IReadOnlyList<Line> Items, Line? Title = null, bool AllowBack = false)
{
    /// <summary>
    /// Constructs a <see cref="SelectRequest"/> from plain-text item strings.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    /// <param name="title">Optional leading title row.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public SelectRequest(IReadOnlyList<string> items, Line? title = null, bool allowBack = false)
        : this(ConvertItems(items), title, allowBack) { }

    /// <summary>
    /// Constructs a <see cref="SelectRequest"/> from a params array of plain-text item strings.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    public SelectRequest(params string[] items)
        : this((IReadOnlyList<string>)items, null) { }

    private static List<Line> ConvertItems(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(s => Line.FromText(s)).ToList();
    }
}

/// <summary>
/// Parameters for <see cref="Terminal.MultiSelectAsync"/> — a multi-select list dialog where
/// Space toggles individual items.
/// </summary>
/// <param name="Items">The list items to display.</param>
/// <param name="Title">Optional leading title row rendered above the list.</param>
public sealed record MultiSelectRequest(IReadOnlyList<Line> Items, Line? Title = null)
{
    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> from plain-text item strings.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    /// <param name="title">Optional leading title row.</param>
    public MultiSelectRequest(IReadOnlyList<string> items, Line? title = null)
        : this(ConvertItems(items), title) { }

    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> from a params array of plain-text item strings.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    public MultiSelectRequest(params string[] items)
        : this((IReadOnlyList<string>)items, null) { }

    private static List<Line> ConvertItems(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(s => Line.FromText(s)).ToList();
    }
}

/// <summary>
/// Parameters for <see cref="Terminal.ChoiceAsync"/> — a single-select choice dialog.
/// Semantically equivalent to <see cref="SelectRequest"/>; the separate type is provided for
/// caller clarity when presenting mutually-exclusive options with an explanatory prompt.
/// </summary>
/// <param name="Options">The choice options to display.</param>
/// <param name="Prompt">Optional leading prompt row rendered above the options.</param>
/// <param name="AllowBack">
/// When <see langword="true"/>, pressing Backspace before moving the selection cursor (and
/// before entering any filter text) closes the dialog with <see cref="DialogOutcome.Back"/>.
/// Intended for wizard flows where the user can step backwards. Defaults to
/// <see langword="false"/>; existing callers are unaffected.
/// </param>
public sealed record ChoiceRequest(IReadOnlyList<Line> Options, Line? Prompt = null, bool AllowBack = false)
{
    /// <summary>
    /// Constructs a <see cref="ChoiceRequest"/> from plain-text option strings.
    /// Shorthand equivalent to passing <c>options.Select(Line.FromText).ToList()</c> as <c>Options</c>.
    /// </summary>
    /// <param name="options">The plain-text options to display.</param>
    /// <param name="prompt">Optional leading prompt row.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public ChoiceRequest(IReadOnlyList<string> options, Line? prompt = null, bool allowBack = false)
        : this(ConvertOptions(options), prompt, allowBack) { }

    /// <summary>
    /// Constructs a <see cref="ChoiceRequest"/> from a params array of plain-text option strings.
    /// Shorthand equivalent to passing <c>options.Select(Line.FromText).ToList()</c> as <c>Options</c>.
    /// </summary>
    /// <param name="options">The plain-text options to display.</param>
    public ChoiceRequest(params string[] options)
        : this((IReadOnlyList<string>)options, null) { }

    private static List<Line> ConvertOptions(IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Select(s => Line.FromText(s)).ToList();
    }
}

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
public sealed record InputRequest(Line? Prompt = null, string? Default = null, bool IsSecret = false)
{
    /// <summary>
    /// Constructs an <see cref="InputRequest"/> with a plain-text prompt string.
    /// Shorthand equivalent to passing <c>Line.FromText(prompt)</c> as <c>Prompt</c>.
    /// </summary>
    /// <param name="prompt">Optional plain-text prompt string; <see langword="null"/> means no prompt row.</param>
    /// <param name="Default">Optional pre-filled text.</param>
    /// <param name="IsSecret">When <see langword="true"/>, input characters are masked.</param>
    public InputRequest(string? prompt, string? Default = null, bool IsSecret = false)
        : this(prompt is null ? null : Line.FromText(prompt), Default, IsSecret) { }
}
