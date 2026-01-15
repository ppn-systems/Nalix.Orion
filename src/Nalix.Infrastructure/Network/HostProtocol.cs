using Nalix.Common.Connection;
using Nalix.Common.Infrastructure.Connection;
using Nalix.Common.Messaging.Packets.Abstractions;
using Nalix.Framework.Injection;
using Nalix.Logging;
using Nalix.Network.Abstractions;
using Nalix.Network.Connections;
using System;
using System.Threading;

namespace Nalix.Infrastructure.Network;

/// <summary>
/// Lớp `HostProtocol` xử lý giao thức máy chủ, quản lý kết nối và xử lý dữ liệu.
/// </summary>
public sealed class HostProtocol : Nalix.Network.Protocols.Protocol
{
    /// <summary>
    /// Bộ điều phối gói tin được sử dụng để xử lý dữ liệu nhận được.
    /// </summary>
    private readonly IPacketDispatch<IPacket> _packetDispatcher;

    /// <summary>
    /// Xác định xem kết nối có được giữ mở liên tục hay không.
    /// </summary>
    public override Boolean KeepConnectionOpen => true;

    public HostProtocol(IPacketDispatch<IPacket> packetDispatcher)
    {
        _packetDispatcher = packetDispatcher;
        IsAccepting = true;
    }

    /// <summary>
    /// Xử lý sự kiện khi chấp nhận một kết nối mới.
    /// </summary>
    /// <param name="connection">Đối tượng kết nối mới.</param>
    /// <param name="cancellationToken">Token hủy kết nối.</param>
    public override void OnAccept(IConnection connection, CancellationToken cancellationToken = default)
    {
        base.OnAccept(connection, cancellationToken);

        // Thêm kết nối vào danh sách quản lý
        _ = InstanceManager.Instance.GetOrCreateInstance<ConnectionHub>().RegisterConnection(connection);
    }

    /// <summary>
    /// Xử lý tin nhắn nhận được từ kết nối.
    /// </summary>
    /// <param name="sender">Nguồn gửi tin nhắn.</param>
    /// <param name="args">Thông tin sự kiện kết nối.</param>
    public override void ProcessMessage(Object sender, IConnectEventArgs args)
    {
        try
        {
            _packetDispatcher.HandlePacket(args.Connection.IncomingPacket, args.Connection);
        }
        catch (Exception ex)
        {
            NLogix.Host.Instance.Error($"[ProcessMessage] Error processing packet from {args.Connection.RemoteEndPoint}: {ex}");
            args.Connection.Disconnect();
        }
    }

    /// <summary>
    /// Xử lý lỗi xảy ra trong quá trình kết nối.
    /// </summary>
    /// <param name="connection">Kết nối bị lỗi.</param>
    /// <param name="exception">Ngoại lệ xảy ra.</param>
    protected override void OnConnectionError(IConnection connection, Exception exception)
    {
        base.OnConnectionError(connection, exception);
        NLogix.Host.Instance.Error($"[OnConnectionError] Connection error with {connection.RemoteEndPoint}: {exception}");
    }

    public override String ToString() => "SERVER_PROTOCOL";
}