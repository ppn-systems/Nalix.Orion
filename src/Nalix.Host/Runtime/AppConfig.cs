// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Application.Operations.Security;
using Nalix.Common.Diagnostics;
using Nalix.Framework.Injection;
using Nalix.Host.Assemblies;
using Nalix.Infrastructure.Database;
using Nalix.Infrastructure.Network;
using Nalix.Infrastructure.Repositories;
using Nalix.Logging;
using Nalix.Network.Dispatch;
using Nalix.Network.Middleware.Inbound;
using Nalix.Network.Middleware.Outbound;
using Nalix.Protocol;

namespace Nalix.Host.Runtime;

internal static class AppConfig
{
    /// <summary>
    /// Banner phiên bản cho log/console.
    /// </summary>
    public static System.String VersionBanner
        => $"Nalix.Host {AssemblyInspector.GetAssemblyInformationalVersion()} | {(System.Diagnostics.Debugger.IsAttached ? "Debug" : "Release")}";

    public static HostListener Listener;

    public static DbConnectionFactory DbFactory
        => InstanceManager.Instance.GetOrCreateInstance<DbConnectionFactory>();

    static AppConfig()
    {
        Registry.Load();

        if (InstanceManager.Instance.GetExistingInstance<ILogger>() == null)
        {
            InstanceManager.Instance.Register<ILogger>(NLogix.Host.Instance);
        }

        DbConnectionFactory factory = InstanceManager.Instance.GetOrCreateInstance<DbConnectionFactory>();

        DbInitializer.EnsureDatabaseInitializedAsync(factory).GetAwaiter().GetResult();

        CredentialsRepository credentials = new(factory);

        PacketDispatchChannel channel = new(cfg => cfg
            .WithLogging(NLogix.Host.Instance)
            .WithErrorHandling((exception, command)
                => NLogix.Host.Instance.Error($"Error handling command: {command}", exception))
            .WithInbound(new PermissionMiddleware())
            .WithInbound(new TokenBucketMiddleware())
            .WithInbound(new ConcurrencyMiddleware())
            .WithInbound(new RateLimitMiddleware())
            .WithInbound(new UnwrapPacketMiddleware())
            .WithInbound(new TimeoutMiddleware())
            .WithOutbound(new WrapPacketMiddleware())
            .WithHandler(() => new HandshakeOps())
            .WithHandler(() => new AccountOps(credentials))
            .WithHandler(() => new PasswordOps(credentials))
        );

        channel.Activate();
        HostProtocol protocol = new(channel);

        Listener = new HostListener(protocol);
    }
}