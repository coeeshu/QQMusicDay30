using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QQDay30;

/// <summary>Moon / sun / gear segmented switch with a sliding thumb and per-icon motion.</summary>
public partial class ThemeSwitch : UserControl
{
    private ThemeMode _mode = ThemeMode.System;

    /// <summary>Raised on user selection, with the icon center in window coordinates.</summary>
    public event Action<ThemeMode, Point>? ModeSelected;

    public ThemeSwitch()
    {
        InitializeComponent();
        // per-icon transforms (a style setter would share one frozen instance)
        foreach (var icon in Icons)
            icon.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new RotateTransform() } };
    }

    public void SetMode(ThemeMode mode, bool animate)
    {
        _mode = mode;
        var index = (int)mode; // Dark, Light, System = button order
        var (scale, move) = Parts(Thumb);
        var x = index * 34.0;
        if (!animate)
        {
            move.X = x;
            foreach (var g in Icons) g.Opacity = 0.5;
            Icon(mode).Opacity = 1;
            return;
        }

        var slide = new DoubleAnimation(x, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } };
        move.BeginAnimation(TranslateTransform.XProperty, slide);
        // a quick stretch while travelling makes the thumb feel elastic
        var stretch = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1.28, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), Anim.EaseOut));
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), Anim.EaseOut));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, stretch);

        foreach (var g in Icons)
            g.BeginAnimation(OpacityProperty, new DoubleAnimation(g == Icon(mode) ? 1 : 0.5, TimeSpan.FromMilliseconds(250)));
        PlayIcon(mode);
    }

    private TextBlock[] Icons => [MoonIcon, SunIcon, GearIcon];

    private TextBlock Icon(ThemeMode m) => m switch
    {
        ThemeMode.Dark => MoonIcon,
        ThemeMode.Light => SunIcon,
        _ => GearIcon,
    };

    private static (ScaleTransform, TranslateTransform) Parts(UIElement e)
    {
        var g = (TransformGroup)e.RenderTransform;
        return ((ScaleTransform)g.Children[0], (TranslateTransform)g.Children[1]);
    }

    private void PlayIcon(ThemeMode mode)
    {
        var icon = Icon(mode);
        var g = (TransformGroup)icon.RenderTransform;
        var scale = (ScaleTransform)g.Children[0];
        var rotate = (RotateTransform)g.Children[1];
        var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

        var grow = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = pop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        // moon swings in, sun rays turn half a revolution, gear spins once
        var (from, to, ms) = mode switch
        {
            ThemeMode.Dark => (-50.0, 0.0, 600),
            ThemeMode.Light => (-180.0, 0.0, 700),
            _ => (-360.0, 0.0, 800),
        };
        rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Seg_Click(object sender, RoutedEventArgs e)
    {
        var mode = sender == DarkBtn ? ThemeMode.Dark : sender == LightBtn ? ThemeMode.Light : ThemeMode.System;
        if (mode == _mode) return;
        SetMode(mode, animate: true);
        var btn = (Button)sender;
        var window = Window.GetWindow(this);
        var center = window != null && window.Content is Visual root
            ? btn.TransformToAncestor(root).Transform(new Point(btn.ActualWidth / 2, btn.ActualHeight / 2))
            : new Point();
        ModeSelected?.Invoke(mode, center);
    }
}
