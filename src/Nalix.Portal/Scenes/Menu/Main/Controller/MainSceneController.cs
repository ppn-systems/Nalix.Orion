// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Framework.Random;
using Nalix.Portal.Objects.Notifications;
using Nalix.Portal.Scenes.Menu.Main.Model;
using Nalix.Portal.Scenes.Menu.Main.View;
using Nalix.Portal.Services.Abstractions;
using Nalix.Rendering.Attributes;
using Nalix.Rendering.Scenes;

namespace Nalix.Portal.Scenes.Menu.Main.Controller;

[IgnoredLoad("RenderObject")]
internal sealed class MainSceneController(
    MainMenuModel model,
    ISceneNavigator nav,
    ISfxPlayer sfx,
    IParallaxPresetProvider parallaxFactory,
    IThemeProvider theme)
{
    private readonly IParallaxPresetProvider _parallaxPresets = parallaxFactory ?? throw new System.ArgumentNullException(nameof(parallaxFactory));
    private readonly IThemeProvider _theme = theme ?? throw new System.ArgumentNullException(nameof(theme));
    private readonly MainMenuModel _model = model ?? throw new System.ArgumentNullException(nameof(model));
    private readonly ISceneNavigator _nav = nav ?? throw new System.ArgumentNullException(nameof(nav));
    private readonly ISfxPlayer _sfx = sfx ?? throw new System.ArgumentNullException(nameof(sfx));

    // Lắp ráp MVC vào scene (composition root)
    public void Compose(Scene scene)
    {
        // View: hiệu ứng mở đầu
        scene.AddObject(new RectRevealEffectView(_theme.Current));

        // View: logo
        scene.AddObject(new LauncherLogoView(_theme.Current));

        // View: icon 12+
        scene.AddObject(new TwelveIconView(_theme.Current));

        // Model parallax + view
        System.Int32 v = Csprng.GetInt32(1, 4);
        var preset = _parallaxPresets.GetByVariant(v);
        var parallaxView = new ParallaxLayerView(_theme.Current, preset);

        scene.AddObject(parallaxView);

        // View: menu
        var menu = new MainMenuView(_theme.Current);
        WireMenu(menu);
        scene.AddObject(menu);

        // View: banner cuộn (giữ nguyên text/tốc độ cũ)
        scene.AddObject(new ScrollingBanner("⚠ Chơi quá 180 phút mỗi ngày sẽ ảnh hưởng xấu đến sức khỏe ⚠", 200f));
    }

    // Gắn sự kiện từ View -> hành vi (điều hướng + âm thanh)
    private void WireMenu(MainMenuView menu)
    {
        menu.LoginRequested += () =>
        {
            if (_model.IsBusy)
            {
                return;
            }

            _sfx.Play("1");
            _nav.Change(SceneNames.Login);
        };

        menu.RegisterRequested += () =>
        {
            if (_model.IsBusy)
            {
                return;
            }

            _sfx.Play("1");
            _nav.Change(SceneNames.Register);
        };

        menu.NewsRequested += () =>
        {
            if (_model.IsBusy)
            {
                return;
            }

            _sfx.Play("1");
            _nav.Change(SceneNames.News);
        };

        menu.ExitRequested += () =>
        {
            if (_model.IsBusy)
            {
                return;
            }

            _sfx.Play("1");
            _nav.CloseWindow();
        };
    }
}
