namespace Dcli;

/// <summary>
/// Composes a <see cref="Line"/> incrementally from styled text fragments.
/// Segments are stored in the order they are appended; <see cref="Build"/> returns the finished
/// <see cref="Line"/>.
/// <para>
/// Each method returns <see langword="this"/> so calls can be chained fluently:
/// <code>
/// Line line = new LineBuilder().Dim("✻ ").Text("Thinking…").Build();
/// </code>
/// </para>
/// <para>
/// A builder instance must not be reused after <see cref="Build"/> is called.
/// </para>
/// </summary>
public sealed class LineBuilder
{
    private readonly List<Segment> _segments = [];

    /// <summary>
    /// Appends a segment with the given text and style.
    /// </summary>
    /// <param name="text">The literal text to append.</param>
    /// <param name="style">The style to apply.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Append(string text, Style style = default)
    {
        _segments.Add(new Segment(text, style));
        return this;
    }

    /// <summary>
    /// Appends a segment with the given text and no styling (inherits terminal defaults).
    /// </summary>
    /// <param name="text">The literal text to append.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Text(string text) => Append(text, default);

    /// <summary>Appends a segment styled with <see cref="Format.Bold"/>.</summary>
    /// <param name="text">The literal text to append.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Bold(string text) =>
        Append(text, new Style(Format: Format.Bold));

    /// <summary>Appends a segment styled with <see cref="Format.Italic"/>.</summary>
    /// <param name="text">The literal text to append.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Italic(string text) =>
        Append(text, new Style(Format: Format.Italic));

    /// <summary>Appends a segment styled with <see cref="Format.Underline"/>.</summary>
    /// <param name="text">The literal text to append.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Underline(string text) =>
        Append(text, new Style(Format: Format.Underline));

    /// <summary>Appends a segment styled with <see cref="Format.Dim"/>.</summary>
    /// <param name="text">The literal text to append.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Dim(string text) =>
        Append(text, new Style(Format: Format.Dim));

    /// <summary>Appends a segment styled with <see cref="Format.Reverse"/>.</summary>
    /// <param name="text">The literal text to append.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Reverse(string text) =>
        Append(text, new Style(Format: Format.Reverse));

    /// <summary>Appends a segment styled with <see cref="Format.Strikethrough"/>.</summary>
    /// <param name="text">The literal text to append.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Strikethrough(string text) =>
        Append(text, new Style(Format: Format.Strikethrough));

    /// <summary>
    /// Appends a segment with the given text and a foreground color applied (no other formatting).
    /// </summary>
    /// <param name="text">The literal text to append.</param>
    /// <param name="foreground">The foreground color.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Fg(string text, Color foreground) =>
        Append(text, new Style(Foreground: foreground));

    /// <summary>
    /// Appends a segment with the given text and a background color applied (no other formatting).
    /// </summary>
    /// <param name="text">The literal text to append.</param>
    /// <param name="background">The background color.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Bg(string text, Color background) =>
        Append(text, new Style(Background: background));

    /// <summary>
    /// Appends a <b>verbatim</b> (raw) segment whose text is stored without sanitization.
    /// <para>
    /// Use only for internally-generated, audited sequences such as renderer-owned SGR strings.
    /// Consumer-supplied strings must never be passed here; use <see cref="Append"/>,
    /// <see cref="Text"/>, or any other builder method instead — those all sanitize via
    /// <see cref="Segment(string, Style)"/>.
    /// </para>
    /// <para>
    /// The appended segment has <see cref="Segment.IsRaw"/> set to <see langword="true"/>
    /// (participates in equality). The caller owns terminal integrity.
    /// </para>
    /// </summary>
    /// <param name="text">The verbatim text to append. Must not be <see langword="null"/>.</param>
    /// <param name="style">The style to apply. Defaults to <c>default(Style)</c>.</param>
    /// <returns>This builder.</returns>
    public LineBuilder Raw(string text, Style style = default)
    {
        _segments.Add(Segment.Raw(text, style));
        return this;
    }

    /// <summary>
    /// Returns the composed <see cref="Line"/> containing all appended segments in the order they
    /// were added.
    /// </summary>
    /// <returns>The finished <see cref="Line"/>.</returns>
    public Line Build() => new(_segments);
}
