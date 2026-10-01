using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KsfCompanion.Ui
{
    /// <summary>
    /// ui:BarFill.Value="{Binding ...}" on a bar (0..1): instead of jumping, the bar fills smoothly to the new
    /// value - from empty when it first appears, from the old length when a new time comes in.
    /// </summary>
    static class BarFill
    {
        /// <summary>Off for --preview renders, which take their picture before any animation has run.</summary>
        public static bool Animate { get; set; } = true;

        // The default isn't a real value, so the first one bound - even 0 - sets the bar up (an untouched bar would show full).
        public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
            "Value", typeof(double), typeof(BarFill), new PropertyMetadata(-1.0, OnValueChanged));

        public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);
        public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

        static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement element)) return;
            // Transforms that come from a template are frozen; the bar gets its own.
            if (!(element.RenderTransform is ScaleTransform scale) || scale.IsFrozen)
                element.RenderTransform = scale = new ScaleTransform(element.RenderTransform is ScaleTransform old ? old.ScaleX : 0, 1);
            var to = Math.Max(0, Math.Min(1, (double)e.NewValue));
            if (!Animate || to == scale.ScaleX)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.ScaleX = to;
                return;
            }
            // Longer for a bigger change, so a new best visibly grows into place.
            var distance = Math.Abs(to - scale.ScaleX);
            var duration = TimeSpan.FromMilliseconds(600 + 1400 * Math.Min(1, distance * 2));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}
