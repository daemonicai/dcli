using System.Threading.Channels;
using Xunit;

namespace Dcli.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Hand-written fakes — this entire file is intentionally self-contained so
// that it reads as documentation for how a dmon controller would test against
// dcli without a real terminal, raw-mode session, or static state to reset.
// ─────────────────────────────────────────────────────────────────────────────

#region Fake sub-surfaces

/// <summary>Records every Append/BeginLive/BeginCollapsible call for assertion.</summary>
internal sealed class FakeScrollback : IScrollback
{
    internal List<Line> Appended { get; } = [];
    internal int BeginLiveCount { get; private set; }
    internal List<(Line Summary, IReadOnlyList<Line> Hidden)> Collapsibles { get; } = [];

    public void Append(Line line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Appended.Add(line);
    }

    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Append(Line.FromText(text));
    }

    public ILiveBlock BeginLive()
    {
        BeginLiveCount++;
        return new NoOpLiveBlock();
    }

    public ICollapsible BeginCollapsible(Line summary, IReadOnlyList<Line> hiddenLines)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(hiddenLines);
        Collapsibles.Add((summary, hiddenLines));
        return new NoOpCollapsible();
    }

    private sealed class NoOpLiveBlock : ILiveBlock
    {
        public void AppendText(string text) { }
        public void SetContent(IReadOnlyList<Line> lines) { }
        public void Commit() { }
    }

    private sealed class NoOpCollapsible : ICollapsible
    {
        public void Expand() { }
    }
}

/// <summary>Records SetText and Clear calls.</summary>
internal sealed class FakeInput : IInput
{
    internal List<string> SetTextCalls { get; } = [];
    internal int ClearCount { get; private set; }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SetTextCalls.Add(text);
    }

    public void Clear() => ClearCount++;
}

/// <summary>Records SetRows calls.</summary>
internal sealed class FakeStatus : IStatus
{
    internal List<IReadOnlyList<Line>> SetCalls { get; } = [];

    public void SetRows(params Line[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        SetCalls.Add(rows);
    }

    public void SetRows(IReadOnlyList<Line> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        SetCalls.Add(rows);
    }
}

/// <summary>Records Show/Hide calls.</summary>
internal sealed class FakeAutocomplete : IAutocomplete
{
    internal List<IReadOnlyList<AutocompleteCandidate>> ShowCalls { get; } = [];
    internal int HideCount { get; private set; }

    public void Show(IReadOnlyList<AutocompleteCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ShowCalls.Add(candidates);
    }

    public void Hide() => HideCount++;
}

#endregion

#region FakeTerminal

/// <summary>
/// A hand-written fake <see cref="ITerminal"/> that records command-side calls and returns
/// scripted dialog results, for use in consumer-side tests without a real terminal.
/// </summary>
internal sealed class FakeTerminal : ITerminal
{
    private readonly Channel<TerminalEvent> _events =
        Channel.CreateUnbounded<TerminalEvent>(new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });

    // ── Sub-surfaces ──────────────────────────────────────────────────────────

    internal FakeScrollback FakeScrollback { get; } = new();
    internal FakeInput FakeInput { get; } = new();
    internal FakeStatus FakeStatus { get; } = new();
    internal FakeAutocomplete FakeAutocomplete { get; } = new();

    // ── Dialog recordings ─────────────────────────────────────────────────────

    internal List<SelectRequest> SelectRequests { get; } = [];
    internal List<MultiSelectRequest> MultiSelectRequests { get; } = [];
    internal List<ChoiceRequest> ChoiceRequests { get; } = [];
    internal List<InputRequest> InputRequests { get; } = [];

    // ── Scripted dialog results ───────────────────────────────────────────────

    /// <summary>Scripted result returned by the next <see cref="SelectAsync"/> call.</summary>
    internal DialogResult<int> NextSelectResult { get; set; } =
        new(DialogOutcome.Cancelled, default);

    /// <summary>Scripted result returned by the next <see cref="MultiSelectAsync"/> call.</summary>
    internal DialogResult<int[]> NextMultiSelectResult { get; set; } =
        new(DialogOutcome.Cancelled, []);

    /// <summary>Scripted result returned by the next <see cref="ChoiceAsync"/> call.</summary>
    internal DialogResult<int> NextChoiceResult { get; set; } =
        new(DialogOutcome.Cancelled, default);

    /// <summary>Scripted result returned by the next <see cref="InputAsync"/> call.</summary>
    internal DialogResult<string> NextInputResult { get; set; } =
        new(DialogOutcome.Cancelled, string.Empty);

    // ── ITerminal implementation ──────────────────────────────────────────────

    public IScrollback Scrollback => FakeScrollback;
    public IInput Input => FakeInput;
    public IStatus Status => FakeStatus;
    public IAutocomplete Autocomplete => FakeAutocomplete;

    public ChannelReader<TerminalEvent> Events => _events.Reader;

    public (int Columns, int Rows) GetTerminalSize() => (80, 24);

    public Task<DialogResult<int>> SelectAsync(
        SelectRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        SelectRequests.Add(req);
        return Task.FromResult(NextSelectResult);
    }

    public Task<DialogResult<int[]>> MultiSelectAsync(
        MultiSelectRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        MultiSelectRequests.Add(req);
        return Task.FromResult(NextMultiSelectResult);
    }

    public Task<DialogResult<int>> ChoiceAsync(
        ChoiceRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        ChoiceRequests.Add(req);
        return Task.FromResult(NextChoiceResult);
    }

    public Task<DialogResult<string>> InputAsync(
        InputRequest req,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        InputRequests.Add(req);
        return Task.FromResult(NextInputResult);
    }

    /// <summary>
    /// Pushes a synthesized <see cref="TerminalEvent"/> into the <see cref="Events"/> channel
    /// so that consumer code draining the channel can observe and react to it.
    /// </summary>
    internal void PushEvent(TerminalEvent evt) => _events.Writer.TryWrite(evt);

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

#endregion

#region Consumer-style code under test

/// <summary>
/// Represents a consumer-style controller that depends only on <see cref="ITerminal"/>.
/// Demonstrates the pattern a dmon controller would use.
/// </summary>
internal sealed class SampleConsumer
{
    private readonly ITerminal _terminal;

    internal SampleConsumer(ITerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        _terminal = terminal;
    }

    /// <summary>
    /// Opens a select dialog; on Submitted appends the selected index as a status line
    /// and clears the input editor. On Cancelled appends a "cancelled" line.
    /// </summary>
    internal async Task RunSelectAndReactAsync(SelectRequest req)
    {
        DialogResult<int> result = await _terminal.SelectAsync(req);
        if (result.Outcome == DialogOutcome.Submitted)
        {
            Segment seg = new($"selected:{result.Value}", new Style());
            _terminal.Scrollback.Append(new Line([seg]));
            _terminal.Input.Clear();
        }
        else
        {
            Segment seg = new("cancelled", new Style());
            _terminal.Status.SetRows(new Line([seg]));
        }
    }

    /// <summary>
    /// Reads one event from the channel and reacts: on <see cref="KeyPressed"/> appends
    /// the key description to scrollback.
    /// </summary>
    internal async Task DrainOneEventAsync()
    {
        TerminalEvent evt = await _terminal.Events.ReadAsync();
        if (evt is KeyPressed kp)
        {
            Segment seg = new($"key:{kp.Key.Code}", new Style());
            _terminal.Scrollback.Append(new Line([seg]));
        }
    }
}

#endregion

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for §12 task 12.7: proves tier-A substitutability of the <see cref="ITerminal"/>
/// façade. All tests run with no real terminal, loop, or parser.
/// </summary>
public sealed class FakeTerminalTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Line PlainLine(string text) => new([new Segment(text, new Style())]);

    // ── (a) No static state — fake compiles and runs standalone ──────────────

    [Fact]
    public async Task FakeTerminalCanBeCreatedAndDisposedWithNoStaticState()
    {
        // Two independent fakes must not share any state.
        FakeTerminal fake1 = new();
        FakeTerminal fake2 = new();

        fake1.FakeScrollback.Append(PlainLine("hello"));

        await fake1.DisposeAsync();
        await fake2.DisposeAsync();

        Assert.Single(fake1.FakeScrollback.Appended);
        Assert.Empty(fake2.FakeScrollback.Appended);
    }

    // ── (b) Public event/result types are directly constructible ─────────────

    [Fact]
    public void KeyEventIsDirectlyConstructible()
    {
        // Tier-A requirement: a test can build a KeyEvent for an arrow key without the parser.
        KeyEvent keyEvt = new(KeyCode.Named(NamedKey.Down), Modifiers.None);
        Assert.Equal(NamedKey.Down, keyEvt.Code.NamedValue);
        Assert.Equal(Modifiers.None, keyEvt.Modifiers);
    }

    [Fact]
    public void DialogResultIsDirectlyConstructible()
    {
        // Tier-A requirement: scripted dialog result without driving the loop.
        DialogResult<int> result = new(DialogOutcome.Submitted, 2);
        Assert.Equal(DialogOutcome.Submitted, result.Outcome);
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void TerminalEventsAreDirectlyConstructible()
    {
        KeyEvent key = new(KeyCode.Named(NamedKey.Up), Modifiers.None);
        KeyPressed kp = new(key);
        InputSubmitted sub = new("hello");
        InputChanged chg = new("he");
        Resized rsz = new(120, 30);

        Assert.Equal("hello", sub.Text);
        Assert.Equal("he", chg.Text);
        Assert.Equal(120, rsz.Columns);
        Assert.Equal(30, rsz.Rows);
        Assert.Equal(key, kp.Key);
    }

    // ── (c) Command-side calls are recorded ───────────────────────────────────

    [Fact]
    public async Task SubmittedSelectRecordsRequestAndAppendedLine()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            Line item = PlainLine("option A");
            SelectRequest req = new([item]);
            fake.NextSelectResult = new DialogResult<int>(DialogOutcome.Submitted, 0);

            SampleConsumer consumer = new(fake);
            await consumer.RunSelectAndReactAsync(req);

            // The request was recorded.
            Assert.Single(fake.SelectRequests);
            Assert.Same(req, fake.SelectRequests[0]);

            // On Submitted: Scrollback.Append was called, Input.Clear was called.
            Assert.Single(fake.FakeScrollback.Appended);
            Assert.Contains("selected:0", fake.FakeScrollback.Appended[0].Segments[0].Text, StringComparison.Ordinal);
            Assert.Equal(1, fake.FakeInput.ClearCount);

            // Status was NOT called on Submitted path.
            Assert.Empty(fake.FakeStatus.SetCalls);
        }
    }

    [Fact]
    public async Task CancelledSelectSetsStatusAndDoesNotAppend()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            SelectRequest req = new([PlainLine("x")]);
            fake.NextSelectResult = new DialogResult<int>(DialogOutcome.Cancelled, default);

            SampleConsumer consumer = new(fake);
            await consumer.RunSelectAndReactAsync(req);

            // On Cancelled: Status.Set was called.
            Assert.Single(fake.FakeStatus.SetCalls);
            Assert.Contains("cancelled", fake.FakeStatus.SetCalls[0][0].Segments[0].Text, StringComparison.Ordinal);

            // Scrollback.Append was NOT called on Cancelled path.
            Assert.Empty(fake.FakeScrollback.Appended);
            Assert.Equal(0, fake.FakeInput.ClearCount);
        }
    }

    // ── (d) Synthesized event drives consumer reaction ────────────────────────

    [Fact]
    public async Task SynthesizedArrowKeyEventDrivesConsumerReaction()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            // Consumer-style code: depends only on ITerminal.
            SampleConsumer consumer = new(fake);

            // Synthesize a Down-arrow KeyPressed event (no real parser needed).
            KeyEvent keyEvt = new(KeyCode.Named(NamedKey.Down), Modifiers.None);
            fake.PushEvent(new KeyPressed(keyEvt));

            // Consumer drains one event and reacts.
            await consumer.DrainOneEventAsync();

            // The consumer appended a "key:…" line to scrollback.
            Assert.Single(fake.FakeScrollback.Appended);
            Assert.Contains("key:", fake.FakeScrollback.Appended[0].Segments[0].Text, StringComparison.Ordinal);
        }
    }

    // ── (e) Other surface calls are recorded ─────────────────────────────────

    [Fact]
    public async Task ScrollbackBeginLiveAndBeginCollapsibleAreRecorded()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            ILiveBlock block = fake.Scrollback.BeginLive();
            Assert.NotNull(block);
            Assert.Equal(1, fake.FakeScrollback.BeginLiveCount);

            Line summary = PlainLine("summary");
            Line hidden = PlainLine("detail");
            ICollapsible coll = fake.Scrollback.BeginCollapsible(summary, [hidden]);
            Assert.NotNull(coll);
            Assert.Single(fake.FakeScrollback.Collapsibles);
            Assert.Same(summary, fake.FakeScrollback.Collapsibles[0].Summary);
        }
    }

    [Fact]
    public async Task InputSetTextIsRecorded()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            fake.Input.SetText("hello");
            fake.Input.SetText("world");

            Assert.Equal(["hello", "world"], fake.FakeInput.SetTextCalls);
        }
    }

    [Fact]
    public async Task AutocompleteShowAndHideAreRecorded()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            AutocompleteCandidate candidate = new("val", PlainLine("display"));
            fake.Autocomplete.Show([candidate]);
            fake.Autocomplete.Hide();

            Assert.Single(fake.FakeAutocomplete.ShowCalls);
            Assert.Equal(1, fake.FakeAutocomplete.HideCount);
        }
    }

    [Fact]
    public async Task MultiSelectChoiceAndInputDialogsRecordRequests()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            // MultiSelect
            MultiSelectRequest msReq = new([PlainLine("a"), PlainLine("b")]);
            fake.NextMultiSelectResult = new DialogResult<int[]>(DialogOutcome.Submitted, [0, 1]);
            DialogResult<int[]> msResult = await fake.MultiSelectAsync(msReq);
            Assert.Equal(DialogOutcome.Submitted, msResult.Outcome);
            Assert.Equal([0, 1], msResult.Value);
            Assert.Single(fake.MultiSelectRequests);

            // Choice
            ChoiceRequest chReq = new([PlainLine("yes"), PlainLine("no")]);
            fake.NextChoiceResult = new DialogResult<int>(DialogOutcome.Submitted, 1);
            DialogResult<int> chResult = await fake.ChoiceAsync(chReq);
            Assert.Equal(1, chResult.Value);
            Assert.Single(fake.ChoiceRequests);

            // Input
            InputRequest inReq = new(Prompt: PlainLine("Name?"));
            fake.NextInputResult = new DialogResult<string>(DialogOutcome.Submitted, "Alice");
            DialogResult<string> inResult = await fake.InputAsync(inReq);
            Assert.Equal("Alice", inResult.Value);
            Assert.Single(fake.InputRequests);
        }
    }

    [Fact]
    public async Task GetTerminalSizeReturnsConfiguredValue()
    {
        FakeTerminal fake = new();
        await using (fake)
        {
            (int cols, int rows) = fake.GetTerminalSize();
            Assert.Equal(80, cols);
            Assert.Equal(24, rows);
        }
    }

    // ── §2 — string-overload symmetry ────────────────────────────────────────

    [Fact]
    public void FakeScrollbackAppendStringProducesSameRecordingAsAppendLine()
    {
        FakeScrollback fake = new();

        // Two independent fakes, same text — one called with Line, one with string.
        FakeScrollback fakeViaLine = new();
        FakeScrollback fakeViaString = new();

        fakeViaLine.Append(Line.FromText("hello"));
        fakeViaString.Append("hello");

        // Both record exactly one line with the same segment text.
        Assert.Single(fakeViaLine.Appended);
        Assert.Single(fakeViaString.Appended);
        Assert.Equal(
            fakeViaLine.Appended[0].Segments.Select(s => s.Text),
            fakeViaString.Appended[0].Segments.Select(s => s.Text));
    }
}
