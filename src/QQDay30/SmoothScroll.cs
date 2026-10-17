using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QQDay30;

/// <summary>
/// Eased mouse-wheel scrolling: the wheel moves a target offset and the real offset glides towards it each frame.
/// Usage: local:SmoothScroll.IsEnabled="True" on any control that hosts a ScrollViewer.
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && (bool)e.NewValue)
            fe.Loaded += (_, _) =>
            {
                var sv = fe as ScrollViewer ?? FindScrollViewer(fe);
                if (sv != null) new Scroller(sv);
            };
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is ScrollViewer sv) return sv;
            if (FindScrollViewer(c) is { } found) return found;
        }
        return null;
    }

    private sealed class Scroller
    {
        private readonly ScrollViewer _sv;
        private double _target;
        private bool _running;

        public Scroller(ScrollViewer sv)
        {
            _sv = sv;
            sv.PreviewMouseWheel += OnWheel;
            // keep the target in sync when something else scrolls (scrollbar drag, new items)
            sv.ScrollChanged += (_, _) => { if (!_running) _target = sv.VerticalOffset; };
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_sv.ScrollableHeight <= 0) return;
            e.Handled = true;
            _target = Math.Clamp(_target - e.Delta * 0.9, 0, _sv.ScrollableHeight);
            if (_running) return;
            _running = true;
            CompositionTarget.Rendering += Step;
        }

        private void Step(object? sender, EventArgs e)
        {
            var cur = _sv.VerticalOffset;
            var diff = _target - cur;
            if (Math.Abs(diff) < 0.5)
            {
                _sv.ScrollToVerticalOffset(_target);
                CompositionTarget.Rendering -= Step;
                _running = false;
                return;
            }
            _sv.ScrollToVerticalOffset(cur + diff * 0.2);
        }
    }
}
