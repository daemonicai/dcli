# DEVLOG — `vt-escape-sanitization`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see the project's `CLAUDE.md`). Captures per-section narrative the spec
> files don't carry: decisions under uncertainty, deviations, surfaced bugs, HITL
> verifications. On archive this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-vt-escape-sanitization/DEVLOG.md` and the status flips
> to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/vt-escape-sanitization`** (created from `main`). Stay on it.
- Working tree state: CLEAN (§1, §2, §3 committed).
- Sanity check command:
  `dotnet build && dotnet test && dotnet format --verify-no-changes && openspec validate vt-escape-sanitization --strict`
- Resume point: **§4 — End-to-end rendering safety tests** (first unticked task: `4.1`). See the Section status table for what's done so far.
- Check the memory files listed at the bottom before briefing — several encode hard-won constraints.

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Sanitizer core | `d4c7de8` | 771 | Standalone `TextSanitizer` (+30 tests, then +5 reviewer-requested boundary/spec tests = 35 new). Reviewer approved with nits; all three nits landed before commit (class-edge boundary tests 0x08/0x09, 0x0D/0x0E, 0x7E/0x7F, 0xA0; literal `ESC[2J` spec test; `ClassAGlyph` comment tightened). |
| 2 | Segment safe-by-default + Raw | `ee320cf` | 795 | `Segment` positional→explicit record; get-only `Text`/`Style`; sanitizing primary ctor; `internal IsRaw` in value equality; `Segment.Raw` verbatim seam. Surfaced a real `with`-on-Segment site in `ScrollableList` reverse-video path (get-only broke it) — fixed with an `IsRaw`-branching rebuild (raw→`Segment.Raw`, sanitized→`new Segment` hits idempotent fast path). Reviewer signed off, no nits. |
| 3 | Wire construction paths + `LineBuilder.Raw` | `<pending>` | 814 | Additive `LineBuilder.Raw` + audit. **First worker died mid-section (socket error) after writing `LineBuilder.Raw` + a non-compiling test file; no SendMessage-resume available in this harness, so a fresh worker finished from the partial tree.** Fixed test compile errors (no `(items, string title)` overload on Multi/Choice → use single-element `IReadOnlyList<string>` title path; CA1307). Reviewer **empirically verified the §2 single-chokepoint claim**: only two `Segment.Raw` consumers repo-wide (`LineBuilder.Raw`, `ScrollableList` reverse-video); `LiveBlock.AppendText` holds raw string in a StringBuilder transiently but only emits via `new Segment(_text.ToString())` at `Render()` — no bypass. One reviewer nit (scrollback test vacuous-pass) fixed. |

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

> **Currently at §4.1 — End-to-end rendering safety tests.** §1–§3 shipped, green (814 tests). Construction is proven safe and the chokepoint is empirically single. Next worker call: §4 end-to-end via `HeadlessTerminal`/`FrameSnapshot`/`InMemoryOutputSink` — assert consumer ESC/CSI/OSC/newline never reaches the output sink except via `Segment.Raw`; the sync-fence-cannot-be-defeated test (`[?2026l` in content → only the renderer's own fence-close in output); `Segment.Raw` byte-for-byte passthrough; width/wrapping consistency across strip and replace modes (use `TextSanitizer.Apply(text, mode)` explicit overload, since `DefaultMode` is process-cached).
