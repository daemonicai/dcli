namespace Dcli;

/// <summary>
/// Parameters for <see cref="Terminal.SelectAsync"/> — a single-select list dialog.
/// </summary>
/// <param name="Items">The list items to display. An empty list is allowed; Submit on an
/// empty list returns <see cref="DialogOutcome.Submitted"/> with value <c>-1</c>.</param>
/// <param name="Title">Optional multi-line preamble rendered top-to-bottom above the list.
/// When <see langword="null"/> or empty, no preamble row is painted and the full overlay
/// budget is available to the list. Single-line forms (see convenience constructors) are
/// internally equivalent to a one-element list.</param>
/// <param name="AllowBack">
/// When <see langword="true"/>, pressing Backspace before moving the selection cursor (and
/// before entering any filter text) closes the dialog with <see cref="DialogOutcome.Back"/>.
/// Intended for wizard flows where the user can step backwards. Defaults to
/// <see langword="false"/>; existing callers are unaffected.
/// </param>
public sealed record SelectRequest(IReadOnlyList<Line> Items, IReadOnlyList<Line>? Title = null, bool AllowBack = false)
{
    /// <summary>
    /// Constructs a <see cref="SelectRequest"/> with a single <see cref="Line"/> title.
    /// Internally equivalent to passing a one-element <see cref="IReadOnlyList{Line}"/>;
    /// no implicit <see cref="Line"/> → <c>IReadOnlyList&lt;Line&gt;</c> conversion is
    /// defined — use this constructor explicitly when you have a single styled line.
    /// The <c>Title</c> parameter name matches the primary constructor so existing
    /// named-argument call sites (e.g. <c>Title: someLine</c>) continue to compile.
    /// </summary>
    /// <param name="Items">The list items to display.</param>
    /// <param name="Title">Optional single-line preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="AllowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public SelectRequest(IReadOnlyList<Line> Items, Line? Title, bool AllowBack = false)
        : this(Items, Title is null ? null : (IReadOnlyList<Line>)[Title], AllowBack) { }

    /// <summary>
    /// Constructs a <see cref="SelectRequest"/> with multiple <see cref="Line"/> preamble
    /// entries supplied as a <see langword="params"/> array. Each line is painted in order
    /// above the list. No implicit conversion is defined; pass lines explicitly.
    /// </summary>
    /// <param name="items">The list items to display.</param>
    /// <param name="title">Preamble lines in top-to-bottom order (may be empty).</param>
    public SelectRequest(IReadOnlyList<Line> items, params Line[] title)
        : this(items, (IReadOnlyList<Line>)title) { }

    /// <summary>
    /// Constructs a <see cref="SelectRequest"/> from plain-text item strings with a
    /// multi-line string preamble. Each string entry is converted via
    /// <see cref="Line.FromText(string, Style?)"/>.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>
    /// and <c>title.Select(Line.FromText).ToList()</c> as <c>Title</c>.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    /// <param name="title">Optional multi-line string preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public SelectRequest(IReadOnlyList<string> items, IReadOnlyList<string>? title, bool allowBack = false)
        : this(ConvertItems(items), ConvertPreamble(title), allowBack) { }

    /// <summary>
    /// Constructs a <see cref="SelectRequest"/> from plain-text item strings with an optional
    /// single-<see cref="Line"/> title.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// The single-<see cref="Line"/> form is internally equivalent to a one-element list.
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
    /// Constructs a <see cref="SelectRequest"/> from plain-text item strings with a
    /// <see langword="params"/> string preamble. Each preamble string is converted via
    /// <see cref="Line.FromText(string, Style?)"/>.
    /// Shorthand for inline multi-line preambles without pre-building a list.
    /// </summary>
    /// <param name="items">The list items to display.</param>
    /// <param name="title">Preamble strings in top-to-bottom order (may be empty).</param>
    public SelectRequest(IReadOnlyList<Line> items, params string[] title)
        : this(items, ConvertPreamble(title)) { }

    /// <summary>
    /// Constructs a <see cref="SelectRequest"/> from a params array of plain-text item strings.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    public SelectRequest(params string[] items)
        : this((IReadOnlyList<string>)items, (Line?)null) { }

    /// <summary>
    /// Single-string convenience over <see cref="IReadOnlyList{Line}"/> items — equivalent to
    /// a one-element preamble list with default style.
    /// </summary>
    /// <param name="items">The list items to display.</param>
    /// <param name="title">Optional plain-text title string; <see langword="null"/> means no preamble.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public SelectRequest(IReadOnlyList<Line> items, string? title, bool allowBack = false)
        : this(items, title is null ? null : (IReadOnlyList<Line>)[Line.FromText(title)], allowBack) { }

    private static List<Line> ConvertItems(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(s => Line.FromText(s)).ToList();
    }

    private static List<Line>? ConvertPreamble(IReadOnlyList<string>? preamble)
    {
        if (preamble is null || preamble.Count == 0)
            return null;
        return preamble.Select(s => Line.FromText(s)).ToList();
    }
}

/// <summary>
/// Parameters for <see cref="Terminal.MultiSelectAsync"/> — a multi-select list dialog where
/// Space toggles individual items.
/// </summary>
/// <param name="Items">The list items to display.</param>
/// <param name="Title">Optional multi-line preamble rendered top-to-bottom above the list.
/// When <see langword="null"/> or empty, no preamble row is painted and the full overlay
/// budget is available to the list. Single-line forms (see convenience constructors) are
/// internally equivalent to a one-element list.</param>
/// <param name="AllowBack">
/// When <see langword="true"/>, pressing <c>[</c> at any time (including after toggling items
/// with Space) closes the dialog with <see cref="DialogOutcome.Back"/>.
/// Backspace is intentionally NOT bound for multi-select: because Space toggles items,
/// Backspace-at-empty is ambiguous and was never shipped. Use <c>[</c> for unambiguous
/// back-navigation in wizard flows. Defaults to <see langword="false"/>; existing callers
/// are unaffected. When <see langword="false"/>, <c>[</c> has no effect and the dialog
/// remains open — v1 callers that omit this parameter are unaffected.
/// </param>
public sealed record MultiSelectRequest(IReadOnlyList<Line> Items, IReadOnlyList<Line>? Title = null, bool AllowBack = false)
{
    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> with a single <see cref="Line"/> title.
    /// Internally equivalent to passing a one-element <see cref="IReadOnlyList{Line}"/>;
    /// no implicit <see cref="Line"/> → <c>IReadOnlyList&lt;Line&gt;</c> conversion is
    /// defined — use this constructor explicitly when you have a single styled line.
    /// The <c>Title</c> parameter name matches the primary constructor so existing
    /// named-argument call sites (e.g. <c>Title: someLine</c>) continue to compile.
    /// </summary>
    /// <param name="Items">The list items to display.</param>
    /// <param name="Title">Optional single-line preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="AllowBack">
    /// When <see langword="true"/>, <c>[</c> closes the dialog with <see cref="DialogOutcome.Back"/>
    /// at any time. Defaults to <see langword="false"/>.
    /// </param>
    public MultiSelectRequest(IReadOnlyList<Line> Items, Line? Title, bool AllowBack = false)
        : this(Items, Title is null ? null : (IReadOnlyList<Line>)[Title], AllowBack) { }

    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> with multiple <see cref="Line"/> preamble
    /// entries supplied as a <see langword="params"/> array. Each line is painted in order
    /// above the list. No implicit conversion is defined; pass lines explicitly.
    /// <c>AllowBack</c> cannot be set via this constructor because <see langword="params"/> must
    /// be the last parameter; use the primary constructor or another overload.
    /// </summary>
    /// <param name="items">The list items to display.</param>
    /// <param name="title">Preamble lines in top-to-bottom order (may be empty).</param>
    public MultiSelectRequest(IReadOnlyList<Line> items, params Line[] title)
        : this(items, (IReadOnlyList<Line>)title) { }

    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> from plain-text item strings with a
    /// multi-line string preamble. Each string entry is converted via
    /// <see cref="Line.FromText(string, Style?)"/>.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>
    /// and <c>title.Select(Line.FromText).ToList()</c> as <c>Title</c>.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    /// <param name="title">Optional multi-line string preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, <c>[</c> closes the dialog with <see cref="DialogOutcome.Back"/>
    /// at any time. Defaults to <see langword="false"/>.
    /// </param>
    public MultiSelectRequest(IReadOnlyList<string> items, IReadOnlyList<string>? title, bool allowBack = false)
        : this(ConvertItems(items), ConvertPreamble(title), allowBack) { }

    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> from plain-text item strings with an
    /// optional single-<see cref="Line"/> title.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// The single-<see cref="Line"/> form is internally equivalent to a one-element list.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    /// <param name="title">Optional leading title row.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, <c>[</c> closes the dialog with <see cref="DialogOutcome.Back"/>
    /// at any time. Defaults to <see langword="false"/>.
    /// </param>
    public MultiSelectRequest(IReadOnlyList<string> items, Line? title = null, bool allowBack = false)
        : this(ConvertItems(items), title, allowBack) { }

    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> from plain-text item strings with a
    /// <see langword="params"/> string preamble. Each preamble string is converted via
    /// <see cref="Line.FromText(string, Style?)"/>.
    /// Shorthand for inline multi-line preambles without pre-building a list.
    /// <c>AllowBack</c> cannot be set via this constructor because <see langword="params"/> must
    /// be the last parameter; use the primary constructor or another overload.
    /// </summary>
    /// <param name="items">The list items to display.</param>
    /// <param name="title">Preamble strings in top-to-bottom order (may be empty).</param>
    public MultiSelectRequest(IReadOnlyList<Line> items, params string[] title)
        : this(items, ConvertPreamble(title)) { }

    /// <summary>
    /// Constructs a <see cref="MultiSelectRequest"/> from a params array of plain-text item strings.
    /// Shorthand equivalent to passing <c>items.Select(Line.FromText).ToList()</c> as <c>Items</c>.
    /// <c>AllowBack</c> cannot be set via this constructor because <see langword="params"/> must
    /// be the last parameter; use the primary constructor or another overload.
    /// </summary>
    /// <param name="items">The plain-text items to display.</param>
    public MultiSelectRequest(params string[] items)
        : this((IReadOnlyList<string>)items, (Line?)null) { }

    /// <summary>
    /// Single-string convenience over <see cref="IReadOnlyList{Line}"/> items — equivalent to
    /// a one-element preamble list with default style.
    /// </summary>
    /// <param name="items">The list items to display.</param>
    /// <param name="title">Optional plain-text title string; <see langword="null"/> means no preamble.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, <c>[</c> closes the dialog with <see cref="DialogOutcome.Back"/>
    /// at any time. Defaults to <see langword="false"/>.
    /// </param>
    public MultiSelectRequest(IReadOnlyList<Line> items, string? title, bool allowBack = false)
        : this(items, title is null ? null : (IReadOnlyList<Line>)[Line.FromText(title)], allowBack) { }

    private static List<Line> ConvertItems(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(s => Line.FromText(s)).ToList();
    }

    private static List<Line>? ConvertPreamble(IReadOnlyList<string>? preamble)
    {
        if (preamble is null || preamble.Count == 0)
            return null;
        return preamble.Select(s => Line.FromText(s)).ToList();
    }
}

/// <summary>
/// Parameters for <see cref="Terminal.ChoiceAsync"/> — a single-select choice dialog.
/// Semantically equivalent to <see cref="SelectRequest"/>; the separate type is provided for
/// caller clarity when presenting mutually-exclusive options with an explanatory prompt.
/// </summary>
/// <param name="Options">The choice options to display.</param>
/// <param name="Prompt">Optional multi-line preamble rendered top-to-bottom above the options.
/// When <see langword="null"/> or empty, no preamble row is painted and the full overlay
/// budget is available to the options. Single-line forms (see convenience constructors) are
/// internally equivalent to a one-element list.</param>
/// <param name="AllowBack">
/// When <see langword="true"/>, pressing Backspace before moving the selection cursor (and
/// before entering any filter text) closes the dialog with <see cref="DialogOutcome.Back"/>.
/// Intended for wizard flows where the user can step backwards. Defaults to
/// <see langword="false"/>; existing callers are unaffected.
/// </param>
public sealed record ChoiceRequest(IReadOnlyList<Line> Options, IReadOnlyList<Line>? Prompt = null, bool AllowBack = false)
{
    /// <summary>
    /// Constructs a <see cref="ChoiceRequest"/> with a single <see cref="Line"/> prompt.
    /// Internally equivalent to passing a one-element <see cref="IReadOnlyList{Line}"/>;
    /// no implicit <see cref="Line"/> → <c>IReadOnlyList&lt;Line&gt;</c> conversion is
    /// defined — use this constructor explicitly when you have a single styled line.
    /// The <c>Prompt</c> parameter name matches the primary constructor so existing
    /// named-argument call sites (e.g. <c>Prompt: someLine</c>) continue to compile.
    /// </summary>
    /// <param name="Options">The choice options to display.</param>
    /// <param name="Prompt">Optional single-line preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="AllowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public ChoiceRequest(IReadOnlyList<Line> Options, Line? Prompt, bool AllowBack = false)
        : this(Options, Prompt is null ? null : (IReadOnlyList<Line>)[Prompt], AllowBack) { }

    /// <summary>
    /// Constructs a <see cref="ChoiceRequest"/> with multiple <see cref="Line"/> preamble
    /// entries supplied as a <see langword="params"/> array. Each line is painted in order
    /// above the options. No implicit conversion is defined; pass lines explicitly.
    /// </summary>
    /// <param name="options">The choice options to display.</param>
    /// <param name="prompt">Preamble lines in top-to-bottom order (may be empty).</param>
    public ChoiceRequest(IReadOnlyList<Line> options, params Line[] prompt)
        : this(options, (IReadOnlyList<Line>)prompt) { }

    /// <summary>
    /// Constructs a <see cref="ChoiceRequest"/> from plain-text option strings with a
    /// multi-line string preamble. Each string entry is converted via
    /// <see cref="Line.FromText(string, Style?)"/>.
    /// Shorthand equivalent to passing <c>options.Select(Line.FromText).ToList()</c> as <c>Options</c>
    /// and <c>prompt.Select(Line.FromText).ToList()</c> as <c>Prompt</c>.
    /// </summary>
    /// <param name="options">The plain-text options to display.</param>
    /// <param name="prompt">Optional multi-line string preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public ChoiceRequest(IReadOnlyList<string> options, IReadOnlyList<string>? prompt, bool allowBack = false)
        : this(ConvertOptions(options), ConvertPreamble(prompt), allowBack) { }

    /// <summary>
    /// Constructs a <see cref="ChoiceRequest"/> from plain-text option strings with an optional
    /// single-<see cref="Line"/> prompt.
    /// Shorthand equivalent to passing <c>options.Select(Line.FromText).ToList()</c> as <c>Options</c>.
    /// The single-<see cref="Line"/> form is internally equivalent to a one-element list.
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
    /// Constructs a <see cref="ChoiceRequest"/> from plain-text option strings with a
    /// <see langword="params"/> string preamble. Each preamble string is converted via
    /// <see cref="Line.FromText(string, Style?)"/>.
    /// Shorthand for inline multi-line preambles without pre-building a list.
    /// </summary>
    /// <param name="options">The choice options to display.</param>
    /// <param name="prompt">Preamble strings in top-to-bottom order (may be empty).</param>
    public ChoiceRequest(IReadOnlyList<Line> options, params string[] prompt)
        : this(options, ConvertPreamble(prompt)) { }

    /// <summary>
    /// Constructs a <see cref="ChoiceRequest"/> from a params array of plain-text option strings.
    /// Shorthand equivalent to passing <c>options.Select(Line.FromText).ToList()</c> as <c>Options</c>.
    /// </summary>
    /// <param name="options">The plain-text options to display.</param>
    public ChoiceRequest(params string[] options)
        : this((IReadOnlyList<string>)options, (Line?)null) { }

    /// <summary>
    /// Single-string convenience over <see cref="IReadOnlyList{Line}"/> options — equivalent to
    /// a one-element preamble list with default style.
    /// </summary>
    /// <param name="options">The choice options to display.</param>
    /// <param name="prompt">Optional plain-text prompt string; <see langword="null"/> means no preamble.</param>
    /// <param name="allowBack">
    /// When <see langword="true"/>, Backspace at position zero (before any movement) closes
    /// the dialog with <see cref="DialogOutcome.Back"/>. Defaults to <see langword="false"/>.
    /// </param>
    public ChoiceRequest(IReadOnlyList<Line> options, string? prompt, bool allowBack = false)
        : this(options, prompt is null ? null : (IReadOnlyList<Line>)[Line.FromText(prompt)], allowBack) { }

    private static List<Line> ConvertOptions(IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Select(s => Line.FromText(s)).ToList();
    }

    private static List<Line>? ConvertPreamble(IReadOnlyList<string>? preamble)
    {
        if (preamble is null || preamble.Count == 0)
            return null;
        return preamble.Select(s => Line.FromText(s)).ToList();
    }
}

/// <summary>
/// Parameters for <see cref="Terminal.InputAsync"/> — a free-text entry dialog.
/// </summary>
/// <param name="Prompt">
/// Optional multi-line preamble rendered top-to-bottom above the input field. When
/// <see langword="null"/> or empty, no preamble row is shown and the full overlay budget
/// goes to the input field. Single-line forms (see convenience constructors) are internally
/// equivalent to a one-element list.
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
public sealed record InputRequest(IReadOnlyList<Line>? Prompt = null, string? Default = null, bool IsSecret = false)
{
    /// <summary>
    /// Constructs an <see cref="InputRequest"/> with a single <see cref="Line"/> prompt.
    /// Internally equivalent to passing a one-element <see cref="IReadOnlyList{Line}"/>;
    /// no implicit <see cref="Line"/> → <c>IReadOnlyList&lt;Line&gt;</c> conversion is
    /// defined — use this constructor explicitly when you have a single styled line.
    /// The <c>Prompt</c> parameter name matches the primary constructor so existing
    /// named-argument call sites (e.g. <c>Prompt: someLine</c>) continue to compile.
    /// </summary>
    /// <param name="Prompt">Optional single-line preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="Default">Optional pre-filled text.</param>
    /// <param name="IsSecret">When <see langword="true"/>, input characters are masked.</param>
    public InputRequest(Line? Prompt, string? Default = null, bool IsSecret = false)
        : this(Prompt is null ? null : (IReadOnlyList<Line>)[Prompt], Default, IsSecret) { }

    /// <summary>
    /// Constructs an <see cref="InputRequest"/> with a plain-text prompt string.
    /// Shorthand equivalent to passing <c>Line.FromText(prompt)</c> as a one-element preamble.
    /// The single-<see langword="string"/> form is internally equivalent to a one-element list.
    /// </summary>
    /// <param name="prompt">Optional plain-text prompt string; <see langword="null"/> means no preamble.</param>
    /// <param name="Default">Optional pre-filled text.</param>
    /// <param name="IsSecret">When <see langword="true"/>, input characters are masked.</param>
    public InputRequest(string? prompt, string? Default = null, bool IsSecret = false)
        : this(prompt is null ? null : Line.FromText(prompt), Default, IsSecret) { }

    /// <summary>
    /// Constructs an <see cref="InputRequest"/> with a multi-line string preamble. Each string
    /// entry is converted via <see cref="Line.FromText(string, Style?)"/>.
    /// Shorthand equivalent to passing <c>prompt.Select(Line.FromText).ToList()</c> as <c>Prompt</c>.
    /// No implicit conversion is defined; pass strings explicitly. Note: the
    /// <see langword="params"/> form does not accept <c>Default</c> or <c>IsSecret</c>; use this
    /// overload when those parameters are needed.
    /// </summary>
    /// <param name="prompt">Optional multi-line string preamble; <see langword="null"/> means no preamble.</param>
    /// <param name="Default">Optional pre-filled text.</param>
    /// <param name="IsSecret">When <see langword="true"/>, input characters are masked.</param>
    public InputRequest(IReadOnlyList<string>? prompt, string? Default = null, bool IsSecret = false)
        : this(ConvertPreamble(prompt), Default, IsSecret) { }

    /// <summary>
    /// Constructs an <see cref="InputRequest"/> with multiple <see cref="Line"/> preamble
    /// entries supplied as a <see langword="params"/> array. Each line is painted in order
    /// above the input field. No implicit conversion is defined; pass lines explicitly.
    /// Note: <c>Default</c> and <c>IsSecret</c> cannot be specified alongside
    /// <see langword="params"/>; use the <see cref="IReadOnlyList{Line}"/>-overload for those.
    /// </summary>
    /// <param name="prompt">Preamble lines in top-to-bottom order (may be empty).</param>
    public InputRequest(params Line[] prompt)
        : this((IReadOnlyList<Line>?)prompt) { }

    /// <summary>
    /// Constructs an <see cref="InputRequest"/> with a <see langword="params"/> string preamble.
    /// Each string is converted via <see cref="Line.FromText(string, Style?)"/>.
    /// Note: <c>Default</c> and <c>IsSecret</c> cannot be specified alongside
    /// <see langword="params"/>; use the <see cref="IReadOnlyList{String}"/>-overload for those.
    /// </summary>
    /// <param name="prompt">Preamble strings in top-to-bottom order (may be empty).</param>
    public InputRequest(params string[] prompt)
        : this(ConvertPreamble(prompt)) { }

    private static List<Line>? ConvertPreamble(IReadOnlyList<string>? preamble)
    {
        if (preamble is null || preamble.Count == 0)
            return null;
        return preamble.Select(s => Line.FromText(s)).ToList();
    }
}
