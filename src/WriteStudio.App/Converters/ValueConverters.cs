using System.Globalization;
using WriteStudio.Core.Models;

#if WINDOWS
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
#endif

namespace WriteStudio.App.Converters;

#if WINDOWS
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = value is true;
        if (parameter is string p && p.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ColorInfo c)
        {
            return new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        }
        return Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class LevelToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float level && parameter is double maxWidth)
        {
            return Math.Clamp(level * maxWidth, 0.0, maxWidth);
        }
        if (value is float lvl)
        {
            return Math.Clamp(lvl * 120.0, 0.0, 120.0);
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class RecordingStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RecordingState state)
        {
            return state switch
            {
                RecordingState.Recording => new SolidColorBrush(Color.FromRgb(235, 52, 52)), // Red
                RecordingState.Paused => new SolidColorBrush(Color.FromRgb(245, 197, 24)),   // Yellow/Amber
                _ => new SolidColorBrush(Color.FromRgb(46, 184, 92))                        // Green/Ready
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
#endif
