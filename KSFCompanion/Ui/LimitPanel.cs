using System;
using Avalonia;
using Avalonia.Controls;

namespace KsfCompanion.Ui
{
    /// <summary>A vertical stack that shows only its first MaxItems children - the Simple view's shorter lists.</summary>
    class LimitPanel : Panel
    {
        public static readonly StyledProperty<int> MaxItemsProperty = AvaloniaProperty.Register<LimitPanel, int>(nameof(MaxItems), int.MaxValue);

        static LimitPanel() => AffectsMeasure<LimitPanel>(MaxItemsProperty);

        public int MaxItems
        {
            get => GetValue(MaxItemsProperty);
            set => SetValue(MaxItemsProperty, value);
        }

        protected override Size MeasureOverride(Size available)
        {
            double width = 0, height = 0;
            for (var i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (i >= MaxItems)
                {
                    child.Measure(new Size(0, 0));
                    continue;
                }
                child.Measure(new Size(available.Width, double.PositiveInfinity));
                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height;
            }
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double y = 0;
            for (var i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                // Past the limit: put far off the window, as Avalonia doesn't clip what gets no room.
                if (i >= MaxItems)
                {
                    child.Arrange(new Rect(-100000, -100000, 0, 0));
                    continue;
                }
                child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
            return final;
        }
    }
}
