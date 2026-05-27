namespace Dcli;

/// <summary>
/// An immutable run of text with a single <see cref="Dcli.Style"/> applied uniformly across it.
/// <para>
/// Text is stored and returned verbatim — no markup, escape sequences, or control characters are
/// interpreted. A string such as <c>"[bold]"</c> is literal content, not a formatting directive.
/// </para>
/// </summary>
/// <param name="Text">
/// The literal text content. May be empty; must not be <see langword="null"/> (enforced by the
/// nullable reference-type annotation).
/// </param>
/// <param name="Style">
/// The style applied to this segment. Defaults to <c>default(Style)</c> (terminal defaults, no
/// formatting attributes).
/// </param>
public record Segment(string Text, Style Style = default);
