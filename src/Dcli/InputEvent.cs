using System.Text;

namespace Dcli;

/// <summary>
/// Discriminated union of all events that the VT input pipeline can produce.
/// Use pattern matching on the concrete subtypes: <see cref="KeyEvent"/>,
/// <see cref="PasteEvent"/>, and <see cref="ResizeEvent"/>.
/// </summary>
public abstract record InputEvent;

// ─────────────────────────────────────────────────────────────────────────────
// KeyEvent
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single key press, synthesised by <c>VtInputParser</c> from the raw byte stream.
/// </summary>
/// <param name="Code">
/// The key that was pressed, either a Unicode scalar value or a named terminal key.
/// </param>
/// <param name="Modifiers">
/// Modifier keys active during the press (Ctrl, Alt, Shift, or combinations thereof).
/// </param>
public sealed record KeyEvent(KeyCode Code, Modifiers Modifiers) : InputEvent;

// ─────────────────────────────────────────────────────────────────────────────
// PasteEvent
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A pasted block of text delivered via bracketed-paste mode
/// (<c>ESC [ 2 0 0 ~</c> … <c>ESC [ 2 0 1 ~</c>).
/// <para>
/// <c>VtInputParser</c> accumulates all bytes between the start and end markers into
/// a single event. The raw bytes are decoded as UTF-8; feed-call boundaries inside the paste
/// body are transparent. A lone ESC that is not followed by the end marker is included verbatim
/// as <c>U+001B</c> in the decoded text.
/// </para>
/// </summary>
/// <param name="Text">The full pasted text, decoded from UTF-8.</param>
public sealed record PasteEvent(string Text) : InputEvent;

// ─────────────────────────────────────────────────────────────────────────────
// ResizeEvent
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Signals that the terminal window has been resized.
/// <para>
/// This event is <em>not</em> synthesised from bytes by <c>VtInputParser</c>.
/// It originates from <c>SIGWINCH</c> (POSIX) or a <c>WINDOW_BUFFER_SIZE_EVENT</c>
/// (Windows) and is injected into the event stream by the platform resize watcher.
/// </para>
/// </summary>
/// <param name="Columns">New terminal width in columns.</param>
/// <param name="Rows">New terminal height in rows.</param>
public sealed record ResizeEvent(int Columns, int Rows) : InputEvent;

// ─────────────────────────────────────────────────────────────────────────────
// NamedKey
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Terminal keys that do not correspond to a single printable Unicode scalar value.
/// </summary>
public enum NamedKey
{
    /// <summary>Enter / Return (0x0D in raw mode, or CSI sequences ending in 'M').</summary>
    Enter,

    /// <summary>Horizontal tab (0x09 in raw mode; distinct from Ctrl+I on the wire).</summary>
    Tab,

    /// <summary>Backspace (0x7F in raw mode or 0x08 on older configurations).</summary>
    Backspace,

    /// <summary>Escape (lone 0x1B after the ESC-disambiguation timeout).</summary>
    Escape,

    /// <summary>Up-arrow (<c>ESC [ A</c> or <c>ESC O A</c>).</summary>
    Up,

    /// <summary>Down-arrow (<c>ESC [ B</c> or <c>ESC O B</c>).</summary>
    Down,

    /// <summary>Right-arrow (<c>ESC [ C</c> or <c>ESC O C</c>).</summary>
    Right,

    /// <summary>Left-arrow (<c>ESC [ D</c> or <c>ESC O D</c>).</summary>
    Left,

    /// <summary>Home (<c>ESC [ H</c>, <c>ESC O H</c>, or <c>ESC [ 1 ~</c> / <c>ESC [ 7 ~</c>).</summary>
    Home,

    /// <summary>End (<c>ESC [ F</c>, <c>ESC O F</c>, or <c>ESC [ 4 ~</c> / <c>ESC [ 8 ~</c>).</summary>
    End,

    /// <summary>Page-Up (<c>ESC [ 5 ~</c>).</summary>
    PageUp,

    /// <summary>Page-Down (<c>ESC [ 6 ~</c>).</summary>
    PageDown,

    /// <summary>Insert (<c>ESC [ 2 ~</c>).</summary>
    Insert,

    /// <summary>Delete (<c>ESC [ 3 ~</c>).</summary>
    Delete,

    /// <summary>F1 (<c>ESC O P</c> or <c>ESC [ 1 ; &lt;m&gt; P</c>).</summary>
    F1,

    /// <summary>F2 (<c>ESC O Q</c> or <c>ESC [ 1 ; &lt;m&gt; Q</c>).</summary>
    F2,

    /// <summary>F3 (<c>ESC O R</c> or <c>ESC [ 1 ; &lt;m&gt; R</c>).</summary>
    F3,

    /// <summary>F4 (<c>ESC O S</c> or <c>ESC [ 1 ; &lt;m&gt; S</c>).</summary>
    F4,

    /// <summary>F5 (<c>ESC [ 1 5 ~</c>).</summary>
    F5,

    /// <summary>F6 (<c>ESC [ 1 7 ~</c>).</summary>
    F6,

    /// <summary>F7 (<c>ESC [ 1 8 ~</c>).</summary>
    F7,

    /// <summary>F8 (<c>ESC [ 1 9 ~</c>).</summary>
    F8,

    /// <summary>F9 (<c>ESC [ 2 0 ~</c>).</summary>
    F9,

    /// <summary>F10 (<c>ESC [ 2 1 ~</c>).</summary>
    F10,

    /// <summary>F11 (<c>ESC [ 2 3 ~</c>).</summary>
    F11,

    /// <summary>F12 (<c>ESC [ 2 4 ~</c>).</summary>
    F12,

    /// <summary>Shift+Tab (<c>ESC [ Z</c>); also called "Back Tab".</summary>
    BackTab,
}

// ─────────────────────────────────────────────────────────────────────────────
// Modifiers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Modifier keys that may accompany a key press.
/// Values are powers of two so they can be combined with bitwise OR.
/// </summary>
[Flags]
public enum Modifiers
{
    /// <summary>No modifier keys.</summary>
    None = 0,

    /// <summary>The Ctrl key was held.</summary>
    Ctrl = 1,

    /// <summary>The Alt (Meta) key was held, or an ESC prefix was received.</summary>
    Alt = 2,

    /// <summary>
    /// The Shift key was held.
    /// Only appears on <see cref="KeyCode.Named"/> keys (e.g. Shift+Arrow, BackTab).
    /// Shift is never synthesised for printable <see cref="KeyCode.FromRune(Rune)"/> runes — the
    /// upper-case letter itself encodes the shift implicitly.
    /// Shift is never reported together with Ctrl (the terminal collapses them on the wire).
    /// </summary>
    Shift = 4,
}

// ─────────────────────────────────────────────────────────────────────────────
// KeyCode
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Discriminated value identifying which key was pressed.
/// Either a Unicode scalar (<see cref="FromRune(Rune)"/>) or a named terminal key
/// (<see cref="Named(NamedKey)"/>).
/// </summary>
public readonly record struct KeyCode
{
    /// <summary>Identifies which representation this code uses.</summary>
    public enum KeyCodeKind
    {
        /// <summary>A Unicode scalar value (printable character or Ctrl-modified letter).</summary>
        UnicodeScalar,

        /// <summary>A named terminal key from <see cref="NamedKey"/>.</summary>
        Named,
    }

    private readonly KeyCodeKind _kind;
    private readonly Rune _rune;       // valid when Kind == UnicodeScalar
    private readonly NamedKey _named;  // valid when Kind == Named

    private KeyCode(KeyCodeKind kind, Rune rune, NamedKey named)
    {
        _kind = kind;
        _rune = rune;
        _named = named;
    }

    /// <summary>Which representation this code uses.</summary>
    public KeyCodeKind Kind => _kind;

    /// <summary>
    /// Creates a <see cref="KeyCode"/> representing a Unicode scalar value.
    /// </summary>
    /// <param name="rune">The Unicode scalar.</param>
    /// <returns>A <see cref="KeyCode"/> with <see cref="KeyCodeKind.UnicodeScalar"/> kind.</returns>
    public static KeyCode FromRune(Rune rune) => new(KeyCodeKind.UnicodeScalar, rune, default);

    /// <summary>
    /// Creates a <see cref="KeyCode"/> representing a named terminal key.
    /// </summary>
    /// <param name="key">The named key.</param>
    /// <returns>A <see cref="KeyCode"/> with <see cref="KeyCodeKind.Named"/> kind.</returns>
    public static KeyCode Named(NamedKey key) => new(KeyCodeKind.Named, default, key);

    /// <summary>
    /// For <see cref="KeyCodeKind.UnicodeScalar"/> codes, returns the Unicode scalar.
    /// Throws <see cref="InvalidOperationException"/> for other kinds.
    /// </summary>
    public Rune RuneValue =>
        _kind == KeyCodeKind.UnicodeScalar
            ? _rune
            : throw new InvalidOperationException($"KeyCode kind is {_kind}, not UnicodeScalar.");

    /// <summary>
    /// For <see cref="KeyCodeKind.Named"/> codes, returns the <see cref="NamedKey"/> value.
    /// Throws <see cref="InvalidOperationException"/> for other kinds.
    /// </summary>
    public NamedKey NamedValue =>
        _kind == KeyCodeKind.Named
            ? _named
            : throw new InvalidOperationException($"KeyCode kind is {_kind}, not Named.");

    /// <inheritdoc/>
    public override string ToString() => _kind switch
    {
        KeyCodeKind.UnicodeScalar => $"UnicodeScalar({_rune})",
        KeyCodeKind.Named => $"Named({_named})",
        _ => "Unknown",
    };
}
