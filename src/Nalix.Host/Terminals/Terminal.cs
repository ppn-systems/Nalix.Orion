// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Common.Core.Abstractions;
using Nalix.Framework.Injection;
using Nalix.Framework.Tasks;
using Nalix.Host.Runtime;
using Nalix.Infrastructure.Network;
using Nalix.Logging;
using Nalix.Network.Connections;
using Nalix.Network.Throttling;
using Nalix.Shared.Memory.Pooling;

namespace Nalix.Host.Terminals;

/// <summary>
/// Console-driven host service: handles shortcuts and graceful shutdown.
/// </summary>
internal sealed class TerminalService(IConsoleReader reader, ShortcutManager shortcuts, HostListener server) : IActivatable
{
    private static readonly System.Threading.Lock ReadLock = new();
    private readonly IConsoleReader _reader = reader ?? throw new System.ArgumentNullException(nameof(reader));
    private readonly HostListener _server = server ?? throw new System.ArgumentNullException(nameof(server));
    private readonly ShortcutManager _shortcuts = shortcuts ?? throw new System.ArgumentNullException(nameof(shortcuts));

    private System.Threading.CancellationToken _hostToken;
    private System.Threading.Tasks.Task? _loopTask;
    private volatile System.Boolean _started;
    private volatile System.Boolean _disposed;

    // double-press tracking
    private readonly System.Diagnostics.Stopwatch _quitWatch = System.Diagnostics.Stopwatch.StartNew();
    private System.Int64 _lastQuitTick = -1; // ticks from Stopwatch

    public System.Threading.ManualResetEventSlim ExitEvent { get; } = new(false); // still available for external waiters

    public void Activate(System.Threading.CancellationToken token)
    {
        if (_started)
        {
            return;
        }

        _hostToken = token;

        InitializeConsole(); // events + console config
        RegisterDefaultShortcuts();

        _loopTask = System.Threading.Tasks.Task.Run(EVENT_LOOP, token);
        _started = true;
        NLogix.Host.Instance.Info("[TERMINAL] started");
    }

    public void Deactivate(System.Threading.CancellationToken token)
    {
        if (!_started)
        {
            return;
        }

        // Signal exit and wait for loop to finish (best effort)
        this.ExitEvent.Set();

        if (_loopTask is not null)
        {
            try { _ = _loopTask.Wait(System.TimeSpan.FromSeconds(2), token); }
            catch { /* ignore */ }
        }

        UnsubscribeConsoleEvents();
        _started = false;
        NLogix.Host.Instance.Info("[TERMINAL] stopped");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try { UnsubscribeConsoleEvents(); } catch { }
        try { this.ExitEvent.Dispose(); } catch { }
    }

    // ===== console init & events =====

    private void InitializeConsole()
    {
        System.Console.CursorVisible = false;
        System.Console.TreatControlCAsInput = false;
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.Title = $"Auto ({AppConfig.VersionBanner})";

        System.Console.CancelKeyPress += OnCancelKeyPress;
        System.AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        System.AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        System.Console.ResetColor();
        NLogix.Host.Instance.Info("Terminal initialized successfully.");
    }

    private void UnsubscribeConsoleEvents()
    {
        System.Console.CancelKeyPress -= OnCancelKeyPress;
        System.AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        System.AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
    }

    private void OnCancelKeyPress(System.Object? sender, System.ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        NLogix.Host.Instance.Warn("Ctrl+C is disabled. Use Ctrl+H for shortcuts, Ctrl+Q to exit.");
    }

    private void OnProcessExit(System.Object? _, System.EventArgs __) => this.ExitEvent.Set();

    private void OnUnhandledException(System.Object? _, System.UnhandledExceptionEventArgs e)
    {
        NLogix.Host.Instance.Error("Unhandled exception: " + e.ExceptionObject);
        this.ExitEvent.Set();
    }

    // ===== shortcuts =====

    private void RegisterDefaultShortcuts()
    {
        _shortcuts.AddOrUpdateShortcut(System.ConsoleModifiers.Control, System.ConsoleKey.R, () => _server.Activate(_hostToken), "Run server");

        _shortcuts.AddOrUpdateShortcut(System.ConsoleModifiers.Control, System.ConsoleKey.X, () => _server.Deactivate(), "Stop server");

        _shortcuts.AddOrUpdateShortcut(System.ConsoleModifiers.Control, System.ConsoleKey.L, System.Console.Clear, "Clear screen");

        _shortcuts.AddOrUpdateShortcut(System.ConsoleModifiers.Control, System.ConsoleKey.M, SHOW_REPORT, "Report");

        _shortcuts.AddOrUpdateShortcut(System.ConsoleModifiers.Control, System.ConsoleKey.H, SHOW_SHORTCUTS, "Show shortcuts");

        _shortcuts.AddOrUpdateShortcut(System.ConsoleModifiers.Control, System.ConsoleKey.Q, () =>
        {
            if (TryHandleQuit())
            {
                return;
            }

            NLogix.Host.Instance.Warn("Press Ctrl+Q again within 2 seconds to exit.");
        }, "Exit (double-press)");
    }

    private System.Boolean TryHandleQuit()
    {
        const System.Double windowSeconds = 2.0;
        System.Int64 now = _quitWatch.ElapsedTicks;

        if (_lastQuitTick >= 0)
        {
            System.Double delta = (now - _lastQuitTick) / (System.Double)System.Diagnostics.Stopwatch.Frequency;
            if (delta <= windowSeconds)
            {
                NLogix.Host.Instance.Info("Exiting gracefully...");
                _server.Deactivate();
                this.ExitEvent.Set();
                return true;
            }
        }

        _lastQuitTick = now;
        return false;
    }

    // ===== report & shortcuts helpers =====

    private void SHOW_SHORTCUTS()
    {
        var sb = new System.Text.StringBuilder().AppendLine("Available shortcuts:");
        foreach (var (mod, key, desc) in _shortcuts.GetAllShortcuts())
        {
            _ = sb.AppendLine($"{FORMAT_MODIFIERS(mod)}{key,-6} → {desc}");
        }
        NLogix.Host.Instance.Info(sb.ToString());
    }

    private void SHOW_REPORT()
    {
        System.Console.WriteLine("Generating report...");
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
        System.Console.WriteLine(InstanceManager.Instance.GenerateReport());
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
        System.Console.WriteLine(InstanceManager.Instance.GetOrCreateInstance<TaskManager>().GenerateReport());
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
        System.Console.WriteLine(InstanceManager.Instance.GetOrCreateInstance<BufferPoolManager>().GenerateReport());
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
        System.Console.WriteLine(InstanceManager.Instance.GetOrCreateInstance<ObjectPoolManager>().GenerateReport());
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
        System.Console.WriteLine(InstanceManager.Instance.GetOrCreateInstance<ConnectionHub>().GenerateReport());
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
        System.Console.WriteLine(InstanceManager.Instance.GetOrCreateInstance<ConnectionLimiter>().GenerateReport());
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
        System.Console.WriteLine(InstanceManager.Instance.GetOrCreateInstance<TokenBucketLimiter>().GenerateReport());
        System.Console.WriteLine("\n-------------------------------------------------------------\n");
    }

    private static System.String FORMAT_MODIFIERS(System.ConsoleModifiers mod)
    {
        if (mod == 0)
        {
            return System.String.Empty;
        }

        var sb = new System.Text.StringBuilder();
        if (mod.HasFlag(System.ConsoleModifiers.Control))
        {
            _ = sb.Append("Ctrl+");
        }

        if (mod.HasFlag(System.ConsoleModifiers.Shift))
        {
            _ = sb.Append("Shift+");
        }

        if (mod.HasFlag(System.ConsoleModifiers.Alt))
        {
            _ = sb.Append("Alt+");
        }

        return sb.ToString();
    }

    // ===== event loop =====

    private async System.Threading.Tasks.Task EVENT_LOOP()
    {
        try
        {
            while (!_hostToken.IsCancellationRequested && !this.ExitEvent.IsSet)
            {
                if (_reader.KeyAvailable)
                {
                    System.ConsoleKeyInfo keyInfo;
                    lock (ReadLock) { keyInfo = _reader.ReadKey(intercept: true); }
                    _ = _shortcuts.TryExecuteShortcut(keyInfo.Modifiers, keyInfo.Key);
                }

                await System.Threading.Tasks.Task.Delay(10, _hostToken).ConfigureAwait(false);
            }
        }
        catch (System.OperationCanceledException) { /* normal */ }
        catch (System.Exception ex)
        {
            NLogix.Host.Instance.Error("[TERMINAL] loop faulted", ex);
        }
    }
}
