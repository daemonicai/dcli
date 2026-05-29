# DEVLOG — `vt-escape-sanitization`

> **Status: shipped.** Applied per the OpenSpec apply workflow (see the project's `CLAUDE.md`)
> across 5 sections, all gates green (827 tests). Final section commit `698d5a7`; frozen
> 2026-05-29 on branch `change/vt-escape-sanitization` (not yet merged — no PR number at freeze).
> This file is the narrative record the spec files don't carry: per-section status, decisions
> under uncertainty, deviations, surfaced bugs. Archived with the change to
> `openspec/changes/archive/2026-05-29-vt-escape-sanitization/DEVLOG.md`.

## How to resume (historical — change is shipped)

- Branch: **`change/vt-escape-sanitization`** (created from `main`).
- Final state at archive: CLEAN, all 5 sections committed, 18/18 tasks ticked, 827 tests green.
- Final commits: §1 `d4c7de8` · §2 `ee320cf` · §3 `1d91cd0` · §4 `094ca73` · §5 `698d5a7`.
- Sanity check command (if revisiting):
  `dotnet build && dotnet test && dotnet format --verify-no-changes && openspec validate vt-escape-sanitization --strict`

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Sanitizer core | `d4c7de8` | 771 | Standalone `TextSanitizer` (+30 tests, then +5 reviewer-requested boundary/spec tests = 35 new). Reviewer approved with nits; all three nits landed before commit (class-edge boundary tests 0x08/0x09, 0x0D/0x0E, 0x7E/0x7F, 0xA0; literal `ESC[2J` spec test; `ClassAGlyph` comment tightened). |
| 2 | Segment safe-by-default + Raw | `ee320cf` | 795 | `Segment` positional→explicit record; get-only `Text`/`Style`; sanitizing primary ctor; `internal IsRaw` in value equality; `Segment.Raw` verbatim seam. Surfaced a real `with`-on-Segment site in `ScrollableList` reverse-video path (get-only broke it) — fixed with an `IsRaw`-branching rebuild (raw→`Segment.Raw`, sanitized→`new Segment` hits idempotent fast path). Reviewer signed off, no nits. |
| 3 | Wire construction paths + `LineBuilder.Raw` | `1d91cd0` | 814 | Additive `LineBuilder.Raw` + audit. **First worker died mid-section (socket error) after writing `LineBuilder.Raw` + a non-compiling test file; no SendMessage-resume available in this harness, so a fresh worker finished from the partial tree.** Fixed test compile errors (no `(items, string title)` overload on Multi/Choice → use single-element `IReadOnlyList<string>` title path; CA1307). Reviewer **empirically verified the §2 single-chokepoint claim**: only two `Segment.Raw` consumers repo-wide (`LineBuilder.Raw`, `ScrollableList` reverse-video); `LiveBlock.AppendText` holds raw string in a StringBuilder transiently but only emits via `new Segment(_text.ToString())` at `Render()` — no bypass. One reviewer nit (scrollback test vacuous-pass) fixed. |
| 4 | End-to-end rendering safety tests | `094ca73` | 827 | Tests only (13 new). Emit-byte-level proof via `VtFrameRenderer`+`StringWriter` (deviation from task's model-level harness — see Decisions). Headline: consumer `ESC[?2026l` → one fence-close in output, `Segment.Raw` → two (byte-sensitive). Worker caught the greedy-`\x`-hex-escape C# hazard (`"\x1bB"`→U+01BB). Reviewer sign-off; two must-fix nits landed (replace-mode width tautologies → `.Length == Measure`; ESC constants → `` form). |
| 5 | Docs, sample audit, release notes | `698d5a7` | 827 | Docs only. Sample audit: ZERO raw-VT passthrough in `samples/` (nothing to convert). New `CHANGELOG.md` (BREAKING behavioural + `Segment.Raw`/`LineBuilder.Raw` + `DCLI_SANITIZE_MODE`). `docs/styled-text.md` rewritten ("Sanitize by default" replaces a stale "consumers must pre-sanitize" note). Reviewer caught a blocker the first pass missed — `docs/api-reference.md:207` still claimed "emitted verbatim"; fixed + added `Segment.Raw`/`LineBuilder.Raw` entries. Full docs/ stale-sweep clean. |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped.

- **Design pre-agreed with user (2026-05-29) before apply.** All seven design.md decisions were ratified with the user during the propose step (hybrid safe-by-default + `Segment.Raw`; construction-time chokepoint; `DCLI_SANITIZE_MODE` env var default `strip`; two byte classes; get-only props to block the `with`-bypass; allocation-free fast path; raw flag in value equality). These are NOT open questions — implement as written; only deviate if implementation reveals one is wrong (then stop and ask).

- **§2 — get-only props broke a real `with`-on-`Segment` site (`ScrollableList` reverse-video).** Decision 6's get-only `Style` turned `seg with { Style = reversedStyle }` (the selection-highlight rebuild in `src/Dcli/Internal/FixedRegion/ScrollableList.cs`) into a compile error — exactly the bypass we wanted to close, surfaced as a real call site rather than a hypothetical. Fixed in scope by branching on `IsRaw`: raw segments rebuild via `Segment.Raw(text, reversedStyle)` (stay verbatim), sanitized via `new Segment(text, reversedStyle)` (re-sanitization is a no-op fast-path by Decision 7). This was the latent trap the reviewer was asked to scrutinize — a naive `new Segment(seg.Text, …)` would have **stripped a raw segment's escapes during selection**. Reviewer confirmed correct.

- **§2 — deferred (reviewer architectural note, NOT this change):** the `IsRaw ? Segment.Raw(...) : new Segment(...)` restyle idiom currently lives at one site. When a *second* restyle site appears, extract an internal `Segment WithStyle(Style)` that preserves `IsRaw` in one place, so the pattern can't be copy-pasted wrongly. Single site today → not worth it yet.

- **§4 — deliberate deviation from the literal task-4.1 harness.** `tasks.md` §4.1 names `HeadlessTerminal`/`FrameSnapshot`/`InMemoryOutputSink`, but those are **model-level** seams: `FrameSnapshot` exposes `IReadOnlyList<Line>` (already-sanitized `Segment`s) and `InMemoryOutputSink` captures only the `RenderModel` (zero bytes; `EmitRestoreSequence` is a no-op). A sync-fence-defeat is *invisible* at that layer because the model holds post-sanitization segments. §4.1/§4.2 therefore test at the **emitted-byte** level via `VtFrameRenderer` + `StringWriter` (mirroring `VtFrameRendererTests`) — the only repo seam exposing the actual VT byte stream. This is a stronger proof, not a weaker one; the spec scenario "the synchronized-output fence cannot be defeated by consumer text" can only be verified against emitted bytes. Reviewer confirmed and asked this be recorded so the substitution isn't "fixed" back to the weaker seam. The headline test: consumer `ESC[?2026l` in content → exactly **one** fence-close in output (the renderer's); the same via `Segment.Raw` → **two** (proves the assertion is byte-sensitive).

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

> **SHIPPED (frozen 2026-05-29).** All 5 sections, 18/18 tasks, 827 tests green. Final section commit `698d5a7`; archived to `openspec/changes/archive/2026-05-29-vt-escape-sanitization/`. Not yet merged to `main` at freeze — no PR number. The VT-escape injection gap is closed: `Segment` sanitizes at construction (single chokepoint), `Segment.Raw`/`LineBuilder.Raw` is the only audited verbatim seam, `DCLI_SANITIZE_MODE` (default `strip`) configures the transform, and the sync-fence-cannot-be-defeated guarantee is proven at the emit-byte level. Follow-ups (the `Segment.WithStyle` helper, and giving `CHANGELOG.md`'s `[Unreleased]` entry a version heading at release) live outside this change.
