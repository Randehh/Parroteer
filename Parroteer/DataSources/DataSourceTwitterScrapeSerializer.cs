using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Parroteer.DataSources {
	public class DataSourceTwitterScrapeSerializer : IDataSourceSerializer {

		private DataSourceTwitterScrape m_Source;

		public DataSourceTwitterScrapeSerializer(DataSourceTwitterScrape source) {
			m_Source = source;
		}

		public void Deserialize(string jsonPath) {
			if (!File.Exists(jsonPath)) return;

			string jsonData = File.ReadAllText(jsonPath);
			TwitterData data = JsonSerializer.Deserialize<TwitterData>(jsonData);
			m_Source.SourceID = data.Handle;
			m_Source.DataLines = new ObservableCollection<string>(data.Tweets);
		}

		public string Serialize() {
			TwitterData data = new TwitterData() {
				Handle = m_Source.SourceID,
				Tweets = new List<string>(m_Source.DataLines),
			};
			return JsonSerializer.Serialize(data);
		}

		private class TwitterData {
			public string Handle { get; set; }
			public List<string> Tweets { get; set; }
		}
	}
}
