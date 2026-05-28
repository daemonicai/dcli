using System.Text;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for tasks 11.2 and 11.5: OverlayState invariant, intercept-chain key routing,
/// and FixedRegionComposer overlay slotting / budget / cursor placement.
/// </summary>
public sealed class OverlayRoutingTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RenderModel MakeModel(int cols = 80, int rows = 24)
    {
        ConstantSizeSource size = new(cols, rows);
        return new RenderModel(size);
    }

    private sealed class ConstantSizeSource : ITerminalSizeSource
    {
        private readonly int _cols;
        private readonly int _rows;
        internal ConstantSizeSource(int cols, int rows) { _cols = cols; _rows = rows; }
        public (int Columns, int Rows) GetSize() => (_cols, _rows);
    }

    private static Line PlainLine(string text) => new([new Segment(text)]);

    private static Autocomplete MakeAutocomplete(TextBuffer? buffer = null)
    {
        buffer ??= new TextBuffer();
        Autocomplete ac = new(buffer);
        ac.Show([new AutocompleteCandidate("item1", PlainLine("item1")), new AutocompleteCandidate("item2", PlainLine("item2"))]);
        return ac;
    }

    private static Dialog MakeDialog(bool modal = true) => new(modal: modal);

    // ── LoopEngine test infrastructure (mirrors FixedRegionTests) ─────────────

    private sealed class CapturingOutputSink : IOutputSink
    {
        internal RenderModel? LastModel { get; private set; }
        public void Paint(RenderModel model) { LastModel = model; }
        public void EmitRestoreSequence() { }
    }

    private sealed class VirtualClock : IClock
    {
        private readonly object _lock = new();
        private TimeSpan _now;
        private readonly List<(TimeSpan Deadline, TaskCompletionSource Tcs, CancellationTokenSource LinkedCts)> _waiters = [];

        internal VirtualClock(TimeSpan? initial = null) => _now = initial ?? TimeSpan.Zero;

        public TimeSpan Now { get { lock (_lock) { return _now; } } }

        internal void Advance(TimeSpan by)
        {
            List<(TaskCompletionSource Tcs, CancellationTokenSource LinkedCts)> toComplete = [];
            lock (_lock)
            {
                _now += by;
                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (_waiters[i].Deadline <= _now)
                    {
                        toComplete.Add((_waiters[i].Tcs, _waiters[i].LinkedCts));
                        _waiters.RemoveAt(i);
                    }
                }
            }
            foreach ((TaskCompletionSource tcs, CancellationTokenSource linkedCts) in toComplete)
            {
                tcs.TrySetResult();
                linkedCts.Dispose();
            }
        }

        public Task WaitUntilAsync(TimeSpan deadline, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (deadline <= _now)
                    return Task.CompletedTask;
            }
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            linkedCts.Token.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                tcs,
                useSynchronizationContext: false);
            lock (_lock)
            {
                if (deadline <= _now) { linkedCts.Dispose(); return Task.CompletedTask; }
                _waiters.Add((deadline, tcs, linkedCts));
            }
            return tcs.Task;
        }
    }

    /// <summary>Posts an ILoopCommand to inject an overlay into the model.</summary>
    private sealed class ShowAutocompleteCommand(Autocomplete autocomplete) : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            model.ShowAutocomplete(autocomplete);
            model.MarkDirty();
        }
    }

    private sealed class ShowDialogCommand(Dialog dialog) : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            model.ShowModal(dialog);
            model.MarkDirty();
        }
    }

    // ── 11.2 — RenderModel overlay invariant ─────────────────────────────────

    [Fact]
    public void ShowDialogWhileAutocompleteActiveReplacesIt()
    {
        RenderModel model = MakeModel();
        Autocomplete ac = MakeAutocomplete();
        Dialog dialog = MakeDialog();

        model.ShowAutocomplete(ac);
        Assert.IsType<Autocomplete>(model.ActiveOverlay);

        model.ShowModal(dialog);
        Assert.IsType<Dialog>(model.ActiveOverlay);
        Assert.Same(dialog, model.ActiveOverlay);
    }

    [Fact]
    public void ShowAutocompleteWhileDialogActiveIsNoOp()
    {
        RenderModel model = MakeModel();
        Dialog dialog = MakeDialog();
        Autocomplete ac = MakeAutocomplete();

        model.ShowModal(dialog);
        model.ShowAutocomplete(ac); // must be ignored

        Assert.IsType<Dialog>(model.ActiveOverlay);
        Assert.Same(dialog, model.ActiveOverlay);
    }

    [Fact]
    public void ClearOverlayRemovesActiveOverlay()
    {
        RenderModel model = MakeModel();
        model.ShowModal(MakeDialog());
        Assert.NotNull(model.ActiveOverlay);

        model.ClearOverlay();
        Assert.Null(model.ActiveOverlay);
    }

    [Fact]
    public void ShowAutocompleteWhenNoOverlayIsActive()
    {
        RenderModel model = MakeModel();
        Autocomplete ac = MakeAutocomplete();
        model.ShowAutocomplete(ac);
        Assert.Same(ac, model.ActiveOverlay);
    }

    // ── 11.2 — LoopEngine intercept-chain routing ─────────────────────────────

    [Fact]
    public async Task AutocompleteArrowConsumedNoKeyPressedEvent()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        Autocomplete ac = new(new TextBuffer());
        ac.Show([new AutocompleteCandidate("a", PlainLine("a")), new AutocompleteCandidate("b", PlainLine("b"))]);
        engine.Post(new ShowAutocompleteCommand(ac));

        // ↓ should be consumed by autocomplete (moves selection); no KeyPressed emitted.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        Assert.Equal(0, engine.OutboundEvents.Count);

        // Verify the selection moved (index should now be 1).
        // We can observe this by snapshotting; the second item renders as selected.
        // (We simply assert no event emitted — selection state is internal to ScrollableList.)
    }

    [Fact]
    public async Task AutocompleteUpConsumedNoKeyPressedEvent()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        TextBuffer buf = new();
        Autocomplete ac = new(buf);
        ac.Show([new AutocompleteCandidate("x", PlainLine("x")), new AutocompleteCandidate("y", PlainLine("y"))]);
        engine.Post(new ShowAutocompleteCommand(ac));

        // First move down to index 1, then up to index 0.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Up), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // Both keys consumed by autocomplete — no events on outbound channel.
        Assert.Equal(0, engine.OutboundEvents.Count);
    }

    [Fact]
    public async Task AutocompletePrintableFallsThroughToEditor()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        TextBuffer buf = new TextBuffer();
        Autocomplete ac = new(buf);
        ac.Show([new AutocompleteCandidate("item", PlainLine("item"))]);
        engine.Post(new ShowAutocompleteCommand(ac));

        // A printable character — autocomplete doesn't consume printables, so it falls through
        // to the editor and inserts the character.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('z')), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // No KeyPressed on outbound (the editor consumed it). However, 'z' changes the buffer
        // so InputChanged is emitted. Exactly one outbound event.
        Assert.Equal(1, engine.OutboundEvents.Count);
        Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? printableEv));
        Assert.IsType<InputChanged>(printableEv);

        // Verify the character is in the editor.
        Assert.NotNull(sink.LastModel);
        string fixedText = string.Concat(
            sink.LastModel!.FixedRegionRows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.Contains("z", fixedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModalDialogPreventsEditorMutation()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        Dialog dialog = new(modal: true);
        engine.Post(new ShowDialogCommand(dialog));

        // Type a printable — modal dialog consumes all non-exit keys.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('A')), Modifiers.None));
        // Arrow — also consumed.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Left), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // No KeyPressed emitted (modal ate all keys).
        Assert.Equal(0, engine.OutboundEvents.Count);

        // Editor buffer must be unchanged (empty).
        Assert.NotNull(sink.LastModel);
        string editorText = sink.LastModel!.FixedRegion.Editor.Text;
        Assert.Equal(string.Empty, editorText);
    }

    [Fact]
    public async Task ModalDialogEnterClosesOverlay()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        Dialog dialog = new(modal: true);
        engine.Post(new ShowDialogCommand(dialog));

        // Enter closes the dialog (Submit).
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // Dialog set CloseRequest = Submit and was dismissed → cleared from the model.
        Assert.Equal(OverlayCloseKind.Submit, dialog.CloseRequest);
        Assert.Null(sink.LastModel?.ActiveOverlay);
    }

    [Fact]
    public async Task AutocompleteEscapeDismissesOverlay()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        Autocomplete ac = new(new TextBuffer());
        ac.Show([new AutocompleteCandidate("x", PlainLine("x"))]);
        engine.Post(new ShowAutocompleteCommand(ac));

        // Escape dismisses the autocomplete.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // Autocomplete is dismissed → cleared from the model.
        Assert.True(ac.IsDismissed);
        Assert.Null(sink.LastModel?.ActiveOverlay);
    }

    // ── 11.5 — Composer: cursor placement and row order ───────────────────────

    [Fact]
    public void AutocompleteBelowInputCursorVisibleAndRowOrder()
    {
        // Autocomplete (BelowInput): cursor visible, EditorCaretLocal is in input rows,
        // and row order is [input…][autocomplete…][status…].
        RenderModel model = MakeModel(cols: 80, rows: 24);
        StatusLine status = new() { Rows = [PlainLine("status")] };
        TextBuffer editor = new();
        editor.Insert("hi");
        FixedRegionComposer composer = new(editor, status);

        Autocomplete ac = new(editor);
        ac.Show([new AutocompleteCandidate("hello", PlainLine("hello")), new AutocompleteCandidate("hi", PlainLine("hi"))]);
        model.ShowAutocomplete(ac);

        composer.Compose(model);

        // Cursor visible.
        Assert.True(model.IsCursorVisible);
        Assert.NotNull(model.EditorCaretLocal);

        // Row order: input (not offset by overlay rows above), then autocomplete, then status.
        // Status is the last row.
        IReadOnlyList<Line> rows = model.FixedRegionRows;
        Assert.True(rows.Count >= 2, "Should have at least input + status rows");

        // Status must be at the very bottom.
        Line lastRow = rows[^1];
        Assert.Contains(lastRow.Segments, s => s.Text.Contains("status", StringComparison.Ordinal));

        // Autocomplete should appear before status but after input (BelowInput).
        // The caret row is within input, so EditorCaretLocal.Row < (rows before status).
        (int caretRow, _) = model.EditorCaretLocal!.Value;
        // No above-overlay rows, so caretRow == editor-internal caret row (0 for single-line input).
        Assert.True(caretRow >= 0 && caretRow < rows.Count - 1 /* exclude status */,
            $"Caret row {caretRow} should be within the input section");
    }

    [Fact]
    public void ModalDialogAboveInputCursorHiddenAndRowOrder()
    {
        // Modal Dialog (AboveInput, HidesCursor=true): cursor hidden, EditorCaretLocal null,
        // and row order is [dialog…][input…][status…].
        RenderModel model = MakeModel(cols: 80, rows: 24);
        StatusLine status = new() { Rows = [PlainLine("status")] };
        TextBuffer editor = new();
        editor.Insert("hi");
        FixedRegionComposer composer = new(editor, status);

        Dialog dialog = new(modal: true);
        dialog.List.SetItems([PlainLine("option A"), PlainLine("option B")]);
        model.ShowModal(dialog);

        composer.Compose(model);

        // Cursor hidden.
        Assert.False(model.IsCursorVisible);
        Assert.Null(model.EditorCaretLocal);
        Assert.Null(model.CaretPosition);

        // Row order: dialog (above) then input then status.
        IReadOnlyList<Line> rows = model.FixedRegionRows;
        Assert.True(rows.Count >= 3, "Should have dialog + input + status rows");

        // Status at the bottom.
        Line lastRow = rows[^1];
        Assert.Contains(lastRow.Segments, s => s.Text.Contains("status", StringComparison.Ordinal));
    }

    [Fact]
    public void BudgetSqueezeTotalNeverExceedsCap()
    {
        // On a tight terminal: cap = 8 (10 rows, 50%=5<8 → floor=8), status=2.
        // budget = 6. Input=5 rows (multiline), overlay gets budget - inputRows ≥ 0.
        // Assert total ≤ cap.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        StatusLine status = new() { Rows = [PlainLine("s1"), PlainLine("s2")] };
        TextBuffer editor = new();
        editor.Insert("line1\nline2\nline3\nline4\nline5\nline6");
        FixedRegionComposer composer = new(editor, status);

        Autocomplete ac = new(editor);
        ac.Show(Enumerable.Range(1, 20).Select(i => new AutocompleteCandidate($"item{i}", PlainLine($"item{i}"))).ToList());
        model.ShowAutocomplete(ac);

        composer.Compose(model);

        int cap = FixedRegionComposer.ComputeCap(10, null); // 8
        Assert.True(model.FixedRegionRows.Count <= cap,
            $"Fixed region {model.FixedRegionRows.Count} exceeds cap {cap}");

        // Status must be fully present (sacred) — 2 rows at the bottom.
        IReadOnlyList<Line> rows = model.FixedRegionRows;
        Assert.Contains(rows, r => r.Segments.Any(s => s.Text.Contains("s1", StringComparison.Ordinal)));
        Assert.Contains(rows, r => r.Segments.Any(s => s.Text.Contains("s2", StringComparison.Ordinal)));
    }

    [Fact]
    public void BudgetSqueezeOverlayShrinksNotStatus()
    {
        // cap=8, status=2 → budget=6.
        // Large input (6 rows) → input gets 6 rows, overlay gets 0 (it absorbs the squeeze).
        // Total = 6 + 0 + 2 = 8 = cap. Status still present.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        StatusLine status = new() { Rows = [PlainLine("s1"), PlainLine("s2")] };
        TextBuffer editor = new();
        editor.Insert("line1\nline2\nline3\nline4\nline5\nline6");
        FixedRegionComposer composer = new(editor, status);

        Autocomplete ac = new(editor);
        ac.Show([new AutocompleteCandidate("suggestion", PlainLine("suggestion"))]);
        model.ShowAutocomplete(ac);

        composer.Compose(model);

        // Status rows both present.
        IReadOnlyList<Line> rows = model.FixedRegionRows;
        int cap = FixedRegionComposer.ComputeCap(10, null); // 8
        Assert.True(rows.Count <= cap);

        // Status at the bottom two rows.
        Assert.Contains(rows[^1].Segments, s => s.Text.Contains("s2", StringComparison.Ordinal));
        Assert.Contains(rows[^2].Segments, s => s.Text.Contains("s1", StringComparison.Ordinal));
    }

    [Fact]
    public void NoOverlayNormalComposeBehaviourUnchanged()
    {
        // No active overlay: behaviour is identical to the §10 path.
        RenderModel model = MakeModel(cols: 80, rows: 24);
        StatusLine status = new() { Rows = [PlainLine("status")] };
        TextBuffer editor = new();
        editor.Insert("hello");
        FixedRegionComposer composer = new(editor, status);

        composer.Compose(model);

        Assert.Equal(2, model.FixedRegionRows.Count); // input row + status row
        Assert.True(model.IsCursorVisible);
        Assert.NotNull(model.EditorCaretLocal);
    }

    /// <summary>
    /// Wires an Autocomplete to the engine's own TextBuffer (obtained from the model at
    /// Apply-time) and then shows it. This ensures Accept() writes into the same buffer
    /// that the engine and sink observe.
    /// </summary>
    private sealed class ShowAutocompleteOnEngineBufferCommand(string insertText) : ILoopCommand
    {
        private Autocomplete? _created;
        internal Autocomplete Created => _created ?? throw new InvalidOperationException("Not yet applied.");

        void ILoopCommand.Apply(RenderModel model)
        {
            TextBuffer buf = model.FixedRegion.Editor;
            _created = new Autocomplete(buf);
            _created.Show([new AutocompleteCandidate(insertText, PlainLine(insertText))]);
            model.ShowAutocomplete(_created);
            model.MarkDirty();
        }
    }

    // ── 11.2 — Autocomplete Accept clears overlay at the engine level ────────

    [Theory]
    [InlineData(NamedKey.Enter)]
    [InlineData(NamedKey.Tab)]
    public async Task AutocompleteAcceptClearsOverlay(NamedKey acceptKey)
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        // Use a command that wires the autocomplete to the engine's own editor buffer,
        // so that Accept() writes into the same TextBuffer the model/sink observe.
        ShowAutocompleteOnEngineBufferCommand cmd = new("accepted_text");
        engine.Post(cmd);

        // Let the command apply so the autocomplete is wired.
        clock.Advance(TimeSpan.FromMilliseconds(50));
        using CancellationTokenSource cts1 = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts1.Token);

        // Accept the candidate — Enter and Tab are both accept keys.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(acceptKey), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts2 = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts2.Token);

        // Overlay must be cleared.
        Assert.True(cmd.Created.IsDismissed);
        Assert.Null(sink.LastModel?.ActiveOverlay);

        // The accepted InsertText must be in the engine's editor buffer.
        Assert.NotNull(sink.LastModel);
        Assert.Equal("accepted_text", sink.LastModel!.FixedRegion.Editor.Text);
    }

    // ── 11.5 — Zero-cap guard: overlay contributes exactly zero rows ──────────

    [Fact]
    public void OverlayContributesNoRowsWhenBudgetFullyConsumed()
    {
        // cap=8 (rows=10, ComputeCap), status=2 → budget=6.
        // Input has 6 lines → inputRows == 6 → overlayCap = budget - inputRows = 0.
        // The autocomplete has candidates but must contribute zero rows.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        StatusLine status = new() { Rows = [PlainLine("s1"), PlainLine("s2")] };
        TextBuffer editor = new();
        editor.Insert("line1\nline2\nline3\nline4\nline5\nline6");
        FixedRegionComposer composer = new(editor, status);

        Autocomplete ac = new(editor);
        ac.Show(Enumerable.Range(1, 5).Select(i =>
            new AutocompleteCandidate($"ZZ_CANDIDATE_ZZ_{i}", PlainLine($"ZZ_CANDIDATE_ZZ_{i}"))).ToList());
        model.ShowAutocomplete(ac);

        composer.Compose(model);

        int cap = FixedRegionComposer.ComputeCap(10, null); // 8
        IReadOnlyList<Line> rows = model.FixedRegionRows;

        // Total rows must equal cap exactly (status + input fill it, overlay gets 0).
        Assert.Equal(cap, rows.Count);

        // No sentinel candidate text must appear anywhere in the fixed region.
        string allText = string.Concat(rows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.DoesNotContain("ZZ_CANDIDATE_ZZ", allText, StringComparison.Ordinal);
    }

    // ── 11.5 — Autocomplete defensive copy ───────────────────────────────────

    [Fact]
    public void AutocompleteShowStoresDefensiveCopy()
    {
        TextBuffer buf = new();
        Autocomplete ac = new(buf);

        List<AutocompleteCandidate> candidates =
        [
            new AutocompleteCandidate("original", PlainLine("original")),
        ];
        ac.Show(candidates);

        // Mutate the original list after Show.
        candidates.Add(new AutocompleteCandidate("added", PlainLine("added")));

        // The autocomplete should still render only the original candidate.
        // (MaxRows must be set first, or the list renders 0 rows.)
        ac.MaxRows = 10;
        IReadOnlyList<Line> rows = ac.Render(width: 80);
        Assert.Single(rows); // only the original, not the added one
    }
}
