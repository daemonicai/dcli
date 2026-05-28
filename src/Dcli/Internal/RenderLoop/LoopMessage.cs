using Dcli.Internal.Input;

namespace Dcli.Internal.RenderLoop;

// ─────────────────────────────────────────────────────────────────────────────
// Inbound message union
//
// All mutations of the render model flow through this FIFO channel. Input events
// from InputReader and API commands from the consumer share one channel so ordering
// is deterministic and both are reflected in the same coalesced frame.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Discriminated union of all messages that enter the render loop via the inbound channel.
/// The loop applies each message on its own dedicated thread in FIFO order.
/// </summary>
/// <remarks>
/// Two families:
/// <list type="bullet">
///   <item><see cref="InputMessage"/> — wraps a decoded <see cref="InputEvent"/> from §6.</item>
///   <item><see cref="ILoopCommand"/> — API commands from the consumer (§9–§12 add commands
///     by implementing this interface; the loop does not need to be modified).</item>
/// </list>
/// </remarks>
internal abstract record LoopMessage;

/// <summary>
/// Wraps a decoded input event from the §6 <see cref="InputReader"/> for
/// delivery to the render loop thread.
/// </summary>
/// <param name="Event">The decoded input event.</param>
internal sealed record InputMessage(InputEvent Event) : LoopMessage;

/// <summary>
/// A command issued by the consumer via the fire-and-forget API.
/// </summary>
/// <remarks>
/// Implement this interface to add new API commands (§9–§12) without modifying the loop.
/// <see cref="Apply"/> is always called on the render loop thread so implementors may mutate
/// the render model directly without locks.
/// </remarks>
internal interface ILoopCommand
{
    /// <summary>
    /// Applies the command to <paramref name="model"/>.
    /// Called exclusively on the render loop thread; no synchronization is required.
    /// </summary>
    void Apply(RenderModel model);
}

/// <summary>
/// Wraps an <see cref="ILoopCommand"/> as a <see cref="LoopMessage"/> for inbound-channel delivery.
/// </summary>
/// <param name="Command">The command to apply.</param>
internal sealed record CommandMessage(ILoopCommand Command) : LoopMessage;
