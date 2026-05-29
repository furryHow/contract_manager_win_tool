using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ContractManager.Converters
{
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value?.ToString() ?? "";
            var mode = parameter?.ToString() ?? "Foreground";

            return (status, mode) switch
            {
                ("expired", "Background") => new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xE0)),
                ("expired", "Foreground") => new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
                ("expiring", "Background") => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)),
                ("expiring", "Foreground") => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
                ("warning", "Background") => new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xE7)),
                ("warning", "Foreground") => new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B)),
                ("normal", "Background") => new SolidColorBrush(Colors.White),
                ("normal", "Foreground") => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                _ => mode == "Background" ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var invert = parameter?.ToString() == "Invert";
            var visible = value is bool b && b;
            if (invert) visible = !visible;
            return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var invert = parameter?.ToString() == "Invert";
            var visible = value != null;
            if (invert) visible = !visible;
            return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringFormatConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is string s0 && values[1] is string s1)
                return $"{s0} - {s1}";
            return "";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}