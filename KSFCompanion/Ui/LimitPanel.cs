using System;
using System.Windows;
using System.Windows.Controls;

namespace KsfCompanion.Ui
{
    /// <summary>A vertical stack that shows only its first MaxItems children - the Simple view's shorter lists.</summary>
    class LimitPanel : Panel
    {
        public static readonly DependencyProperty MaxItemsProperty = DependencyProperty.Register(
            nameof(MaxItems), typeof(int), typeof(LimitPanel), new FrameworkPropertyMetadata(int.MaxValue, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int MaxItems
        {
            get => (int)GetValue(MaxItemsProperty);
            set => SetValue(MaxItemsProperty, value);
        }

        protected override Size MeasureOverride(Size available)
        {
            double width = 0, height = 0;
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
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
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                // Past the limit: no room at all, so nothing of it shows (the layout clips it away).
                if (i >= MaxItems)
                {
                    child.Arrange(new Rect(0, y, 0, 0));
                    continue;
                }
                child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
            return final;
        }
    }
}
