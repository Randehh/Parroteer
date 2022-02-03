using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Parroteer.DataSources {
	public class DataSourceTwitterSerializer : IDataSourceSerializer {

		private DataSourceTwitter m_Source;

		public DataSourceTwitterSerializer(DataSourceTwitter source) {
			m_Source = source;
		}

		public void Deserialize(string jsonPath) {
			string jsonData = File.ReadAllText(jsonPath);
			TwitterData data = JsonSerializer.Deserialize<TwitterData>(jsonData);
			m_Source.SourceID = data.Handle;
			m_Source.DataLines = new ObservableCollection<string>(data.Tweets);
			m_Source.LatestTweet = data.LatestTweet;
		}

		public string Serialize() {
			TwitterData data = new TwitterData() {
				Handle = m_Source.SourceID,
				Tweets = new List<string>(m_Source.DataLines),
				LatestTweet = m_Source.LatestTweet,
			};
			return JsonSerializer.Serialize(data);
		}

		private class TwitterData {
			public string Handle { get; set; }
			public List<string> Tweets { get; set; }
			public Tuple<DateTime, long> LatestTweet { get; set; }
		}
	}
}
