using Nalix.Common.Messaging.Protocols;
using Nalix.Framework.Injection;
using Nalix.Framework.Random;
using Nalix.Logging;
using Nalix.Portal.Scenes.Menu.Main.View;
using Nalix.Portal.Scenes.Shared.Model;
using Nalix.Portal.Scenes.Shared.View;
using Nalix.Portal.Services.Abstractions;
using Nalix.Protocol.Collections;
using Nalix.Protocol.Enums;
using Nalix.Protocol.Models;
using Nalix.Rendering.Input;
using Nalix.Rendering.Objects;
using Nalix.Rendering.Runtime;
using Nalix.Rendering.Scenes;
using Nalix.SDK.Remote;
using Nalix.SDK.Remote.Extensions;
using Nalix.Shared.Messaging.Controls;
using SFML.Window;

namespace Nalix.Portal.Scenes.Shared.Controller;

/// <summary>
/// Base controller consolidating common flow for Login/Register scenes.
/// </summary>
internal abstract class CredentialsSceneController<TView>
    where TView : RenderObject, ICredentialsView
{
    protected readonly IParallaxPresetProvider _parallaxPresets;
    protected readonly IThemeProvider _theme;
    protected readonly ISceneNavigator _nav;
    protected readonly TView _view;
    protected readonly CredentialsModel _model = new();

    private ParallaxLayerView _parallaxLayerView;
    private System.Threading.CancellationTokenSource _cts;

    protected const System.Int32 CooldownMs = 600;
    protected const System.Int32 ServerTimeoutMs = 4000;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "<Pending>")]
    protected CredentialsSceneController(
        TView view,
        IThemeProvider theme,
        ISceneNavigator nav,
        IParallaxPresetProvider parallaxPreset)
    {
        _parallaxPresets = parallaxPreset ?? throw new System.ArgumentNullException(nameof(parallaxPreset));
        _theme = theme ?? throw new System.ArgumentNullException(nameof(theme));
        _nav = nav ?? throw new System.ArgumentNullException(nameof(nav));
        _view = view ?? throw new System.ArgumentNullException(nameof(view));
    }

    /// <summary>Operation command to send (LOGIN or REGISTER).</summary>
    protected abstract OpCommand Command { get; }

    /// <summary>Message mapping for errors (per scene).</summary>
    protected abstract System.String MapErrorMessage(ProtocolReason code);

    /// <summary>Scene to navigate when success.</summary>
    protected virtual System.String SuccessScene => SceneNames.Main;

    /// <summary>Text shown when too many attempts or invalid input.</summary>
    protected virtual System.String TooManyAttemptsMessage => "Too many attempts. Please wait a moment.";
    protected virtual System.String EmptyFieldsMessage => "Please enter username & password";
    protected virtual System.String CancelledMessage => "Request cancelled or timed out.";
    protected virtual System.String TimeoutMessage => "Request timeout. Please try again.";
    protected virtual System.String FailedMessage => "Request failed due to an error.";

    public virtual void Compose(Scene scene)
    {
        scene.AddObject(_view);

        // Parallax
        System.Int32 v = Csprng.GetInt32(1, 4);
        var preset = _parallaxPresets.GetByVariant(v);
        _parallaxLayerView = new ParallaxLayerView(_theme.Current, preset);
        scene.AddObject(_parallaxLayerView);

        WireView();
        GraphicsEngine.OnUpdate += Update;
    }

    protected virtual void WireView()
    {
        _view.SubmitRequested += () => _ = TrySendAsync();
        _view.BackRequested += () => SceneManager.ChangeScene(SceneNames.Main);
        _view.TabToggled += _ => { /* no-op: view already switches focus */ };
    }

    public void Update(System.Single dt)
    {
        var client = InstanceManager.Instance.GetOrCreateInstance<ReliableClient>();
        if (!client.IsConnected)
        {
            GraphicsEngine.OnUpdate -= Update;
            _nav.Change(SceneNames.Network);
            return;
        }

        if (InputState.IsKeyPressed(Keyboard.Key.Tab))
        {
            _view.OnTab();
        }

        if (InputState.IsKeyPressed(Keyboard.Key.Enter))
        {
            _view.OnEnter();
        }

        if (InputState.IsKeyPressed(Keyboard.Key.Escape))
        {
            _view.OnEscape();
        }

        if (InputState.IsKeyPressed(Keyboard.Key.F2))
        {
            _view.OnTogglePassword();
        }
    }

    private async System.Threading.Tasks.Task TrySendAsync()
    {
        if (_model.IsBusy)
        {
            return;
        }

        // Debounce
        if ((System.DateTime.UtcNow - _model.LastSubmitAtUtc).TotalMilliseconds < CooldownMs)
        {
            return;
        }

        _model.LastSubmitAtUtc = System.DateTime.UtcNow;

        var client = InstanceManager.Instance.GetOrCreateInstance<ReliableClient>();
        if (!client.IsConnected)
        {
            ShowNote("Not connected to server.");
            _nav.Change(SceneNames.Network);
            GraphicsEngine.OnUpdate -= Update;
            return;
        }

        // Local rate limit
        if (!_model.AllowSend())
        {
            ShowNote(TooManyAttemptsMessage);
            return;
        }

        System.String user = _view.Username;
        System.String pass = _view.Password;
        if (System.String.IsNullOrWhiteSpace(user) || System.String.IsNullOrEmpty(pass))
        {
            ShowNote(EmptyFieldsMessage);
            return;
        }

        _cts?.Dispose();
        _cts = new System.Threading.CancellationTokenSource(ServerTimeoutMs);
        _model.IsBusy = true;
        _view.LockUi(true);

        try
        {
            // Build and send credentials packet
            var creds = new Credentials { Username = user, Password = pass };
            var packet = new CredentialsPacket
            {
                SequenceId = Csprng.NextUInt32()
            };
            packet.Initialize((System.UInt16)Command, creds);

            await client.SendAsync(packet, _cts.Token).ConfigureAwait(false);

            using var subs = client.Subscribe(
                client.On<Directive>(d => client.TryHandleDirectiveAsync(d, null, null, _cts.Token))
            );

            var ctrl = await client.AwaitPacketAsync<Directive>(
                predicate: c => c.SequenceId == packet.SequenceId &&
                                (c.Type == ControlType.ACK || c.Type == ControlType.ERROR),
                timeoutMs: ServerTimeoutMs,
                ct: _cts.Token
            ).ConfigureAwait(false);

            if (ctrl.Type == ControlType.ACK)
            {
                _nav.Change(SuccessScene);
                GraphicsEngine.OnUpdate -= Update;
                return;
            }

            // Error branch
            var msg = MapErrorMessage(ctrl.Reason);
            ShowNote(msg);

            var backoff = MapBackoff(ctrl.Action);
            if (backoff is System.TimeSpan wait && wait > System.TimeSpan.Zero)
            {
                await System.Threading.Tasks.Task.Delay(wait, _cts.Token).ConfigureAwait(false);
            }

            if (ctrl.Action == ProtocolAdvice.DO_NOT_RETRY)
            {
                _view.LockUi(true);
            }
            else if (ctrl.Action == ProtocolAdvice.REAUTHENTICATE)
            {
                _view.FocusPass();
            }
        }
        catch (System.OperationCanceledException)
        {
            ShowNote(CancelledMessage);
        }
        catch (System.TimeoutException)
        {
            ShowNote(TimeoutMessage);
        }
        catch (System.Exception ex)
        {
            NLogix.Host.Instance.Error($"{Command} exception", ex);
            ShowNote(FailedMessage);
        }
        finally
        {
            _model.IsBusy = false;
            _view.LockUi(false);
        }
    }

    protected static System.TimeSpan? MapBackoff(ProtocolAdvice action) => action switch
    {
        ProtocolAdvice.BACKOFF_RETRY => System.TimeSpan.FromSeconds(3),
        ProtocolAdvice.REAUTHENTICATE => System.TimeSpan.Zero,
        ProtocolAdvice.DO_NOT_RETRY => null,
        _ => null
    };

    protected void ShowNote(System.String msg)
    {
        _view.ShowWarning(msg);
        _ = AutoHideAsync();
    }

    private async System.Threading.Tasks.Task AutoHideAsync()
    {
        await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(8));
        _view.ShowWarning(System.String.Empty);
    }
}
