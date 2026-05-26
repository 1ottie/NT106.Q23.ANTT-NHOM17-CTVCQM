using System;
using System.Globalization;
using System.Windows.Data;

namespace DrawClient.Converters
{
    public class BoolToPlayIconConverter : IValueConverter
    {
        // Play triangle / Stop square
        private const string PlayIcon = "M5 3 L19 12 L5 21 Z";
        private const string StopIcon = "M3 3 H21 V21 H3 Z";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlaying && isPlaying)
                return StopIcon;
            return PlayIcon;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
