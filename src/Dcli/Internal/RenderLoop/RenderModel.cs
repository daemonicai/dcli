namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Minimal mutable UI state owned exclusively by the render loop thread.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Thread discipline:</strong> this object must only ever be read or written on the
/// render loop thread — the single dedicated thread running <see cref="LoopEngine"/>.
/// No locks are needed because the single-writer loop is the only mutator. Sections 9 and 10
/// will add scrollback and fixed-region state as fields here.
/// </para>
/// <para>
/// <strong>Dirty flag:</strong> the loop sets <see cref="IsDirty"/> to <see langword="true"/>
/// whenever any state changes (command applied, input event processed). The flag is cleared
/// after each paint. A frame is only produced when dirty AND the minimum frame interval has
/// elapsed, giving the throttled-coalescing cadence.
/// </para>
/// <para>
/// <strong>Terminal size snapshot:</strong> <see cref="Columns"/> and <see cref="Rows"/> are
/// read by consumers via a volatile snapshot (Decision 10 — "value reads served from snapshot,
/// not round-trip"). The snapshot is initialised from the injected
/// <see cref="ITerminalSizeSource"/> and updated by resize messages. §13 will wire SIGWINCH
/// resize events into the inbound channel.
/// </para>
/// <para>
/// <strong>MaxFixedHeight:</strong> recorded from <see cref="TerminalOptions.MaxFixedHeight"/>
/// but not yet enforced. §10 will apply the fixed-region MaxHeight budget
/// (<c>clamp(appSet ?? 50%, 8, rows)</c>) when the fixed-region painter is implemented.
/// </para>
/// </remarks>
internal sealed class RenderModel
{
    /// <summary>
    /// Initialises the render model with the terminal dimensions reported by
    /// <paramref name="sizeSource"/>.
    /// </summary>
    /// <param name="sizeSource">Provides the initial terminal dimensions.</param>
    /// <param name="maxFixedHeight">
    /// Optional consumer-supplied cap on the fixed-region height (rows). Recorded here;
    /// §10's painter will enforce it.
    /// </param>
    internal RenderModel(ITerminalSizeSource sizeSource, int? maxFixedHeight = null)
    {
        ArgumentNullException.ThrowIfNull(sizeSource);
        (Columns, Rows) = sizeSource.GetSize();
        MaxFixedHeight = maxFixedHeight;
    }

    /// <summary>
    /// Whether any mutation has occurred since the last paint. Cleared by
    /// <see cref="ClearDirty"/>; set by callers applying commands or input events.
    /// </summary>
    internal bool IsDirty { get; private set; }

    /// <summary>Current terminal width in columns (from the latest size snapshot).</summary>
    internal int Columns { get; private set; }

    /// <summary>Current terminal height in rows (from the latest size snapshot).</summary>
    internal int Rows { get; private set; }

    /// <summary>
    /// Consumer-supplied cap on the fixed-region height in rows.
    /// <see langword="null"/> means no explicit cap. Recorded but not yet enforced;
    /// §10 will apply the full <c>clamp(appSet ?? 50%, 8, rows)</c> budget.
    /// </summary>
    internal int? MaxFixedHeight { get; }

    /// <summary>Marks the model as dirty so the loop will produce a frame.</summary>
    internal void MarkDirty() => IsDirty = true;

    /// <summary>Clears the dirty flag after a frame has been painted.</summary>
    internal void ClearDirty() => IsDirty = false;

    /// <summary>Updates the terminal size in the model.</summary>
    internal void UpdateSize(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
    }
}
