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
}
