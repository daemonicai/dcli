using Dcli.Testing;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for the multi-line-dialog-prompts change: §3.5 backwards-compat round-trip tests
/// and §3.6 null/empty preamble tests.
/// </summary>
public sealed class DialogRequestsTests
{
    private static Line PlainLine(string text) => new([new Segment(text)]);

    // ── §3.5 — Backwards-compat: single-Line constructor produces one-element preamble ─

    // Spec scenario: "Single-Line preamble constructor still works"

    [Fact]
    public void SelectRequestSingleLineCtorProducesOneElementTitle()
    {
        Line title = PlainLine("Choose:");
        SelectRequest req = new([PlainLine("a"), PlainLine("b")], Title: title);

        Assert.NotNull(req.Title);
        Line only = Assert.Single(req.Title);
        Assert.Same(title, only);
    }

    [Fact]
    public void MultiSelectRequestSingleLineCtorProducesOneElementTitle()
    {
        Line title = PlainLine("Pick items:");
        MultiSelectRequest req = new([PlainLine("x"), PlainLine("y")], Title: title);

        Assert.NotNull(req.Title);
        Line only = Assert.Single(req.Title);
        Assert.Same(title, only);
    }

    [Fact]
    public void ChoiceRequestSingleLineCtorProducesOneElementPrompt()
    {
        Line prompt = PlainLine("Confirm?");
        ChoiceRequest req = new([PlainLine("yes"), PlainLine("no")], Prompt: prompt);

        Assert.NotNull(req.Prompt);
        Line only = Assert.Single(req.Prompt);
        Assert.Same(prompt, only);
    }

    [Fact]
    public void InputRequestSingleLineCtorProducesOneElementPrompt()
    {
        Line prompt = PlainLine("Name:");
        InputRequest req = new(Prompt: prompt);

        Assert.NotNull(req.Prompt);
        Line only = Assert.Single(req.Prompt);
        Assert.Same(prompt, only);
    }

    // ── §3.5 — Backwards-compat: single-string constructor produces one-element preamble ─

    // Spec scenario: "Single-string preamble constructor still works"

    [Fact]
    public void SelectRequestSingleStringCtorProducesOneElementTitle()
    {
        SelectRequest req = new([PlainLine("a"), PlainLine("b")], title: "Choose:");

        Assert.NotNull(req.Title);
        Line only = Assert.Single(req.Title);
        Assert.Equal("Choose:", only.Segments[0].Text);
    }

    [Fact]
    public void MultiSelectRequestSingleStringCtorProducesOneElementTitle()
    {
        MultiSelectRequest req = new([PlainLine("x"), PlainLine("y")], title: "Pick items:");

        Assert.NotNull(req.Title);
        Line only = Assert.Single(req.Title);
        Assert.Equal("Pick items:", only.Segments[0].Text);
    }

    [Fact]
    public void ChoiceRequestSingleStringCtorProducesOneElementPrompt()
    {
        // Spec literal call shape: new ChoiceRequest(options, prompt: "Permission:")
        ChoiceRequest req = new([PlainLine("yes"), PlainLine("no")], prompt: "Permission:");

        Assert.NotNull(req.Prompt);
        Line only = Assert.Single(req.Prompt);
        Assert.Equal("Permission:", only.Segments[0].Text);
    }

    [Fact]
    public void InputRequestSingleStringCtorProducesOneElementPrompt()
    {
        InputRequest req = new InputRequest("Name:");

        Assert.NotNull(req.Prompt);
        Line only = Assert.Single(req.Prompt);
        Assert.Equal("Name:", only.Segments[0].Text);
    }

    // ── §3.6 — Null preamble produces no preamble rows (property inspection) ───

    // Spec scenario: "Null or empty preamble paints no preamble row"

    [Fact]
    public void SelectRequestNullTitlePropertyIsNull()
    {
        SelectRequest req = new([PlainLine("a")]);
        Assert.Null(req.Title);
    }

    [Fact]
    public void MultiSelectRequestNullTitlePropertyIsNull()
    {
        MultiSelectRequest req = new([PlainLine("a")]);
        Assert.Null(req.Title);
    }

    [Fact]
    public void ChoiceRequestNullPromptPropertyIsNull()
    {
        ChoiceRequest req = new([PlainLine("yes")]);
        Assert.Null(req.Prompt);
    }

    [Fact]
    public void InputRequestNullPromptPropertyIsNull()
    {
        InputRequest req = new();
        Assert.Null(req.Prompt);
    }

    // ── §3.6 — Null string preamble convenience ctor produces null property ──

    [Fact]
    public void SelectRequestNullStringTitleProducesNullTitle()
    {
        SelectRequest req = new([PlainLine("a")], title: (string?)null);
        Assert.Null(req.Title);
    }

    [Fact]
    public void ChoiceRequestNullStringPromptProducesNullPrompt()
    {
        ChoiceRequest req = new([PlainLine("yes")], prompt: (string?)null);
        Assert.Null(req.Prompt);
    }

    // ── §3.6 — End-to-end: null preamble paints zero preamble rows ───────────

    [Fact]
    public async Task SelectRequestNullPreamblePaintsNoPreambleRow()
    {
        // Spec scenario: "Null or empty preamble paints no preamble row" — end-to-end snapshot.
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 10 });

        SelectRequest req = new([PlainLine("item-a"), PlainLine("item-b")]);
        Assert.Null(req.Title);

        Task<DialogResult<int>> dialogTask = harness.Terminal.SelectAsync(req);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        Assert.Equal(OverlayKind.Dialog, snap.Overlay.Kind);

        // Count rows that contain list item text.
        int itemRows = snap.FixedRegionRows.Count(r =>
            r.Segments.Any(s => s.Text.Contains("item-a", StringComparison.Ordinal) ||
                                s.Text.Contains("item-b", StringComparison.Ordinal)));
        Assert.True(itemRows >= 1, "At least one item row should appear");
        // Without a preamble the overlay rows consist entirely of list item rows.
        Assert.Equal(snap.Overlay.VisibleRowCount, itemRows);

        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await dialogTask;
    }
}
