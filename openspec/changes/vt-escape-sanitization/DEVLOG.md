# DEVLOG — `vt-escape-sanitization`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see the project's `CLAUDE.md`). Captures per-section narrative the spec
> files don't carry: decisions under uncertainty, deviations, surfaced bugs, HITL
> verifications. On archive this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-vt-escape-sanitization/DEVLOG.md` and the status flips
> to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/vt-escape-sanitization`** (created from `main`). Stay on it.
- Working tree state: CLEAN (§1, §2 committed).
- Sanity check command:
  `dotnet build && dotnet test && dotnet format --verify-no-changes && openspec validate vt-escape-sanitization --strict`
- Resume point: **§3 — Wire construction paths + LineBuilder.Raw** (first unticked task: `3.1`). See the Section status table for what's done so far.
- Check the memory files listed at the bottom before briefing — several encode hard-won constraints.

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Sanitizer core | `d4c7de8` | 771 | Standalone `TextSanitizer` (+30 tests, then +5 reviewer-requested boundary/spec tests = 35 new). Reviewer approved with nits; all three nits landed before commit (class-edge boundary tests 0x08/0x09, 0x0D/0x0E, 0x7E/0x7F, 0xA0; literal `ESC[2J` spec test; `ClassAGlyph` comment tightened). |
| 2 | Segment safe-by-default + Raw | `<pending>` | 795 | `Segment` positional→explicit record; get-only `Text`/`Style`; sanitizing primary ctor; `internal IsRaw` in value equality; `Segment.Raw` verbatim seam. Surfaced a real `with`-on-Segment site in `ScrollableList` reverse-video path (get-only broke it) — fixed with an `IsRaw`-branching rebuild (raw→`Segment.Raw`, sanitized→`new Segment` hits idempotent fast path). Reviewer signed off, no nits. |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped.

- **Design pre-agreed with user (2026-05-29) before apply.** All seven design.md decisions were ratified with the user during the propose step (hybrid safe-by-default + `Segment.Raw`; construction-time chokepoint; `DCLI_SANITIZE_MODE` env var default `strip`; two byte classes; get-only props to block the `with`-bypass; allocation-free fast path; raw flag in value equality). These are NOT open questions — implement as written; only deviate if implementation reveals one is wrong (then stop and ask).

- **§2 — get-only props broke a real `with`-on-`Segment` site (`ScrollableList` reverse-video).** Decision 6's get-only `Style` turned `seg with { Style = reversedStyle }` (the selection-highlight rebuild in `src/Dcli/Internal/FixedRegion/ScrollableList.cs`) into a compile error — exactly the bypass we wanted to close, surfaced as a real call site rather than a hypothetical. Fixed in scope by branching on `IsRaw`: raw segments rebuild via `Segment.Raw(text, reversedStyle)` (stay verbatim), sanitized via `new Segment(text, reversedStyle)` (re-sanitization is a no-op fast-path by Decision 7). This was the latent trap the reviewer was asked to scrutinize — a naive `new Segment(seg.Text, …)` would have **stripped a raw segment's escapes during selection**. Reviewer confirmed correct.

- **§2 — deferred (reviewer architectural note, NOT this change):** the `IsRaw ? Segment.Raw(...) : new Segment(...)` restyle idiom currently lives at one site. When a *second* restyle site appears, extract an internal `Segment WithStyle(Style)` that preserves `IsRaw` in one place, so the pattern can't be copy-pasted wrongly. Single site today → not worth it yet.

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

> **Currently at §3.1 — Wire construction paths + `LineBuilder.Raw`.** §1+§2 shipped, green (795 tests). `Segment` now sanitizes by default with the `Raw` seam. Next worker call: add `LineBuilder.Raw(text, style)`; verify `Line.FromText`, the string-accepting `*Request` overloads (`DialogRequests.cs`), and the scrollback/status surfaces all funnel through the sanitizing `Segment` ctor (no bypass); add tests proving escape bytes are neutralized through each public surface. Mostly a verification/wiring section since §2's ctor is the chokepoint — but confirm no surface builds a `Segment` via a path that skips the primary ctor.
