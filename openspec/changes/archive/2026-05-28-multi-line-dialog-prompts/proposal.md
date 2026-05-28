## Why

The first real consumer of dcli's dialog surface — `dmon-migration` Phase 2's `ToolConfirmPrompt` port — surfaced a uniform API gap across all four dialog request types. `SelectRequest.Title`, `MultiSelectRequest.Title`, `ChoiceRequest.Prompt`, and `InputRequest.Prompt` are each typed as `Line?` (a single styled line). The dmon `terminal-host` spec, however, requires the tool-confirmation overlay's preamble to carry **multiple** styled lines (tool name, args, and — for high-risk confirmations — a styled `⚠ HIGH RISK` indicator) "as part of the request's prompt content" / "before the option list."

The dmon worker shipped a scrollback workaround (append the preamble lines to `Scrollback` before opening `ChoiceAsync`; pass only `"Permission:"` into the dialog's `Prompt`). User-visible outcome is identical to multi-line in-overlay rendering, but the workaround codifies a gap in dcli's API rather than closing it.

The dmon migration is explicitly a vehicle for proving and improving dcli — workarounds in dmon are substrate-gap signals. This change closes the gap so the dmon Phase 2 commit lands cleanly via dcli's intended surface, and any future consumer presenting tool confirmations, multi-step wizards with explanatory text, or branching choice dialogs gets the same affordance for free.

## What Changes

- **Widen the dialog-preamble field on all four request types** from `Line?` to `IReadOnlyList<Line>?`, symmetrically:
  - `SelectRequest.Title : Line?` → `IReadOnlyList<Line>? Title`
  - `MultiSelectRequest.Title : Line?` → `IReadOnlyList<Line>? Title`
  - `ChoiceRequest.Prompt : Line?` → `IReadOnlyList<Line>? Prompt`
  - `InputRequest.Prompt : Line?` → `IReadOnlyList<Line>? Prompt`
- **Backwards-compat constructors** preserve every existing call site:
  - Single-`Line` overload: wraps the line into a single-element list internally.
  - Single-`string` overload: `Line.FromText(s)` → single-element list (preserves the api-ergonomics-pass-1 string overload).
  - `params Line[]` / `IReadOnlyList<Line>` overloads for the multi-line cases.
- **Renderer iterates the preamble list** instead of rendering one line. `Dialog` / `ChoiceDialog` / `InputDialog` each paint the preamble lines top-to-bottom above the interactive widget; the overlay's vertical layout-budget arithmetic already handles variable preamble heights (live-blocks have shipped this since v1).
- **Field name stays the same** on each request — `Title` for `Select`/`MultiSelect`, `Prompt` for `Choice`/`Input`. The semantic distinction is carried by the field name, not the type; multi-line is now allowed in both.
- **No breaking changes.** Every existing caller — including the `Line.FromText` single-line shortcut callers added in `api-ergonomics-pass-1` — continues to compile and behave identically. The widening is additive.
- **Version bump:** `dcli` and `Dcli.Testing` from `0.2.0-rc.1` → `0.2.0-rc.2` (additive minor revision; preview channel).

## Capabilities

### New Capabilities

None — this change extends an existing capability only.

### Modified Capabilities

- `fixed-region`: MODIFIED dialog request shape to specify that the preamble (`Title` on `Select`/`MultiSelect`, `Prompt` on `Choice`/`Input`) is a list of styled lines rendered top-to-bottom above the interactive widget. Backwards-compat is explicit in the spec: single-`Line` and single-`string` overload constructors continue to function and produce a one-element preamble.

## Impact

- **Public API (`Dcli`):** four request records change a field's type. Source-level call-site compatibility is preserved by the new convenience constructors; binary compatibility breaks for any caller compiled against rc.1's `Line?` field. `dcli` is `0.2.0-rc.x` (preview channel) and dmon (the only known consumer) takes a local `<ProjectReference>` — recompile suffices.
- **Public API (`Dcli.Testing`):** no surface change. `HeadlessTerminal` consumes the request types as opaque inputs; multi-line preambles render correctly through the existing painter.
- **Production code:** small. Each request record gains a new field type and 1–2 convenience constructors. The three dialogs (`Dialog`, `ChoiceDialog`, `InputDialog`) each get one loop replacing a one-shot preamble paint. Existing layout-budget arithmetic is unchanged.
- **Tests:** ~6–8 new tests in `tests/Dcli.Tests/` covering the multi-line preamble for each of the four request types (via `HeadlessTerminal`); round-trip tests on the new convenience constructors. The existing single-line tests stay green as a regression guard for backwards compatibility.
- **Consumers:** unblocks the dmon-migration Phase 2 commit. dmon will drop its `ToolConfirmPrompt` scrollback workaround in favour of passing the preamble lines directly into `ChoiceRequest.Prompt`.
- **Out of scope (deferred):**
  - Multi-select with `AllowBack` (still deferred from `api-ergonomics-pass-1`).
  - Layout-budget overflow handling specifically for very tall dialog preambles. The current overlay budget already truncates if the preamble + widget exceeds available rows; nothing changes here.
  - Renaming any field (e.g. `Title` → `Header`). Existing names stay; the type changes only.
  - VT-escape sanitisation of `Segment.Text` (still tracked in memory `vt-escape-sanitization-gap`).
