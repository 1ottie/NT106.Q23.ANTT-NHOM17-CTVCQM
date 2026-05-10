using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DrawClient.Converters
{
    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            try
            {
                if (value == null)
                    return Brushes.Black;

                return (SolidColorBrush)
                    new BrushConverter().ConvertFromString(value.ToString());
            }
            catch
            {
                return Brushes.Black;
            }
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                return brush.Color.ToString();
            }

            return "#000000";
        }
    }
}