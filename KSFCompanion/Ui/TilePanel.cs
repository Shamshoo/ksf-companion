using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KsfCompanion.Ui
{
    /// <summary>
    /// Tiles in as many equal columns as fit (none narrower than MinItemWidth), stretched so each row
    /// fills the width exactly - a wrap panel of fixed-size tiles leaves a ragged gap on the right.
    /// </summary>
    class TilePanel : Panel
    {
        public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
            nameof(MinItemWidth), typeof(double), typeof(TilePanel), new FrameworkPropertyMetadata(220.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(TilePanel), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinItemWidth
        {
            get => (double)GetValue(MinItemWidthProperty);
            set => SetValue(MinItemWidthProperty, value);
        }

        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        int columns = 1;
        double itemWidth;
        double[] rowHeights = new double[0];

        protected override Size MeasureOverride(Size available)
        {
            var unbounded = double.IsInfinity(available.Width);
            var width = unbounded ? MinItemWidth : available.Width;
            columns = Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
            itemWidth = Math.Max(0, Math.Floor((width - Spacing * (columns - 1)) / columns));

            var count = InternalChildren.Count;
            rowHeights = new double[(count + columns - 1) / columns];
            for (var i = 0; i < count; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeights[i / columns] = Math.Max(rowHeights[i / columns], child.DesiredSize.Height);
            }
            var height = rowHeights.Sum() + Spacing * Math.Max(0, rowHeights.Length - 1);
            return new Size(unbounded ? itemWidth : width, height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double y = 0;
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                int row = i / columns, column = i % columns;
                if (column == 0 && row > 0) y += rowHeights[row - 1] + Spacing;
                InternalChildren[i].Arrange(new Rect(column * (itemWidth + Spacing), y, itemWidth, rowHeights[row]));
            }
            return final;
        }
    }

    /// <summary>Keeps its content at a fixed height-to-width ratio, like a picture that grows with its tile.</summary>
    class AspectBox : Decorator
    {
        public double Ratio { get; set; } = 0.56;

        protected override Size MeasureOverride(Size available)
        {
            var width = double.IsInfinity(available.Width) ? 236 : available.Width;
            var size = new Size(width, Math.Round(width * Ratio));
            Child?.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size final)
        {
            Child?.Arrange(new Rect(final));
            return final;
        }
    }
}
