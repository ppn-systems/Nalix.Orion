// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Framework.Random;
using Nalix.Portal.Scenes.Menu.Main.View;
using Nalix.Portal.Scenes.Menu.News.Model;
using Nalix.Portal.Scenes.Menu.News.View;
using Nalix.Portal.Services.Abstractions;
using Nalix.Rendering.Attributes;
using Nalix.Rendering.Input;
using Nalix.Rendering.Objects;
using Nalix.Rendering.Runtime;
using Nalix.Rendering.Scenes;

namespace Nalix.Portal.Scenes.Menu.News.Controller;

[IgnoredLoad("RenderObject")]
internal sealed class NewsSceneController
{
    private readonly IParallaxPresetProvider _parallaxPresets;
    private readonly IThemeProvider _theme;
    private readonly ISceneNavigator _nav;
    private readonly NewsView _view;
    private readonly NewsModel _model;

    private ParallaxLayerView _parallax;
    private ShutterBladesRevealEffect _reveal;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "<Pending>")]
    public NewsSceneController(
        NewsView view,
        NewsModel model,
        IThemeProvider theme,
        ISceneNavigator nav,
        IParallaxPresetProvider parallaxPresets)
    {
        _view = view ?? throw new System.ArgumentNullException(nameof(view));
        _model = model ?? throw new System.ArgumentNullException(nameof(model));
        _theme = theme ?? throw new System.ArgumentNullException(nameof(theme));
        _nav = nav ?? throw new System.ArgumentNullException(nameof(nav));
        _parallaxPresets = parallaxPresets ?? throw new System.ArgumentNullException(nameof(parallaxPresets));
    }

    public void Compose(Scene scene)
    {
        // Add parallax background (optional but consistent with other menus)
        System.Int32 v = Csprng.GetInt32(1, 4);
        var preset = _parallaxPresets.GetByVariant(v);
        _parallax = new ParallaxLayerView(_theme.Current, preset);
        scene.AddObject(_parallax);

        // Add main view
        scene.AddObject(_view);

        // Add reveal effect on top
        _reveal = new ShutterBladesRevealEffect(bladeCount: 10, duration: 0.9f, stagger: 0.06f);
        scene.AddObject(_reveal);

        // Wire view events
        _view.BackRequested += () => _nav.Change(SceneNames.Main);

        // Keyboard shortcuts
        GraphicsEngine.OnUpdate += Update;
    }

    private void Update(System.Single dt)
    {
        if (InputState.IsKeyPressed(SFML.Window.Keyboard.Key.Escape))
        {
            _nav.Change(SceneNames.Main);
        }
    }

    /// <summary>
    /// Vertical "shutter blades" reveal transition.
    /// </summary>
    [IgnoredLoad("RenderObject")]
    private sealed class ShutterBladesRevealEffect : RenderObject
    {
        private readonly System.Collections.Generic.List<SFML.Graphics.RectangleShape> _blades = [];

        private System.Single _t;
        private readonly System.Single _duration;
        private readonly System.Single _stagger;

        private readonly System.Int32 _bladeCount;
        private readonly SFML.Graphics.Color _bladeColor;
        private readonly System.Func<System.Single, System.Single> _ease;

        public ShutterBladesRevealEffect(System.Int32 bladeCount = 10, System.Single duration = 0.9f, System.Single stagger = 0.06f,
                                         SFML.Graphics.Color? color = null)
        {
            _bladeCount = System.Math.Max(2, bladeCount);
            _duration = System.Math.Max(0.05f, duration);
            _stagger = System.Math.Max(0f, stagger);
            _bladeColor = color ?? new SFML.Graphics.Color(0, 0, 0, 255);

            _ease = t =>
            {
                t = System.Math.Clamp(t, 0f, 1f);
                return 1f - System.MathF.Pow(1f - t, 3f);
            };

            for (System.Int32 i = 0; i < _bladeCount; i++)
            {
                var blade = new SFML.Graphics.RectangleShape { FillColor = _bladeColor };
                _blades.Add(blade);
            }

            SetZIndex(9999);
        }

        protected override SFML.Graphics.Drawable GetDrawable() => _blades[0];

        public override void Update(System.Single deltaTime)
        {
            _t += deltaTime;

            var screen = GraphicsEngine.ScreenSize;
            System.Single sw = screen.X;
            System.Single sh = screen.Y;
            if (sw <= 0f || sh <= 0f)
            {
                return;
            }

            System.Single bladeWidth = sw / _bladeCount;
            System.Boolean allFinished = true;

            for (System.Int32 i = 0; i < _bladeCount; i++)
            {
                var blade = _blades[i];
                blade.Size = new SFML.System.Vector2f(bladeWidth + 1f, sh + 1f);
                blade.Position = new SFML.System.Vector2f(i * bladeWidth, 0f);

                System.Single localStart = i * _stagger;
                System.Single localT = (_t - localStart) / _duration;
                System.Single eased = localT <= 0f ? 0f : _ease(localT);
                System.Boolean moveDown = i % 2 == 0;
                System.Single targetY = moveDown ? sh : -sh;
                System.Single yOffset = targetY * eased;

                blade.Position = new SFML.System.Vector2f(i * bladeWidth, yOffset);
                if (localT < 1f)
                {
                    allFinished = false;
                }
            }

            if (allFinished)
            {
                Destroy();
            }
        }

        public override void Render(SFML.Graphics.RenderTarget target)
        {
            for (System.Int32 i = 0; i < _blades.Count; i++)
            {
                target.Draw(_blades[i]);
            }
        }
    }
}
