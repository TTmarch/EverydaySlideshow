using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EverydaySlideshow.Core;

namespace EverydaySlideshow.Converters;

public sealed class CurrentMediaVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var wantsVideo = string.Equals(parameter as string, "Video", StringComparison.OrdinalIgnoreCase);
        var isVideo = value is bool flag && flag;
        return isVideo == wantsVideo ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class FitModeToStretchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is FitMode mode
            ? mode switch
            {
                FitMode.Fill => Stretch.UniformToFill,
                FitMode.Original => Stretch.None,
                _ => Stretch.Uniform
            }
            : Stretch.Uniform;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class FavoriteGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag && flag ? "★" : "☆";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
