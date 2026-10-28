using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace SPlayer.Ui;

/// <summary>Short fades that stay off when Windows has animations disabled.</summary>
internal static class Motion
{
    private static readonly UISettings Settings = new();
    private static readonly ConditionalWeakTable<UIElement, Storyboard> Running = new();

    internal static readonly TimeSpan Short = TimeSpan.FromMilliseconds(167);

    internal static bool Enabled
    {
        get
        {
            try { return Settings.AnimationsEnabled; }
            catch (Exception) { return true; }
        }
    }

    internal static void Fade(UIElement element, bool visible, Action? done = null)
    {
        double to = visible ? 1 : 0;
        element.IsHitTestVisible = visible;
        if (Running.TryGetValue(element, out var running))
        {
            var opacity = element.Opacity;
            running.Stop();
            element.Opacity = opacity;
            Running.Remove(element);
        }
        if (!Enabled)
        {
            element.Opacity = to;
            done?.Invoke();
            return;
        }
        if (Math.Abs(element.Opacity - to) < .001) { done?.Invoke(); return; }
        var animation = new DoubleAnimation
        {
            From = element.Opacity, To = to, Duration = Short,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var story = new Storyboard();
        story.Children.Add(animation);
        story.Completed += (_, _) =>
        {
            if (!Running.TryGetValue(element, out var current) || current != story) return;
            Running.Remove(element);
            element.Opacity = to;
            story.Stop();
            done?.Invoke();
        };
        Running.Add(element, story);
        story.Begin();
    }
}
