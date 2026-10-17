using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QQDay30;

/// <summary>Shared entrance / feedback animations. Everything eases out so motion settles softly.</summary>
internal static class Anim
{
    public static readonly IEasingFunction EaseOut = new QuarticEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EaseInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

    private static TranslateTransform Translate(UIElement e)
    {
        if (e.RenderTransform is TranslateTransform t) return t;
        t = new TranslateTransform();
        e.RenderTransform = t;
        return t;
    }

    /// <summary>Fades the element in while it slides from (dx, dy) to its place.</summary>
    public static void FadeSlideIn(UIElement e, double dx, double dy, int delayMs = 0, int durationMs = 380)
    {
        var t = Translate(e);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var dur = TimeSpan.FromMilliseconds(durationMs);
        e.Opacity = 0;
        t.X = dx;
        t.Y = dy;
        e.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, dur) { BeginTime = begin, EasingFunction = EaseOut });
        t.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(dx, 0, dur) { BeginTime = begin, EasingFunction = EaseOut });
        t.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(dy, 0, dur) { BeginTime = begin, EasingFunction = EaseOut });
    }

    public static void Fade(UIElement e, double to, int durationMs = 200, Action? done = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = EaseInOut };
        if (done != null) a.Completed += (_, _) => done();
        e.BeginAnimation(UIElement.OpacityProperty, a);
    }

    public static void SlideY(UIElement e, double to, int durationMs = 320)
    {
        Translate(e).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = EaseOut });
    }
}
