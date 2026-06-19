## 1. Public surface

- [x] 1.1 Add `SetPrompt(Line)` and `SetPrompt(string)` to `IInput` (string via `Line.FromText`); doc-comment that empty/`null` clears and that it does not emit `InputChanged`
- [x] 1.2 Implement both overloads on `InputSurface`: each posts `_loop.Post(new SetPromptCommand(line))`
- [x] 1.3 Remove the `Prompt` bullet from the `InputSurface` "Documented gaps" remarks (keep the `ReadOnly` bullet)

## 2. Command + editor model

- [x] 2.1 Add `SetPromptCommand : ILoopCommand` whose `Apply(model)` calls `model.FixedRegion.Editor.SetPrompt(line)` and `model.MarkDirty()` (mirror `SetTextCommand`)
- [x] 2.2 Add a prompt field to the owned editor (`TextBuffer`/editor) with a `SetPrompt` mutator; empty/`null` means no prefix
- [x] 2.3 Render the prefix on the editor's first visual row: editable text starts at `promptWidth`, caret column offset by `promptWidth` on row 0, first-row capacity = `width - promptWidth`; continuation rows begin at column 0 (no repeat). Ensure the prefix is never inserted into buffer contents

## 3. Tests (tests/Dcli.Tests, via HeadlessTerminal)

- [x] 3.1 Prefix renders before the editable text on the first row
- [x] 3.2 Caret parks immediately after the prefix when the buffer is empty
- [x] 3.3 No prompt set ⇒ editor renders identically to v1 (regression guard)
- [x] 3.4 Prompt persists across multiple submissions and across `Clear()`
- [x] 3.5 Submitted value and `InputChanged` payload exclude the prefix; history stores only user text
- [x] 3.6 First-row wrapping uses `width - promptWidth`; continuation row starts at column 0

## 4. Sample / demo

- [x] 4.1 Update a sample (or the demo) to set an input prompt (e.g. `SetPrompt("❯ ")`) so the surface is exercised end-to-end

## 5. Validation & packaging

- [ ] 5.1 `dotnet build` clean (analyzers warnings-as-errors; nullable enabled)
- [ ] 5.2 `dotnet test` all green
- [ ] 5.3 `dotnet format --verify-no-changes` clean
- [ ] 5.4 `openspec validate input-prompt-prefix --strict` passes
- [ ] 5.5 Version bump: `src/Dcli/Dcli.csproj` and `src/Dcli.Testing/Dcli.Testing.csproj` to the next preview revision after `persistent-input-preamble` (`0.2.0-rc.6`, or a shared bump if applied together)
- [ ] 5.6 Update `CHANGELOG.md` with the new `SetPrompt` surface
- [ ] 5.7 Keep a `DEVLOG.md` in the change directory while applying
- [ ] 5.8 dmon coordination: dmon's Terminal UX change consumes `ITerminal.Input.SetPrompt(...)` for the `❯` glyph alongside `ITerminal.InputPreamble`
