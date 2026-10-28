using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SPlayer.Ui;

internal static class Dialogs
{
    public static async Task Alert(XamlRoot root, string title, string summary, string? detail)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var box = new TextBox
            {
                Text = detail,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 220,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
                FontSize = 12,
            };
            stack.Children.Add(new TextBlock { Text = "技术细节", Foreground = Kit.Secondary, FontSize = 12 });
            stack.Children.Add(box);
        }
        var dialog = new ContentDialog
        {
            Title = title,
            Content = stack,
            CloseButtonText = "关闭",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    public static async Task<string?> AskUrl(XamlRoot root)
    {
        var box = new TextBox { PlaceholderText = "https://", Width = 420 };
        var dialog = new ContentDialog
        {
            Title = "打开网络地址",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "输入一个直接指向媒体文件的 HTTP 或 HTTPS 地址。", TextWrapping = TextWrapping.Wrap, Foreground = Kit.Secondary },
                    box,
                },
            },
            PrimaryButtonText = "打开",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        var error = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.Colors.Firebrick), TextWrapping = TextWrapping.Wrap };
        ((StackPanel)dialog.Content).Children.Add(error);
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (!MediaPaths.IsHttp(box.Text.Trim()))
            { error.Text = "请输入有效的 HTTP 或 HTTPS 媒体地址。"; e.Cancel = true; box.Focus(FocusState.Programmatic); }
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;
        var text = box.Text.Trim();
        return MediaPaths.IsHttp(text) ? text : null;
    }

    public static async Task Info(XamlRoot root, PlaybackFacts facts, PlaybackClock clock)
    {
        string line(string name, string value) => string.IsNullOrWhiteSpace(value) ? "" : name + "\t" + value;
        var rows = new[]
        {
            line("文件", facts.Path),
            line("标题", facts.MediaTitle),
            line("艺术家", facts.Artist),
            line("专辑", facts.Album),
            line("容器", facts.Container),
            line("视频", Join(facts.VideoCodec, facts.Width > 0 ? facts.Width + "×" + facts.Height : "", facts.Fps > 0 ? facts.Fps.ToString("0.##") + " fps" : "", facts.PixelFormat)),
            line("色彩", Join(facts.Primaries, facts.Gamma)),
            line("解码", string.IsNullOrWhiteSpace(facts.Hwdec) ? "" : facts.Hwdec),
            line("音频", Join(facts.AudioCodec, facts.ChannelLayout, facts.SampleRate > 0 ? facts.SampleRate + " Hz" : "")),
            line("时长", clock.Duration > 0 ? ClockText.Format(clock.Duration) : ""),
        };
        var text = string.Join(Environment.NewLine, rows.Where(r => r.Length > 0));
        var dialog = new ContentDialog
        {
            Title = "播放信息",
            Content = new TextBlock { Text = text.Length == 0 ? "还没有打开文件。" : text, FontSize = 14, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = "关闭",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
