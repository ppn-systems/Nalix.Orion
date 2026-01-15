using Nalix.Common.Messaging.Protocols;
using Nalix.Portal.Scenes.Menu.Register.View;
using Nalix.Portal.Scenes.Shared.Controller;
using Nalix.Portal.Services.Abstractions;
using Nalix.Protocol.Enums;

namespace Nalix.Portal.Scenes.Menu.Register.Controller;

internal sealed class RegisterSceneController
    : CredentialsSceneController<RegisterView>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "<Pending>")]
    public RegisterSceneController(
        RegisterView view,
        IThemeProvider theme,
        ISceneNavigator nav,
        IParallaxPresetProvider parallaxPreset)
        : base(view, theme, nav, parallaxPreset)
    {
    }

    protected override OpCommand Command => OpCommand.REGISTER;

    protected override System.String MapErrorMessage(ProtocolReason code) => code switch
    {
        ProtocolReason.UNSUPPORTED_PACKET => "Client/server version mismatch detected.",
        ProtocolReason.INVALID_USERNAME => "Invalid credentials.",
        ProtocolReason.WEAK_PASSWORD => "Password is too weak. Choose a stronger one.",
        ProtocolReason.ALREADY_EXISTS => "Unable to complete registration.",
        ProtocolReason.VALIDATION_FAILED => "Please verify all fields are correct.",
        ProtocolReason.INTERNAL_ERROR => "Server error. Please try again later.",
        ProtocolReason.NONE => "None",
        _ => "Registration failed. Try again later."
    };
}
