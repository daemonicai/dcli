# DEVLOG — `vt-escape-sanitization`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see the project's `CLAUDE.md`). Captures per-section narrative the spec
> files don't carry: decisions under uncertainty, deviations, surfaced bugs, HITL
> verifications. On archive this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-vt-escape-sanitization/DEVLOG.md` and the status flips
> to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/vt-escape-sanitization`** (created from `main`). Stay on it.
- Working tree state: CLEAN (§1 committed).
- Sanity check command:
  `dotnet build && dotnet test && dotnet format --verify-no-changes && openspec validate vt-escape-sanitization --strict`
- Resume point: **§2 — Segment safe-by-default construction + Raw seam** (first unticked task: `2.1`). See the Section status table for what's done so far.
- Check the memory files listed at the bottom before briefing — several encode hard-won constraints.

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Sanitizer core | `<pending>` | 771 | Standalone `TextSanitizer` (+30 tests, then +5 reviewer-requested boundary/spec tests = 35 new). Reviewer approved with nits; all three nits landed before commit (class-edge boundary tests 0x08/0x09, 0x0D/0x0E, 0x7E/0x7F, 0xA0; literal `ESC[2J` spec test; `ClassAGlyph` comment tightened). |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped.

- **Design pre-agreed with user (2026-05-29) before apply.** All seven design.md decisions were ratified with the user during the propose step (hybrid safe-by-default + `Segment.Raw`; construction-time chokepoint; `DCLI_SANITIZE_MODE` env var default `strip`; two byte classes; get-only props to block the `with`-bypass; allocation-free fast path; raw flag in value equality). These are NOT open questions — implement as written; only deviate if implementation reveals one is wrong (then stop and ask).

## Human-in-the-loop verifications

Anything that can't be settled by automated gates. For each: section reference, exact copy-pasteable command, what the user should see, and current status.

- *(None anticipated.)* The whole surface is exercisable via `Dcli.Testing.HeadlessTerminal` / `FrameSnapshot` / `InMemoryOutputSink`; no real-terminal eyeballing is expected. If a §4 end-to-end test surfaces a real-terminal interaction worth confirming, log it here.

## Open follow-ups / known gaps (after this change lands — NOT in scope here)

Surface gaps for future changes. Link to memory files where the constraint is encoded.

- **Column-aware tab expansion at wrap time** — deferred (design Decision 4): tab is normalized to a single space at construction because the column is unknown there. A future change could add column-accurate expansion at wrap time if a real need appears.
- **Input-side VT parsing** — `VtInputParser` is a separate capability, untouched here.

## Memory files (indexed by `~/.claude/projects/-Users-emmz-github-emmz-dcli/memory/MEMORY.md`)

- `vt-escape-sanitization-gap` — the gap this change closes; now annotated "proposed, design agreed" with the seven decisions.
- `ca2007-render-loop-thread-discipline` — CA2007 suppressed repo-wide; render-path thread-correctness checked by hand (relevant only if §4 touches the loop).
- `section14-api-ergonomics-findings` — adjacent styled-text/API surface context.

## Resume point

> **Currently at §2.1 — Segment safe-by-default construction.** §1 (`TextSanitizer`) shipped and is green (771 tests). Next worker call: convert `Segment` from a positional to an explicit `record` whose primary ctor runs `Text` through `TextSanitizer.Apply`; props **get-only** (Decision 6, blocks `with`-bypass); add `internal bool IsRaw` participating in value equality; `public static Segment Raw(...)` bypass; update XML docs. Watch: `Deconstruct` may be needed if any call site uses positional deconstruction; `with { Text = ... }` must become a compile error.
