using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace SPlayer.Ui;

/// <summary>
/// The late Windows 10 look shared with SPhoto: white content, light grey
/// chrome, thin dividers, borderless commands, square corners.
/// </summary>
internal static class Kit
{
    internal const double Inset = 32;
    internal const double TitleBarHeight = 32;

    internal static readonly SolidColorBrush White = new(Colors.White);
    internal static readonly SolidColorBrush Chrome = new(Color.FromArgb(255, 0xF2, 0xF2, 0xF2));
    internal static readonly SolidColorBrush Divider = new(Color.FromArgb(255, 0xE1, 0xE1, 0xE1));
    internal static readonly SolidColorBrush Secondary = new(Color.FromArgb(255, 0x60, 0x60, 0x60));
    internal static readonly SolidColorBrush Tertiary = new(Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
    internal static readonly SolidColorBrush Hover = new(Color.FromArgb(0x19, 0, 0, 0));
    internal static readonly SolidColorBrush Press = new(Color.FromArgb(0x33, 0, 0, 0));
    internal static readonly SolidColorBrush Clear = new(Colors.Transparent);
    internal static readonly SolidColorBrush Ink = new(Colors.Black);

    internal static class G
    {
        internal const string Back = "\uE72B";
        internal const string Play = "\uE768";
        internal const string Pause = "\uE769";
        internal const string Previous = "\uE892";
        internal const string Next = "\uE893";
        internal const string Volume = "\uE767";
        internal const string Mute = "\uE74F";
        internal const string FullScreen = "\uE740";
        internal const string Restore = "\uE73F";
        internal const string Open = "\uE8E5";
        internal const string Folder = "\uE8B7";
        internal const string List = "\uE8FD";
        internal const string More = "\uE712";
        internal const string Info = "\uE946";
        internal const string Caption = "\uE7F4";
        internal const string Music = "\uE189";
        internal const string Delete = "\uE74D";
        internal const string Add = "\uE710";
        internal const string Link = "\uE71B";
        internal const string Stop = "\uE71A";
        internal const string Repeat = "\uE8EE";
        internal const string Shuffle = "\uE8B1";
        internal const string Save = "\uE74E";
    }

    internal static TextBlock Text(string text, double size = 15, bool secondary = false, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = secondary ? Secondary : Ink,
        };
        if (weight is { } w) block.FontWeight = w;
        return block;
    }

    internal static TextBlock Title(string text, double size = 28)
    {
        var block = Text(text, size, weight: FontWeights.Bold);
        block.CharacterSpacing = 30;
        return block;
    }

    internal static T Flat<T>(T control) where T : Control
    {
        foreach (var key in new[] { "ButtonBackground", "ToggleButtonBackground" }) control.Resources[key] = Clear;
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ToggleButtonBackgroundPointerOver" }) control.Resources[key] = Hover;
        foreach (var key in new[] { "ButtonBackgroundPressed", "ToggleButtonBackgroundPressed" }) control.Resources[key] = Press;
        foreach (var key in new[]
        {
            "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "ToggleButtonBorderBrush", "ToggleButtonBorderBrushPointerOver", "ToggleButtonBorderBrushPressed",
        })
        {
            control.Resources[key] = Clear;
        }
        control.Background = Clear;
        control.BorderThickness = new Thickness(0);
        return control;
    }

    internal static Button Button(string text, Action click, string? glyph = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = row;
        }
        var button = Flat(new Button { Content = content, Padding = new Thickness(12, 6, 12, 7) });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    internal static Button IconButton(string glyph, string tip, Action click, double size = 16)
    {
        var button = Flat(new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = size },
            Padding = new Thickness(10, 8, 10, 8),
            IsTabStop = true,
        });
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    internal static Border Line() => new() { Height = 1, Background = Divider };

    internal static void DarkCommand(Control control)
    {
        control.Foreground = Brush(Colors.White);
        control.Background = Brush(Colors.Transparent);
        control.BorderBrush = Brush(Colors.Transparent);
        control.BorderThickness = new Thickness(0);
        Put(control, "ButtonBackground", Colors.Transparent);
        Put(control, "ButtonBackgroundPointerOver", Color.FromArgb(0x28, 255, 255, 255));
        Put(control, "ButtonBackgroundPressed", Color.FromArgb(0x40, 255, 255, 255));
        Put(control, "ButtonForeground", Colors.White);
        Put(control, "ButtonForegroundPointerOver", Colors.White);
        Put(control, "ButtonForegroundPressed", Colors.White);
    }

    /// <summary>
    /// A brush can belong to only one resource slot. WinUI rejects a second insert of the same instance with 0x800F0902.
    /// </summary>
    private static void Put(Control control, string key, Color color)
    {
        try { control.Resources[key] = Brush(color); }
        catch (Exception ex) { Log.Write("样式 " + key + "：" + ex.Message); }
    }

    private static SolidColorBrush Brush(Color color) => new(color);
}
