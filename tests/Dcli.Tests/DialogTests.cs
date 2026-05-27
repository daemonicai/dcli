using System.Text;
using Dcli.Internal.FixedRegion;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Unit tests for <see cref="Dialog"/> — task 11.4.
/// </summary>
public sealed class DialogTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static KeyEvent Named(NamedKey key) =>
        new(KeyCode.Named(key), Modifiers.None);

    private static KeyEvent Char(char c) =>
        new(KeyCode.FromRune(new Rune(c)), Modifiers.None);

    private static void AddItems(Dialog dialog, params string[] texts)
    {
        dialog.List.SetItems(texts.Select(t => new LineBuilder().Text(t).Build()).ToList());
    }

    private static bool AllReversed(Line line) =>
        line.Segments.Count > 0 &&
        line.Segments.All(s => (s.Style.Format & Format.Reverse) == Format.Reverse);

    private static bool AnySegmentContains(Line line, string text) =>
        line.Segments.Any(s => s.Text.Contains(text, StringComparison.Ordinal));

    // ── Placement / HidesCursor ───────────────────────────────────────────────────────────────

    [Fact]
    public void PlacementIsAboveInput()
    {
        Dialog d = new();
        Assert.Equal(OverlayPlacement.AboveInput, d.Placement);
    }

    [Fact]
    public void HidesCursorTrueWhenModal()
    {
        Dialog d = new(modal: true);
        Assert.True(d.HidesCursor);
    }

    [Fact]
    public void HidesCursorFalseWhenNonModal()
    {
        Dialog d = new(modal: false);
        Assert.False(d.HidesCursor);
    }

    // ── IsDismissed ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InitiallyNotDismissed()
    {
        Dialog d = new();
        Assert.False(d.IsDismissed);
        Assert.Null(d.CloseRequest);
    }

    [Fact]
    public void EnterSetsDismissedWithSubmit()
    {
        Dialog d = new();
        d.HandleKey(Named(NamedKey.Enter));
        Assert.True(d.IsDismissed);
        Assert.Equal(OverlayCloseKind.Submit, d.CloseRequest);
    }

    [Fact]
    public void EscapeSetsDismissedWithCancel()
    {
        Dialog d = new();
        d.HandleKey(Named(NamedKey.Escape));
        Assert.True(d.IsDismissed);
        Assert.Equal(OverlayCloseKind.Cancel, d.CloseRequest);
    }

    // ── Navigation ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DownMovesSelectionDown()
    {
        Dialog d = new();
        AddItems(d, "alpha", "beta", "gamma");

        bool consumed = d.HandleKey(Named(NamedKey.Down));

        Assert.True(consumed);
        IReadOnlyList<Line> rows = d.Render(80);
        Assert.True(AllReversed(rows[1]));
        Assert.False(AllReversed(rows[0]));
    }

    [Fact]
    public void UpMovesSelectionUp()
    {
        Dialog d = new();
        AddItems(d, "alpha", "beta", "gamma");
        d.HandleKey(Named(NamedKey.Down));
        d.HandleKey(Named(NamedKey.Down));

        bool consumed = d.HandleKey(Named(NamedKey.Up));

        Assert.True(consumed);
        IReadOnlyList<Line> rows = d.Render(80);
        Assert.True(AllReversed(rows[1]));
    }

    // ── Multi-select: space toggle ────────────────────────────────────────────────────────────

    [Fact]
    public void SpaceTogglesCurrentItemInMultiSelectDialog()
    {
        Dialog d = new(multiSelect: true);
        AddItems(d, "one", "two", "three");

        bool consumed = d.HandleKey(Char(' '));

        Assert.True(consumed);
        Assert.Contains(0, d.List.CheckedIndices);
    }

    [Fact]
    public void SpaceTogglesOffAfterToggleOn()
    {
        Dialog d = new(multiSelect: true);
        AddItems(d, "one", "two");

        d.HandleKey(Char(' ')); // check 0
        d.HandleKey(Char(' ')); // uncheck 0

        Assert.DoesNotContain(0, d.List.CheckedIndices);
    }

    [Fact]
    public void SpaceNavigationAndToggle()
    {
        Dialog d = new(multiSelect: true);
        AddItems(d, "alpha", "beta", "gamma");

        d.HandleKey(Named(NamedKey.Down)); // move to 1
        d.HandleKey(Char(' '));            // toggle 1
        d.HandleKey(Named(NamedKey.Down)); // move to 2
        d.HandleKey(Char(' '));            // toggle 2

        Assert.Contains(1, d.List.CheckedIndices);
        Assert.Contains(2, d.List.CheckedIndices);
        Assert.DoesNotContain(0, d.List.CheckedIndices);
    }

    [Fact]
    public void SpaceInNonMultiSelectModalDialogIsConsumedWithNoToggle()
    {
        // Non-multiselect, modal, no type-to-filter: space hits the modal catch-all.
        Dialog d = new(multiSelect: false, modal: true, typeToFilter: false);
        AddItems(d, "one", "two");

        bool consumed = d.HandleKey(Char(' '));

        Assert.True(consumed);
        Assert.Empty(d.List.CheckedIndices);
    }

    // ── Enter / Escape ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnterReturnsConsumedTrue()
    {
        Dialog d = new();
        bool consumed = d.HandleKey(Named(NamedKey.Enter));
        Assert.True(consumed);
    }

    [Fact]
    public void EscapeReturnsConsumedTrue()
    {
        Dialog d = new();
        bool consumed = d.HandleKey(Named(NamedKey.Escape));
        Assert.True(consumed);
    }

    // ── Modal catch-all ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void ModalDialogConsumesUnhandledPrintableKey()
    {
        // modal=true, typeToFilter=false → printable should hit catch-all and return true.
        Dialog d = new(modal: true, typeToFilter: false);
        bool consumed = d.HandleKey(Char('z'));
        Assert.True(consumed);
    }

    [Fact]
    public void ModalDialogConsumesArbitraryNamedKey()
    {
        Dialog d = new(modal: true);
        bool consumed = d.HandleKey(Named(NamedKey.F5));
        Assert.True(consumed);
    }

    [Fact]
    public void NonModalDialogFallsThroughForUnhandledKey()
    {
        Dialog d = new(modal: false);
        bool consumed = d.HandleKey(Char('z'));
        Assert.False(consumed);
    }

    // ── Type-to-filter ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TypeToFilterAppendsCharacter()
    {
        Dialog d = new(typeToFilter: true);

        d.HandleKey(Char('h'));
        d.HandleKey(Char('i'));

        Assert.Equal("hi", d.FilterText);
    }

    [Fact]
    public void TypeToFilterPrintableKeyConsumed()
    {
        Dialog d = new(typeToFilter: true);
        bool consumed = d.HandleKey(Char('x'));
        Assert.True(consumed);
    }

    [Fact]
    public void TypeToFilterBackspaceTrimLastChar()
    {
        Dialog d = new(typeToFilter: true);
        d.HandleKey(Char('a'));
        d.HandleKey(Char('b'));
        d.HandleKey(Char('c'));

        bool consumed = d.HandleKey(Named(NamedKey.Backspace));

        Assert.True(consumed);
        Assert.Equal("ab", d.FilterText);
    }

    [Fact]
    public void TypeToFilterBackspaceOnEmptyIsNoOp()
    {
        Dialog d = new(typeToFilter: true);
        // Should not throw on empty FilterText.
        bool consumed = d.HandleKey(Named(NamedKey.Backspace));
        Assert.True(consumed);
        Assert.Equal(string.Empty, d.FilterText);
    }

    [Fact]
    public void TypeToFilterBackspaceHandlesMultiCharClusters()
    {
        // A surrogate pair (e.g. 😀 U+1F600) should be removed as one cluster.
        Dialog d = new(typeToFilter: true);
        // Append 😀 by constructing a KeyEvent with its Rune.
        Rune emoji = new(0x1F600);
        d.HandleKey(new KeyEvent(KeyCode.FromRune(emoji), Modifiers.None));
        d.HandleKey(Char('!'));

        Assert.Equal("\U0001F600!", d.FilterText);

        d.HandleKey(Named(NamedKey.Backspace)); // remove '!'
        Assert.Equal("\U0001F600", d.FilterText);

        d.HandleKey(Named(NamedKey.Backspace)); // remove emoji
        Assert.Equal(string.Empty, d.FilterText);
    }

    [Fact]
    public void NonTypeToFilterModalDialogDoesNotChangeFilterText()
    {
        Dialog d = new(modal: true, typeToFilter: false);
        d.HandleKey(Char('a'));
        Assert.Equal(string.Empty, d.FilterText);
    }

    [Fact]
    public void TypeToFilterSpaceTakesMultiSelectPrecedence()
    {
        // When both multiSelect and typeToFilter are true, space toggles (rule 4)
        // rather than appending to FilterText (rule 5).
        Dialog d = new(multiSelect: true, modal: true, typeToFilter: true);
        AddItems(d, "item");

        d.HandleKey(Char(' '));

        Assert.Contains(0, d.List.CheckedIndices); // space toggled, not appended to filter
        Assert.Equal(string.Empty, d.FilterText);
    }

    // ── Render ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderDelegatesToList()
    {
        Dialog d = new();
        AddItems(d, "apple", "banana");

        IReadOnlyList<Line> rows = d.Render(80);

        Assert.Equal(2, rows.Count);
        Assert.True(AnySegmentContains(rows[0], "apple"));
        Assert.True(AnySegmentContains(rows[1], "banana"));
    }

    // ── MaxRows forwarding ────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxRowsForwardsToList()
    {
        Dialog d = new();
        AddItems(d, "a", "b", "c", "d", "e");
        d.MaxRows = 3;

        IReadOnlyList<Line> rows = d.Render(80);

        Assert.Equal(3, rows.Count);
    }
}
