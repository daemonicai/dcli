# dcli

`dcli` is a C#/.NET (`net10.0`) inline terminal-rendering **library**, shipped as a NuGet
package. It does Claude-Code-style **inline** rendering: styled output flows into the terminal's real
scrollback, with a small interactive region (input + status + dropdowns) pinned at the bottom — not a
full-screen TUI. It owns rendering and widget mechanics; the consumer (dmon) owns data and semantics.

Spec-driven development is managed with **OpenSpec** (`openspec/`, schema `spec-driven`). All feature
work flows through a change in `openspec/changes/`.

### Commands

> The solution itself is created by section 1 of the first change. Before that, only `openspec` commands apply.

- Build: `dotnet build` — must be clean (analyzers run as **warnings-as-errors**; nullable enabled).
- Test: `dotnet test` — all green.
- Format: `dotnet format --verify-no-changes` — clean.
- Validate a change: `openspec validate <change-name> --strict`.
- List changes: `openspec list` (or the directories under `openspec/changes/`, excluding `archive/`).

---

# OpenSpec Apply Workflow

**This section is authoritative.** `/opsx:apply` is the entry point for implementing a change; if the
skill's behavior ever conflicts with what's written here, **follow this document**.

## Roles — the main thread never writes feature code

- **Orchestrator** = the main thread (you). You read specs, select work, brief agents, run the gates,
  tick boxes, and commit. **You do not implement feature code directly.**
- **`worker`** agent — implements the tasks.
- **`reviewer`** agent — audits the worker's diff.

Both agents are defined for this repo. Delegate; don't shortcut by writing the implementation yourself.

## 1. Select the change

1. List active changes = directories in `openspec/changes/` **excluding `archive/`**.
2. **Always ask the user which change to apply**, even when there is exactly one. If there are none,
   say so and stop.
3. Resume point = the **first unticked `- [ ]` task** in that change's `tasks.md`.

## 2. Pre-flight (orchestrator, before any section)

1. Read `proposal.md`, `design.md`, and the relevant `specs/<capability>/spec.md` for the section(s)
   you're about to work.
2. **Working tree must be clean** (`git status`). If it's dirty, stop and ask.
3. **Change must validate**: `openspec validate <change-name> --strict`. If it doesn't, stop and ask.
4. **Be on the change branch** `change/<change-name>`. Create it from `main` if missing:
   `git switch -c change/<change-name>`.

## 3. Implement — section by section

The unit of work is a **`## N.` section**. Walk sections in order from the resume point. For each:

1. **Brief the worker.** Hand it: the section's tasks (`N.1`…`N.k`), the relevant spec excerpts, the
   design decisions/constraints that bind them, and the done-gates below. The worker should not need to
   go hunting — give it what it needs to stay focused.
2. **Worker implements the whole section.** If a section is large or complex (e.g. the VT input parser),
   split it into sub-chunks across multiple `worker` calls — but it remains **one commit at section end**.
3. **Audit.** Spawn `reviewer` on the section diff (correctness, ADR compliance, OpenSpec scope, C#
   idiom, agentic-AI design quality).
4. **Review loop.** Feed the reviewer's findings back to the `worker`; worker fixes; `reviewer`
   re-audits. **Repeat until the reviewer signs off.**
5. **Gates — all four must pass before ticking any box:**
   - `dotnet build` clean (no errors; analyzers/warnings-as-errors clean)
   - `dotnet test` green — new tests for the section **and** all existing tests
   - `openspec validate <change-name> --strict`
   - `dotnet format --verify-no-changes` clean
   If a gate fails, it's back to step 4, not a commit.
6. **Tick the boxes.** Mark every `- [x] N.M` in the section in `tasks.md`.
7. **Commit — one conventional commit per section:**
   ```
   feat(<change-name>): <section title> (section N)

   - N.1 <task summary>
   - N.2 <task summary>
   ...

   Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
   ```

## 4. Stop and ask — do not push on

Stop **immediately** and ask the user (do not improvise a fix) when:

- a spec/design is **ambiguous**, or two specs **contradict** each other;
- doing the task properly needs changes **outside this change's scope** (its proposal/specs);
- a task is **blocked by an unresolved Open Question** in `design.md` (e.g. the deferred paint mechanism);
- implementation or tests reveal the **spec itself is wrong** (not just the code);
- a task **requires human-in-the-loop verification** that can't be settled by automated gates — e.g.
  confirming real-terminal UI behavior (raw-mode entry/restore, the manual harness in 4.5, visual
  correctness of a rendered frame, signal handling). Implement and self-test as far as possible, then
  hand the user a precise, copy-pasteable way to verify (exact command, what to do, what they should
  see) and **wait for their confirmation before ticking that task**.

**On stopping mid-section:** leave the WIP **uncommitted**, do **not** tick the section, do **not**
revert. Report the **exact task (`N.M`)** that stopped you and why. The WIP stays in the working tree
for the user to inspect.

## 5. Done

When every task in the change is ticked and the final review is clean:

1. Report status: sections completed, commits made, test summary.
2. **Propose archiving** — offer to run `/opsx:archive` and **wait for the user's confirmation**.
   Do not archive automatically.
