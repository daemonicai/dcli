using System.Buffers;
using System.Text;

namespace Dcli.Internal.Input;

/// <summary>
/// Incremental, pure-state-machine VT input parser.
/// <para>
/// Feed raw bytes from the terminal with <see cref="Feed"/>; decoded <see cref="InputEvent"/>
/// values are appended to the caller-supplied sink collection. The parser retains
/// partial state across calls, so a multi-byte UTF-8 scalar or an escape sequence that arrives
/// split across two reads is decoded correctly.
/// </para>
/// <para>
/// The parser has <em>no I/O, no clock, and no threads</em>. The §6 input reader owns the
/// timeout and calls <see cref="Flush"/> when a timed read expires while an ESC is pending.
/// </para>
/// <para>
/// Unrecognised CSI / SS3 sequences are consumed and silently dropped; the stream does not
/// desync. The design is extensible: mouse, Kitty, and focus sequences can be added by
/// extending <see cref="EmitCsi"/> / <see cref="EmitSs3"/> without restructuring the state
/// machine.
/// </para>
/// </summary>
internal sealed class VtInputParser
{
    // ─── Parser state ────────────────────────────────────────────────────────

    private enum State
    {
        Ground,
        Utf8,      // accumulating a multi-byte UTF-8 scalar
        Esc,       // received 0x1B, waiting for follow-up
        CsiParam,  // inside CSI (ESC [), accumulating parameter bytes
        Ss3,       // inside SS3 (ESC O), waiting for final
        Paste,     // inside bracketed paste, accumulating content
    }

    private State _state = State.Ground;

    // UTF-8 accumulator (up to 4 bytes)
    private readonly byte[] _utf8Buf = new byte[4];
    private int _utf8Len;        // bytes stored so far
    private int _utf8Needed;     // total bytes expected for this scalar

    // CSI accumulator: parameter + intermediate bytes (e.g. "1;5" from ESC[1;5A)
    // We store at most 64 bytes — more than enough for any real sequence.
    private readonly byte[] _csiBuf = new byte[64];
    private int _csiLen;

    // Bracketed-paste accumulator.
    // _pasteBuf holds the literal content bytes accumulated so far.
    // _pasteEndCandidateLen tracks how many leading bytes of _pasteEndMarker have been
    // matched; if the stream diverges those bytes are flushed as literal content.
    private List<byte>? _pasteBuf;
    private readonly byte[] _pasteEndMarker = [(byte)0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];
    private int _pasteEndCandidateLen;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Feeds a span of raw terminal bytes into the parser.
    /// Decoded <see cref="InputEvent"/> values are appended to <paramref name="sink"/>.
    /// </summary>
    /// <param name="bytes">Raw bytes from the terminal read buffer.</param>
    /// <param name="sink">Collection that receives decoded events.</param>
    public void Feed(ReadOnlySpan<byte> bytes, ICollection<InputEvent> sink)
    {
        foreach (byte b in bytes)
            ConsumeByte(b, sink);
    }

    /// <summary>
    /// <see langword="true"/> when the parser is holding a bare, unresolved ESC byte
    /// (state == <c>Esc</c>). The §6 input reader uses this to decide whether to call
    /// <see cref="Flush"/> on a 0-byte timed-read: flush only when this is
    /// <see langword="true"/>; do NOT flush on a partial CSI/UTF-8/paste boundary.
    /// </summary>
    public bool HasPendingEscape => _state == State.Esc;

    /// <summary>
    /// Resolves a pending lone ESC into a <see cref="NamedKey.Escape"/> key event.
    /// Call this when a timed read returns no bytes while the parser is in the
    /// <c>Esc</c> state (i.e. the §6 reader's ESC-disambiguation timeout has expired).
    /// Has no effect when no ESC is pending.
    /// </summary>
    /// <param name="sink">Collection that receives the flushed event (if any).</param>
    public void Flush(ICollection<InputEvent> sink)
    {
        if (_state == State.Esc)
            sink.Add(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));

        // Any other pending state (partial UTF-8, partial CSI, paste) is silently discarded —
        // those are malformed/incomplete sequences that cannot be completed.
        ResetToGround();
    }

    // ─── Core state machine ──────────────────────────────────────────────────

    private void ConsumeByte(byte b, ICollection<InputEvent> sink)
    {
        switch (_state)
        {
            case State.Ground:
                ConsumeGround(b, sink);
                break;

            case State.Utf8:
                ConsumeUtf8Continuation(b, sink);
                break;

            case State.Esc:
                ConsumeAfterEsc(b, sink);
                break;

            case State.CsiParam:
                ConsumeCsiParam(b, sink);
                break;

            case State.Ss3:
                ConsumeSs3(b, sink);
                break;

            case State.Paste:
                ConsumePaste(b, sink);
                break;
        }
    }

    private void ResetToGround()
    {
        _state = State.Ground;
        _utf8Len = 0;
        _utf8Needed = 0;
        _csiLen = 0;
        _pasteBuf = null;
        _pasteEndCandidateLen = 0;
    }

    // ─── GROUND ──────────────────────────────────────────────────────────────

    private void ConsumeGround(byte b, ICollection<InputEvent> sink)
    {
        if (b == 0x1B)
        {
            _state = State.Esc;
            return;
        }

        if (b == 0x09) { sink.Add(Key(KeyCode.Named(NamedKey.Tab))); return; }
        if (b == 0x0D) { sink.Add(Key(KeyCode.Named(NamedKey.Enter))); return; }
        if (b == 0x08 || b == 0x7F) { sink.Add(Key(KeyCode.Named(NamedKey.Backspace))); return; }

        // C0 control bytes 0x01–0x1A (minus 0x08/0x09/0x0D already handled above):
        // These encode Ctrl+letter. 0x01='A'-0x40 → 'a', …, 0x1A='Z'-0x40 → 'z'.
        // 0x00 (NUL) and 0x1C–0x1F (FS/GS/RS/US) are also control codes but lack a
        // clear letter mapping; we emit them as Char(Rune) + Ctrl with the raw value
        // so consumers can decide what to do.
        if (b <= 0x1F)
        {
            Rune r = b switch
            {
                >= 0x01 and <= 0x1A => new Rune('a' + (b - 0x01)),  // Ctrl+A..Ctrl+Z
                _ => new Rune(b),                                      // NUL, 0x1C-0x1F
            };
            sink.Add(new KeyEvent(KeyCode.FromRune(r), Modifiers.Ctrl));
            return;
        }

        // DEL (0x7F) is already handled above.

        // Multi-byte UTF-8 start bytes:
        if ((b & 0xE0) == 0xC0 && b >= 0xC2) { StartUtf8(b, 2); return; }
        if ((b & 0xF0) == 0xE0) { StartUtf8(b, 3); return; }
        if ((b & 0xF8) == 0xF0 && b <= 0xF4) { StartUtf8(b, 4); return; }

        // Invalid UTF-8 lead byte (continuation byte in GROUND, overlong, or > 0xF4):
        // Emit U+FFFD and stay in GROUND.
        if ((b & 0x80) != 0) { sink.Add(Key(KeyCode.FromRune(Rune.ReplacementChar))); return; }

        // ASCII printable (0x20–0x7E).
        sink.Add(Key(KeyCode.FromRune(new Rune(b))));
    }

    // ─── UTF-8 multi-byte accumulation ───────────────────────────────────────

    private void StartUtf8(byte leadByte, int totalBytes)
    {
        _utf8Buf[0] = leadByte;
        _utf8Len = 1;
        _utf8Needed = totalBytes;
        _state = State.Utf8;
    }

    private void ConsumeUtf8Continuation(byte b, ICollection<InputEvent> sink)
    {
        if ((b & 0xC0) != 0x80)
        {
            // Not a continuation byte — the sequence is malformed.
            // Emit U+FFFD for the truncated sequence and re-process this byte from GROUND.
            sink.Add(Key(KeyCode.FromRune(Rune.ReplacementChar)));
            _state = State.Ground;
            _utf8Len = 0;
            _utf8Needed = 0;
            ConsumeGround(b, sink);
            return;
        }

        _utf8Buf[_utf8Len++] = b;

        if (_utf8Len < _utf8Needed)
            return; // still accumulating

        // Attempt to decode the complete scalar.
        OperationStatus status = Rune.DecodeFromUtf8(_utf8Buf.AsSpan(0, _utf8Needed), out Rune rune, out _);
        if (status == OperationStatus.Done)
            sink.Add(Key(KeyCode.FromRune(rune)));
        else
            sink.Add(Key(KeyCode.FromRune(Rune.ReplacementChar)));

        _state = State.Ground;
        _utf8Len = 0;
        _utf8Needed = 0;
    }

    // ─── ESC dispatch ────────────────────────────────────────────────────────

    private void ConsumeAfterEsc(byte b, ICollection<InputEvent> sink)
    {
        if (b == (byte)'[')
        {
            // CSI: ESC [
            _csiLen = 0;
            _state = State.CsiParam;
            return;
        }

        if (b == (byte)'O')
        {
            // SS3: ESC O (used by xterm/macOS for arrows, Home, End, F1-F4)
            _state = State.Ss3;
            return;
        }

        // Alt+key prefix: ESC followed by a printable/control byte that is not '[' or 'O'.
        // Decode the byte as if in GROUND but with Alt modifier.
        _state = State.Ground;

        if (b == 0x1B)
        {
            // ESC ESC: emit Escape, then start another pending ESC.
            sink.Add(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            _state = State.Esc;
            return;
        }

        // Produce the Alt-modified event. ConsumeGround always yields exactly one event
        // for any non-ESC byte, so the list will have exactly one KeyEvent.
        List<InputEvent> tmp = new(1);
        ConsumeGround(b, tmp);
        sink.Add(((KeyEvent)tmp[0]) with { Modifiers = ((KeyEvent)tmp[0]).Modifiers | Modifiers.Alt });
    }

    // ─── CSI parameter accumulation ──────────────────────────────────────────

    // CSI parameter bytes: 0x30–0x3F (digits and ';', '<', '=', '>', '?')
    // CSI intermediate bytes: 0x20–0x2F
    // CSI final bytes: 0x40–0x7E
    private void ConsumeCsiParam(byte b, ICollection<InputEvent> sink)
    {
        if (b is >= 0x30 and <= 0x3F || b is >= 0x20 and <= 0x2F)
        {
            // Parameter or intermediate byte — accumulate.
            if (_csiLen < _csiBuf.Length)
                _csiBuf[_csiLen++] = b;
            // If the buffer overflows we keep going (drop excess bytes) — still safe.
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            // Final byte — dispatch. EmitCsi may set _state to Paste.
            string paramStr = Encoding.ASCII.GetString(_csiBuf, 0, _csiLen);
            _state = State.Ground;
            _csiLen = 0;
            EmitCsi(paramStr, b, sink);
            return;
        }

        // Anything else (C0, DEL, etc.) in the middle of a CSI sequence is invalid —
        // discard the sequence and process this byte from GROUND.
        _state = State.Ground;
        _csiLen = 0;
        ConsumeGround(b, sink);
    }

    // ─── SS3 final ───────────────────────────────────────────────────────────

    private void ConsumeSs3(byte b, ICollection<InputEvent> sink)
    {
        _state = State.Ground;
        EmitSs3(b, sink);
    }

    // ─── CSI decode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes a complete CSI sequence. <paramref name="paramStr"/> holds the parameter/
    /// intermediate bytes (everything between ESC[ and the final byte); <paramref name="final"/>
    /// is the final byte. May transition <see cref="_state"/> to <see cref="State.Paste"/>
    /// when the bracketed-paste start marker (200~) is recognised.
    /// </summary>
    private void EmitCsi(string paramStr, byte final, ICollection<InputEvent> sink)
    {
        // ── BackTab: ESC [ Z ──────────────────────────────────────────────────
        if (final == (byte)'Z' && paramStr.Length == 0)
        {
            sink.Add(new KeyEvent(KeyCode.Named(NamedKey.BackTab), Modifiers.Shift));
            return;
        }

        // ── Parse modifier from paramStr ────────────────────────────────────
        // Standard modifier-encoded form: "1;<m><final>" or "<n>;<m>~"
        // Modifier bitmask (m): 1 + bitmask, where bitmask = Shift(1)|Alt(2)|Ctrl(4).
        // So m=1→None, m=2→Shift, m=3→Alt, m=4→Shift+Alt, m=5→Ctrl, m=6→Shift+Ctrl,
        //    m=7→Alt+Ctrl, m=8→Shift+Alt+Ctrl.

        Modifiers mods = Modifiers.None;
        string coreParam = paramStr;

        int semicolonIdx = paramStr.IndexOf(';', StringComparison.Ordinal);
        if (semicolonIdx >= 0)
        {
            string modPart = paramStr[(semicolonIdx + 1)..];
            coreParam = paramStr[..semicolonIdx];
            if (int.TryParse(modPart, out int mRaw) && mRaw >= 1)
            {
                int bitmask = mRaw - 1;
                if ((bitmask & 1) != 0) mods |= Modifiers.Shift;
                if ((bitmask & 2) != 0) mods |= Modifiers.Alt;
                if ((bitmask & 4) != 0) mods |= Modifiers.Ctrl;
            }
        }

        // Under Ctrl, strip Shift (terminal-truth rule 5.5).
        if ((mods & Modifiers.Ctrl) != 0)
            mods &= ~Modifiers.Shift;

        // ── Cursor-movement finals (A–D, H, F) ──────────────────────────────
        if (final is (byte)'A' or (byte)'B' or (byte)'C' or (byte)'D' or (byte)'H' or (byte)'F')
        {
            NamedKey? key = final switch
            {
                (byte)'A' => NamedKey.Up,
                (byte)'B' => NamedKey.Down,
                (byte)'C' => NamedKey.Right,
                (byte)'D' => NamedKey.Left,
                (byte)'H' => NamedKey.Home,
                (byte)'F' => NamedKey.End,
                _ => null,
            };
            if (key is not null)
            {
                sink.Add(new KeyEvent(KeyCode.Named(key.Value), mods));
                return;
            }
        }

        // ── SS3-style F1–F4 via CSI with modifiers (ESC[1;<m>P…S) ───────────
        if (final is (byte)'P' or (byte)'Q' or (byte)'R' or (byte)'S')
        {
            NamedKey? key = final switch
            {
                (byte)'P' => NamedKey.F1,
                (byte)'Q' => NamedKey.F2,
                (byte)'R' => NamedKey.F3,
                (byte)'S' => NamedKey.F4,
                _ => null,
            };
            if (key is not null)
            {
                sink.Add(new KeyEvent(KeyCode.Named(key.Value), mods));
                return;
            }
        }

        // ── Tilde-terminated sequences (ESC [ <n> ~) ─────────────────────────
        if (final == (byte)'~')
        {
            if (!int.TryParse(coreParam, out int n))
            {
                // Non-numeric param — consume and drop, no desync.
                return;
            }

            // ── Bracketed paste start (ESC [ 2 0 0 ~) ────────────────────────
            if (n == 200)
            {
                _state = State.Paste;
                _pasteBuf = [];
                _pasteEndCandidateLen = 0;
                return;
            }

            NamedKey? key = n switch
            {
                1 or 7 => NamedKey.Home,
                2 => NamedKey.Insert,
                3 => NamedKey.Delete,
                4 or 8 => NamedKey.End,
                5 => NamedKey.PageUp,
                6 => NamedKey.PageDown,
                11 => NamedKey.F1,
                12 => NamedKey.F2,
                13 => NamedKey.F3,
                14 => NamedKey.F4,
                15 => NamedKey.F5,
                17 => NamedKey.F6,
                18 => NamedKey.F7,
                19 => NamedKey.F8,
                20 => NamedKey.F9,
                21 => NamedKey.F10,
                23 => NamedKey.F11,
                24 => NamedKey.F12,
                _ => null,
            };

            if (key is not null)
            {
                sink.Add(new KeyEvent(KeyCode.Named(key.Value), mods));
                return;
            }

            // Unrecognised tilde code — consume and drop (safe, no desync).
            return;
        }

        // ── Unrecognised final — consume and drop ────────────────────────────
        // This is the catch-all that makes the parser safe for mouse / Kitty /
        // focus events (those use finals like 'M', 'm', 'I', 'O', etc.).
    }

    // ─── Bracketed paste accumulation ────────────────────────────────────────

    /// <summary>
    /// Consumes one byte while inside bracketed paste mode.
    /// <para>
    /// Paste content is literal — all bytes including control bytes and ESC sequences are
    /// accumulated verbatim until the end marker <c>ESC [ 2 0 1 ~</c> is recognised.
    /// The end marker may arrive split across multiple <see cref="Feed"/> calls; partial
    /// matches are buffered and only committed to the paste body if the byte stream diverges
    /// from the end marker before it completes.
    /// </para>
    /// </summary>
    private void ConsumePaste(byte b, ICollection<InputEvent> sink)
    {
        // _pasteEndMarker = [0x1B, '[', '2', '0', '1', '~']
        // _pasteEndCandidateLen tracks how many leading bytes of _pasteEndMarker have been matched.

        if (_pasteEndCandidateLen > 0)
        {
            // We are inside a partial end-marker match. Check if this byte continues it.
            if (b == _pasteEndMarker[_pasteEndCandidateLen])
            {
                _pasteEndCandidateLen++;
                if (_pasteEndCandidateLen == _pasteEndMarker.Length)
                {
                    // End marker fully matched — emit the paste event.
                    string text = Encoding.UTF8.GetString(_pasteBuf!.ToArray());
                    sink.Add(new PasteEvent(text));
                    ResetToGround();
                }
                // Otherwise: still matching, wait for more bytes.
                return;
            }

            // Diverged from end marker — the buffered candidate bytes are literal paste content.
            for (int i = 0; i < _pasteEndCandidateLen; i++)
                _pasteBuf!.Add(_pasteEndMarker[i]);
            _pasteEndCandidateLen = 0;

            // Now process the current byte from the top of ConsumePaste (fall through below).
        }

        // Start of a potential end-marker match?
        if (b == _pasteEndMarker[0])
        {
            _pasteEndCandidateLen = 1;
            return;
        }

        // Ordinary paste content byte.
        _pasteBuf!.Add(b);
    }

    // ─── SS3 decode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes an SS3 sequence. On macOS/xterm, arrow keys and F1–F4 arrive as
    /// <c>ESC O A/B/C/D</c> and <c>ESC O P/Q/R/S</c> respectively.
    /// Home/End can also arrive as <c>ESC O H/F</c>.
    /// </summary>
    private static void EmitSs3(byte final, ICollection<InputEvent> sink)
    {
        NamedKey? key = final switch
        {
            (byte)'A' => NamedKey.Up,
            (byte)'B' => NamedKey.Down,
            (byte)'C' => NamedKey.Right,
            (byte)'D' => NamedKey.Left,
            (byte)'H' => NamedKey.Home,
            (byte)'F' => NamedKey.End,
            (byte)'P' => NamedKey.F1,
            (byte)'Q' => NamedKey.F2,
            (byte)'R' => NamedKey.F3,
            (byte)'S' => NamedKey.F4,
            _ => null,
        };

        if (key is not null)
            sink.Add(new KeyEvent(KeyCode.Named(key.Value), Modifiers.None));

        // Unrecognised SS3 final — consume and drop.
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static KeyEvent Key(KeyCode code) => new(code, Modifiers.None);
}
