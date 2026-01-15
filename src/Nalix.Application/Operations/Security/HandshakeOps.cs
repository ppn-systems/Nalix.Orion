using Nalix.Common.Connection;
using Nalix.Common.Core.Enums;
using Nalix.Common.Messaging.Packets.Abstractions;
using Nalix.Common.Messaging.Packets.Attributes;
using Nalix.Common.Messaging.Protocols;
using Nalix.Framework.Injection;
using Nalix.Logging;
using Nalix.Network.Connections;
using Nalix.Protocol.Enums;
using Nalix.Shared.Memory.Pooling;
using Nalix.Shared.Messaging.Controls;
using Nalix.Shared.Security.Asymmetric;
using Nalix.Shared.Security.Hashing;

namespace Nalix.Application.Operations.Security;

/// <summary>
/// Quản lý quá trình bắt tay bảo mật để thiết lập kết nối mã hóa an toàn với client.
/// Sử dụng thuật toán trao đổi khóa X25519 và băm SHA3256 để đảm bảo tính bảo mật và toàn vẹn của kết nối.
/// Lớp này chịu trách nhiệm khởi tạo bắt tay, tạo cặp khóa, và tính toán khóa mã hóa chung.
/// </summary>
[PacketController]
public sealed class HandshakeOps
{
    static HandshakeOps()
    {
        _ = InstanceManager.Instance.GetOrCreateInstance<ObjectPoolManager>()
                                    .SetMaxCapacity<Handshake>(1024);

        _ = InstanceManager.Instance.GetOrCreateInstance<ObjectPoolManager>()
                                    .Prealloc<Handshake>(64);
    }

    /// <summary>
    /// Khởi tạo quá trình bắt tay bảo mật với client.
    /// Nhận gói tin chứa khóa công khai X25519 (32 byte) từ client, tạo cặp khóa X25519 cho server,
    /// tính toán khóa mã hóa chung, và gửi khóa công khai của server về client.
    /// Phương thức này kiểm tra định dạng gói tin để đảm bảo an toàn và hiệu quả.
    /// </summary>
    /// <param name="p">Gói tin chứa khóa công khai X25519 của client, yêu cầu định dạng nhị phân và độ dài 32 byte.</param>
    /// <param name="connection">Thông tin kết nối của client yêu cầu bắt tay bảo mật.</param>
    /// <returns>Gói tin chứa khóa công khai của server hoặc thông báo lỗi nếu quá trình thất bại.</returns>
    [PacketEncryption(false)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketOpcode((System.UInt16)OpCommand.HANDSHAKE)]
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static async System.Threading.Tasks.Task Handshake(
        IPacket p,
        IConnection connection)
    {
        if (p is not Handshake packet)
        {
            await connection.SendAsync(
                ControlType.ERROR,
                ProtocolReason.UNSUPPORTED_PACKET,
                ProtocolAdvice.DO_NOT_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Error(
                "Invalid packet type. Expected HandshakePacket from {0}", connection.RemoteEndPoint);

            return;
        }

        // Defensive programming - kiểm tra payload null
        if (packet.Data is null)
        {
            await connection.SendAsync(
                ControlType.ERROR,
                ProtocolReason.MISSING_REQUIRED_FIELD,
                ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Error(
                "Null payload in handshake packet from {0}", connection.RemoteEndPoint);

            return;
        }

        // Xác thực độ dài khóa công khai, phải đúng 32 byte theo chuẩn X25519
        if (packet.Data.Length != 32)
        {
            await connection.SendAsync(
                ControlType.ERROR,
                ProtocolReason.VALIDATION_FAILED,
                ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Invalid public key length [Length={0}] from {1}", packet.Data.Length, connection.RemoteEndPoint);

            return;
        }

        // Tạo response packet chứa public key của server
        Handshake response = InstanceManager.Instance.GetOrCreateInstance<ObjectPoolManager>()
                                                     .Get<Handshake>();
        System.Byte[] payload = [];

        try
        {
            // Tạo cặp khóa X25519 (khóa riêng và công khai) cho server
            X25519.X25519KeyPair keyPair = X25519.GenerateKeyPair();

            // Tính toán shared secret từ private key của server và public key của client
            System.Byte[] secret = X25519.Agreement(keyPair.PrivateKey, packet.Data);

            // Băm bí mật chung bằng Keccak256  để tạo khóa mã hóa an toàn
            connection.Secret = Keccak256.HashData(secret);

            // Security: Clear sensitive data từ memory
            System.Array.Clear(keyPair.PrivateKey, 0, keyPair.PrivateKey.Length);
            System.Array.Clear(secret, 0, secret.Length);

            // Nâng cấp quyền truy cập của client lên mức Guest
            connection.Level = PermissionLevel.GUEST;

            response.Initialize(keyPair.PublicKey);
            response.OpCode = (System.UInt16)OpCommand.HANDSHAKE;

            payload = response.Serialize();
        }
        catch (System.Exception ex)
        {
            // Error handling theo security best practices
            NLogix.Host.Instance.Error(
                "HANDSHAKE failed for {0}: {1}",
                connection.RemoteEndPoint, ex.Message);

            // Reset connection state nếu có lỗi
            connection.Secret = null;
            connection.Level = PermissionLevel.NONE;

            await connection.SendAsync(
                ControlType.ERROR,
                ProtocolReason.INTERNAL_ERROR,
                ProtocolAdvice.BACKOFF_RETRY,
                flags: ControlFlags.IS_TRANSIENT).ConfigureAwait(false);
        }
        finally
        {
            InstanceManager.Instance.GetOrCreateInstance<ObjectPoolManager>()
                                    .Return(response);
        }

        if (payload is { Length: > 0 })
        {
            // If send fails, rollback state to avoid “half-upgraded” connection
            var sent = await connection.TCP.SendAsync(payload).ConfigureAwait(false);
            if (!sent)
            {
                connection.Secret = null;
                connection.Level = PermissionLevel.GUEST;
                NLogix.Host.Instance.Warn("HANDSHAKE send failed; rolled back state for {0}", connection.RemoteEndPoint);
                return;
            }
            else
            {
                NLogix.Host.Instance.Info("HANDSHAKE completed for {0}", connection.RemoteEndPoint);
            }
        }
    }
}