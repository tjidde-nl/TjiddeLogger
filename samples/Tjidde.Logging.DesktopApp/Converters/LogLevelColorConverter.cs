using Avalonia.Data.Converters;
using Avalonia.Media;
using Tjidde.Logging.Extensions;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Tjidde.Logging.DesktopApp.Converters;

public sealed class LogLevelColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace    => new SolidColorBrush(Color.Parse("#607D8B")),
                LogLevel.Debug    => new SolidColorBrush(Color.Parse("#4CAF50")),
                LogLevel.Information => new SolidColorBrush(Color.Parse("#2196F3")),
                LogLevel.Warning  => new SolidColorBrush(Color.Parse("#FF9800")),
                LogLevel.Error    => new SolidColorBrush(Color.Parse("#F44336")),
                LogLevel.Critical => new SolidColorBrush(Color.Parse("#9C27B0")),
                _ when level == MetricsLoggerExtensions.Metrics => new SolidColorBrush(Color.Parse("#e879f9")),
                _                 => new SolidColorBrush(Color.Parse("#555555"))
            };
        }
        return new SolidColorBrush(Color.Parse("#555555"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
