using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FluentCourse.Runtime.Controls;

public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var b = value is bool x && x;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        (value is Visibility v && v == Visibility.Visible) ^ Invert;
}
