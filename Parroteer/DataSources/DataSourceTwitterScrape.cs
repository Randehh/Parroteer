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
		private string TwitterSpinnerDetectFunction => @"Array.from(document.querySelectorAll('div')).map(div => div.className).filter(div => div.includes('r-17bb2tj'));";
		private string TwitterNoTweetsFunction => @"Array.from(document.querySelectorAll('img')).map(img => img.src).filter(div => div.includes('rubber-chicken'));";

		private TwitterScrapeDateProvider m_DateProvider;

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
			if (m_DateProvider != null) {
				return;
			}

			m_DateProvider = new TwitterScrapeDateProvider(since, until.AddDays(1));
			m_DateProvider.OnNextDateTimeRequested += (o, e) => {
				DataFetchProgress = m_DateProvider.CurrentProgress;
				FetchStatus = m_DateProvider.CurrentProgressText;
			};

			for (int i = 0; i < ScraperCount; i++) {
				Task.Run(async () => {
					await ScrapeUser(m_DateProvider);
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

			LaunchOptions launchOptions = new LaunchOptions {
				Headless = true,
				Args = new string[] { "--disable-dev-shm-usage" },
			};
			launchOptions.Env.Add("CONNECTION_TIMEOUT", "1000");
			using (Browser browser = await Puppeteer.LaunchAsync(launchOptions)) {
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
				await Task.Delay(2500);

				string lastTweet = "";

				while (true) {
					int previousUrlCount = 0;
					string[] urls = new string[0];
					for (int i = 0; i < PageRetryCount; i++) {
						urls = await page.EvaluateExpressionAsync<string[]>(TweetLinksFunction);
						if (urls.Length == previousUrlCount) break;
						previousUrlCount = urls.Length;
						await page.EvaluateExpressionAsync("window.scrollBy(0, 100000)");
						await Task.Delay(500);

						while (true) {
							string[] spinnerElements = await page.EvaluateExpressionAsync<string[]>(TwitterSpinnerDetectFunction);
							if(spinnerElements.Length == 0) {
								break;
                            }
							await Task.Delay(250);
                        }
					}

					if (urls.Length == 0) {
						string[] noTweetsImages = await page.EvaluateExpressionAsync<string[]>(TwitterNoTweetsFunction);
						if (noTweetsImages.Length == 0) {
							// No tweets chicken not found, retry later
							datetimeProvider.AddTimesToRetry(startRangeHigh, startRangeLow);
						}
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

		private readonly object m_TweetsFoundLock = new object();
		private void ProcessTweetUrl(string tweetUrl) {
			string tweetIdString = tweetUrl.Split('/')[5];
			if (tweetIdString.Contains("?")) {
				tweetIdString = tweetIdString.Split('?')[0];
			}

			lock (m_TweetsFoundLock) {
				long tweetId;
				if (long.TryParse(tweetIdString, out tweetId) && m_TweetsFound.Add(tweetId)) {
					m_TweetsToFetch.Add(tweetId);
				}
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

			private Queue<Tuple<DateTimeOffset, DateTimeOffset>> m_TimesToRetry = new Queue<Tuple<DateTimeOffset, DateTimeOffset>>();
			private bool m_IsRetrying = false;
			private int m_TimesToRetryInitialCount = 0;

			public event EventHandler OnNextDateTimeRequested;

			public float CurrentProgress { get; set; }
			public string CurrentProgressText {
				get
				{
					if (!m_IsRetrying) {
						float totalDays = (float)(UntilTime - SinceTime).TotalDays;
						CurrentProgress = (totalDays - (float)(StartRangeLow - SinceTime).TotalDays) / totalDays;
						return $"Fetching {StartRangeLow:dd/MM/yyyy} to {StartRangeLow:dd/MM/yyyy}... Total: {(int)(CurrentProgress * 100)}%, pages to retry: {m_TimesToRetry.Count}";
                    } else {
						return $"Retrying times: {m_TimesToRetry.Count}/{m_TimesToRetryInitialCount}...";
                    }
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
				if(StartRangeHigh > SinceTime) {
					return true;
                }

				if (m_TimesToRetry.Count > 0) {
					if (!m_IsRetrying) {
						m_IsRetrying = true;
						m_TimesToRetryInitialCount = m_TimesToRetry.Count;
					}
					return true;
				}

				return true;
			}

			public void GetNextDateTime(out DateTimeOffset high, out DateTimeOffset low) {
				if (!m_IsRetrying) {
					StartRangeHigh = StartRangeHigh.AddDays(-1);
					StartRangeLow = StartRangeHigh.AddDays(-1);
					high = StartRangeHigh;
					low = StartRangeLow;
                } else {
					if (m_TimesToRetry.Count == 0) {
						throw new Exception("No times to retry exist");
					}

					Tuple<DateTimeOffset, DateTimeOffset> tuple = m_TimesToRetry.Dequeue();
					high = tuple.Item1;
					low = tuple.Item2;
                }

				OnNextDateTimeRequested?.Invoke(this, new EventArgs());
			}

			public void AddTimesToRetry(DateTimeOffset high, DateTimeOffset low) {
                if (m_IsRetrying) {
					return;
                }
				m_TimesToRetry.Enqueue(Tuple.Create(high, low));
            }
		}
	}
}
