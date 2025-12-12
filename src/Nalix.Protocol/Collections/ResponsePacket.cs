using Nalix.Common.Attributes;
using Nalix.Common.Caching;
using Nalix.Common.Packets;
using Nalix.Common.Packets.Abstractions;
using Nalix.Common.Serialization;
using Nalix.Framework.Injection;
using Nalix.Protocol.Enums;
using Nalix.Protocol.Extensions;
using Nalix.Shared.Memory.Pooling;
using Nalix.Shared.Messaging;
using Nalix.Shared.Serialization;

namespace Nalix.Protocol.Collections;

/// <summary>
/// Gói phản hồi siêu nhẹ từ server.
/// Chỉ gồm StatusCode (1 byte), không có chuỗi message để tiết kiệm băng thông.
/// </summary>
[SerializePackable(SerializeLayout.Explicit)]
[MagicNumber((System.UInt32)PacketMagic.RESPONSE)]
public sealed class ResponsePacket : FrameBase, IPoolable, IPacketDeserializer<ResponsePacket>
{
    /// <summary>
    /// Trạng thái phản hồi (Ok, InvalidCredentials, Locked…).
    /// Được serialize thành 1 byte.
    /// </summary>
    [SerializeOrder(PacketHeaderOffset.DATA_REGION)]
    public ResponseStatus Status { get; set; }

    /// <summary>
    /// Tổng độ dài gói tin = Header + 1 byte status.
    /// </summary>
    [SerializeIgnore]
    public override System.UInt16 Length =>
        PacketConstants.HeaderSize + sizeof(System.Byte);

    /// <summary>
    /// Khởi tạo mặc định.
    /// </summary>
    public ResponsePacket()
    {
        OpCode = OpCommand.NONE.AsUInt16();
        Status = ResponseStatus.INTERNAL_ERROR;
        MagicNumber = PacketMagic.RESPONSE.AsUInt32();
    }

    /// <summary>
    /// Thiết lập nhanh giá trị.
    /// </summary>
    public void Initialize(System.UInt16 opCode, ResponseStatus status)
    {
        OpCode = opCode;
        Status = status;
    }

    /// <summary>
    /// Reset trạng thái để reuse từ pool.
    /// </summary>
    public override void ResetForPool()
    {
        OpCode = OpCommand.NONE.AsUInt16();
        Status = ResponseStatus.INTERNAL_ERROR;
    }

    /// <summary>
    /// Deserialize từ buffer.
    /// </summary>
    public static ResponsePacket Deserialize(System.ReadOnlySpan<System.Byte> buffer)
    {
        ResponsePacket packet = InstanceManager.Instance
                                               .GetOrCreateInstance<ObjectPoolManager>()
                                               .Get<ResponsePacket>();

        _ = LiteSerializer.Deserialize(buffer, ref packet);
        return packet;
    }

    /// <inheritdoc/>
    public override System.Byte[] Serialize() => LiteSerializer.Serialize(this);

    /// <inheritdoc/>
    public override System.Int32 Serialize(System.Span<System.Byte> buffer) => LiteSerializer.Serialize(this, buffer);
}
