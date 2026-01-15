// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Common.Core.Abstractions;

namespace Nalix.Host.Runtime;

/// <summary>
/// Tiny host that manages a set of IHostService instances without external libs.
/// </summary>
public sealed class SimpleHost : IAsyncActivatable
{
    private readonly System.Threading.CancellationTokenSource _cts = new();
    private readonly System.Collections.Generic.List<IActivatable> _servicesSync = [];
    private readonly System.Collections.Generic.List<IAsyncActivatable> _servicesAsync = [];

    private volatile System.Boolean _started;
    private volatile System.Boolean _stopping;

    public System.Threading.CancellationToken Token => _cts.Token;

    public SimpleHost AddService(IAsyncActivatable service)
    {
        System.ArgumentNullException.ThrowIfNull(service);
        _servicesAsync.Add(service);
        return this;
    }

    public SimpleHost AddService(IActivatable service)
    {
        System.ArgumentNullException.ThrowIfNull(service);
        _servicesSync.Add(service);
        return this;
    }

    /// <summary>Start all services sequentially (fail-fast on first error).</summary>
    public async System.Threading.Tasks.Task ActivateAsync(
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        foreach (var s in _servicesAsync)
        {
            await s.ActivateAsync(_cts.Token).ConfigureAwait(false);
        }
        foreach (var s in _servicesSync)
        {
            s.Activate(_cts.Token);
        }
        _started = true;
    }

    /// <summary>Stop all services in reverse order.</summary>
    public async System.Threading.Tasks.Task DeactivateAsync(
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;

        using var linked = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        linked.CancelAfter(System.TimeSpan.FromSeconds(5));

        for (System.Int32 i = _servicesAsync.Count - 1; i >= 0; i--)
        {
            try { await _servicesAsync[i].DeactivateAsync(linked.Token).ConfigureAwait(false); }
            catch (System.OperationCanceledException) { /* best effort */ }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception during service deactivation: {ex}");
            }
        }

        for (System.Int32 i = _servicesSync.Count - 1; i >= 0; i--)
        {
            try { _servicesSync[i].Deactivate(linked.Token); }
            catch (System.OperationCanceledException) { /* best effort */ }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception during service deactivation: {ex}");
            }
        }

        _cts.Cancel();
    }

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        try { await DeactivateAsync().ConfigureAwait(false); }
        catch { /* ignore */ }

        foreach (var s in _servicesAsync)
        {
            await s.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var s in _servicesSync)
        {
            s.Dispose();
        }

        _cts.Dispose();
    }
}
