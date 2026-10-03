using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace KsfCompanion.Ui
{
    /// <summary>
    /// Cards of different heights in as many columns as fit (each at least MinItemWidth wide), each card going
    /// under the shortest column so far - no gaps under the short ones.
    /// </summary>
    class ColumnsPanel : Panel
    {
        public static readonly StyledProperty<double> MinItemWidthProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(MinItemWidth), 400.0);
        public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(Spacing), 16.0);

        static ColumnsPanel() => AffectsMeasure<ColumnsPanel>(MinItemWidthProperty, SpacingProperty);

        public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
        public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

        int Columns(double width) => double.IsInfinity(width) ? 1 : Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
        double ColumnWidth(double width, int columns) => double.IsInfinity(width) ? MinItemWidth : (width - Spacing * (columns - 1)) / columns;

        protected override Size MeasureOverride(Size available)
        {
            var columns = Columns(available.Width);
            var width = ColumnWidth(available.Width, columns);
            var heights = new double[columns];
            foreach (var child in Children)
            {
                child.Measure(new Size(width, double.PositiveInfinity));
                var shortest = Array.IndexOf(heights, heights.Min());
                heights[shortest] += child.DesiredSize.Height + Spacing;
            }
            var total = heights.Max();
            return new Size(double.IsInfinity(available.Width) ? width : available.Width, Math.Max(0, total - Spacing));
        }

        protected override Size ArrangeOverride(Size final)
        {
            var columns = Columns(final.Width);
            var width = ColumnWidth(final.Width, columns);
            var heights = new double[columns];
            foreach (var child in Children)
            {
                var shortest = Array.IndexOf(heights, heights.Min());
                child.Arrange(new Rect(shortest * (width + Spacing), heights[shortest], width, child.DesiredSize.Height));
                heights[shortest] += child.DesiredSize.Height + Spacing;
            }
            return final;
        }
    }
}
