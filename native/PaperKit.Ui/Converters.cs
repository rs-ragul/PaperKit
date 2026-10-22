using System.Globalization;
using Avalonia.Data.Converters;

namespace PaperKit.App;

/// <summary>
/// Compares the bound value with the parameter of the binding. Used to show
/// which colour or which choice is the selected one.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
