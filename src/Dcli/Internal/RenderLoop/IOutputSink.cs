namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Abstracts the destination for rendered output, allowing §8's real escape-sequence paint
/// to be swapped in without changing the loop's cadence logic, and allowing tests to capture
/// paint calls deterministically.
/// </summary>
/// <remarks>
/// §8 will replace the minimal sentinel implementation with a real frame renderer.
/// Tests inject an in-memory capturing sink that records each paint call.
/// The real implementation writes to stdout via a buffered writer.
/// </remarks>
internal interface IOutputSink
{
    /// <summary>
    /// Called by the render loop exactly once per coalesced frame when the model is dirty
    /// and the minimum frame interval has elapsed.
    /// </summary>
    /// <param name="model">The current render model snapshot (read-only for the sink).</param>
    void Paint(RenderModel model);

    /// <summary>
    /// Emits the minimal ANSI restore sequence and flushes the output destination.
    /// Called from the render-loop <c>finally</c> block on every exit path so that cursor
    /// visibility, synchronized-output mode, and SGR state are cleaned up before the
    /// terminal session is released.
    /// </summary>
    /// <remarks>
    /// The sequence emitted is:
    /// <list type="bullet">
    ///   <item><c>ESC[?2026l</c> — synchronized-output OFF (in case the process died mid-frame)</item>
    ///   <item><c>ESC[?25h</c> — cursor visible (undoes any <c>ESC[?25l</c> from dialog / parking)</item>
    ///   <item><c>ESC[0m</c> — SGR reset (clear any lingering colour/style attributes)</item>
    /// </list>
    /// Implementations must be idempotent — the method may be called more than once.
    /// </remarks>
    void EmitRestoreSequence();
}
