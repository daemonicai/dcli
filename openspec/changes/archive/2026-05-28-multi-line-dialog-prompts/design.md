## Context

`dcli` v1 (`core-rendering-architecture`, archived `2026-05-28`) shipped the dialog surface with single-line preamble fields on each of the four request types. `api-ergonomics-pass-1` (archived `2026-05-28`) added string overloads and the `AllowBack` flag to close three blockers for the upcoming `dmon-migration` change. Pass-1's Decision 2 framed the request-record surface as "every overload is an addition" — backwards-compatible by construction.

`dmon-migration` Phase 2 — the first end-to-end port of dmon's dialog surfaces onto dcli — surfaced a fourth gap that pass-1 did not anticipate: the dialog preamble itself wants to be multi-line, not just multi-styled-segment-on-one-line. The dmon `terminal-host` spec text for tool confirmation explicitly says the `⚠ HIGH RISK` indicator and the tool/args context belong "in the request's prompt content" / "before the option list" — multiple visually-separated lines, inside the overlay, above the option list.

The worker's fallback was to append `Tool:` / `Args:` / risk-indicator to `Scrollback` before opening `ChoiceAsync`. User-visible outcome is identical (the user sees all the right information before the permission prompt), but the workaround puts dialog-preamble content outside the overlay's logical boundary. dmon held its Phase 2 commit and asked dcli to widen the preamble surface instead.

This change is consumer-driven: the dmon migration is dcli's first real validation pass, and a real consumer surfacing a gap is exactly the signal pass-1's design anticipated.

## Goals / Non-Goals

**Goals:**
- Allow multi-line preambles on all four dialog request types (`SelectRequest.Title`, `MultiSelectRequest.Title`, `ChoiceRequest.Prompt`, `InputRequest.Prompt`).
- Preserve source-level backwards compatibility: existing single-`Line` and single-`string` overload call sites continue to compile and behave identically.
- Symmetric across all four request types — no per-type special-casing.
- Render the preamble lines top-to-bottom in the overlay above the interactive widget, using the existing fixed-region layout-budget primitives.

**Non-Goals:**
- Renaming any field (`Title` stays `Title`; `Prompt` stays `Prompt`). The type changes; the names do not.
- New layout-budget overflow handling. The existing overlay-budget arithmetic already truncates when the preamble + widget overflow available rows; the truncation behaviour for tall multi-line preambles inherits this, not a new design.
- `MultiSelectRequest.AllowBack`. Still deferred from pass-1.
- Implicit `string → Line` or `Line → IReadOnlyList<Line>` conversions. Rejected for the same reasons pass-1 rejected implicit `string → Line` (Decision 1): explicit factories and overload constructors keep typing intentional.
- A new "Header" / "Body" field split. Considered (and rejected — see Decision 5).
- Touching internal mechanics beyond the dialog paint loops: no loop changes, no scrollback model changes, no parser changes.

## Decisions

### 1. Widen all four preamble fields symmetrically — same change, four sites

The change applies uniformly to `SelectRequest.Title`, `MultiSelectRequest.Title`, `ChoiceRequest.Prompt`, and `InputRequest.Prompt`. Each goes from `Line?` to `IReadOnlyList<Line>?`.

- **Why symmetric:** the dmon use case is `ChoiceRequest`, but the constraint is uniform across the dialog surface. Widening only `ChoiceRequest` would create inconsistency at the public API level — a consumer presenting a multi-line context for a Select dialog ("here's what this list is showing") or InputDialog ("explain what we're collecting and why") would hit the same wall. The cost of widening four records vs one is trivial; the symmetry cost of NOT widening all four is permanent.
- **Why keep separate names (`Title` vs `Prompt`):** the semantic distinction is real — `Select`/`MultiSelect` present a list to pick from (the "title" labels the list); `Choice`/`Input` present a prompt with options or a field to fill. Renaming to `Header` everywhere would erase that distinction without functional benefit. Each name stays; only the cardinality changes.

### 2. `IReadOnlyList<Line>?` over `Line[]`, `IEnumerable<Line>`, or a custom wrapper

The widened field is typed `IReadOnlyList<Line>?`. Alternatives considered:

- **`Line[]?`** — concrete, but commits the API to an array. Forces consumers passing `List<Line>` to call `.ToArray()`. Rejected.
- **`IEnumerable<Line>?`** — most permissive, but defeats the dialog's need to count lines for layout-budget arithmetic. Forces internal `ToList()`. Rejected.
- **A custom `PreambleContent` record** — adds a new public type for one field across four records. Over-engineered. Rejected.
- **`IReadOnlyList<Line>?`** — matches the existing `Items` / `Options` types (`IReadOnlyList<Line>`); deterministic enumeration; supports `Count` for budget arithmetic. **Chosen.**

### 3. Backwards-compat via overload constructors, not implicit conversions

Each request type gains constructors covering:
- `IReadOnlyList<Line>` (the new canonical form).
- `params Line[]` (inline literal multi-line lists).
- `Line` single-line (wraps in a one-element list internally, matching prior behaviour).
- `string` single-line (wraps `Line.FromText(s)` in a one-element list — preserves pass-1's string overload).
- `IReadOnlyList<string>` / `params string[]` for plain-text multi-line preambles (parallel to pass-1's item-list string overloads).

The single-`Line` and single-`string` constructors call into the multi-line constructor with a one-element list. There's no semantic change for existing callers — internally the dialog sees an `IReadOnlyList<Line>?` of length one, identical to today's single-line render.

- **Why not an implicit `Line → IReadOnlyList<Line>` conversion:** pass-1's Decision 1 rejected implicit `string → Line` on the same grounds. Implicit cardinality-widening would mean a `Line` accidentally collected into a list with no opt-in, which is the wrong default for a styling-sensitive surface.
- **Why `params Line[]` AND `IReadOnlyList<Line>`:** identical rationale to pass-1's Decision 2 — `params` reads naturally for inline literals (`new ChoiceRequest(options, prompt1, prompt2)`) and `IReadOnlyList<Line>` covers the "I computed these lines" case.

### 4. Renderer iterates; budget arithmetic is unchanged

The dialog paint loop changes from:

```csharp
if (preamble is not null) PaintLine(preamble);
PaintWidget();
```

to:

```csharp
foreach (Line line in preamble ?? []) PaintLine(line);
PaintWidget();
```

The overlay-budget arithmetic already knows how to allocate rows for a variable-height preamble — it does this for live-blocks shipped in v1. The dialog overlay re-uses the same budget machinery; the consumer cap on preamble height is "however many rows fit before the widget gets clipped", which is the existing constraint.

- **What happens if preamble + widget exceeds the overlay budget:** the existing truncation behaviour applies. If the widget is the only widget and the preamble is too tall, the widget paints with whatever rows remain; the preamble paints up to the budget, then truncates. No new behaviour, no new edge case — the v1 budget arithmetic already covers this.

### 5. No `Header` / `Body` field split

Considered: add a new `IReadOnlyList<Line>? Header` alongside the existing `Line? Prompt`. Each is conceptually "a different thing" — header is a title-like row, body is the content.

**Rejected:**
- Two fields with overlapping purpose is confusing for consumers ("when do I use Prompt vs Header?").
- The dmon use case (tool/args/risk + "Permission:" prompt) doesn't actually need two fields — it needs one multi-line prompt. The styled separation between "context lines" and "the actual prompt" lives in the `Style` of each `Line`, not in a separate field.
- A single multi-line field carrying ordered lines composes more naturally for future use cases (a wizard step with a multi-paragraph explanation followed by the choice options).

### 6. Tests ride on `Dcli.Testing` only

Same convention as pass-1 (Decision 7). Every new test uses `HeadlessTerminal` from `Dcli.Testing`. The tier-A `FakeTerminalTests` is in dmon's test project, not dcli's; dcli stays test-substrate-symmetric without a hand-rolled fake.

### 7. Sample updates

The `samples/Dcli.Demo.DmonWizard` sample is updated to demonstrate a multi-line wizard-step preamble (e.g. the auth-config step gets a brief description above the input field). This validates the API end-to-end through a real consumer flow before dmon picks it up.

## Risks / Trade-offs

- **Binary compatibility break for any caller compiled against rc.1's `Line?` field.** `dcli` is preview-channel (`0.2.0-rc.x`); the only known consumer is dmon, which takes a local `<ProjectReference>` and recompiles. Outside that, callers picking up `0.2.0-rc.2` need to recompile — no source-level changes required.
- **Renderer must handle the `null` / empty-list / `[single line]` / `[multi-line]` cases consistently.** The paint loop's `foreach` over `preamble ?? []` covers all four cleanly; existing tests covering "no preamble" via `Line? Prompt = null` continue to pass after the widening (now `IReadOnlyList<Line>? Prompt = null`).
- **Overlay-budget tall-preamble edge case:** a consumer passing a 30-line preamble for a 20-row terminal will hit truncation. This is inherent to the budget model; this change does not introduce it, and a tall-preamble UX is a consumer-side decision (paginate the preamble, append to scrollback, etc.).
- **The "scrollback above + single-line Prompt" workaround dmon shipped will become a soft anti-pattern** once this change lands. The dmon Phase 2 resume recipe (memory `dmon-migration-phase2-paused-on-dcli`) calls out exactly this: drop the workaround in favour of multi-line `Prompt`. No risk to the dcli side; this is the intended consumer follow-up.
- **Future field additions to request records may want similar widening** (e.g. if `Default` on `InputRequest` ever wants to be multi-line, or if multi-select gains a multi-line title). The pattern this change establishes — `IReadOnlyList<Line>?` + convenience constructors — sets the template for future widenings.

## Migration Plan

- Open the change on a branch `change/multi-line-dialog-prompts` off `main`.
- Apply per `tasks.md`, one §-section per commit, gated by the standard four gates + reviewer audit.
- The spec delta in `specs/fixed-region/spec.md` is `MODIFIED` to "Awaitable modal dialogs" — strictly additive in behaviour terms (multi-line is allowed; single-line still works).
- After archive, fold the delta into `openspec/specs/fixed-region/spec.md`.
- Bump `Dcli.csproj` and `Dcli.Testing.csproj` from `0.2.0-rc.1` → `0.2.0-rc.2` (additive minor revision, preview channel).
- Notify dmon (or coordinate via the user) so the dmon Phase 2 resume recipe runs: drop the `ToolConfirmPrompt` scrollback workaround, restore lines into `ChoiceRequest.Prompt`, update `ToolConfirmPromptTests`, run gates, commit Phase 2.

## Open Questions

None. The shape (`IReadOnlyList<Line>?`), naming (keep `Title` / `Prompt`), backwards compatibility approach (overload constructors), and renderer change (iterate the list) are all settled by the alternatives-considered analysis under Decisions.
