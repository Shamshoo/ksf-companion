using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace KsfCompanion.Ui
{
    /// <summary>
    /// The filled part of a bar (Value 0..1, drawn from the left). With Animate on, instead of jumping it fills smoothly
    /// to the new value - from empty when it first appears, from the old length when a new time comes in.
    /// </summary>
    class FillBar : Control
    {
        /// <summary>Off for --preview renders, which take their picture before any animation has run.</summary>
        public static bool AnimationsOn { get; set; } = true;

        public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<FillBar, double>(nameof(Value));
        public static readonly StyledProperty<IBrush> FillProperty = AvaloniaProperty.Register<FillBar, IBrush>(nameof(Fill));
        public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<FillBar, double>(nameof(Radius));
        public static readonly StyledProperty<bool> AnimateProperty = AvaloniaProperty.Register<FillBar, bool>(nameof(Animate));

        static FillBar() => AffectsRender<FillBar>(FillProperty, RadiusProperty);

        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
        public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
        public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }

        double shown, from, to;
        TimeSpan duration;
        readonly Stopwatch clock = new Stopwatch();
        DispatcherTimer timer;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != ValueProperty) return;
            var target = Math.Max(0, Math.Min(1, Value));
            if (!Animate || !AnimationsOn || Math.Abs(target - shown) < 0.0001)
            {
                timer?.Stop();
                shown = to = target;
                InvalidateVisual();
                return;
            }
            // Longer for a bigger change, so a new best visibly grows into place.
            from = shown;
            to = target;
            duration = TimeSpan.FromMilliseconds(600 + 1400 * Math.Min(1, Math.Abs(to - from) * 2));
            clock.Restart();
            if (timer == null)
            {
                timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (s, e) => Step());
            }
            timer.Start();
        }

        void Step()
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            // Cubic ease out.
            var eased = 1 - Math.Pow(1 - t, 3);
            shown = from + (to - from) * eased;
            if (t >= 1)
            {
                shown = to;
                timer.Stop();
            }
            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            timer?.Stop();
            shown = to;
        }

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width * shown;
            if (width <= 0.01 || Fill == null) return;
            var radius = Math.Min(Radius, Math.Min(width, Bounds.Height) / 2);
            context.DrawRectangle(Fill, null, new Rect(0, 0, width, Bounds.Height), radius, radius);
        }
    }
}
