using CoreTweet;
using Microsoft.Extensions.Logging;
using Parroteer.Utilities;
using PuppeteerSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Parroteer.DataSources {
	public class DataSourceTwitterScrape : DataSourceBase {
		public const string TwitterSearchUrlTemplate = @"https://twitter.com/search?q=from%3A{0}%20since%3A{1}%20until%3A{2}&f=live";
		public const string GetTweetLinksFunctionTemplate = @"Array.from(document.querySelectorAll('a')).map(a => a.href).filter(a => a.startsWith('{0}')).filter(a => a.includes('/photo/') == false);";
		public const int PageRetryCount = 10;

		public override DataSourceTypes SourceType => DataSourceTypes.TWITTER_SCRAPER;
		public override IDataSourceSerializer Serializer { get; set; }
		public override string FileName => "Twitter_Scraped.json";
		private string TweetLinkFormat => string.Format(@"https://twitter.com/{0}/status/", SourceID);
		private string TweetLinksFunction => string.Format(GetTweetLinksFunctionTemplate, TweetLinkFormat);

		private int m_ScraperCount = 5;
		public int ScraperCount {
			get => m_ScraperCount;
			set => SetProperty(ref m_ScraperCount, value);
		}

		private SynchronizationContext m_SyncContext;

		public SimpleCommand GetDataCommand { get; set; }

		private OAuth2Token m_Session;
		private BlockingCollection<long> m_TweetsToFetch = new BlockingCollection<long>();
		private HashSet<long> m_TweetsFound = new HashSet<long>();

		private string m_FetchStatus = "Ready.";
		public string FetchStatus {
			get => m_FetchStatus;
			set => SetProperty(ref m_FetchStatus, value);
		}

		public DataSourceTwitterScrape(string twitterHandle) {
			Serializer = new DataSourceTwitterScrapeSerializer(this);

			SourceID = twitterHandle;

			m_SyncContext = SynchronizationContext.Current;

			GetDataCommand = new SimpleCommand((o) => GetData());
		}

		public override void GetData() {
			base.GetData();

			DataLines.Clear();

			m_Session = OAuth2.GetToken(ConfigurationManager.AppSettings.Get("TwitterConsumerKey"), ConfigurationManager.AppSettings.Get("TwitterConsumerSecret"));
			UserResponse user = m_Session.Users.Show(SourceID);
			DateTimeOffset since = user.CreatedAt;
			DateTimeOffset until = DateTimeOffset.UtcNow;

			GetDataProcess(since, until);
		}

		private void GetDataProcess(DateTimeOffset since, DateTimeOffset until) {
			TwitterScrapeDateProvider dateProvider = new TwitterScrapeDateProvider(since, until.AddDays(1));
			dateProvider.OnNextDateTimeRequested += (o, e) => {
				DataFetchProgress = dateProvider.CurrentProgress;
				FetchStatus = dateProvider.CurrentProgressText;
			};

			for (int i = 0; i < ScraperCount; i++) {
				Task.Run(async () => {
					await ScrapeUser(dateProvider);
					OnDataReceived();
				});
			}

			Thread dataThread = new Thread(ScrapedTweetFetcherProcess);
			dataThread.IsBackground = true;
			dataThread.Name = "Twitter fetch thread";
			dataThread.Start();
		}

		private async Task ScrapeUser(TwitterScrapeDateProvider datetimeProvider) {
			await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
			using (Browser browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true })) {
				using (Page page = await browser.NewPageAsync()) {
					await ScrapeUserTask(page, datetimeProvider);
				}
				await browser.CloseAsync();
			}
		}

		private async Task ScrapeUserTask(Page page, TwitterScrapeDateProvider datetimeProvider) {
			DateTimeOffset startRangeHigh;
			DateTimeOffset startRangeLow;
			datetimeProvider.GetNextDateTime(out startRangeHigh, out startRangeLow);
			while (startRangeHigh > datetimeProvider.SinceTime) {
				string currentUrl = string.Format(TwitterSearchUrlTemplate, SourceID, startRangeLow.ToString("yyyy-MM-dd"), startRangeHigh.ToString("yyyy-MM-dd"));

				await page.GoToAsync(currentUrl);

				string lastTweet = "";

				while (true) {
					string[] urls = new string[0];
					for (int i = 0; i < PageRetryCount; i++) {
						urls = await page.EvaluateExpressionAsync<string[]>(TweetLinksFunction);
						if (urls.Length != 0 && urls[urls.Length - 1] != lastTweet) break;
						await page.EvaluateExpressionAsync("window.scrollBy(0, 1000)");
						await Task.Delay(1000);
					}

					if (urls.Length == 0) {
						// Next datetime, no tweets on this day
						break;
					} else {
						if (urls[urls.Length - 1] == lastTweet) {
							// Last tweet has been reached
							break;
						}
						lastTweet = urls[urls.Length - 1];

						foreach (string tweetUrl in urls) {
							ProcessTweetUrl(tweetUrl);
						}
					}
				}

				if (datetimeProvider.HasNextDate()) {
					datetimeProvider.GetNextDateTime(out startRangeHigh, out startRangeLow);
				} else {
					break;
				}
			}
		}

		private void ProcessTweetUrl(string tweetUrl) {
			string tweetIdString = tweetUrl.Split('/')[5];
			if (tweetIdString.Contains("?")) {
				tweetIdString = tweetIdString.Split('?')[0];
			}
			long tweetId;
			if (long.TryParse(tweetIdString, out tweetId) && m_TweetsFound.Add(tweetId)) {
				m_TweetsToFetch.Add(tweetId);
			}
		}

		private void ScrapedTweetFetcherProcess() {
			List<long> batch = new List<long>();
			int retryCount = 3;
			foreach (long tweetId in m_TweetsToFetch.GetConsumingEnumerable()) {
				batch.Add(tweetId);
				if (batch.Count != 100) {
					continue;
				}

				for (int i = 0; i < retryCount; i++) {
					try { // In case Twitter API service errors
						SendBatchToTwitterApi(batch);
					} catch (Exception) { }
				}
				batch.Clear();
			}
		}

		private void SendBatchToTwitterApi(List<long> batch) {
			CoreTweet.Core.DictionaryResponse<string, Status> result = m_Session.Statuses.LookupMap(batch, trim_user: true, include_entities: false, include_ext_alt_text: false, tweet_mode: TweetMode.Extended);
			foreach (Status tweet in result.Values) {
				if (tweet != null) {
					string sanitizedTweet = SanitizeTweet(tweet.FullText);
					if (string.IsNullOrEmpty(sanitizedTweet)) continue;

					m_SyncContext.Send((_) => {
						DataLines.Add(sanitizedTweet);
					}, null);
				}
			}
		}

		public override void OnDataReceived() {
			base.OnDataReceived();
		}

		private string SanitizeTweet(string tweet) {
			tweet = Regex.Replace(tweet, @"http[^\s]+", "");        // Hyperlinks
			tweet = Regex.Replace(tweet, @"[@]\w+ ", "");           // @ handles
			tweet = Regex.Replace(tweet, @"[^\u0000-\u007F]+", ""); // Emojis
			tweet = tweet.Replace(" .", ".");						// Spaces left by emojis (maybe)
			tweet = tweet.Trim();
			return tweet;
		}

		/// <summary>
		/// Provides a class from which multiple scrapers can get the next datetime to scrape
		/// </summary>
		private class TwitterScrapeDateProvider {
			public event EventHandler OnNextDateTimeRequested;

			public float CurrentProgress { get; set; }
			public string CurrentProgressText {
				get {
					float totalDays = (float)(UntilTime - SinceTime).TotalDays;
					CurrentProgress = (totalDays - (float)(StartRangeLow - SinceTime).TotalDays) / totalDays;
					return $"Fetching {StartRangeLow:dd/MM/yyyy} to {StartRangeLow:dd/MM/yyyy}... Total: {(int)(CurrentProgress * 100)}%";
				}
			}

			public DateTimeOffset SinceTime { get; set; }
			public DateTimeOffset UntilTime { get; set; }
			public DateTimeOffset StartRangeHigh { get; set; }
			public DateTimeOffset StartRangeLow { get; set; }

			public TwitterScrapeDateProvider(DateTimeOffset since, DateTimeOffset until) {
				SinceTime = since;
				UntilTime = until.AddDays(1);
				StartRangeHigh = new DateTimeOffset(UntilTime.UtcDateTime);
			}

			public bool HasNextDate() {
				return StartRangeHigh > SinceTime;
			}

			public void GetNextDateTime(out DateTimeOffset high, out DateTimeOffset low) {
				StartRangeHigh = StartRangeHigh.AddDays(-1);
				StartRangeLow = StartRangeHigh.AddDays(-1);
				high = StartRangeHigh;
				low = StartRangeLow;

				OnNextDateTimeRequested?.Invoke(this, new EventArgs());
			}
		}
	}
}
