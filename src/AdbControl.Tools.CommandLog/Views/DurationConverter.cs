using System.Globalization;
using System.Windows.Data;
using AdbControl.Tools.CommandLog.ViewModels;

namespace AdbControl.Tools.CommandLog.Views;

/// <summary>Длительность команды в строке списка: миллисекунды, секунды или прочерк.</summary>
public sealed class DurationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return CommandLogToolViewModel.FormatDuration(value as int?);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
