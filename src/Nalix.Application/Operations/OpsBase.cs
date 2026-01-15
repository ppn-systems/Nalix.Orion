// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Common.Connection;
using Nalix.Common.Messaging.Packets.Abstractions;
using Nalix.Common.Messaging.Protocols;
using Nalix.Network.Connections;

namespace Nalix.Application.Operations;

/// <summary>
/// Base class for packet operation controllers that need
/// SequenceId correlation and directive response helpers.
/// </summary>
public abstract class OpsBase
{
    /// <summary>
    /// Attempts to obtain a correlation SequenceId from the incoming packet.
    /// Returns 0 when unavailable.
    /// </summary>
    protected static System.UInt32 GetSequenceIdOrZero(IPacket p)
        => p is IPacketSequenced seq ? seq.SequenceId : 0u;

    /// <summary>
    /// Sends an ACK directive correlated with the given sequence.
    /// </summary>
    protected static System.Threading.Tasks.Task SendAckAsync(IConnection c, System.UInt32 seq)
        => c.SendAsync(ControlType.ACK, ProtocolReason.NONE, ProtocolAdvice.NONE, sequenceId: seq);

    /// <summary>
    /// Sends an ERROR directive with code, action, and optional flags.
    /// </summary>
    protected static System.Threading.Tasks.Task SendErrorAsync(
        IConnection c,
        System.UInt32 seq,
        ProtocolReason code,
        ProtocolAdvice action,
        ControlFlags flags = ControlFlags.NONE)
        => c.SendAsync(ControlType.ERROR, code, action, sequenceId: seq, flags: flags);
}
