using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SPlayer.Ui;

/// <summary>The same light Acrylic SPhoto uses behind its sidebar.</summary>
internal sealed class SidebarBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _acrylic;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        _acrylic = new DesktopAcrylicController
        {
            TintColor = Color.FromArgb(255, 0xF3, 0xF3, 0xF3),
            TintOpacity = 0.15f,
            LuminosityOpacity = 0.55f,
            FallbackColor = Color.FromArgb(255, 0xF3, 0xF3, 0xF3),
        };
        _acrylic.AddSystemBackdropTarget(target);
        _acrylic.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(target, root));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _acrylic?.RemoveSystemBackdropTarget(target);
        _acrylic?.Dispose();
        _acrylic = null;
    }
}
