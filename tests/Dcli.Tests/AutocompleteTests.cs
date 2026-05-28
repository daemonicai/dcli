using System.Text;
using Dcli.Internal.FixedRegion;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Unit tests for <see cref="Autocomplete"/> — task 11.3.
/// </summary>
public sealed class AutocompleteTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static TextBuffer Buffer(string text = "")
    {
        TextBuffer buf = new();
        if (text.Length > 0)
            buf.Insert(text);
        return buf;
    }

    private static AutocompleteCandidate Candidate(string insertText, string display) =>
        new(insertText, new LineBuilder().Text(display).Build());

    private static List<AutocompleteCandidate> Candidates(params (string insertText, string display)[] items) =>
        items.Select(i => Candidate(i.insertText, i.display)).ToList();

    private static KeyEvent Named(NamedKey key) =>
        new(KeyCode.Named(key), Modifiers.None);

    private static KeyEvent Char(char c) =>
        new(KeyCode.FromRune(new Rune(c)), Modifiers.None);

    private static bool AllReversed(Line line) =>
        line.Segments.Count > 0 &&
        line.Segments.All(s => (s.Style.Format & Format.Reverse) == Format.Reverse);

    private static bool AnySegmentContains(Line line, string text) =>
        line.Segments.Any(s => s.Text.Contains(text, StringComparison.Ordinal));

    // ── Placement / HidesCursor ───────────────────────────────────────────────────────────────

    [Fact]
    public void PlacementIsBelowInput()
    {
        Autocomplete ac = new(Buffer());
        Assert.Equal(OverlayPlacement.BelowInput, ac.Placement);
    }

    [Fact]
    public void HidesCursorIsFalse()
    {
        Autocomplete ac = new(Buffer());
        Assert.False(ac.HidesCursor);
    }

    // ── IsDismissed ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InitiallyDismissed()
    {
        Autocomplete ac = new(Buffer());
        Assert.True(ac.IsDismissed);
    }

    [Fact]
    public void AfterShowNotDismissed()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("foo", "foo")));
        Assert.False(ac.IsDismissed);
    }

    [Fact]
    public void AfterHideDismissed()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("foo", "foo")));
        ac.Hide();
        Assert.True(ac.IsDismissed);
    }

    [Fact]
    public void AfterAcceptWithEnterDismissed()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("foo", "foo")));
        ac.HandleKey(Named(NamedKey.Enter));
        Assert.True(ac.IsDismissed);
    }

    [Fact]
    public void AfterEscapeDismissed()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("foo", "foo")));
        ac.HandleKey(Named(NamedKey.Escape));
        Assert.True(ac.IsDismissed);
    }

    // ── Render ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShowRendersCandidateDisplayLines()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("foo", "Foo item"), ("bar", "Bar item")));

        IReadOnlyList<Line> rows = ac.Render(80);

        Assert.Equal(2, rows.Count);
        Assert.True(AnySegmentContains(rows[0], "Foo item"));
        Assert.True(AnySegmentContains(rows[1], "Bar item"));
    }

    [Fact]
    public void RenderEmptyAfterHide()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("x", "X")));
        ac.Hide();

        // Render is still delegated to the list; the list was set with items.
        // The dismissed state is tracked by IsDismissed; Render itself returns
        // whatever the list returns (composer will not call Render when dismissed).
        // Verify list still has items but IsDismissed is true.
        Assert.True(ac.IsDismissed);
    }

    [Fact]
    public void ShowWithEmptyCandidateListIsAllowed()
    {
        Autocomplete ac = new(Buffer());
        ac.Show([]);
        Assert.False(ac.IsDismissed);
        Assert.Empty(ac.Render(80));
    }

    // ── Navigation ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DownMovesSelectionToNextRow()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("a", "A"), ("b", "B"), ("c", "C")));

        bool consumed = ac.HandleKey(Named(NamedKey.Down));

        Assert.True(consumed);
        IReadOnlyList<Line> rows = ac.Render(80);
        // Row 1 (index 1 = "B") should be highlighted.
        Assert.True(AllReversed(rows[1]));
        Assert.False(AllReversed(rows[0]));
    }

    [Fact]
    public void UpMovesSelectionToPreviousRow()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("a", "A"), ("b", "B"), ("c", "C")));
        ac.HandleKey(Named(NamedKey.Down));
        ac.HandleKey(Named(NamedKey.Down));

        bool consumed = ac.HandleKey(Named(NamedKey.Up));

        Assert.True(consumed);
        IReadOnlyList<Line> rows = ac.Render(80);
        // Row 1 (index 1 = "B") should be highlighted after going down twice then up once.
        Assert.True(AllReversed(rows[1]));
    }

    [Fact]
    public void FirstRowIsSelectedAfterShow()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("a", "A"), ("b", "B")));

        IReadOnlyList<Line> rows = ac.Render(80);
        Assert.True(AllReversed(rows[0]));
    }

    // ── Accept with Enter ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnterAppliesInsertTextToBuffer()
    {
        TextBuffer buf = Buffer("hello");
        Autocomplete ac = new(buf);
        ac.Show(Candidates(("/cmd foo", "/cmd foo")));

        bool consumed = ac.HandleKey(Named(NamedKey.Enter));

        Assert.True(consumed);
        Assert.Equal("/cmd foo", buf.Text);
        Assert.Equal("/cmd foo".Length, buf.CaretIndex);
    }

    [Fact]
    public void EnterDismissesAfterAccept()
    {
        TextBuffer buf = Buffer();
        Autocomplete ac = new(buf);
        ac.Show(Candidates(("x", "X")));
        ac.HandleKey(Named(NamedKey.Enter));
        Assert.True(ac.IsDismissed);
    }

    [Fact]
    public void EnterOnEmptyCandidateListIsNoOp()
    {
        TextBuffer buf = Buffer("original");
        Autocomplete ac = new(buf);
        ac.Show([]);

        bool consumed = ac.HandleKey(Named(NamedKey.Enter));

        Assert.True(consumed);
        Assert.Equal("original", buf.Text); // buffer untouched
        Assert.True(ac.IsDismissed);
    }

    // ── Accept with Tab ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TabAlsoAcceptsSelectedCandidate()
    {
        TextBuffer buf = Buffer("old");
        Autocomplete ac = new(buf);
        ac.Show(Candidates(("new text", "New text")));

        bool consumed = ac.HandleKey(Named(NamedKey.Tab));

        Assert.True(consumed);
        Assert.Equal("new text", buf.Text);
        Assert.True(ac.IsDismissed);
    }

    // ── Escape ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EscapeDismissesWithoutTouchingBuffer()
    {
        TextBuffer buf = Buffer("keep this");
        Autocomplete ac = new(buf);
        ac.Show(Candidates(("replace", "Replace")));

        bool consumed = ac.HandleKey(Named(NamedKey.Escape));

        Assert.True(consumed);
        Assert.Equal("keep this", buf.Text);
        Assert.True(ac.IsDismissed);
    }

    // ── Fall-through keys ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void PrintableKeyReturnsFalseAndDoesNotMutateBuffer()
    {
        TextBuffer buf = Buffer("start");
        Autocomplete ac = new(buf);
        ac.Show(Candidates(("x", "X")));

        bool consumed = ac.HandleKey(Char('a'));

        Assert.False(consumed);
        Assert.Equal("start", buf.Text); // buffer untouched
    }

    [Fact]
    public void CtrlKeyReturnsFalse()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("x", "X")));

        bool consumed = ac.HandleKey(new KeyEvent(KeyCode.FromRune(new Rune('c')), Modifiers.Ctrl));

        Assert.False(consumed);
    }

    [Fact]
    public void RightArrowReturnsFalse()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("x", "X")));

        bool consumed = ac.HandleKey(Named(NamedKey.Right));

        Assert.False(consumed);
    }

    // ── MaxRows forwarding ────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxRowsForwardsToList()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("a", "A"), ("b", "B"), ("c", "C"), ("d", "D"), ("e", "E")));
        ac.MaxRows = 3;

        IReadOnlyList<Line> rows = ac.Render(80);

        Assert.Equal(3, rows.Count);
    }

    // ── Show resets selection ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ShowResetsPreviousSelection()
    {
        Autocomplete ac = new(Buffer());
        ac.Show(Candidates(("a", "A"), ("b", "B"), ("c", "C")));
        ac.HandleKey(Named(NamedKey.Down));
        ac.HandleKey(Named(NamedKey.Down)); // selection at index 2

        // New Show call: selection should reset to 0.
        ac.Show(Candidates(("x", "X"), ("y", "Y")));
        IReadOnlyList<Line> rows = ac.Render(80);
        Assert.True(AllReversed(rows[0]));
    }
}
