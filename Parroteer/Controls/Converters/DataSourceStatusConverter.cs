using Parroteer.DataSources;
using System;
using System.Globalization;
using System.Windows.Data;

namespace Parroteer.Controls.Converters {

	[ValueConversion(typeof(DataSourceStatuses), typeof(String))]
	public class DataSourceStatusConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToString().Replace("_", " ").ToLower());
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}
}
