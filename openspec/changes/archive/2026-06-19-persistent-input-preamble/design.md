## Context

The fixed region is a bottom-pinned stack composed by `FixedRegionComposer.Compose` (`src/Dcli/Internal/FixedRegion/FixedRegionComposer.cs`). Its bands, top-to-bottom, are: an optional above-input overlay, the input editor, an optional below-input overlay, and the always-sacred status rows. The consumer can drive the bottom band via `StatusSurface.SetRows` (`ITerminal.Status`), which posts a `SetStatusCommand : ILoopCommand` that sets `model.FixedRegion.Status.Rows` and marks the model dirty; the composer reads those rows each frame.

There is no consumer surface for content pinned **above** the input editor. Transient dialogs already render a multi-line preamble above their widget (`multi-line-dialog-prompts`), but that is overlay-scoped and disappears with the dialog. dmon needs a *persistent* preamble for the base editor.

## Goals / Non-Goals

**Goals:**
- A persistent, consumer-set band of styled rows pinned directly above the base input editor, surviving across renders and input submissions until changed.
- An API that mirrors `StatusSurface` exactly, so the surface is immediately familiar and the wiring reuses a proven path.
- Sensible height-budget behaviour: the preamble yields before the input editor does; status stays sacred.

**Non-Goals:**
- The input prompt-prefix glyph (`❯`) on the editor line — separately deferred.
- Any key handling by the preamble — it is presentational.
- Welcome banner / MOTD — scrollback content owned by the consumer.

## Decisions

### D1: Mirror the StatusSurface wiring exactly

The preamble reuses the status path, one layer up the stack:

| Status (exists) | Preamble (new) |
|---|---|
| `ITerminal.Status : IStatus` | `ITerminal.InputPreamble : IInputPreamble` |
| `StatusSurface.SetRows(…)` → `_loop.Post(new SetStatusCommand(rows))` | `InputPreambleSurface.SetRows(…)` → `_loop.Post(new SetInputPreambleCommand(rows))` |
| `SetStatusCommand.Apply` → `model.FixedRegion.Status.Rows = rows; model.MarkDirty()` | `SetInputPreambleCommand.Apply` → `model.FixedRegion.Preamble.Rows = rows; model.MarkDirty()` |
| `Terminal` ctor: `Status = new StatusSurface(loop)` | `Terminal` ctor: `InputPreamble = new InputPreambleSurface(loop)` |
| Composer reads `_status.Rows` | Composer reads `_preamble.Rows` |

Same `params Line[]` + `IReadOnlyList<Line>` overloads; empty clears. This keeps the public surface symmetric ("rows above" / "rows below") and the implementation a near-copy of a tested path.

### D2: Band placement — directly above the input editor

`FixedRegionComposer.Compose` assembles rows in order. The preamble rows are inserted immediately above the input editor band:

```
live window (scrollback)
[ above-input overlay ]   (when an overlay is placed AboveInput)
[ preamble rows ]         ← NEW, when set
[ input editor rows ]
[ below-input overlay ]   (Autocomplete)
[ status rows ]           (sacred, bottommost)
```

The preamble sits below an above-input overlay: while a modal Dialog occupies the above-input slot it owns the visual foreground; the persistent preamble is chrome for the *base* editor. (In practice dmon clears or ignores the preamble's relevance during a modal dialog; the composer simply renders it in its band when present.)

### D3: Budget — preamble yields before the input, status stays sacred

The fixed-region height budget (`MaxHeight = clamp(appSet ?? 50% rows, 8, rows)`) is unchanged. The sacrifice order under pressure becomes: overlays squeeze (as today) → **preamble truncates** → the input editor retains at least one usable row → status rows are never squeezed. This mirrors how dialog preambles already truncate before their widget, so the arithmetic is the established pattern applied to the persistent band.

### D4: Persistence and clearing

`SetRows` is set-and-hold: once applied, the preamble renders every frame until `SetRows` is called again (replace) or with an empty argument (clear). This matches `StatusSurface` and is the whole point — the consumer sets the frame once at startup, not per turn.

### D5: Presentational — not in the intercept chain

The preamble never registers in the key-routing intercept chain (`active overlay → input editor`). It is pure presentation; all keys continue to reach the input editor (or the active overlay). This keeps it orthogonal to the existing **Intercept-chain key routing** and **Single cursor placement** requirements (the hardware cursor still parks at the input caret).

### D6: Naming

`InputPreamble` reuses the "preamble" vocabulary already established for dialogs (`InputRequest.Prompt` is documented as the dialog preamble), and reads as the natural counterpart to `Status`. The surface interface is `IInputPreamble` and the impl `InputPreambleSurface`, paralleling `IStatus` / `StatusSurface`.

## Risks / Trade-offs

- **Above-input overlay vs preamble ordering** is a genuine design choice; D2 places the preamble below an above-input overlay. If a future consumer needs the preamble above a dialog too, that is an additive follow-up — the band is independent.
- **Budget contention on tiny terminals**: with a 1–2 line preamble, an editor that needs ≥1 row, and ≥1 status row, an 8-row floor is comfortable; the truncation rule (D3) guarantees the editor and status survive.

## Open Questions

- **Version target.** `0.2.0-rc.5` assumed (additive, preview channel). Confirm at packaging time against the release-tag flow (`.github/workflows/release.yml`).
