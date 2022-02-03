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
		public override DataSourceTypes SourceType => DataSourceTypes.TWITTER_SCRAPER;
		public override IDataSourceSerializer Serializer { get; set; }
		public override string FileName => "Twitter_Scraped.json";
		private string TweetLinkFormat => String.Format(@"https://twitter.com/{0}/status/", SourceID);

		private int m_ScraperCount = 8;
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
					string baseUrl = @"https://twitter.com/search?q=from%3A{0}%20since%3A{1}%20until%3A{2}&f=live";

					DateTimeOffset startRangeHigh;
					DateTimeOffset startRangeLow;
					datetimeProvider.GetNextDateTime(out startRangeHigh, out startRangeLow);
					while (startRangeHigh > datetimeProvider.SinceTime) {
						string currentUrl = string.Format(baseUrl, SourceID, startRangeLow.ToString("yyyy-MM-dd"), startRangeHigh.ToString("yyyy-MM-dd"));

						await page.GoToAsync(currentUrl);

						string lastTweet = "";
						string tweetsFilter = $@"Array.from(document.querySelectorAll('a')).map(a => a.href).filter(a => a.startsWith('{TweetLinkFormat}')).filter(a => a.includes('/photo/') == false);";
						int retryCount = 10;

						while (true) {
							string[] urls = new string[0];
							for (int i = 0; i < retryCount; i++) {
								urls = await page.EvaluateExpressionAsync<string[]>(tweetsFilter);
								if (urls.Length != 0 && urls[urls.Length - 1] != lastTweet) break;
								await Task.Delay(1000);
							}

							if (urls.Length == 0) {
								// Next datetime, no tweets on this day
								await page.ScreenshotAsync($"Resources/{startRangeLow:yyyy-MM-dd} to {startRangeHigh:yyyy-MM-dd}.png");
								break;
							} else {
								if (urls[urls.Length - 1] == lastTweet) {
									// Last tweet has been reached
									await page.ScreenshotAsync($"Resources/{startRangeLow:yyyy-MM-dd} to {startRangeHigh:yyyy-MM-dd}.png");
									break;
								}
								lastTweet = urls[urls.Length - 1];

								foreach (string tweetUrl in urls) {
									string tweetIdString = tweetUrl.Split('/')[5];
									if (tweetIdString.Contains("?")) {
										tweetIdString = tweetIdString.Split('?')[0];
									}
									long tweetId;
									if (long.TryParse(tweetIdString, out tweetId) && m_TweetsFound.Add(tweetId)) {
										m_TweetsToFetch.Add(tweetId);
									}
								}
								await page.EvaluateExpressionAsync("window.scrollBy(0, 5000)");
							}
						}

						if (datetimeProvider.HasNextDate()) {
							datetimeProvider.GetNextDateTime(out startRangeHigh, out startRangeLow);
						} else {
							break;
						}
					}
				}
				await browser.CloseAsync();
			}
		}

		private void ScrapedTweetFetcherProcess() {
			List<long> batch = new List<long>();
			int retryCount = 3;
			foreach (long tweetId in m_TweetsToFetch.GetConsumingEnumerable()) {
				batch.Add(tweetId);
				if (batch.Count == 100) {
					for (int i = 0; i < retryCount; i++) {
						try {
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
						} catch (Exception) { }
					}
					batch.Clear();
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
			tweet = tweet.Replace(" .", ".");
			tweet = tweet.Trim();
			return tweet;
		}

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
