using CoreTweet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Threading;

namespace Parroteer.DataSources {
	public class DataSourceTwitter : DataSourceBase {

		public override DataSourceTypes SourceType => DataSourceTypes.TWITTER_API;
		public override IDataSourceSerializer Serializer { get; set; }
		public override string FileName => "Twitter_API.json";

		public Tuple<DateTime, long> LatestTweet { get; set; }

		private OAuth2Token m_Session;
		private ConcurrentDictionary<long, string> m_Tweets = new ConcurrentDictionary<long, string>();
		private SynchronizationContext m_SyncContext;

		public DataSourceTwitter(string twitterHandle) {
			Serializer = new DataSourceTwitterSerializer(this);

			m_Session = OAuth2.GetToken(ConfigurationManager.AppSettings.Get("TwitterConsumerKey"), ConfigurationManager.AppSettings.Get("TwitterConsumerSecret"));
			SourceID = twitterHandle;

			m_SyncContext = SynchronizationContext.Current;
		}

		public override void GetData() {
			base.GetData();

			Thread dataThread = new Thread(GetDataProcess);
			dataThread.IsBackground = true;
			dataThread.Name = "Twitter fetch thread";
			dataThread.Start();
		}

		private void GetDataProcess() {
			long lastTweet = 0;

			CoreTweet.Core.ListedResponse<Status> tweets;
			Dictionary<string, object> requestParams = new Dictionary<string, object>() {
				["screen_name"] = SourceID,
				["count"] = 3200,
				["include_ext_alt_text"] = false,
				["include_rts"] = false,
				["tweet_mode"] = TweetMode.Extended,
			};

			bool shouldContinue = true;
			while (shouldContinue) {
				tweets = m_Session.Statuses.UserTimeline(requestParams);

				if (tweets.Count <= 1) {
					break;
				}

				foreach (Status status in tweets) {
					if(status.Id != lastTweet && !m_Tweets.TryAdd(status.Id, SanitizeTweetText(status.FullText))) {
						shouldContinue = false;
						break;
					}
					lastTweet = status.Id;
					requestParams["max_id"] = status.Id.ToString();

					UpdateTweetTimes(status);
				}

				if(tweets.RateLimit.Remaining == 0) {
					break;
				}

				tweets = m_Session.Statuses.UserTimeline(SourceID, max_id: lastTweet);
			}

			m_SyncContext.Send((_) => OnDataReceived(), null);
		}

		private void UpdateTweetTimes(Status tweet) {
			bool replaceLatestTweet = false;

			if (LatestTweet == null || tweet.CreatedAt.UtcDateTime > LatestTweet.Item1) {
				replaceLatestTweet = true;
			}

			if (replaceLatestTweet) {
				LatestTweet = Tuple.Create(tweet.CreatedAt.UtcDateTime, tweet.Id);
			}
		}

		private string SanitizeTweetText(string text) {
			return Regex.Replace(text, @"http[^\s]+", "");
		}

		public override void OnDataReceived() {
			DataLines = new ObservableCollection<string>(m_Tweets.Values);

			base.OnDataReceived();
		}
	}
}
