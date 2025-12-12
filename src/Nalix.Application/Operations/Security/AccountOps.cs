// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Application.Validators;
using Nalix.Common.Connection;
using Nalix.Common.Enums;
using Nalix.Common.Packets.Abstractions;
using Nalix.Common.Packets.Attributes;
using Nalix.Common.Protocols;
using Nalix.Framework.Injection;
using Nalix.Framework.Random;
using Nalix.Infrastructure.Repositories;
using Nalix.Logging;
using Nalix.Network.Connections;
using Nalix.Protocol.Collections;
using Nalix.Protocol.Enums;
using Nalix.Protocol.Models;
using Nalix.Shared.Memory.Pooling;
using Nalix.Shared.Security.Credentials;

namespace Nalix.Application.Operations.Security;

/// <summary>
/// User account management service: register, login, logout with secure practices and pooling (Dapper-based).
/// Now emits synchronized control directives via <see cref="ConnectionExtensions.SendAsync"/>.
/// </summary>
[PacketController]
public sealed class AccountOps(CredentialsRepository accounts) : OpsBase
{
    private readonly CredentialsRepository _accounts = accounts ?? throw new System.ArgumentNullException(nameof(accounts));

    private const System.Int32 MaxFailedLoginAttempts = 5;
    private static readonly System.TimeSpan LockoutWindow = System.TimeSpan.FromMinutes(3);

    static AccountOps()
    {
        _ = InstanceManager.Instance.GetOrCreateInstance<ObjectPoolManager>()
                                    .SetMaxCapacity<CredentialsPacket>(1024);

        _ = InstanceManager.Instance.GetOrCreateInstance<ObjectPoolManager>()
                                    .Prealloc<CredentialsPacket>(128);
    }

    /// <summary>
    /// Handles user registration.
    /// </summary>
    [PacketTimeout(4000)]
    [PacketEncryption(true)]
    [PacketRateLimit(1, 01)]
    [PacketPermission(PermissionLevel.GUEST)]
    [PacketOpcode((System.UInt16)OpCommand.REGISTER)]
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async System.Threading.Tasks.Task RegisterAsync(
        IPacket p, IConnection connection,
        System.Threading.CancellationToken token)
    {
        System.ArgumentNullException.ThrowIfNull(connection);
        System.UInt32 seq = GetSequenceIdOrZero(p);

        if (p is not CredentialsPacket packet)
        {
            await SendErrorAsync(connection, seq, ProtocolReason.UNSUPPORTED_PACKET, ProtocolAdvice.DO_NOT_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Invalid packet type. Expected CredentialsPacket from {0}", connection.RemoteEndPoint);

            return;
        }

        if (packet.Credentials is null)
        {
            await SendErrorAsync(connection, seq, ProtocolReason.VALIDATION_FAILED, ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Null credentials in register packet from {0}", connection.RemoteEndPoint);

            return;
        }

        Credentials credentials = packet.Credentials;

        if (!CredentialPolicy.IsValidUsername(credentials.Username))
        {
            await SendErrorAsync(
                    connection,
                    seq,
                    ProtocolReason.INVALID_USERNAME,
                    ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Invalid username format '{0}' in register attempt from {1}", credentials.Username, connection.RemoteEndPoint);

            return;
        }

        if (!CredentialPolicy.IsStrongPassword(credentials.Password))
        {
            await SendErrorAsync(
                    connection,
                    seq,
                    ProtocolReason.WEAK_PASSWORD,
                    ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Weak password in register attempt from {0}", connection.RemoteEndPoint);

            return;
        }

        try
        {
            // Derive salt/hash
            Pbkdf2.Hash(credentials.Password, out System.Byte[] salt, out System.Byte[] hash);

            Credentials entity = new()
            {
                Username = credentials.Username,
                Salt = salt,
                Hash = hash,
                Role = PermissionLevel.USER,
                CreatedAt = System.DateTime.UtcNow,
                IsActive = true,
                FailedLoginCount = 0
            };

            System.Int32 id = await _accounts.InsertOrIgnoreAsync(entity, token).ConfigureAwait(false);

            // Clear sensitive
            System.Array.Clear(salt, 0, salt.Length);
            System.Array.Clear(hash, 0, hash.Length);

            if (id <= 0)
            {
                await SendErrorAsync(connection, seq, ProtocolReason.ALREADY_EXISTS, ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

                NLogix.Host.Instance.Debug(
                    "Username {0} already exists from connection {1}", credentials.Username, connection.RemoteEndPoint);

                return;
            }

            await SendAckAsync(connection, seq).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Account {0} registered successfully from connection {1}", credentials.Username, connection.RemoteEndPoint);
        }
        catch (System.Exception ex)
        {
            await SendErrorAsync(
                connection, seq,
                ProtocolReason.INTERNAL_ERROR,
                ProtocolAdvice.BACKOFF_RETRY,
                flags: ControlFlags.IS_TRANSIENT).ConfigureAwait(false);

            NLogix.Host.Instance.Error(
                "Failed to register account {0} from connection {1}: {2}", credentials.Username, connection.RemoteEndPoint, ex.Message);
        }
    }

    /// <summary>
    /// Handles user login.
    /// </summary>
    [PacketTimeout(4000)]
    [PacketEncryption(true)]
    [PacketRateLimit(2, 03)]
    [PacketPermission(PermissionLevel.GUEST)]
    [PacketOpcode((System.UInt16)OpCommand.LOGIN)]
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async System.Threading.Tasks.Task LoginAsync(
        IPacket p, IConnection connection,
        System.Threading.CancellationToken token)
    {
        System.ArgumentNullException.ThrowIfNull(connection);
        System.UInt32 seq = GetSequenceIdOrZero(p);

        if (p is not CredentialsPacket packet)
        {
            await SendErrorAsync(connection, seq, ProtocolReason.UNSUPPORTED_PACKET, ProtocolAdvice.DO_NOT_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Invalid packet type. Expected CredentialsPacket from {0}", connection.RemoteEndPoint);

            return;
        }

        if (packet.Credentials is null)
        {
            await SendErrorAsync(connection, seq, ProtocolReason.VALIDATION_FAILED, ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Null credentials in login packet from {0}", connection.RemoteEndPoint);

            return;
        }

        if (System.String.IsNullOrWhiteSpace(packet.Credentials.Username) ||
            System.String.IsNullOrWhiteSpace(packet.Credentials.Password))
        {
            await SendErrorAsync(connection, seq, ProtocolReason.VALIDATION_FAILED, ProtocolAdvice.FIX_AND_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "Empty username or password in login attempt from {0}", connection.RemoteEndPoint);

            return;
        }

        Credentials credentials = packet.Credentials;

        try
        {
            token.ThrowIfCancellationRequested();

            var auth = await _accounts.GetAuthViewByUsernameAsync(packet.Credentials.Username.Trim(), token).ConfigureAwait(false);

            if (auth is null)
            {
                FakeVerifyDelay();

                await SendErrorAsync(
                    connection, seq,
                    ProtocolReason.UNAUTHENTICATED,
                    ProtocolAdvice.REAUTHENTICATE,
                    flags: ControlFlags.IS_AUTH_RELATED).ConfigureAwait(false);

                NLogix.Host.Instance.Debug(
                    "LOGIN attempt with non-existent username {0} from connection {1}", credentials.Username, connection.RemoteEndPoint);

                return;
            }

            var (id, salt, hash, isActive, failedCount, lastFailedAt, role) = auth.Value;

            // Lockout window
            if (failedCount >= MaxFailedLoginAttempts &&
                lastFailedAt.HasValue &&
                System.DateTime.UtcNow < lastFailedAt.Value + LockoutWindow)
            {
                await SendErrorAsync(
                    connection, seq,
                    ProtocolReason.ACCOUNT_LOCKED,
                    ProtocolAdvice.BACKOFF_RETRY,
                    flags: ControlFlags.IS_AUTH_RELATED).ConfigureAwait(false);

                NLogix.Host.Instance.Debug(
                    "Account {0} locked due to too many failed attempts from connection {1}", credentials.Username, connection.RemoteEndPoint);

                return;
            }

            System.Boolean ok = Pbkdf2.Verify(packet.Credentials.Password, salt, hash);
            System.Array.Clear(salt, 0, salt.Length);
            System.Array.Clear(hash, 0, hash.Length);

            // Verify password
            if (!ok)
            {
                _ = await _accounts.IncrementFailedAsync(id, System.DateTime.UtcNow, token).ConfigureAwait(false);
                await SendErrorAsync(
                    connection, seq,
                    ProtocolReason.UNAUTHENTICATED,
                    ProtocolAdvice.REAUTHENTICATE,
                    flags: ControlFlags.IS_AUTH_RELATED).ConfigureAwait(false);

                NLogix.Host.Instance.Debug(
                    "Incorrect password for {0}, attempt {1} from connection {2}", credentials.Username, failedCount, connection.RemoteEndPoint);

                return;
            }

            // Disabled account
            if (!isActive)
            {
                await SendErrorAsync(
                    connection, seq,
                    ProtocolReason.ACCOUNT_SUSPENDED,
                    ProtocolAdvice.DO_NOT_RETRY,
                    flags: ControlFlags.IS_AUTH_RELATED).ConfigureAwait(false);

                NLogix.Host.Instance.Debug(
                    "LOGIN attempt on disabled account {0} from connection {1}", credentials.Username, connection.RemoteEndPoint);

                return;
            }

            // Success: reset counters + stamp login time atomically
            _ = await _accounts.ResetFailedAndStampLoginAsync(id, System.DateTime.UtcNow, token).ConfigureAwait(false);

            // Assign permission level to connection if your pipeline uses it
            connection.Level = (PermissionLevel)role;
            InstanceManager.Instance.GetOrCreateInstance<ConnectionHub>()
                                    .AssociateUsername(connection, packet.Credentials.Username);

            await SendAckAsync(connection, seq).ConfigureAwait(false);

            NLogix.Host.Instance.Debug(
                "User {0} logged in successfully from connection {1}", credentials.Username, connection.RemoteEndPoint);
        }
        catch (System.OperationCanceledException)
        {
            await SendErrorAsync(
                connection, seq,
                ProtocolReason.CANCELLED,
                ProtocolAdvice.DO_NOT_RETRY,
                flags: ControlFlags.IS_TRANSIENT).ConfigureAwait(false);

            NLogix.Host.Instance.Warn(
                "LOGIN operation cancelled for {0} from connection {1}", credentials.Username, connection.RemoteEndPoint);
        }
        catch (System.Exception ex)
        {
            await SendErrorAsync(
                connection, seq,
                ProtocolReason.INTERNAL_ERROR,
                ProtocolAdvice.BACKOFF_RETRY,
                flags: ControlFlags.IS_TRANSIENT).ConfigureAwait(false);

            NLogix.Host.Instance.Error(
                "LOGIN failed for {0} from connection {1}: {2}", credentials.Username, connection.RemoteEndPoint, ex.Message);
        }
    }

    /// <summary>
    /// Handles user logout.
    /// </summary>
    [PacketEncryption(false)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketOpcode((System.UInt16)OpCommand.LOGOUT)]
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async System.Threading.Tasks.Task LogoutAsync(
        IPacket p, IConnection connection,
        System.Threading.CancellationToken token)
    {
        System.ArgumentNullException.ThrowIfNull(p);
        System.ArgumentNullException.ThrowIfNull(connection);

        System.UInt32 seq = GetSequenceIdOrZero(p);
        System.String username = InstanceManager.Instance.GetOrCreateInstance<ConnectionHub>()
                                                         .GetUsername(connection.ID);

        if (username is null)
        {
            await SendErrorAsync(connection, seq, ProtocolReason.SESSION_NOT_FOUND, ProtocolAdvice.DO_NOT_RETRY).ConfigureAwait(false);

            NLogix.Host.Instance.Warn(
                "LOGOUT attempt without valid session from connection {0}", connection.RemoteEndPoint);

            return;
        }

        try
        {
            _ = await _accounts.StampLogoutAsync(username, System.DateTime.UtcNow, token).ConfigureAwait(false);

            // Reset connection state
            connection.Level = PermissionLevel.NONE;
            _ = InstanceManager.Instance.GetOrCreateInstance<ConnectionHub>()
                                        .UnregisterConnection(connection);

            // Inform client to close (correlated), then disconnect so client receives it
            await connection.SendAsync(
                ControlType.DISCONNECT,
                ProtocolReason.CLIENT_QUIT,
                ProtocolAdvice.NONE,
                sequenceId: seq).ConfigureAwait(false);

            connection.Disconnect();

            NLogix.Host.Instance.Debug(
                "User {0} logged out successfully from connection {1}", username, connection.RemoteEndPoint);
        }
        catch (System.Exception ex)
        {
            // Best-effort error report then drop
            await SendErrorAsync(
                    connection, seq,
                    ProtocolReason.INTERNAL_ERROR,
                    ProtocolAdvice.BACKOFF_RETRY,
                    flags: ControlFlags.IS_TRANSIENT)
                .ConfigureAwait(false);

            connection.Level = PermissionLevel.NONE;
            connection.Disconnect();

            NLogix.Host.Instance.Error(
                "LOGOUT failed for {0} from connection {1}: {2}", username, connection.RemoteEndPoint, ex.Message);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void FakeVerifyDelay()
    {
        System.Byte[] salt = new System.Byte[16];
        Csprng.Fill(salt);
        Pbkdf2.Hash("FakePwd_For_Timing", out salt, out System.Byte[] hash);
        System.Array.Clear(salt, 0, salt.Length);
        System.Array.Clear(hash, 0, hash.Length);
    }
}
