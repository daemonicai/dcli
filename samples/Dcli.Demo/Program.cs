// dcli Demo — full-surface smoke driver.
// Run: dotnet run --project samples/Dcli.Demo
// The tour is self-driving. Each wizard dialog auto-cancels after a short timeout so
// the binary finishes without keyboard input.

using Dcli;

// ── Start ───────────────────────────────────────────────────────────────────

await using Terminal t = await Terminal.StartAsync(new TerminalOptions
{
    MaxFixedHeight = 12,
    MinFrameIntervalMs = 16,
});

// ── Phase 1: Banner (~2s) ────────────────────────────────────────────────────

t.Status.SetRows(new LineBuilder()
    .Bold("dcli demo")
    .Text(" -- full surface tour")
    .Build());

t.Scrollback.Append(new LineBuilder()
    .Bold("dcli ")
    .Italic("inline terminal rendering library")
    .Text(" -- smoke tour")
    .Build());

t.Scrollback.Append(Line.Fg("  Styled output flows into the real terminal scrollback.", Color.Named(Color.AnsiColor.Cyan)));

t.Scrollback.Append("  A small interactive region is pinned at the bottom.");

t.Scrollback.Append(Line.Dim("  Content above the commit horizon is frozen and terminal-owned."));

await Task.Delay(TimeSpan.FromMilliseconds(800));

// ── Phase 2: Streaming live block (~3s) ──────────────────────────────────────

t.Status.SetRows(Line.Dim("Phase 2/6 -- streaming live block"));
t.Scrollback.Append(Line.Bold("--- Streaming live block ---"));

ILiveBlock live = t.Scrollback.BeginLive();

string[] streamTokens =
[
    "The ", "model ", "is ", "thinking", "...\n",
    "Generating ", "a ", "structured ", "response", "...\n",
    "Done.",
];

foreach (string token in streamTokens)
{
    live.AppendText(token);
    await Task.Delay(TimeSpan.FromMilliseconds(180));
}

// Demonstrate SetContent: replace the accumulated buffer wholesale.
await Task.Delay(TimeSpan.FromMilliseconds(300));
live.SetContent(
[
    new LineBuilder().Bold("Response (final): ").Text("Hello from dcli!").Build(),
    Line.Dim("  (SetContent replaced the streamed buffer)"),
]);

await Task.Delay(TimeSpan.FromMilliseconds(400));
live.Commit();

t.Scrollback.Append(Line.Dim("  Live block committed."));
await Task.Delay(TimeSpan.FromMilliseconds(300));

// ── Phase 3: Collapsible "thinking" block (~2s) ───────────────────────────────

t.Status.SetRows(Line.Dim("Phase 3/6 -- collapsible block"));
t.Scrollback.Append(Line.Bold("--- Collapsible block ---"));

// Build 24 hidden lines so the expand is visually obvious.
List<Line> hiddenLines = [];
for (int i = 1; i <= 24; i++)
{
    hiddenLines.Add(Line.Dim($"  thinking line {i,2}: reasoning about token {i * 7}..."));
}

ICollapsible collapsed = t.Scrollback.BeginCollapsible(
    summary: Line.Dim("> thinking (24 lines hidden)"),
    hiddenLines: hiddenLines);

await Task.Delay(TimeSpan.FromMilliseconds(1000));

collapsed.Expand();

t.Scrollback.Append("  Subsequent content lands below the expanded block.");

await Task.Delay(TimeSpan.FromMilliseconds(500));

// ── Phase 4: Autocomplete overlay (~3s) ─────────────────────────────────────

t.Status.SetRows(new LineBuilder()
    .Text("Phase 4/6 -- autocomplete: type ")
    .Bold("/")
    .Text(" and you would see suggestions; Esc to dismiss")
    .Build());

t.Scrollback.Append(Line.Bold("--- Autocomplete overlay ---"));

AutocompleteCandidate[] candidates =
[
    new AutocompleteCandidate(
        "/help",
        new LineBuilder().Bold("/help").Dim("  -- show available commands").Build()),
    new AutocompleteCandidate(
        "/quit",
        new LineBuilder().Bold("/quit").Dim("  -- exit the session").Build()),
    new AutocompleteCandidate(
        "/clear",
        new LineBuilder().Bold("/clear").Dim("  -- clear scrollback").Build()),
];

t.Autocomplete.Show(candidates);
await Task.Delay(TimeSpan.FromMilliseconds(1500));
t.Autocomplete.Hide();

t.Scrollback.Append(Line.Dim("  Autocomplete dismissed."));
await Task.Delay(TimeSpan.FromMilliseconds(300));

// ── Phase 5: Wizard chain (Select → Input → MultiSelect → Choice) ────────────
// Dialogs require keyboard input to submit. The demo auto-cancels each via a
// CancellationTokenSource timeout so the tour is fully self-driving.

t.Status.SetRows(Line.Dim("Phase 5/6 -- wizard chain (auto-cancels after 1.5s each)"));
t.Scrollback.Append(Line.Bold("--- Wizard chain ---"));

// 5a: Select
{
    using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(1500));

    DialogResult<int> lang = await t.SelectAsync(
        new SelectRequest(
            Items:
            [
                Line.Fg("C#", Color.Named(Color.AnsiColor.Cyan)),
                Line.Fg("Go", Color.Named(Color.AnsiColor.Yellow)),
                Line.Fg("Rust", Color.Named(Color.AnsiColor.Red)),
            ],
            Title: Line.Bold("Pick your favourite language")),
        cts.Token);

    string langText = lang.Outcome == DialogOutcome.Submitted
        ? $"index {lang.Value}"
        : "Cancelled";
    t.Scrollback.Append(new LineBuilder()
        .Text("Select result: ")
        .Bold(langText)
        .Build());
}

await Task.Delay(TimeSpan.FromMilliseconds(200));

// 5b: Input
{
    using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(1500));

    DialogResult<string> name = await t.InputAsync(
        new InputRequest(
            Prompt: Line.Bold("What's your name?"),
            Default: "ada"),
        cts.Token);

    string nameText = name.Outcome == DialogOutcome.Submitted
        ? name.Value
        : "Cancelled";
    t.Scrollback.Append(new LineBuilder()
        .Text("Input result: ")
        .Bold(nameText)
        .Build());
}

await Task.Delay(TimeSpan.FromMilliseconds(200));

// 5c: MultiSelect
{
    using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(1500));

    DialogResult<int[]> features = await t.MultiSelectAsync(
        new MultiSelectRequest(
            Items:
            [
                Line.FromText("Inline scrollback rendering"),
                Line.FromText("Collapsible blocks"),
                Line.FromText("Autocomplete overlay"),
                Line.FromText("Dialog wizard chain"),
            ],
            Title: Line.Bold("Which features interest you?")),
        cts.Token);

    string featText = features.Outcome == DialogOutcome.Submitted
        ? $"[{string.Join(", ", features.Value)}]"
        : "Cancelled";
    t.Scrollback.Append(new LineBuilder()
        .Text("MultiSelect result: ")
        .Bold(featText)
        .Build());
}

await Task.Delay(TimeSpan.FromMilliseconds(200));

// 5d: Choice
{
    using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(1500));

    // Multi-line Prompt demo: bold question + dim hint on a second line.
    DialogResult<int> confirm = await t.ChoiceAsync(
        new ChoiceRequest(
            Options:
            [
                Line.Fg("Yes", Color.Named(Color.AnsiColor.Green)),
                Line.Fg("No", Color.Named(Color.AnsiColor.Red)),
            ],
            Prompt:
            [
                Line.Bold("Run the tour again?"),
                Line.Dim("Arrow keys to navigate; Enter to confirm."),
            ]),
        cts.Token);

    string choiceText = confirm.Outcome == DialogOutcome.Submitted
        ? (confirm.Value == 0 ? "Yes" : "No")
        : "Cancelled";
    t.Scrollback.Append(new LineBuilder()
        .Text("Choice result: ")
        .Bold(choiceText)
        .Build());
}

await Task.Delay(TimeSpan.FromMilliseconds(300));

// ── Phase 6: Finale (~3s) ────────────────────────────────────────────────────

t.Status.SetRows(Line.Fg("DONE - Tour complete - press Ctrl+C to exit, or wait 3s.", Color.Named(Color.AnsiColor.Green)));

t.Scrollback.Append(Line.Bold("--- Tour complete ---"));
t.Scrollback.Append(Line.Fg("All dcli public surfaces exercised successfully.", Color.Named(Color.AnsiColor.BrightGreen)));

await Task.Delay(TimeSpan.FromSeconds(3));

// await using disposes the terminal, restoring raw mode on exit.
