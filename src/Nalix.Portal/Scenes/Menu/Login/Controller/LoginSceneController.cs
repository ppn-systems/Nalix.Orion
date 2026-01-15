using Nalix.Common.Messaging.Protocols;
using Nalix.Portal.Scenes.Menu.Login.View;
using Nalix.Portal.Scenes.Shared.Controller;
using Nalix.Portal.Services.Abstractions;
using Nalix.Protocol.Enums;

namespace Nalix.Portal.Scenes.Menu.Login.Controller;

internal sealed class LoginSceneController(
    LoginView view,
    IThemeProvider theme,
    ISceneNavigator nav,
    IParallaxPresetProvider parallaxPreset) : CredentialsSceneController<LoginView>(view, theme, nav, parallaxPreset)
{
    protected override OpCommand Command => OpCommand.LOGIN;

    protected override System.String MapErrorMessage(ProtocolReason code) => code switch
    {
        ProtocolReason.UNSUPPORTED_PACKET => "Client/server version mismatch.",
        ProtocolReason.UNAUTHENTICATED => "Invalid username or password.",
        ProtocolReason.ACCOUNT_LOCKED => "Too many failed attempts. Please wait and try again.",
        ProtocolReason.ACCOUNT_SUSPENDED => "Your account is suspended.",
        ProtocolReason.VALIDATION_FAILED => "Please fill both username and password.",
        ProtocolReason.CANCELLED => "Login cancelled.",
        ProtocolReason.INTERNAL_ERROR => "Server error. Please try again later.",
        _ => "Login failed."
    };
}
