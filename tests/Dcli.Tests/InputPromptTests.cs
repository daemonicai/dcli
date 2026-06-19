using System.Threading.Channels;
using Dcli.Testing;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Integration tests for the input prompt prefix feature (tasks 3.1–3.6).
/// These tests exercise the public IInput.SetPrompt surface end-to-end through HeadlessTerminal;
/// unit-level TextBuffer behaviour is covered in TextBufferTests.cs.
/// </summary>
public sealed class InputPromptTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int FindRow(IReadOnlyList<Line> rows, string text) =>
        rows.Select((r, i) => (r, i))
            .Where(x => x.r.Segments.Any(s => s.Text.Contains(text, StringComparison.Ordinal)))
            .Select(x => x.i)
            .FirstOrDefault(-1);

    // ── 3.1 — Prefix renders before the editable text on the first row ────────

    [Fact]
    public async Task PromptPrefixRendersBeforeEditableTextOnFirstRow()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.Input.SetPrompt("❯ ");
        harness.Terminal.Input.SetText("hello");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        IReadOnlyList<Line> fixed_ = snap.FixedRegionRows;

        // Find the editor row (the one containing "hello").
        int editorIdx = FindRow(fixed_, "hello");
        Assert.True(editorIdx >= 0, "Editor row containing 'hello' not found in FixedRegionRows");

        Line editorRow = fixed_[editorIdx];
        Assert.True(editorRow.Segments.Count >= 2,
            $"Expected ≥2 segments in editor row (prompt + text) but got {editorRow.Segments.Count}");

        // First segment is the prompt; second (or later) contains the user text.
        Assert.Equal("❯ ", editorRow.Segments[0].Text);
        Assert.Contains(editorRow.Segments, s => s.Text.Contains("hello", StringComparison.Ordinal));
    }

    // ── 3.2 — Caret parks immediately after the prompt when buffer is empty ───

    [Fact]
    public async Task CaretParksAfterPromptWhenBufferEmpty()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.Input.SetPrompt("❯ ");
        // No SetText — buffer is empty.
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        Assert.NotNull(snap.Caret);

        // "❯" is 1 display column, " " is 1 display column → prompt width = 2.
        // With an empty buffer the caret sits exactly at column 2 (0-based: cols 0 and 1 are the prompt).
        Assert.Equal(2, snap.Caret!.Value.Col);
    }

    // ── 3.3 — No prompt set → editor renders identically to baseline (regression guard) ──

    [Fact]
    public async Task NoPromptEditorRendersWithoutPrefix()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        // Deliberately do NOT call SetPrompt.
        harness.Terminal.Input.SetText("hello");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        IReadOnlyList<Line> fixed_ = snap.FixedRegionRows;

        int editorIdx = FindRow(fixed_, "hello");
        Assert.True(editorIdx >= 0, "Editor row with 'hello' not found");

        Line editorRow = fixed_[editorIdx];
        // Without a prompt the first segment must be the text itself, not a prompt prefix.
        Assert.Equal("hello", editorRow.Segments[0].Text);

        // Caret should be at column 5 (end of "hello"), not offset by a prompt.
        Assert.NotNull(snap.Caret);
        Assert.Equal(5, snap.Caret!.Value.Col);
    }

    // ── 3.4 — Prompt persists across Clear() calls ────────────────────────────

    [Fact]
    public async Task PromptPersistsAcrossClearCalls()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.Input.SetPrompt("❯ ");

        // First round: set text, then clear.
        harness.Terminal.Input.SetText("first");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // Verify prompt present before clear.
        Assert.True(
            FindRow(harness.Snapshot.FixedRegionRows, "❯ ") >= 0,
            "Prompt '❯ ' missing before first Clear()");

        harness.Terminal.Input.Clear();
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // After clear the prompt must still show (caret at col 2, buffer empty).
        FrameSnapshot snap1 = harness.Snapshot;
        Assert.NotNull(snap1.Caret);
        Assert.Equal(2, snap1.Caret!.Value.Col);

        // Second round: set text, then clear again.
        harness.Terminal.Input.SetText("second");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        harness.Terminal.Input.Clear();
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap2 = harness.Snapshot;
        Assert.NotNull(snap2.Caret);
        Assert.Equal(2, snap2.Caret!.Value.Col);
    }

    // ── 3.5 — InputSubmitted payload excludes the prompt prefix ──────────────

    [Fact]
    public async Task InputSubmittedExcludesPromptPrefix()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.Input.SetPrompt("❯ ");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // Type "hello" and press Enter to submit.
        harness.Type("hello");
        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // Drain the outbound events channel.
        ChannelReader<TerminalEvent> events = harness.Terminal.Events;
        InputSubmitted? submitted = null;
        while (events.TryRead(out TerminalEvent? ev))
        {
            if (ev is InputSubmitted s)
            {
                submitted = s;
                break;
            }
        }

        Assert.NotNull(submitted);
        // Must contain only the user's typed text, never the prompt prefix.
        Assert.Equal("hello", submitted!.Text);
        Assert.DoesNotContain("❯", submitted.Text, StringComparison.Ordinal);
    }

    // ── 3.6 — First-row wrapping uses (width - promptWidth); continuation rows start at col 0 ──

    [Fact]
    public async Task WrappingUsesPromptReducedWidthAndContinuationHasNoPrompt()
    {
        // Narrow terminal: 12 cols. Prompt "❯ " = 2 cols → first row capacity = 10 chars.
        // Text "abcdefghijklmno" = 15 chars → wraps after 10, leaving 5 on row 2.
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 12, InitialRows = 24 });

        harness.Terminal.Input.SetPrompt("❯ ");
        harness.Terminal.Input.SetText("abcdefghijklmno");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        IReadOnlyList<Line> fixed_ = snap.FixedRegionRows;

        // Find the first editor row (prompt + start of text).
        int firstEditorIdx = FindRow(fixed_, "abcdefghij");
        if (firstEditorIdx < 0)
        {
            // The 10 chars may be split at a slightly different boundary; find by prompt.
            firstEditorIdx = fixed_
                .Select((r, i) => (r, i))
                .Where(x => x.r.Segments.Any(s => s.Text == "❯ "))
                .Select(x => x.i)
                .FirstOrDefault(-1);
        }

        Assert.True(firstEditorIdx >= 0, "First editor row (with prompt '❯ ') not found in FixedRegionRows");

        Line firstRow = fixed_[firstEditorIdx];
        Assert.Equal("❯ ", firstRow.Segments[0].Text);

        // There must be at least one continuation row after the first editor row.
        Assert.True(fixed_.Count > firstEditorIdx + 1,
            $"Expected a continuation row after firstEditorIdx={firstEditorIdx} but FixedRegionRows.Count={fixed_.Count}");

        // The continuation row must NOT start with the prompt.
        Line continuationRow = fixed_[firstEditorIdx + 1];
        Assert.False(continuationRow.Segments.Count > 0 && continuationRow.Segments[0].Text == "❯ ",
            "Continuation row must not start with the prompt prefix");

        // Continuation row should contain the remaining text ("klmno").
        Assert.True(
            continuationRow.Segments.Any(s => s.Text.Contains("klmno", StringComparison.Ordinal)),
            $"Continuation row should contain 'klmno' but segments were: [{string.Join(", ", continuationRow.Segments.Select(s => s.Text))}]");
    }
}
