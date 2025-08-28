using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace EinAutomation.Api.Controllers
{
	[ApiController]
	[Route("api/test/[controller]")]
	public class PdfDownloadTestController : ControllerBase
	{
		private readonly ILogger<PdfDownloadTestController> _logger;
		private ChromeDriver? Driver;
		private WebDriverWait? Wait;
		private readonly int Timeout = 30;
		private string? DownloadDirectory;

		public PdfDownloadTestController(ILogger<PdfDownloadTestController> logger)
		{
			_logger = logger;
		}

		[HttpPost("run")]
		public async Task<IActionResult> Run([FromBody] PdfDownloadTestRequest request)
		{
			try
			{
				if (request == null || string.IsNullOrWhiteSpace(request.Url))
				{
					return BadRequest("Url is required");
				}

				if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
				{
					return BadRequest("Url must be a valid absolute HTTP/HTTPS URL");
				}

				_logger.LogInformation("Starting PDF download test for {Url}", request.Url);
				var results = new List<TestMethodResult>();

				// Try to initialize WebDriver for both tests
				try
				{
					await InitializeDriver();
					
					// Test base64 extraction via button click
					results.Add(await TestBase64PdfExtraction(request));
					
					// Test clicking the specific download button
					results.Add(await TestDownloadButtonClick(request));
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "ChromeDriver initialization failed, skipping both tests");
					results.Add(Failure("Base64 PDF Extraction", request.Url, $"ChromeDriver not available: {ex.Message}"));
					results.Add(Failure("Download Button Click", request.Url, $"ChromeDriver not available: {ex.Message}"));
				}
				finally
				{
					CleanupDriver();
				}

				var summary = BuildSummary(results);
				return Ok(new
				{
					Success = summary.SuccessCount > 0,
					Summary = $"{summary.SuccessCount}/{summary.TotalCount} strategies succeeded ({summary.SuccessRate:F1}%)",
					BestResult = summary.BestResult == null ? null : new
					{
						summary.BestResult.Method,
						summary.BestResult.Url,
						summary.BestResult.FileSize
					},
					Results = results.Select(r => new
					{
						r.Method,
						r.Success,
						r.Details,
						r.FileSize,
						r.Url,
						r.ActualDownloadUrl
					})
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "PDF download test failed");
				CleanupDriver();
				return StatusCode(500, $"Test run failed: {ex.Message}");
			}
		}

		[HttpPost("test-download-config")]
		public async Task<IActionResult> TestDownloadConfiguration()
		{
			try
			{
				await InitializeDriver();
				
				var testInfo = new Dictionary<string, object>
				{
					["DownloadDirectory"] = DownloadDirectory ?? "null",
					["DownloadDirectoryExists"] = Directory.Exists(DownloadDirectory),
					["DownloadDirectoryWritable"] = await TestDirectoryWriteAccess(DownloadDirectory!),
					["ChromePreferences"] = await GetChromeDownloadPreferences(),
					["CurrentUrl"] = Driver?.Url ?? "null",
					["PageTitle"] = Driver?.Title ?? "null"
				};
				
				// Test a simple download
				try
				{
					Driver?.Navigate().GoToUrl("https://www.google.com");
					await Task.Delay(2000);
					
					// Try to trigger a download via JavaScript
					var script = @"
						var link = document.createElement('a');
						link.href = 'data:text/plain;charset=utf-8,Hello World';
						link.download = 'test.txt';
						document.body.appendChild(link);
						link.click();
						document.body.removeChild(link);
						return 'Download link created and clicked';
					";

					var jsExecutor = Driver as IJavaScriptExecutor;
					var result = jsExecutor!.ExecuteScript(script);

					await Task.Delay(5000); // Wait for download
					
					var filesAfterDownload = Directory.GetFiles(DownloadDirectory!);
					
					testInfo["JavaScriptResult"] = result?.ToString() ?? "null";
					testInfo["FilesAfterDownload"] = filesAfterDownload.Select(Path.GetFileName).ToArray();
				}
				catch (Exception ex)
				{
					testInfo["JavaScriptError"] = ex.Message;
				}
				
				CleanupDriver();
				return Ok(testInfo);
			}
			catch (Exception ex)
			{
				CleanupDriver();
				return StatusCode(500, $"Test failed: {ex.Message}");
			}
		}
		
		[HttpGet("debug-download")]
		public IActionResult DebugDownload()
		{
			try
			{
				var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
				var chromeHome = isWindows ? Path.Combine(Path.GetTempPath(), "chrome-home") : "/tmp/chrome-home";
				var chromeDownloads = Path.Combine(chromeHome, "Downloads");
				
				var debugInfo = new Dictionary<string, object>
				{
					["OS"] = isWindows ? "Windows" : "Linux",
					["ChromeHome"] = chromeHome,
					["ChromeDownloads"] = chromeDownloads,
					["ChromeHomeExists"] = Directory.Exists(chromeHome),
					["ChromeDownloadsExists"] = Directory.Exists(chromeDownloads),
					["TempPath"] = Path.GetTempPath(),
					["CurrentDirectory"] = Directory.GetCurrentDirectory()
				};
				
				// Check if directories exist and are writable
				if (Directory.Exists(chromeDownloads))
				{
					var files = Directory.GetFiles(chromeDownloads);
					debugInfo["FilesInDownloadDirectory"] = files.Select(Path.GetFileName).ToArray();
					debugInfo["FileCount"] = files.Length;
				}
				else
				{
					debugInfo["FilesInDownloadDirectory"] = new string[0];
					debugInfo["FileCount"] = 0;
				}
				
				return Ok(debugInfo);
			}
			catch (Exception ex)
			{
				return StatusCode(500, $"Debug failed: {ex.Message}");
			}
		}
		
		private async Task<bool> TestDirectoryWriteAccess(string directory)
		{
			try
			{
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				
				var testFile = Path.Combine(directory, $"test_write_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt");				
				await System.IO.File.WriteAllTextAsync(testFile, "test");				
				var exists = System.IO.File.Exists(testFile);
				System.IO.File.Delete(testFile);
				return exists;
			}
			catch
			{
				return false;
			}
		}
		
		private Task<object> GetChromeDownloadPreferences()
		{
			try
			{
				var script = @"
					return {
						downloadDefaultDirectory: window.chrome?.downloads?.defaultDirectory || 'Not available',
						downloadPromptForDownload: window.chrome?.downloads?.promptForDownload || 'Not available'
					};
				";

				var result = Driver!.ExecuteScript(script);
				return Task.FromResult(result ?? "JavaScript execution failed");
			}
			catch (Exception ex)
			{
				return Task.FromResult<object>($"Error: {ex.Message}");
			}
		}

		[NonAction]
		public async Task InitializeDriver()
		{
			try
			{
				LogSystemResources();

				// Determine if we're on Windows or Linux
				var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

				string chromeHome, chromeDownloads, chromeUserData;

				if (isWindows)
				{
					// Windows paths - use temp directory with chrome-home structure
					chromeHome = Path.Combine(Path.GetTempPath(), "chrome-home");
					chromeDownloads = Path.Combine(chromeHome, "Downloads");
					chromeUserData = Path.Combine(Path.GetTempPath(), $"chrome-{Guid.NewGuid()}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
				}
				else
				{
					// Linux paths - use /tmp with chrome-home structure
					chromeHome = "/tmp/chrome-home";
					chromeDownloads = "/tmp/chrome-home/Downloads";
					chromeUserData = $"/tmp/chrome-{Guid.NewGuid()}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
				}

				// Create directories - same pattern as EinFormFiller.cs
				Directory.CreateDirectory(chromeHome);
				Directory.CreateDirectory(chromeDownloads);
				Directory.CreateDirectory(chromeUserData);

				DownloadDirectory = chromeDownloads;

				var options = new ChromeOptions();

				// Set binary location based on OS
				if (isWindows)
				{
					// Try to find Chrome on Windows
					var chromePaths = new[]
					{
						@"C:\Program Files\Google\Chrome\Application\chrome.exe",
						@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
						@"C:\Users\{0}\AppData\Local\Google\Chrome\Application\chrome.exe".Replace("{0}", Environment.UserName)
					};

					var chromePath = chromePaths.FirstOrDefault(System.IO.File.Exists);
					if (!string.IsNullOrEmpty(chromePath))
					{
						options.BinaryLocation = chromePath;
					}
				}
				else
				{
					// Linux Chrome binary
					options.BinaryLocation = "/usr/bin/chromium";
				}

				options.AcceptInsecureCertificates = true;

				// Chromium runtime arguments - same as EinFormFiller.cs
				options.AddArgument($"--user-data-dir={chromeUserData}");
				// Remove headless for visible browser as requested
				// options.AddArgument("--headless=new"); 
				options.AddArgument("--no-sandbox");
				options.AddArgument("--disable-setuid-sandbox");
				options.AddArgument("--disable-dev-shm-usage");
				options.AddArgument("--disable-gpu");
				options.AddArgument("--disable-software-rasterizer");
				options.AddArgument("--disable-infobars");
				options.AddArgument("--disable-blink-features=AutomationControlled");
				options.AddArgument("--disable-extensions");
				options.AddArgument("--no-first-run");
				options.AddArgument("--no-default-browser-check");
				options.AddArgument("--disable-background-networking");
				options.AddArgument("--disable-sync");
				options.AddArgument("--disable-default-apps");
				options.AddArgument("--disable-translate");
				options.AddArgument("--window-size=1920,1080");
				options.AddArgument("--remote-debugging-port=9222");
				options.AddArgument("--remote-debugging-address=0.0.0.0");
				options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

				// Route downloads into the dedicated HOME/Downloads directory - same as EinFormFiller.cs
				options.AddUserProfilePreference("download.default_directory", chromeDownloads);
				options.AddUserProfilePreference("download.prompt_for_download", false);
				options.AddUserProfilePreference("download.directory_upgrade", true);
				options.AddUserProfilePreference("safebrowsing.enabled", true);
				options.AddUserProfilePreference("plugins.always_open_pdf_externally", true);
				options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 1);

				// Enhanced debugging: Log Chrome preferences
				_logger.LogInformation("Chrome download preferences configured:");
				_logger.LogInformation("  - download.default_directory: {DownloadDir}", chromeDownloads);
				_logger.LogInformation("  - download.prompt_for_download: false");
				_logger.LogInformation("  - plugins.always_open_pdf_externally: true");
				_logger.LogInformation("  - profile.default_content_setting_values.automatic_downloads: 1");

				// Additional preferences to ensure downloads work
				options.AddUserProfilePreference("download.open_pdf_in_system_reader", false);
				options.AddUserProfilePreference("download.directory_upgrade", true);
				options.AddUserProfilePreference("safebrowsing.enabled", false);
				options.AddUserProfilePreference("safebrowsing.disable_download_protection", true);

				// Force Chrome to use the download directory
				options.AddArgument($"--download-directory={chromeDownloads}");
				options.AddArgument("--disable-web-security");
				options.AddArgument("--allow-running-insecure-content");

				// ChromeDriver service configuration
				ChromeDriverService driverService;

				if (isWindows)
				{
					// On Windows, try to use the default service or find chromedriver.exe
					try
					{
						driverService = ChromeDriverService.CreateDefaultService();
					}
					catch (Exception)
					{
						// If default service fails, try to find chromedriver.exe in common locations
						var chromedriverPaths = new[]
						{
							"chromedriver.exe",
							Path.Combine(Environment.CurrentDirectory, "chromedriver.exe"),
							Path.Combine(Environment.CurrentDirectory, "drivers", "chromedriver.exe"),
							@"C:\chromedriver\chromedriver.exe"
						};

						var chromedriverPath = chromedriverPaths.FirstOrDefault(path => System.IO.File.Exists(path));
						if (string.IsNullOrEmpty(chromedriverPath))
						{
							throw new InvalidOperationException("ChromeDriver not found. Please ensure chromedriver.exe is in the PATH or in the application directory.");
						}

						driverService = ChromeDriverService.CreateDefaultService(Path.GetDirectoryName(chromedriverPath), Path.GetFileName(chromedriverPath));
					}
				}
				else
				{
					// Linux ChromeDriver service - same as EinFormFiller.cs
					driverService = ChromeDriverService.CreateDefaultService(Path.GetDirectoryName("/usr/bin/"), "chromedriver");
				}

				// Set log path based on OS
				var logPath = isWindows ? Path.Combine(Path.GetTempPath(), "chromedriver.log") : "/tmp/chromedriver.log";
				driverService.LogPath = logPath;
				driverService.EnableVerboseLogging = true;
				driverService.SuppressInitialDiagnosticInformation = false;

				Driver = new ChromeDriver(driverService, options);
				Wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(Timeout));

				// Disable JS popups
				Driver.ExecuteScript(@"
					window.alert = function() { return true; };
					window.confirm = function() { return true; };
					window.prompt = function() { return null; };
					window.open = function() { return null; };
				");

				_logger.LogInformation("WebDriver initialized successfully with Chrome/Chromium");
				_logger.LogInformation("Download directory created at: {DownloadDirectory}", DownloadDirectory);

				await Task.CompletedTask;

				// Wait 15 seconds for manual captcha solving
				// _logger.LogInformation("Waiting 15 seconds for manual captcha solving...");
				// await Task.Delay(15000);
				// _logger.LogInformation("Continuing with automation after captcha wait period");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to initialize WebDriver");
				LogChromeDriverDiagnostics();
				throw;
			}
		}

		private void LogSystemResources()
		{
			try
			{
				_logger.LogInformation("System Resources Check:");
				_logger.LogInformation("Available Memory: {Memory} MB", GC.GetTotalMemory(false) / 1024 / 1024);
				_logger.LogInformation("Working Directory: {Directory}", Directory.GetCurrentDirectory());
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to log system resources");
			}
		}

		private void LogChromeDriverDiagnostics()
		{
			try
			{
				var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
				
				_logger.LogInformation("Chrome Driver Diagnostics:");
				
				if (isWindows)
				{
					// Windows Chrome paths
					var chromePaths = new[]
					{
						@"C:\Program Files\Google\Chrome\Application\chrome.exe",
						@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
						@"C:\Users\{0}\AppData\Local\Google\Chrome\Application\chrome.exe".Replace("{0}", Environment.UserName)
					};
					
					foreach (var path in chromePaths)
					{
						_logger.LogInformation("Chrome binary exists at {Path}: {Exists}", path, System.IO.File.Exists(path));
					}
					
					// ChromeDriver paths
					var chromedriverPaths = new[]
					{
						"chromedriver.exe",
						Path.Combine(Environment.CurrentDirectory, "chromedriver.exe"),
						Path.Combine(Environment.CurrentDirectory, "drivers", "chromedriver.exe"),
						@"C:\chromedriver\chromedriver.exe"
					};
					
					foreach (var path in chromedriverPaths)
					{
						_logger.LogInformation("ChromeDriver exists at {Path}: {Exists}", path, System.IO.File.Exists(path));
					}
					
					// Check log file
					var logPath = Path.Combine(Path.GetTempPath(), "chromedriver.log");
					if (System.IO.File.Exists(logPath))
					{
						var logContent = System.IO.File.ReadAllText(logPath);
						_logger.LogInformation("ChromeDriver Log: {Log}", logContent);
					}
				}
				else
				{
					// Linux diagnostics
					_logger.LogInformation("Chromium binary exists: {Exists}", System.IO.File.Exists("/usr/bin/chromium"));
					_logger.LogInformation("ChromeDriver exists: {Exists}", System.IO.File.Exists("/usr/bin/chromedriver"));
					
					if (System.IO.File.Exists("/tmp/chromedriver.log"))
					{
						var logContent = System.IO.File.ReadAllText("/tmp/chromedriver.log");
					_logger.LogInformation("ChromeDriver Log: {Log}", logContent);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to log Chrome diagnostics");
			}
		}

		private void CleanupDriver()
		{
			try
			{
				Driver?.Quit();
				Driver?.Dispose();
				Driver = null;
				Wait = null;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Error during driver cleanup");
			}
		}

		private async Task<TestMethodResult> TestDownloadButtonClick(PdfDownloadTestRequest request)
		{
			try
			{
				if (Driver == null)
				{
					return Failure("Download Button Click", request.Url, "WebDriver not initialized");
				}

				// Clear download directory
				if (Directory.Exists(DownloadDirectory))
				{
					foreach (var file in Directory.GetFiles(DownloadDirectory))
					{
						System.IO.File.Delete(file);
					}
				}

				Driver.Navigate().GoToUrl(request.Url);
				await Task.Delay(3000); // Wait for page load

				// Wait 60 seconds for manual captcha solving on the website
				_logger.LogInformation("Waiting 60 seconds for manual captcha solving on the website...");
				await Task.Delay(10000);
				_logger.LogInformation("Continuing with automation after captcha wait period");

				// Look for the specific download button
				var downloadSelectors = new[]
				{
					"a[href*='getsamplefiles.com/download/pdf/sample-5.pdf']",
					"a.flex.items-center.w-full.py-3.px-4.rounded-md.shadow.bg-gradient-to-r.from-teal-500.to-cyan-600.text-white.font-medium",
					"a[href='https://getsamplefiles.com/download/pdf/sample-5.pdf']",
					"a.flex.items-center.w-full.py-3.px-4.rounded-md.shadow",
					"a.bg-gradient-to-r.from-teal-500.to-cyan-600"
				};

				IWebElement? downloadElement = null;
				string? usedSelector = null;

				foreach (var selector in downloadSelectors)
				{
					try
					{
						var elements = Driver.FindElements(By.CssSelector(selector));
						if (elements.Count > 0)
						{
							downloadElement = elements[0];
							usedSelector = selector;
							_logger.LogDebug("Found download element using selector: {Selector}", selector);
							break;
						}
					}
					catch (Exception ex)
					{
						_logger.LogDebug("Selector {Selector} failed: {Message}", selector, ex.Message);
					}
				}

				if (downloadElement == null)
				{
					return Failure("Download Button Click", request.Url, "No download button found on page");
				}

				// Scroll element into view and click
				((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView(true);", downloadElement);
				await Task.Delay(1000);

				// Try to get the actual download URL before clicking
				string? downloadUrl = null;
				try
				{
					downloadUrl = downloadElement.GetAttribute("href");
				}
				catch (Exception)
				{
					// Ignore if we can't get href
				}

				// Click the download element
				downloadElement.Click();
				_logger.LogDebug("Clicked download element found with selector: {Selector}", usedSelector);

				// Wait for download to complete with enhanced debugging
				var maxWaitTime = 30000; // 30 seconds
				var waitInterval = 1000; // 1 second
				var waited = 0;
				string? downloadedFilePath = null;
				
				_logger.LogInformation("Starting download wait loop. Max wait time: {MaxWaitTime}ms", maxWaitTime);
				_logger.LogInformation("Monitoring download directory: {DownloadDirectory}", DownloadDirectory);
				
				while (waited < maxWaitTime)
				{
					await Task.Delay(waitInterval);
					waited += waitInterval;

					if (Directory.Exists(DownloadDirectory))
					{
						var pdfFiles = Directory.GetFiles(DownloadDirectory, "*.pdf");
						var crdownloadFiles = Directory.GetFiles(DownloadDirectory, "*.crdownload");
						var allFiles = Directory.GetFiles(DownloadDirectory);
						
						_logger.LogDebug("Download check {Waited}/{MaxWaitTime}ms - PDF files: {PdfCount}, Partial downloads: {PartialCount}, All files: {AllCount}", 
							waited, maxWaitTime, pdfFiles.Length, crdownloadFiles.Length, allFiles.Length);
						
						// Log all files in directory for debugging
						if (allFiles.Length > 0)
						{
							_logger.LogDebug("Files in download directory: {Files}", string.Join(", ", allFiles.Select(Path.GetFileName)));
						}
						
						// Check for any downloaded files (not just PDFs)
						if (allFiles.Length > 0 && crdownloadFiles.Length == 0)
						{
							// Look for the most recently modified file
							var latestFile = allFiles.OrderByDescending(f => System.IO.File.GetLastWriteTime(f)).First();
							var fileBytes = await System.IO.File.ReadAllBytesAsync(latestFile);
							
							_logger.LogInformation("Found downloaded file: {FilePath}, Size: {Size} bytes", latestFile, fileBytes.Length);
							
							// Check if it's a PDF
							if (IsValidPdf(fileBytes))
							{
								downloadedFilePath = latestFile;
								_logger.LogInformation("PDF downloaded successfully to temporary directory: {FilePath}", downloadedFilePath);
								_logger.LogInformation("PDF file size: {FileSize} bytes", fileBytes.Length);
								
								return Success("Download Button Click", request.Url, fileBytes.Length, 
									$"Successfully clicked download button (selector: {usedSelector}) and downloaded {fileBytes.Length} bytes PDF to temporary directory: {downloadedFilePath}",
									downloadUrl);
							}
							else
							{
								_logger.LogWarning("File found but not a valid PDF: {FilePath}", latestFile);
								// Check if it might be a different file type
								var fileExtension = Path.GetExtension(latestFile).ToLower();
								_logger.LogInformation("File extension: {Extension}", fileExtension);
								
								// If it's a text file, read its content to see what it contains
								if (fileExtension == ".txt" || fileExtension == ".html")
								{
									var fileContent = await System.IO.File.ReadAllTextAsync(latestFile);
									_logger.LogInformation("File content preview: {Content}", 
										fileContent.Length > 200 ? fileContent.Substring(0, 200) + "..." : fileContent);
								}
							}
						}
					}
					else
					{
						_logger.LogWarning("Download directory does not exist during wait: {Directory}", DownloadDirectory);
					}
				}

				// Final check - log what's in the directory
				if (Directory.Exists(DownloadDirectory))
				{
					var finalFiles = Directory.GetFiles(DownloadDirectory);
					_logger.LogWarning("Download timeout. Final files in directory: {Files}", 
						finalFiles.Length > 0 ? string.Join(", ", finalFiles.Select(Path.GetFileName)) : "None");
					
					// Log directory contents in detail
					foreach (var file in finalFiles)
					{
						var fileInfo = new System.IO.FileInfo(file);
						_logger.LogWarning("File: {FileName}, Size: {Size} bytes, Modified: {Modified}", 
							fileInfo.Name, fileInfo.Length, fileInfo.LastWriteTime);
					}
				}

				return Failure("Download Button Click", request.Url, 
					$"Download button clicked (selector: {usedSelector}) but no valid PDF was downloaded within {maxWaitTime/1000} seconds");
			}
			catch (Exception ex)
			{
				return Failure("Download Button Click", request.Url, ex.Message);
			}
		}

		/// <summary>
		/// Gets the downloaded PDF file from the temporary directory for local processing
		/// </summary>
		/// <returns>Byte array of the PDF file, or null if not found</returns>
		[NonAction]
		public async Task<byte[]?> GetDownloadedPdf()
		{
			try
			{
				if (string.IsNullOrEmpty(DownloadDirectory) || !Directory.Exists(DownloadDirectory))
				{
					_logger.LogWarning("Download directory does not exist: {Directory}", DownloadDirectory);
					return null;
				}

				var pdfFiles = Directory.GetFiles(DownloadDirectory, "*.pdf");
				if (pdfFiles.Length == 0)
				{
					_logger.LogWarning("No PDF files found in download directory: {Directory}", DownloadDirectory);
					return null;
				}

				var pdfFilePath = pdfFiles[0]; // Get the first PDF file
				var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfFilePath);
				
				if (IsValidPdf(pdfBytes))
				{
					_logger.LogInformation("Retrieved PDF successfully: {FilePath}, Size: {Size} bytes", pdfFilePath, pdfBytes.Length);
					return pdfBytes;
				}
				else
				{
					_logger.LogWarning("File is not a valid PDF: {FilePath}", pdfFilePath);
					return null;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error retrieving PDF");
				return null;
			}
		}

		private async Task<TestMethodResult> TestBase64PdfExtraction(PdfDownloadTestRequest request)
		{
			try
			{
				// Check if WebDriver is available
				if (Driver == null)
				{
					return Failure("Base64 PDF Extraction", request.Url, "WebDriver not initialized - cannot click button");
				}

				// Clear download directory
				if (Directory.Exists(DownloadDirectory))
				{
					foreach (var file in Directory.GetFiles(DownloadDirectory))
					{
						System.IO.File.Delete(file);
					}
				}

				// Navigate to the website
				Driver.Navigate().GoToUrl(request.Url);
				await Task.Delay(3000); // Wait for page load

				// Wait 60 seconds for manual captcha solving on the website
				_logger.LogInformation("Waiting 60 seconds for manual captcha solving on the website...");
				await Task.Delay(60000);
				_logger.LogInformation("Continuing with automation after captcha wait period");

				// Look for the specific download button
				var downloadSelectors = new[]
				{
					"a[href*='getsamplefiles.com/download/pdf/sample-5.pdf']",
					"a.flex.items-center.w-full.py-3.px-4.rounded-md.shadow.bg-gradient-to-r.from-teal-500.to-cyan-600.text-white.font-medium",
					"a[href='https://getsamplefiles.com/download/pdf/sample-5.pdf']",
					"a.flex.items-center.w-full.py-3.px-4.rounded-md.shadow",
					"a.bg-gradient-to-r.from-teal-500.to-cyan-600"
				};

				IWebElement? downloadElement = null;
				string? usedSelector = null;

				foreach (var selector in downloadSelectors)
				{
					try
					{
						var elements = Driver.FindElements(By.CssSelector(selector));
						if (elements.Count > 0)
						{
							downloadElement = elements[0];
							usedSelector = selector;
							_logger.LogDebug("Found download element using selector: {Selector}", selector);
							break;
						}
					}
					catch (Exception ex)
					{
						_logger.LogDebug("Selector {Selector} failed: {Message}", selector, ex.Message);
					}
				}

				if (downloadElement == null)
				{
					return Failure("Base64 PDF Extraction", request.Url, "No download button found on page");
				}

				// Scroll element into view and click
				((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView(true);", downloadElement);
				await Task.Delay(1000);

				// Click the download element
				downloadElement.Click();
				_logger.LogDebug("Clicked download element found with selector: {Selector}", usedSelector);

				// Wait for download to complete
				var maxWaitTime = 30000; // 30 seconds
				var waitInterval = 1000; // 1 second
				var waited = 0;
				string? downloadedFilePath = null;
				
				while (waited < maxWaitTime)
				{
					await Task.Delay(waitInterval);
					waited += waitInterval;

					if (Directory.Exists(DownloadDirectory))
					{
						var pdfFiles = Directory.GetFiles(DownloadDirectory, "*.pdf");
						var crdownloadFiles = Directory.GetFiles(DownloadDirectory, "*.crdownload");
						
						// If we have PDF files and no partial downloads, we're done
						if (pdfFiles.Length > 0 && crdownloadFiles.Length == 0)
						{
							downloadedFilePath = pdfFiles[0];
							var fileBytes = await System.IO.File.ReadAllBytesAsync(downloadedFilePath);
							
							if (IsValidPdf(fileBytes))
							{
								// Convert PDF bytes to base64 string
								var base64String = Convert.ToBase64String(fileBytes);
								
								// Create base64 file path
								var base64FilePath = Path.Combine(Path.GetTempPath(), $"pdf_base64_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt");
								
								// Store base64 string in file
								await System.IO.File.WriteAllTextAsync(base64FilePath, base64String);
								
								_logger.LogInformation("PDF downloaded via button click and converted to base64");
								_logger.LogInformation("Original PDF: {PdfPath}, Size: {PdfSize} bytes", downloadedFilePath, fileBytes.Length);
								_logger.LogInformation("Base64 file: {Base64Path}, Size: {Base64Size} characters", base64FilePath, base64String.Length);
								
								return Success("Base64 PDF Extraction", request.Url, fileBytes.Length, 
									$"Downloaded {fileBytes.Length} bytes PDF via button click (selector: {usedSelector}), converted to base64 ({base64String.Length} characters), and stored in {base64FilePath}");
							}
							else
							{
								return Failure("Base64 PDF Extraction", request.Url, $"Downloaded {fileBytes.Length} bytes but content is not a valid PDF");
							}
						}
					}
				}

				return Failure("Base64 PDF Extraction", request.Url, 
					$"Download button clicked (selector: {usedSelector}) but no valid PDF was downloaded within {maxWaitTime/1000} seconds");
			}
			catch (Exception ex)
			{
				return Failure("Base64 PDF Extraction", request.Url, ex.Message);
			}
		}

		private HttpClient CreateHttpClient()
		{
			var http = new HttpClient();
			
			// Set browser-like headers
			http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
			http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
			http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
			http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
			http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
			http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
			http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.9));
			
			// Add common headers that websites expect
			http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
			http.DefaultRequestHeaders.Add("DNT", "1");
			http.DefaultRequestHeaders.Add("Connection", "keep-alive");
			http.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

			// Set timeout
			http.Timeout = TimeSpan.FromSeconds(30);

			return http;
		}

		private static string? ExtractBase64PdfFromHtml(string htmlContent)
		{
			try
			{
				var patterns = new[]
				{
					@"data:application/pdf;base64,([A-Za-z0-9+/=]+)",
					@"application/pdf.*?([A-Za-z0-9+/=]{1000,})",
					@"%PDF.*?([A-Za-z0-9+/=]{1000,})"
				};
				
				foreach (var pattern in patterns)
				{
					var match = Regex.Match(htmlContent, pattern, RegexOptions.IgnoreCase);
					if (match.Success && match.Groups.Count > 1)
					{
						var base64Data = match.Groups[1].Value;
						if (base64Data.Length > 1000 && Regex.IsMatch(base64Data, @"^[A-Za-z0-9+/=]+$"))
						{
							return base64Data;
						}
					}
				}
			}
			catch (Exception)
			{
				// Ignore extraction errors
			}
			
			return null;
		}

		private static bool IsValidPdf(byte[] content)
		{
			try
			{
				if (content == null || content.Length < 100) return false;
				
				// Check PDF signature (%PDF)
				if (content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46)
				{
					return true;
				}
				
				// Check for PDF version header
				var contentStr = Encoding.ASCII.GetString(content, 0, Math.Min(100, content.Length));
				return contentStr.Contains("%PDF-");
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static (int SuccessCount, int TotalCount, double SuccessRate, TestMethodResult? BestResult) BuildSummary(List<TestMethodResult> results)
		{
			var successCount = results.Count(r => r.Success);
			var total = results.Count;
			var best = results.Where(r => r.Success).OrderByDescending(r => r.FileSize).FirstOrDefault();
			var rate = total == 0 ? 0 : (double)successCount / total * 100;
			return (successCount, total, rate, best);
		}

		private static TestMethodResult Success(string method, string url, long size, string details, string? actualDownloadUrl = null)
		{
			return new TestMethodResult
			{
				Method = method,
				Success = true,
				Details = details,
				FileSize = size,
				Url = url,
				ActualDownloadUrl = actualDownloadUrl ?? url
			};
		}

		private static TestMethodResult Failure(string method, string url, string message)
		{
			return new TestMethodResult
			{
				Method = method,
				Success = false,
				Details = message,
				FileSize = 0,
				Url = url
			};
		}
	}

	public class PdfDownloadTestRequest
	{
		public string Url { get; set; } = string.Empty;
		public string? UserAgent { get; set; }
	}

	public class TestMethodResult
	{
		public string Method { get; set; } = string.Empty;
		public bool Success { get; set; }
		public string Details { get; set; } = string.Empty;
		public long FileSize { get; set; }
		public string Url { get; set; } = string.Empty;
		public string? ActualDownloadUrl { get; set; }
	}
}