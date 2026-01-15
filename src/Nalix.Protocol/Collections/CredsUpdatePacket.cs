using Nalix.Common.Core.Attributes;
using Nalix.Common.Core.Enums;
using Nalix.Common.Infrastructure.Caching;
using Nalix.Common.Messaging.Packets;
using Nalix.Common.Messaging.Packets.Abstractions; // if IPacketTransformer is here
using Nalix.Common.Serialization;
using Nalix.Framework.Injection;
using Nalix.Protocol.Enums;
using Nalix.Protocol.Extensions;
using Nalix.Shared.Extensions;
using Nalix.Shared.Memory.Pooling;
using Nalix.Shared.Messaging;
using Nalix.Shared.Serialization;

namespace Nalix.Protocol.Collections;

/// <summary>
/// Represents a password change request with old/new password.
/// Provides per-packet encryption/compression helpers to protect sensitive fields
/// on top of the session key established by HandshakeOps.
/// </summary>
[SerializePackable(SerializeLayout.Explicit)]
[MagicNumber((System.UInt32)PacketMagic.CHANGE_PASSWORD)]
public sealed class CredsUpdatePacket : FrameBase, IPoolable, IPacketTransformer<CredsUpdatePacket>
{
    /// <summary>
    /// Max allowed bytes (UTF-8) for each password field to limit abuse/DoS.
    /// </summary>
    public const System.Int32 MaxPasswordBytes = 128;

    [SerializeIgnore]
    private static readonly System.Text.Encoding Utf8 = System.Text.Encoding.UTF8;

    /// <summary>
    /// Old password (UTF-8, limited to 128 bytes).
    /// </summary>
    [SerializeDynamicSize(MaxPasswordBytes)]
    [SerializeOrder(PacketHeaderOffset.DATA_REGION)]
    public System.String OldPassword { get; set; } = System.String.Empty;

    /// <summary>
    /// New password (UTF-8, limited to 128 bytes).
    /// </summary>
    [SerializeDynamicSize(MaxPasswordBytes)]
    [SerializeOrder(PacketHeaderOffset.DATA_REGION + 1)]
    public System.String NewPassword { get; set; } = System.String.Empty;

    /// <summary>
    /// Total length (bytes) = header + UTF-8 byte counts of both fields.
    /// </summary>
    [SerializeIgnore]
    public override System.UInt16 Length
    {
        get
        {
            System.Int32 oldBytes = Utf8.GetByteCount(OldPassword);
            System.Int32 newBytes = Utf8.GetByteCount(NewPassword);
            System.Int32 total = PacketConstants.HeaderSize + oldBytes + newBytes;

            return (System.UInt32)total > System.UInt16.MaxValue
                ? throw new System.InvalidOperationException("Packet length exceeds UInt16.")
                : (System.UInt16)total;
        }
    }

    /// <summary>
    /// Default ctor: set defaults for opcode/magic.
    /// </summary>
    public CredsUpdatePacket()
    {
        OpCode = OpCommand.NONE.AsUInt16();
        // Prefer a dedicated magic if available:
        // MagicNumber = PacketMagic.CHANGE_PASSWORD.AsUInt32();
        MagicNumber = PacketMagic.CHANGE_PASSWORD.AsUInt32(); // fallback
    }

    /// <summary>
    /// Initialize the packet with validation (UTF-8 byte limits).
    /// </summary>
    public void Initialize(System.UInt16 opCode, System.String oldPassword, System.String newPassword)
    {
        System.ArgumentNullException.ThrowIfNull(oldPassword);
        System.ArgumentNullException.ThrowIfNull(newPassword);

        EnsureUtf8WithinLimit(oldPassword, MaxPasswordBytes, nameof(oldPassword));
        EnsureUtf8WithinLimit(newPassword, MaxPasswordBytes, nameof(newPassword));

        OpCode = opCode;
        OldPassword = oldPassword;
        NewPassword = newPassword;
    }

    /// <summary>
    /// Reset state for object pooling reuse.
    /// </summary>
    public override void ResetForPool()
    {
        OpCode = OpCommand.NONE.AsUInt16();
        OldPassword = System.String.Empty;
        NewPassword = System.String.Empty;
        // Keep MagicNumber stable for this packet type.
    }

    private static void EnsureUtf8WithinLimit(System.String value, System.Int32 maxBytes, System.String paramName)
    {
        if (Utf8.GetByteCount(value) > maxBytes)
        {
            throw new System.ArgumentOutOfRangeException(paramName,
                $"Field exceeds {maxBytes} bytes when encoded as UTF-8.");
        }
    }

    // -------------------- Transform helpers (per-packet crypto/codec) --------------------

    /// <summary>
    /// Encrypts sensitive fields (OldPassword/NewPassword) to Base64 using the given key/algorithm.
    /// </summary>
    public static CredsUpdatePacket Encrypt(
        CredsUpdatePacket packet,
        System.Byte[] key,
        CipherSuiteType algorithm)
    {
        System.ArgumentNullException.ThrowIfNull(packet);

        packet.OldPassword = packet.OldPassword.EncryptToBase64(key, algorithm);
        packet.NewPassword = packet.NewPassword.EncryptToBase64(key, algorithm);

        packet.Flags |= PacketFlags.ENCRYPTED;

        return packet;
    }

    /// <summary>
    /// Decrypts sensitive fields from Base64 using the given key/algorithm.
    /// </summary>
    public static CredsUpdatePacket Decrypt(
        CredsUpdatePacket packet,
        System.Byte[] key)
    {
        System.ArgumentNullException.ThrowIfNull(packet);

        try
        {
            packet.OldPassword = packet.OldPassword.DecryptFromBase64(key);
            packet.NewPassword = packet.NewPassword.DecryptFromBase64(key);
            packet.Flags &= ~PacketFlags.ENCRYPTED;

            return packet;
        }
        catch (System.FormatException ex)
        {
            throw new System.InvalidOperationException("Failed to decode Base64-encoded passwords.", ex);
        }
        catch (System.Exception ex)
        {
            throw new System.InvalidOperationException("Failed to decrypt passwords.", ex);
        }
    }

    /// <summary>
    /// Compresses fields (LZ4) and encodes as Base64.
    /// </summary>
    public static CredsUpdatePacket Compress(CredsUpdatePacket packet)
    {
        System.ArgumentNullException.ThrowIfNull(packet);

        packet.OldPassword = packet.OldPassword.CompressToBase64();
        packet.NewPassword = packet.NewPassword.CompressToBase64();
        packet.Flags |= PacketFlags.COMPRESSED;

        return packet;
    }

    /// <summary>
    /// Decompresses fields from Base64 (LZ4).
    /// </summary>
    public static CredsUpdatePacket Decompress(CredsUpdatePacket packet)
    {
        System.ArgumentNullException.ThrowIfNull(packet);

        packet.OldPassword = packet.OldPassword.DecompressFromBase64();
        packet.NewPassword = packet.NewPassword.DecompressFromBase64();
        packet.Flags &= ~PacketFlags.COMPRESSED;

        return packet;
    }

    /// <summary>
    /// Deserialize a packet from buffer via object pool.
    /// </summary>
    public static CredsUpdatePacket Deserialize(System.ReadOnlySpan<System.Byte> buffer)
    {
        CredsUpdatePacket packet = InstanceManager.Instance
            .GetOrCreateInstance<ObjectPoolManager>()
            .Get<CredsUpdatePacket>();

        _ = LiteSerializer.Deserialize(buffer, ref packet);
        return packet;
    }

    /// <inheritdoc/>
    public override System.Byte[] Serialize() => LiteSerializer.Serialize(this);

    /// <inheritdoc/>
    public override System.Int32 Serialize(System.Span<System.Byte> buffer) => LiteSerializer.Serialize(this, buffer);
}
