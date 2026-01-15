// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Common.Diagnostics;
using Nalix.Framework.Injection;
using Nalix.Portal.Enums;
using Nalix.Portal.Objects.Indicators;
using Nalix.Portal.Objects.Notifications;
using Nalix.Portal.Services.Abstractions;
using Nalix.Rendering.Attributes;
using Nalix.Rendering.Objects;
using Nalix.Rendering.Scenes;
using Nalix.SDK.Remote;

namespace Nalix.Portal.Scenes.Network;

public class NetworkScene : Scene
{
    /// <summary>
    /// Khởi tạo một cảnh mạng với tên được xác định trong <see cref="SceneNames.Network"/>.
    /// </summary>
    public NetworkScene() : base(SceneNames.Network)
    {
    }

    /// <summary>
    /// Tải các đối tượng cần thiết cho cảnh mạng, bao gồm hiệu ứng tải, trình xử lý kết nối và thông báo kết nối.
    /// </summary>
    protected override void LoadObjects()
    {
        AddObject(new LoadingSpinner());
        AddObject(new NetworkHandler());
        AddObject(new Notification(ConnectText.Initial, Side.Top));
    }

    /// <summary>
    /// Internal strings for user-facing messages. Ready for future localization.
    /// </summary>
    private static class ConnectText
    {
        public const System.String Initial = "Connecting to server";
        public const System.String Attempt = "Connecting (attempt {0}/{1})";
        public const System.String Retrying = "Connection failed. Retrying in {0} s (attempt {1}/{2})";
        public const System.String Connected = "Connected";
        public const System.String LostShort = "Lost connection to server";
        public const System.String LostFull = "Unable to connect to server";
        public const System.String LostHint = "Check your connection or try again later";
    }

    [IgnoredLoad("RenderObject")]
    private class NetworkHandler : RenderObject
    {
        private const System.Single RetryDelay = 3f;
        private const System.Int32 MaxAttempts = 3;

        private enum ConnectState { Waiting, Trying, Success, Failed, ShowFail, Done }

        private System.Int32 _attempt;
        private System.Single _timer;
        private ConnectState _state;

        private System.Threading.CancellationTokenSource _cts;
        private System.Threading.Tasks.Task _connectTask;

        public NetworkHandler()
        {
            _timer = 0f;
            _attempt = 0;
            _state = ConnectState.Waiting;
        }

        public void ForceTryNow()
        {
            if (_state is ConnectState.Waiting)
            {
                // Force immediate transition to Trying in the next Update.
                _timer = RetryDelay;
            }
        }

        public override void Update(System.Single dt)
        {
            _timer += dt;

            switch (_state)
            {
                case ConnectState.Waiting:
                    {
                        // If we previously failed but will retry, show countdown.
                        if (_attempt is > 0 and < MaxAttempts)
                        {
                            var remaining = System.Math.Max(0, (System.Int32)System.Math.Ceiling(RetryDelay - _timer));
                            SceneManager.FindByType<Notification>()
                                ?.UpdateMessage(System.String.Format(ConnectText.Retrying, remaining, _attempt, MaxAttempts));
                        }

                        if (_timer >= RetryDelay)
                        {
                            _attempt++;
                            SceneManager.FindByType<Notification>()
                                ?.UpdateMessage(System.String.Format(ConnectText.Attempt, _attempt, MaxAttempts));

                            StartConnect();
                            _state = ConnectState.Trying;
                            _timer = 0f;
                        }
                        break;
                    }

                case ConnectState.Trying:
                    {
                        if (_connectTask == null)
                        {
                            break;
                        }

                        if (_connectTask.IsFaulted)
                        {
                            CleanupTask();

                            if (_attempt >= MaxAttempts)
                            {
                                // Final failure.
                                SceneManager.FindByType<Notification>()?.UpdateMessage(ConnectText.LostShort);
                                _state = ConnectState.Failed;
                                _timer = 0f;
                            }
                            else
                            {
                                // Prepare a retry cycle.
                                _state = ConnectState.Waiting;
                                _timer = 0f;
                                // Show first “retrying” message immediately (no ellipsis).
                                SceneManager.FindByType<Notification>()
                                    ?.UpdateMessage(System.String.Format(ConnectText.Retrying, (System.Int32)RetryDelay, _attempt, MaxAttempts));
                            }
                        }
                        else if (_connectTask.IsCompletedSuccessfully)
                        {
                            CleanupTask();
                            SceneManager.FindByType<Notification>()?.UpdateMessage(ConnectText.Connected);
                            _state = ConnectState.Success;
                        }
                        break;
                    }

                case ConnectState.Success:
                    {
                        // Proceed to next scene.
                        SceneManager.QueueDestroy(this);
                        SceneManager.ChangeScene(SceneNames.Handshake);
                        InstanceManager.Instance.GetExistingInstance<ISceneNavigator>().Change(SceneNames.Handshake);
                        _state = ConnectState.Done;
                        break;
                    }

                case ConnectState.Failed:
                    {
                        // Clean current banner then show a blocking box.
                        SceneManager.FindByType<Notification>()?.Destroy();
                        ShowFinalFailureBox();
                        _state = ConnectState.ShowFail;
                        break;
                    }

                case ConnectState.ShowFail:
                case ConnectState.Done:
                    // No-op.
                    break;
            }
        }

        private void StartConnect()
        {
            CleanupTask();
            _cts = new System.Threading.CancellationTokenSource();

            try
            {
                var client = InstanceManager.Instance.GetOrCreateInstance<ReliableClient>();
                _connectTask = client.ConnectAsync(20000, _cts.Token);
            }
            catch (System.Exception ex)
            {
                InstanceManager.Instance.GetExistingInstance<ILogger>()?
                                        .Error("Failed to resolve ReliableClient for connection attempt", ex);

                _connectTask = System.Threading.Tasks.Task.FromException(new System.Exception("Dependency resolution failed"));
            }
        }

        private void CleanupTask()
        {
            try { _cts?.Cancel(); } catch { /* ignored */ }
            _cts?.Dispose();
            _cts = null;
            _connectTask = null;
        }

        public override void BeforeDestroy() => CleanupTask();

        public override void Render(SFML.Graphics.RenderTarget target) { }
        protected override SFML.Graphics.Drawable GetDrawable() => null;

        /// <summary>
        /// Shows the final non-recoverable failure dialog.
        /// </summary>
        private static void ShowFinalFailureBox()
        {
            // Keep a single-action dialog if ActionNotification supports only one button.
            var box = new ActionNotification($"{ConnectText.LostFull}\n{ConnectText.LostHint}")
            {
                ButtonExtraOffsetY = 20f,
            };

            box.RegisterAction(() =>
            {
                box.Destroy();
                System.Environment.Exit(0);
            });

            box.Spawn();
        }
    }
}

