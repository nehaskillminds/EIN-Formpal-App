using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace EinAutomation.Api.TestClasses
{
	public class SamplePdfDownloadTest
	{
		private readonly ILogger<SamplePdfDownloadTest> _logger;
		private ChromeDriver? _driver;
		private WebDriverWait? _wait;
		private string? _downloadDir;

		private const string PageUrl = "https://file-examples.com/index.php/sample-documents-download/sample-pdf-download/";
		private const string DirectPdfUrl = "https://file-examples.com/wp-content/storage/2017/10/file-example_PDF_1MB.pdf";

		public SamplePdfDownloadTest(ILogger<SamplePdfDownloadTest> logger)
		{
			_logger = logger;
		}

		public async Task RunAllAsync(CancellationToken cancellationToken = default)
		{
			PrepareTempDownloadsDirectory();
			InitializeLocalDriverWithTempDownloads();

			try
			{
				// Navigate to page
				_driver!.Navigate().GoToUrl(PageUrl);
				_logger.LogInformation("Navigated to page: {Url}", PageUrl);

				// Configure CDP download behavior to target our temp directory
				await ConfigureChromeDownloadBehaviorAsync(_driver, _downloadDir!);

				// Method A: Click the specific anchor and wait 10s
				await ClickSpecificDownloadLinkAsync(cancellationToken);
				await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
				_logger.LogInformation("Waited 10 seconds after clicking download link");

				var resultA = AwaitDownloadCompletion(_downloadDir!, TimeSpan.FromSeconds(30));
				LogResult("ClickLink+Wait10s", resultA);

				// Method B: Directly navigate to PDF URL
				_driver.Navigate().GoToUrl(DirectPdfUrl);
				await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
				var resultB = AwaitDownloadCompletion(_downloadDir!, TimeSpan.FromSeconds(30));
				LogResult("NavigateToPdfUrl", resultB);

				// Method C: JavaScript click (fallback)
				await JavaScriptClickSpecificLinkAsync(cancellationToken);
				await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
				var resultC = AwaitDownloadCompletion(_downloadDir!, TimeSpan.FromSeconds(30));
				LogResult("JsClickLink+Wait10s", resultC);

				// Method D: Direct HTTP download using HttpClient
				var resultD = await DirectHttpDownloadAsync(DirectPdfUrl, _downloadDir!, cancellationToken);
				LogResult("DirectHttpDownload", resultD);

				// Method E: Aggressive scan (fallback)
				var resultE = AggressiveScanForPdf(_downloadDir!);
				LogResult("AggressiveScan", resultE);
			}
			finally
			{
				try { _driver?.Quit(); } catch { /* ignore */ }
			}
		}

		private void PrepareTempDownloadsDirectory()
		{
			var baseDir = Path.Combine(Path.GetTempPath(), "sample_pdf_downloads");
			_downloadDir = Path.Combine(baseDir, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
			Directory.CreateDirectory(_downloadDir);
			_logger.LogInformation("Temp downloads directory: {Dir}", _downloadDir);
		}

		private void InitializeLocalDriverWithTempDownloads()
		{
			var options = new ChromeOptions();
			// Do NOT run headless as requested
			// options.AddArgument("--headless=new");
			options.AddArgument("--disable-gpu");
			options.AddArgument("--no-sandbox");
			options.AddArgument("--disable-dev-shm-usage");
			options.AddArgument("--disable-blink-features=AutomationControlled");
			options.AddArgument("--disable-infobars");
			options.AddArgument("--window-size=1280,900");
			options.AddArgument("--start-maximized");

			// Route downloads into our temp folder
			options.AddUserProfilePreference("download.default_directory", _downloadDir);
			options.AddUserProfilePreference("download.prompt_for_download", false);
			options.AddUserProfilePreference("download.directory_upgrade", true);
			options.AddUserProfilePreference("safebrowsing.enabled", true);
			options.AddUserProfilePreference("plugins.always_open_pdf_externally", true);
			options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 1);

			var service = ChromeDriverService.CreateDefaultService();
			service.EnableVerboseLogging = false;
			service.SuppressInitialDiagnosticInformation = true;

			_driver = new ChromeDriver(service, options);
			_wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
		}

		private async Task ConfigureChromeDownloadBehaviorAsync(ChromeDriver driver, string downloadDir)
		{
			try
			{
				var chromeDriverType = driver.GetType();
				var execMethod = chromeDriverType.GetMethod("ExecuteChromeCommand", new[] { typeof(string), typeof(Dictionary<string, object>) });
				if (execMethod != null)
				{
					var parameters = new Dictionary<string, object>
					{
						["behavior"] = "allow",
						["downloadPath"] = downloadDir
					};
					execMethod.Invoke(driver, new object[] { "Page.setDownloadBehavior", parameters });
					_logger.LogInformation("Configured CDP download behavior to {Dir}", downloadDir);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning("Failed to configure CDP download behavior: {Message}", ex.Message);
			}

			await Task.CompletedTask;
		}

		private async Task ClickSpecificDownloadLinkAsync(CancellationToken cancellationToken)
		{
			try
			{
				var link = _driver?.FindElements(By.CssSelector("a.download-button"))
					?.FirstOrDefault(a => a.GetAttribute("href") == DirectPdfUrl);
				if (link == null)
				{
					// Fallback by href contains
					link = _driver?.FindElements(By.CssSelector("a[href]"))
						?.FirstOrDefault(a => (a.GetAttribute("href") ?? string.Empty).Contains("file-example_PDF_1MB.pdf", StringComparison.OrdinalIgnoreCase));
				}

				if (link != null)
				{
					((IJavaScriptExecutor)_driver!).ExecuteScript("arguments[0].scrollIntoView({block:'center'});", link);
					await Task.Delay(500, cancellationToken);
					link.Click();
					_logger.LogInformation("Clicked sample PDF download link");
				}
				else
				{
					_logger.LogWarning("Download link not found on page");
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning("ClickSpecificDownloadLinkAsync failed: {Message}", ex.Message);
			}
		}

		private async Task JavaScriptClickSpecificLinkAsync(CancellationToken cancellationToken)
		{
			try
			{
				var script = @"
					var anchors = document.querySelectorAll('a[href]');
					for (var i=0;i<anchors.length;i++) {
						var href = anchors[i].getAttribute('href') || '';
						if (href.includes('file-example_PDF_1MB.pdf')) {
							anchors[i].click();
							return true;
						}
					}
					return false;";
				var res = ((IJavaScriptExecutor)_driver!).ExecuteScript(script);
				_logger.LogInformation("JS click result: {Result}", res);
			}
			catch (Exception ex)
			{
				_logger.LogWarning("JavaScriptClickSpecificLinkAsync failed: {Message}", ex.Message);
			}
			await Task.CompletedTask;
		}

		private (bool Success, string? FilePath, long SizeBytes) AwaitDownloadCompletion(string dir, TimeSpan timeout)
		{
			var end = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < end)
			{
				try
				{
					var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly);
					var partial = Directory.GetFiles(dir, "*.crdownload", SearchOption.TopDirectoryOnly);
					if (pdfs.Length > 0 && partial.Length == 0)
					{
						var file = pdfs
							.Select(p => new FileInfo(p))
							.OrderByDescending(f => f.CreationTimeUtc)
							.First();
						if (file.Length > 0)
						{
							return (true, file.FullName, file.Length);
						}
					}
				}
				catch { /* ignore transient IO */ }

				Thread.Sleep(1000);
			}
			return (false, null, 0);
		}

		private async Task<(bool Success, string? FilePath, long SizeBytes)> DirectHttpDownloadAsync(string url, string dir, CancellationToken cancellationToken)
		{
			try
			{
				using var http = new HttpClient();
				var bytes = await http.GetByteArrayAsync(url, cancellationToken);
				if (bytes != null && bytes.Length > 0)
				{
					var filePath = Path.Combine(dir, $"direct_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.pdf");
					await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
					return (true, filePath, bytes.LongLength);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning("DirectHttpDownloadAsync failed: {Message}", ex.Message);
			}
			return (false, null, 0);
		}

		private (bool Success, string? FilePath, long SizeBytes) AggressiveScanForPdf(string dir)
		{
			try
			{
				var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories)
					.Select(p => new FileInfo(p))
					.Where(f => f.Length > 0)
					.OrderByDescending(f => f.CreationTimeUtc)
					.ToList();
				if (pdfs.Count > 0)
				{
					var f = pdfs.First();
					return (true, f.FullName, f.Length);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning("AggressiveScanForPdf failed: {Message}", ex.Message);
			}
			return (false, null, 0);
		}

		private void LogResult(string methodName, (bool Success, string? FilePath, long SizeBytes) result)
		{
			if (result.Success)
			{
				_logger.LogInformation("{Method}: SUCCESS - {File} ({Size} bytes)", methodName, result.FilePath, result.SizeBytes);
			}
			else
			{
				_logger.LogWarning("{Method}: FAILED", methodName);
			}
		}
	}
}


