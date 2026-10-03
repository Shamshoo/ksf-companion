using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace KsfCompanion.Ui
{
    /// <summary>True when the bound value equals the parameter (compared as text): which tier / sort / view button is on.</summary>
    sealed class EqualsConverter : IValueConverter
    {
        public static readonly EqualsConverter Instance = new EqualsConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture), System.Convert.ToString(parameter, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// A picture as a brush that fills its area (cropped, centred): map pictures behind rounded corners, the avatar in
    /// its circle. Stretch can be given as the parameter ("Fill").
    /// </summary>
    sealed class ImageBrushConverter : IValueConverter
    {
        public static readonly ImageBrushConverter Instance = new ImageBrushConverter();
        readonly ConditionalWeakTable<IImageBrushSource, IBrush> brushes = new ConditionalWeakTable<IImageBrushSource, IBrush>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is IImageBrushSource image)) return null;
            if (parameter != null) return Create(image, Enum.TryParse<Stretch>(parameter.ToString(), out var stretch) ? stretch : Stretch.UniformToFill);
            return brushes.GetValue(image, i => Create(i, Stretch.UniformToFill));
        }

        static IBrush Create(IImageBrushSource image, Stretch stretch) =>
            new ImageBrush(image) { Stretch = stretch, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }.ToImmutable();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>One of two texts for a true/false value: the parameter is "when true|when false".</summary>
    sealed class PickConverter : IValueConverter
    {
        public static readonly PickConverter Instance = new PickConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var options = (parameter as string ?? "").Split('|');
            return value is true ? options[0] : options.Length > 1 ? options[1] : "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
