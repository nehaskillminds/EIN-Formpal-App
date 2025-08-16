using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace EinAutomation.Api.Controllers
{
	/// <summary>
	/// Test controller to exercise different strategies for downloading PDFs from a given HTTPS URL.
	/// Provides a Swagger-friendly endpoint where you can input a URL and receive structured results.
	/// </summary>
	[ApiController]
	[Route("api/test/[controller]")]
	public class PdfDownloadTestController : ControllerBase
	{
		private readonly ILogger<PdfDownloadTestController> _logger;

		public PdfDownloadTestController(ILogger<PdfDownloadTestController> logger)
		{
			_logger = logger;
		}

		/// <summary>
		/// Run a suite of download strategies against the provided HTTPS URL and return detailed results.
		/// </summary>
		[HttpPost("run")]
		public async Task<IActionResult> Run([FromBody] PdfDownloadTestRequest request)
		{
			try
			{
				if (request == null || string.IsNullOrWhiteSpace(request.Url))
				{
					return BadRequest("Url is required");
				}

				if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
				{
					return BadRequest("Url must be a valid absolute HTTPS URL");
				}

				_logger.LogInformation("Starting PDF download tests for {Url}", request.Url);
				var results = new List<TestMethodResult>();

				// Core strategies
				results.Add(await TestBasicGet(request));
				results.Add(await TestWithBrowserHeaders(request));
				results.Add(await TestWithIrsHeaders(request));
				results.Add(await TestWithAcceptHeaders(request));
				results.Add(await TestWithCompression(request));
				results.Add(await TestHttpMethods(request));
				results.Add(await TestWithTimeouts(request));

				// Optional variations
				if (request.IncludeUrlVariations)
				{
					results.Add(await TestUrlVariations(request));
				}

				var summary = BuildSummary(results);
				return Ok(new
				{
					Success = summary.SuccessCount > 0,
					Summary = $"{summary.SuccessCount}/{summary.TotalCount} strategies succeeded ({summary.SuccessRate:F1}%)",
					BestResult = summary.BestResult == null ? null : new
					{
						Method = summary.BestResult.Method,
						Url = summary.BestResult.Url,
						FileSize = summary.BestResult.FileSize
					},
					Results = results.Select(r => new
					{
						Method = r.Method,
						Success = r.Success,
						Details = r.Details,
						FileSize = r.FileSize,
						Url = r.Url
					})
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "PDF download tests failed");
				return StatusCode(500, $"Test run failed: {ex.Message}");
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

		private async Task<TestMethodResult> TestBasicGet(PdfDownloadTestRequest request)
		{
			try
			{
				using var http = new HttpClient();
				ApplyRequestHints(http, request);
				var bytes = await http.GetByteArrayAsync(request.Url);
				return Success("Basic GET", request.Url, bytes.Length, $"Downloaded {bytes.Length} bytes");
			}
			catch (Exception ex)
			{
				return Failure("Basic GET", request.Url, ex.Message);
			}
		}

		private async Task<TestMethodResult> TestWithBrowserHeaders(PdfDownloadTestRequest request)
		{
			try
			{
				using var http = new HttpClient();
				// User-Agent override if provided
				var userAgent = string.IsNullOrWhiteSpace(request.UserAgent)
					? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
					: request.UserAgent;
				http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
				// Accept
				http.DefaultRequestHeaders.Accept.Clear();
				http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
				http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
				var xml = new MediaTypeWithQualityHeaderValue("application/xml", 0.9);
				http.DefaultRequestHeaders.Accept.Add(xml);
				http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
				http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
				// Accept-Language
				http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
				http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.5));
				// Encoding and connection
				http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
				http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
				http.DefaultRequestHeaders.Connection.Add("keep-alive");
				ApplyRequestHints(http, request);

				var bytes = await http.GetByteArrayAsync(request.Url);
				return Success("Browser Headers", request.Url, bytes.Length, $"Downloaded {bytes.Length} bytes");
			}
			catch (Exception ex)
			{
				return Failure("Browser Headers", request.Url, ex.Message);
			}
		}

		private async Task<TestMethodResult> TestWithIrsHeaders(PdfDownloadTestRequest request)
		{
			try
			{
				using var http = new HttpClient();
				http.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(request.UserAgent)
					? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
					: request.UserAgent);
				if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
				{
					var referrer = !string.IsNullOrWhiteSpace(request.Referrer)
						? request.Referrer
						: $"{uri.Scheme}://{uri.Host}/";
					http.DefaultRequestHeaders.Referrer = new Uri(referrer);
				}
				http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
				http.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
				http.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
				http.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
				http.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
				ApplyRequestHints(http, request);

				var bytes = await http.GetByteArrayAsync(request.Url);
				return Success("IRS-Style Headers", request.Url, bytes.Length, $"Downloaded {bytes.Length} bytes");
			}
			catch (Exception ex)
			{
				return Failure("IRS-Style Headers", request.Url, ex.Message);
			}
		}

		private async Task<TestMethodResult> TestWithAcceptHeaders(PdfDownloadTestRequest request)
		{
			try
			{
				using var http = new HttpClient();
				http.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(request.UserAgent)
					? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
					: request.UserAgent);
				http.DefaultRequestHeaders.Accept.Clear();
				http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
				http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
				http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
				ApplyRequestHints(http, request);

				var bytes = await http.GetByteArrayAsync(request.Url);
				return Success("Accept Headers", request.Url, bytes.Length, $"Downloaded {bytes.Length} bytes");
			}
			catch (Exception ex)
			{
				return Failure("Accept Headers", request.Url, ex.Message);
			}
		}

		private async Task<TestMethodResult> TestWithCompression(PdfDownloadTestRequest request)
		{
			try
			{
				using var http = new HttpClient();
				http.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(request.UserAgent)
					? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
					: request.UserAgent);
				http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
				http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
				ApplyRequestHints(http, request);

				var bytes = await http.GetByteArrayAsync(request.Url);
				return Success("Compression Headers", request.Url, bytes.Length, $"Downloaded {bytes.Length} bytes");
			}
			catch (Exception ex)
			{
				return Failure("Compression Headers", request.Url, ex.Message);
			}
		}

		private async Task<TestMethodResult> TestHttpMethods(PdfDownloadTestRequest request)
		{
			try
			{
				using var http = new HttpClient();
				ApplyRequestHints(http, request);
				var methods = new[] { "GET", "HEAD" };
				var sizes = new List<(string Method, long Size)>();
				foreach (var method in methods)
				{
					try
					{
						var msg = new HttpRequestMessage(new HttpMethod(method), request.Url);
						var response = await http.SendAsync(msg);
						if (response.IsSuccessStatusCode)
						{
							var content = await response.Content.ReadAsByteArrayAsync();
							if (content.Length > 0)
							{
								sizes.Add((method, content.Length));
							}
						}
					}
					catch (Exception)
					{
						continue;
					}
				}

				if (sizes.Any())
				{
					var best = sizes.OrderByDescending(s => s.Size).First();
					return Success("HTTP Methods", request.Url, best.Size, $"Successful methods: {string.Join(", ", sizes.Select(s => s.Method))}. Best: {best.Method} ({best.Size} bytes)");
				}

				return Failure("HTTP Methods", request.Url, "No successful HTTP methods");
			}
			catch (Exception ex)
			{
				return Failure("HTTP Methods", request.Url, ex.Message);
			}
		}

		private async Task<TestMethodResult> TestWithTimeouts(PdfDownloadTestRequest request)
		{
			try
			{
				var timeouts = new[] { 10, 30 };
				var sizes = new List<(int Timeout, long Size)>();
				foreach (var t in timeouts)
				{
					try
					{
						using var http = new HttpClient();
						http.Timeout = TimeSpan.FromSeconds(t);
						http.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(request.UserAgent)
							? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
							: request.UserAgent);
						ApplyRequestHints(http, request);
						var bytes = await http.GetByteArrayAsync(request.Url);
						if (bytes.Length > 0)
						{
							sizes.Add((t, bytes.Length));
						}
					}
					catch (Exception)
					{
						continue;
					}
				}

				if (sizes.Any())
				{
					var best = sizes.OrderByDescending(s => s.Size).First();
					return Success("Timeouts", request.Url, best.Size, $"Working: {string.Join(", ", sizes.Select(s => s.Timeout + "s"))}. Best: {best.Timeout}s ({best.Size} bytes)");
				}

				return Failure("Timeouts", request.Url, "No working timeout configurations");
			}
			catch (Exception ex)
			{
				return Failure("Timeouts", request.Url, ex.Message);
			}
		}

		private async Task<TestMethodResult> TestUrlVariations(PdfDownloadTestRequest request)
		{
			try
			{
				var variations = new List<string> { request.Url };
				if (request.Url.StartsWith("https://"))
				{
					variations.Add(request.Url.Replace("https://", "http://"));
				}
				if (request.Url.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
				{
					variations.Add(request.Url.Replace(".pdf", ".PDF"));
					variations.Add(request.Url.Replace(".pdf", ".pdf?download=true"));
				}

				using var http = new HttpClient();
				http.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(request.UserAgent)
					? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
					: request.UserAgent);
				ApplyRequestHints(http, request);
				var successes = new List<(string Url, long Size)>();
				foreach (var u in variations.Distinct())
				{
					try
					{
						var bytes = await http.GetByteArrayAsync(u);
						if (bytes.Length > 0)
						{
							successes.Add((u, bytes.Length));
						}
					}
					catch (Exception)
					{
						continue;
					}
				}

				if (successes.Any())
				{
					var best = successes.OrderByDescending(s => s.Size).First();
					return Success("URL Variations", best.Url, best.Size, $"Working variations: {successes.Count}. Best size: {best.Size} bytes");
				}

				return Failure("URL Variations", request.Url, "No working URL variations found");
			}
			catch (Exception ex)
			{
				return Failure("URL Variations", request.Url, ex.Message);
			}
		}

		private static void ApplyRequestHints(HttpClient http, PdfDownloadTestRequest request)
		{
			if (!string.IsNullOrWhiteSpace(request.Referrer))
			{
				if (Uri.TryCreate(request.Referrer, UriKind.Absolute, out var refUri))
				{
					http.DefaultRequestHeaders.Referrer = refUri;
				}
			}
			if (!string.IsNullOrWhiteSpace(request.Cookie))
			{
				http.DefaultRequestHeaders.Remove("Cookie");
				http.DefaultRequestHeaders.Add("Cookie", request.Cookie);
			}
			if (request.Headers != null)
			{
				foreach (var kvp in request.Headers)
				{
					try
					{
						http.DefaultRequestHeaders.Remove(kvp.Key);
						http.DefaultRequestHeaders.Add(kvp.Key, kvp.Value);
					}
					catch
					{
						// ignore invalid custom header entries
					}
				}
			}
		}

		private static TestMethodResult Success(string method, string url, long size, string details)
		{
			return new TestMethodResult
			{
				Method = method,
				Success = true,
				Details = details,
				FileSize = size,
				Url = url
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
		public bool IncludeUrlVariations { get; set; } = true;
		public string? Referrer { get; set; }
		public string? Cookie { get; set; }
		public Dictionary<string, string>? Headers { get; set; }
		public string? UserAgent { get; set; }
	}

	public class TestMethodResult
	{
		public string Method { get; set; } = string.Empty;
		public bool Success { get; set; }
		public string Details { get; set; } = string.Empty;
		public long FileSize { get; set; }
		public string Url { get; set; } = string.Empty;
	}
}


