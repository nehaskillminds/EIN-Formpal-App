using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Interactions;
using System.Diagnostics;
using EinAutomation.Api.Infrastructure;
using System.Text.RegularExpressions;
using System.Globalization;

namespace EinAutomation.Api.Services
{
    public abstract class EinFormFiller : IEinFormFiller
    {
        public IWebDriver? Driver { get; protected set; }
        protected WebDriverWait? Wait { get; set; }
        protected readonly ILogger<EinFormFiller> _logger;
        protected readonly IBlobStorageService _blobStorageService;
        protected readonly ISalesforceClient? _salesforceClient;
        protected int Timeout { get; }
        protected bool Headless { get; }
        protected string? DriverLogPath { get; } = Path.Combine(Path.GetTempPath(), "chromedriver.log");
        protected List<Dictionary<string, object?>> ConsoleLogs { get; } = new List<Dictionary<string, object?>>();
        protected bool ConfirmationUploaded { get; set; } = false;
        protected string? _downloadDirectory;
        protected string? ChromeDownloadDirectory { get; set; }

        public EinFormFiller(ILogger<EinFormFiller> logger, IBlobStorageService blobStorageService, ISalesforceClient? salesforceClient = null, bool headless = false, int timeout = 300)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
            _salesforceClient = salesforceClient;
            Headless = headless;
            Timeout = timeout;
        }

        /// <summary>
        /// Extracts the first 5 digits from a zip code string.
        /// Handles various zip code formats including 5-digit, 9-digit (ZIP+4), and invalid formats.
        /// </summary>
        /// <param name="zipCode">The zip code string to process</param>
        /// <returns>The first 5 digits of the zip code, or empty string if invalid</returns>
        protected string ExtractFirstFiveDigits(string? zipCode)
        {
            if (string.IsNullOrWhiteSpace(zipCode))
                return string.Empty;

            // Remove any non-digit characters and get only the first 5 digits
            var digitsOnly = Regex.Replace(zipCode, @"\D", "");
            
            if (digitsOnly.Length >= 5)
            {
                return digitsOnly.Substring(0, 5);
            }
            
            // If less than 5 digits, return as is (this handles edge cases)
            return digitsOnly;
        }

        public async Task<(string? BlobUrl, bool Success)> CaptureSubmissionPageAsPdf(CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot capture submission PDF - Driver is null");
                    return (null, false);
                }

                // Generate a clean filename
                var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINSubmission.pdf";

                // Wait for page to be fully loaded
                await WaitForPageLoadAsync(cancellationToken);

                string? blobUrl = null;

                // --- Primary PDF generation using Chrome CDP ---
                if (Driver is ChromeDriver chromeDriver)
                {
                    try
                    {
                        var printOptions = new Dictionary<string, object>();

                        var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printOptions);

                        if (result is Dictionary<string, object> resultDict && resultDict.ContainsKey("data"))
                        {
                            var dataObj = resultDict["data"];
                            string? base64Pdf = null;
                            if (dataObj is string s)
                            {
                                base64Pdf = s;
                            }
                            else if (dataObj is System.Text.Json.JsonElement jsonEl && jsonEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                base64Pdf = jsonEl.GetString();
                            }

                            if (!string.IsNullOrEmpty(base64Pdf))
                            {
                                var pdfBytes = Convert.FromBase64String(base64Pdf);

                                // Use legacy UploadBytesToBlob signature: (bytes, blobName, contentType, ct) -> blob URL
                                var uploadedUrl = await _blobStorageService.UploadBytesToBlob(
                                    pdfBytes,
                                    blobName,
                                    "application/pdf",
                                    cancellationToken
                                );

                                if (!string.IsNullOrEmpty(uploadedUrl))
                                {
                                    blobUrl = uploadedUrl;
                                    _logger.LogInformation("Uploaded EINSubmission PDF via CDP to blob: {BlobUrl}", blobUrl);
                                }
                                else
                                {
                                    _logger.LogWarning("CDP PDF upload returned empty URL, will try fallback");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "CDP PDF generation failed, trying fallback");
                    }
                }

                // --- Fallback via HTML2PDF ---
                if (string.IsNullOrEmpty(blobUrl))
                {
                    var (fallbackUrl, success) = await CapturePageAsPdfHtml2PdfFallback(data, cancellationToken);
                    if (success)
                    {
                        blobUrl = fallbackUrl;
                        _logger.LogInformation("Uploaded EINSubmission PDF via fallback to blob: {BlobUrl}", blobUrl);
                    }
                }

                if (!string.IsNullOrEmpty(blobUrl))
                {
                    // Notify Salesforce (Content Migration)
                    if (_salesforceClient != null)
                    {
                        var notified = await _salesforceClient.NotifySubmissionUploadToSalesforceAsync(
                            data?.RecordId,
                            blobUrl,
                            data?.EntityName,
                            data?.AccountId,
                            data?.EntityId,
                            data?.CaseId
                        );

                        if (!notified)
                        {
                            _logger.LogWarning("Salesforce notification for EINSubmission PDF upload failed.");
                        }
                    }

                    return (blobUrl, true);
                }

                return (null, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CaptureSubmissionPageAsPdf failed.");
                return (null, false);
            }
        }
        public bool FillField(By locator, string? value, string label = "field")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning("Skipping {Label} - empty value", label);
                return false;
            }

            try
            {
                if (Wait == null || Driver == null)
                {
                    _logger.LogWarning("Cannot fill {Label} - Wait or Driver is null", label);
                    return false;
                }

                var field = WaitHelper.WaitUntilClickable(Driver, locator, Timeout);
                ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", field);
                field!.Clear();
                field!.SendKeys(value);

                _logger.LogInformation("Filled {Label}: {Value}", label, value);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fill {Label}", label);
                return false;
            }
        }
        public bool ClickButton(By locator, string description = "button", int retries = 3)
        {
            if (Wait == null || Driver == null)
            {
                _logger.LogWarning("Cannot click {Description} - Wait or Driver is null", description);
                return false;
            }

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    var element = WaitHelper.WaitUntilExists(Driver, locator, Timeout);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                    Task.Delay(500).Wait();

                    var clickableElement = WaitHelper.WaitUntilClickable(Driver, locator, Timeout);

                    try
                    {
                        clickableElement!.Click();
                        _logger.LogInformation("Clicked {Description}", description);
                        Task.Delay(1000).Wait();
                        return true;
                    }
                    catch
                    {
                        try
                        {
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].click();", clickableElement);
                            _logger.LogInformation("Clicked {Description} via JavaScript", description);
                            Task.Delay(1000).Wait();
                            return true;
                        }
                        catch
                        {
                            new Actions(Driver).MoveToElement(clickableElement).Click().Perform();
                            _logger.LogInformation("Clicked {Description} via Actions", description);
                            Task.Delay(1000).Wait();
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == retries)
                    {
                        _logger.LogWarning(ex, "Failed to click {Description} after {Retries} attempts", description, retries + 1);
                        return false;
                    }
                    _logger.LogWarning(ex, "Click attempt {Attempt} failed for {Description}, retrying...", attempt + 1, description);
                    Task.Delay(1000).Wait();
                }
            }
            return false;
        }
        public bool ClickButtonByAriaLabel(string ariaLabel, string description = "button", int retries = 3)
        {
            if (Wait == null || Driver == null)
            {
                _logger.LogWarning("Cannot click {Description} - Wait or Driver is null", description);
                return false;
            }

            By locator = By.XPath($"//a[@aria-label='{ariaLabel}']");

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    var element = WaitHelper.WaitUntilExists(Driver, locator, Timeout);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                    Task.Delay(500).Wait();

                    var clickableElement = WaitHelper.WaitUntilClickable(Driver, locator, Timeout);

                    try
                    {
                        clickableElement!.Click();
                        _logger.LogInformation("Clicked {Description} ({AriaLabel})", description, ariaLabel);
                        Task.Delay(1000).Wait();
                        return true;
                    }
                    catch
                    {
                        try
                        {
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].click();", clickableElement);
                            _logger.LogInformation("Clicked {Description} ({AriaLabel}) via JavaScript", description, ariaLabel);
                            Task.Delay(1000).Wait();
                            return true;
                        }
                        catch
                        {
                            new Actions(Driver).MoveToElement(clickableElement).Click().Perform();
                            _logger.LogInformation("Clicked {Description} ({AriaLabel}) via Actions", description, ariaLabel);
                            Task.Delay(1000).Wait();
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == retries)
                    {
                        _logger.LogWarning(ex, "Failed to click {Description} ({AriaLabel}) after {Retries} attempts", description, ariaLabel, retries + 1);
                        return false;
                    }
                    _logger.LogWarning(ex, "Click attempt {Attempt} failed for {Description} ({AriaLabel}), retrying...", attempt + 1, description, ariaLabel);
                    Task.Delay(1000).Wait();
                }
            }
            return false;
        }
        public bool SelectRadio(string? radioId, string description = "radio", int? timeoutSeconds = null, int maxRetries = 3)
        {
            if (string.IsNullOrEmpty(radioId))
            {
                _logger.LogWarning("Cannot select {Description} - radioId is null or empty", description);
                return false;
            }

            if (Wait == null || Driver == null)
            {
                _logger.LogWarning("Cannot select {Description} - Wait or Driver is null", description);
                return false;
            }

            var js = (IJavaScriptExecutor)Driver;
            var effectiveTimeout = TimeSpan.FromSeconds(timeoutSeconds ?? Timeout); // TimeSpan for internal use

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogDebug("Attempting to select {Description} (attempt {Attempt}/{MaxRetries})", description, attempt, maxRetries);

                    // Strategy 1: Enhanced JavaScript selection with framework event handling
                    if (TryJavaScriptSelection(js, radioId, description))
                        return true;

                    // Strategy 2: Wait and click with scroll into view
                    if (TryDirectClick(radioId, description, (int)effectiveTimeout.TotalSeconds)) // Convert TimeSpan to int (seconds)
                        return true;

                    // Strategy 3: Label-based selection
                    if (TryLabelClick(js, radioId, description))
                        return true;

                    // Strategy 4: Parent container click (for custom radio implementations)
                    if (TryParentContainerClick(js, radioId, description))
                        return true;

                    // Strategy 5: CSS selector alternatives
                    if (TryCssSelectorAlternatives(js, radioId, description))
                        return true;

                    // Wait before retry for dynamic content
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(500);
                        _logger.LogDebug("Waiting before retry {NextAttempt}", attempt + 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Attempt {Attempt} failed for {Description}", attempt, description);
                    if (attempt == maxRetries)
                    {
                        _logger.LogWarning(ex, "All attempts failed to select {Description} (ID: {RadioId})", description, radioId);
                    }
                }
            }

            return false;
        }
        private bool TryJavaScriptSelection(IJavaScriptExecutor js, string radioId, string description)
        {
            try
            {
                var jsResult = js.ExecuteScript(@"
            var id = arguments[0];
            var el = document.getElementById(id);
            if (!el) return { success: false, reason: 'Element not found' };
            
            // Scroll into view
            el.scrollIntoView({ block: 'center', behavior: 'smooth' });
            
            // Wait a moment for scroll
            return new Promise(resolve => {
                setTimeout(() => {
                    try {
                        // Set checked property
                        el.checked = true;
                        
                        // Trigger all possible events that frameworks might listen to
                        var events = ['input', 'change', 'click'];
                        events.forEach(eventType => {
                            try {
                                // Native event
                                var event = new Event(eventType, { 
                                    bubbles: true, 
                                    cancelable: true,
                                    composed: true 
                                });
                                el.dispatchEvent(event);
                                
                                // jQuery event (if jQuery is present)
                                if (window.jQuery) {
                                    window.jQuery(el).trigger(eventType);
                                }
                                
                                // React synthetic event simulation
                                if (el._valueTracker) {
                                    el._valueTracker.setValue('');
                                }
                            } catch (e) {}
                        });
                        
                        // Angular-specific event handling
                        if (window.angular) {
                            try {
                                var scope = window.angular.element(el).scope();
                                if (scope && scope.$apply) {
                                    scope.$apply();
                                }
                            } catch (e) {}
                        }
                        
                        // Vue.js event handling
                        if (el.__vue__) {
                            try {
                                el.__vue__.$forceUpdate();
                            } catch (e) {}
                        }
                        
                        resolve({ success: !!el.checked, reason: el.checked ? 'Success' : 'Not checked after operation' });
                    } catch (e) {
                        resolve({ success: false, reason: e.message });
                    }
                }, 100);
            });
        ", radioId);

                // Handle Promise result (newer Selenium versions)
                if (jsResult is Dictionary<string, object?> result && result != null)
                {
                    var success = result.TryGetValue("success", out var successValue) && successValue is bool successBool && successBool;
                    if (success)
                    {
                        _logger.LogInformation("Selected {Description} via enhanced JavaScript", description);
                        return true;
                    }
                }
                // Handle direct boolean result (older Selenium versions)
                else if (jsResult is bool directResult && directResult)
                {
                    _logger.LogInformation("Selected {Description} via JavaScript", description);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "JavaScript selection failed for {Description}", description);
            }

            return false;
        }
        private bool TryDirectClick(string radioId, string description, int timeoutSeconds) // Changed to int
        {
            try
            {
                var radio = WaitHelper.WaitUntilClickable(Driver!, By.Id(radioId), timeoutSeconds); // Assumes WaitUntilClickable accepts int

                // Scroll into view before clicking
                var js = (IJavaScriptExecutor)Driver!;
                js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", radio);
                Thread.Sleep(200); // Brief pause for scroll completion

                radio!.Click();

                // Verify selection
                if (radio.Selected)
                {
                    _logger.LogInformation("Selected {Description} via direct click", description);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Direct click failed for {Description}", description);
            }

            return false;
        }
        private bool TryLabelClick(IJavaScriptExecutor js, string radioId, string description)
        {
            try
            {
                // Try multiple label selector approaches
                var labelSelectors = new[]
                {
                    $"label[for='{radioId}']",
                    $"label[for=\"{radioId}\"]",
                    $"//label[@for='{radioId}']"
                };

                foreach (var selector in labelSelectors)
                {
                    try
                    {
                        IWebElement? label;

                        if (selector.StartsWith("//"))
                        {
                            var labels = Driver!.FindElements(By.XPath(selector));
                            label = labels.FirstOrDefault();
                        }
                        else
                        {
                            var labels = Driver!.FindElements(By.CssSelector(selector));
                            label = labels.FirstOrDefault();
                        }

                        if (label != null)
                        {
                            js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", label);
                            Thread.Sleep(100);

                            try
                            {
                                label.Click();
                            }
                            catch
                            {
                                js.ExecuteScript("arguments[0].click();", label);
                            }

                            _logger.LogInformation("Selected {Description} via label click", description);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Label selector {Selector} failed for {Description}", selector, description);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Label click strategy failed for {Description}", description);
            }

            return false;
        }
        private bool TryParentContainerClick(IJavaScriptExecutor js, string radioId, string description)
        {
            try
            {
                // Some frameworks wrap radio buttons in clickable containers
                var containerSelectors = new[]
                {
                    $"div:has(input#{radioId})",
                    $"span:has(input#{radioId})",
                    $"label:has(input#{radioId})",
                    $"[data-radio-id='{radioId}']",
                    $"[data-testid*='{radioId}']"
                };

                foreach (var selector in containerSelectors)
                {
                    try
                    {
                        var containers = Driver!.FindElements(By.CssSelector(selector));
                        var container = containers.FirstOrDefault();

                        if (container != null)
                        {
                            js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", container);
                            Thread.Sleep(100);

                            try
                            {
                                container.Click();
                            }
                            catch
                            {
                                js.ExecuteScript("arguments[0].click();", container);
                            }

                            _logger.LogInformation("Selected {Description} via parent container click", description);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Container selector {Selector} failed for {Description}", selector, description);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Parent container click strategy failed for {Description}", description);
            }

            return false;
        }
        private bool TryCssSelectorAlternatives(IJavaScriptExecutor js, string radioId, string description)
        {
            try
            {
                // Try alternative ways to find the radio button
                var alternativeSelectors = new[]
                {
                    $"input[type='radio'][id='{radioId}']",
                    $"input[type='radio'][name*='{radioId}']",
                    $"input[type='radio'][value='{radioId}']",
                    $"input[id='{radioId}']",
                    $"[role='radio'][id='{radioId}']",
                    $"[role='radio'][data-value='{radioId}']"
                };

                foreach (var selector in alternativeSelectors)
                {
                    try
                    {
                        var elements = Driver!.FindElements(By.CssSelector(selector));
                        var element = elements.FirstOrDefault();

                        if (element != null)
                        {
                            js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                            Thread.Sleep(100);

                            // Try multiple interaction methods
                            var interactionMethods = new Action[]
                            {
                        () => element.Click(),
                        () => js.ExecuteScript("arguments[0].click();", element),
                        () => js.ExecuteScript("arguments[0].checked = true; arguments[0].dispatchEvent(new Event('change', {bubbles: true}));", element)
                            };

                            foreach (var method in interactionMethods)
                            {
                                try
                                {
                                    method();
                                    _logger.LogInformation("Selected {Description} via alternative selector {Selector}", description, selector);
                                    return true;
                                }
                                catch
                                {
                                    // Continue to next method
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Alternative selector {Selector} failed for {Description}", selector, description);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CSS selector alternatives strategy failed for {Description}", description);
            }

            return false;
        }
        public bool SelectDropdown(By locator, string? value, string label = "dropdown")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning("Cannot select {Label} - value is null or empty", label);
                return false;
            }

            try
            {
                if (Wait == null || Driver == null)
                {
                    _logger.LogWarning("Cannot select {Label} - Wait or Driver is null", label);
                    return false;
                }

                // Wait for element and scroll into view
                var element = WaitHelper.WaitUntilClickable(Driver, locator, Timeout);
                ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                Task.Delay(200).Wait();

                var select = new SelectElement(element);

                // Ensure options are present
                if (select.Options == null || select.Options.Count == 0)
                {
                    // Small wait-and-retry once for dynamic loads
                    Task.Delay(500).Wait();
                    select = new SelectElement(element);
                }

                string normalized = value.Trim();

                // Try 1: by value (exact)
                try
                {
                    select.SelectByValue(normalized);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", element);
                    _logger.LogInformation("Selected {Label} by value: {Value}", label, normalized);
                    return true;
                }
                catch { /* fall through */ }

                // Try 2: by text (exact)
                try
                {
                    select.SelectByText(normalized);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", element);
                    _logger.LogInformation("Selected {Label} by text: {Value}", label, normalized);
                    return true;
                }
                catch { /* fall through */ }

                // Try 3: case-insensitive text match
                var byTextIgnoreCase = select.Options.FirstOrDefault(o => string.Equals(o.Text?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
                if (byTextIgnoreCase != null)
                {
                    byTextIgnoreCase.Click();
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", element);
                    _logger.LogInformation("Selected {Label} by case-insensitive text: {Value}", label, normalized);
                    return true;
                }

                // Try 4: partial match on text or value
                var lowered = normalized.ToLowerInvariant();
                var partial = select.Options.FirstOrDefault(o => (o.Text ?? string.Empty).Trim().ToLowerInvariant() == lowered || (o.Text ?? string.Empty).ToLowerInvariant().Contains(lowered) || (o.GetAttribute("value") ?? string.Empty).ToLowerInvariant().Contains(lowered));
                if (partial != null)
                {
                    partial.Click();
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", element);
                    _logger.LogInformation("Selected {Label} by partial match: {Value}", label, normalized);
                    return true;
                }

                // Try 5: if the input looks like a month number (1-12), try common month name/value variants
                if (int.TryParse(normalized, out var monthNum) && monthNum >= 1 && monthNum <= 12)
                {
                    var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(monthNum);
                    var monthAbbrev = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(monthNum);

                    var candidates = new List<string>
                    {
                        monthName,                                     // e.g., "August"
                        monthName.ToUpperInvariant(),                  // "AUGUST"
                        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(monthName.ToLowerInvariant()),
                        monthAbbrev,                                   // "Aug"
                        monthAbbrev.ToUpperInvariant(),                // "AUG"
                        monthNum.ToString("00"),                      // "08"
                        monthNum.ToString()                            // "8"
                    };

                    foreach (var candidate in candidates)
                    {
                        try
                        {
                            select.SelectByValue(candidate);
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", element);
                            _logger.LogInformation("Selected {Label} by month candidate value: {Candidate} (from {Original})", label, candidate, normalized);
                            return true;
                        }
                        catch { /* try next */ }

                        try
                        {
                            select.SelectByText(candidate);
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", element);
                            _logger.LogInformation("Selected {Label} by month candidate text: {Candidate} (from {Original})", label, candidate, normalized);
                            return true;
                        }
                        catch { /* try next */ }

                        var option = select.Options.FirstOrDefault(o => string.Equals(o.Text?.Trim(), candidate, StringComparison.OrdinalIgnoreCase) || string.Equals(o.GetAttribute("value")?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
                        if (option != null)
                        {
                            option.Click();
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', {bubbles:true}));", element);
                            _logger.LogInformation("Selected {Label} by month candidate option match: {Candidate} (from {Original})", label, candidate, normalized);
                            return true;
                        }
                    }
                }

                _logger.LogWarning("No matching option found for {Label} with value/text '{Value}'", label, normalized);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to select {Label}", label);
                return false;
            }
        }
        public void Cleanup()
        {
            try
            {
                CaptureBrowserLogs();

                // DEBUG MODE: Keep browser open for debugging
                var keepBrowserOpen = Environment.GetEnvironmentVariable("KEEP_BROWSER_OPEN") == "true";
                if (keepBrowserOpen)
                {
                    _logger.LogInformation("🔍 DEBUG MODE: Browser kept open for debugging (KEEP_BROWSER_OPEN=true)");
                    return;
                }

                if (Driver != null)
                {
                    Driver.Quit();
                    Driver = null;
                    _logger.LogInformation("Browser closed successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing browser");
                try
                {
                    if (Driver is ChromeDriver chromeDriver)
                    {
                        chromeDriver.Dispose();
                    }
                    _logger.LogWarning("Force-disposed browser");
                }
                catch (Exception disposeEx)
                {
                    _logger.LogError(disposeEx, "Failed to force-dispose browser");
                }
            }
            finally
            {
                if (File.Exists(DriverLogPath))
                {
                    try
                    {
                        File.Delete(DriverLogPath);
                        _logger.LogDebug("Removed ChromeDriver log file: {DriverLogPath}", DriverLogPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove ChromeDriver log");
                    }
                }
            }
        }
        public void ClearAndFill(By locator, string? value, string description)
        {
            if (string.IsNullOrEmpty(value))
            {
                _logger.LogError("Cannot clear and fill {Description} - value is null or empty", description);
                throw new AutomationError($"Cannot clear and fill {description} - value is null or empty", "Value is null");
            }

            try
            {
                if (Wait == null || Driver == null)
                {
                    throw new InvalidOperationException("Wait or Driver is null");
                }

                var field = WaitHelper.WaitUntilClickable(Driver, locator, Timeout);
                field!.Clear();
                field!.SendKeys(value);
                _logger.LogInformation("Cleared and filled {Description} with value: {Value}", description, value);
            }
            catch (Exception ex)
            {
                CaptureBrowserLogs();
                _logger.LogError(ex, "Failed to clear and fill {Description}", description);
                throw new AutomationError($"Failed to clear and fill {description}", ex.Message);
            }
        }
        public async Task<(string? BlobUrl, bool Success)> CapturePageAsPdf(CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot capture PDF - Driver is null");
                    return (null, false);
                }

                // Generate a clean filename
                var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINConfirmation.pdf";

                // Wait for page to be fully loaded
                await WaitForPageLoadAsync(cancellationToken);

                // Log download directory state before PDF generation
                LogDownloadDirectoryState("Before PDF generation");

                string? blobUrl = null;

                // --- Primary PDF generation using Chrome CDP ---
                if (Driver is ChromeDriver chromeDriver)
                {
                    try
                    {
                        var printOptions = new Dictionary<string, object>();

                        var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printOptions);

                        if (result is Dictionary<string, object> resultDict && resultDict.ContainsKey("data"))
                        {
                            var pdfData = resultDict["data"]?.ToString();
                            if (!string.IsNullOrEmpty(pdfData))
                            {
                                var pdfBytes = Convert.FromBase64String(pdfData);
                                blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                                _logger.LogInformation($"PDF successfully uploaded to: {blobUrl}");
                            }
                        }
                    }
                    catch (Exception cdpEx)
                    {
                        _logger.LogWarning($"Chrome CDP PDF generation failed, trying fallback: {cdpEx.Message}");
                    }
                }

                // --- Fallback if CDP method failed ---
                if (blobUrl == null)
                {
                    (blobUrl, bool fallbackSuccess) = await CapturePageAsPdfHtml2PdfFallback(data, cancellationToken);
                    if (!fallbackSuccess)
                    {
                        _logger.LogError("Both Chrome CDP and HTML2PDF fallback failed.");
                        return (null, false);
                    }
                }

                // Log download directory state after PDF generation
                LogDownloadDirectoryState("After PDF generation");

                // --- Notify Salesforce if blob upload succeeded ---
                if (!string.IsNullOrEmpty(blobUrl))
                {
                    bool notified = await _salesforceClient!.NotifyScreenshotUploadToSalesforceAsync(
                        data?.RecordId,
                        blobUrl,
                        data?.EntityName,
                        data?.AccountId,
                        data?.EntityId,
                        data?.CaseId
                    );

                    if (!notified)
                    {
                        _logger.LogWarning("Salesforce notification for PDF upload failed.");
                    }

                    return (blobUrl, true);
                }

                return (null, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CapturePageAsPdf failed.");
                return (null, false);
            }
        }
        public async Task<(string? BlobUrl, bool Success)> CaptureSuccessPageAsPdf(CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot capture success PDF - Driver is null");
                    return (null, false);
                }

                // AKS-specific logging
                var isContainer =   Environment.GetEnvironmentVariable("CONTAINER_ENV") == "true" ||
                                    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
                                    File.Exists("/.dockerenv");

                if (isContainer)
                {
                    _logger.LogInformation("Capturing success PDF in AKS container environment");
                }

                // Generate a clean filename
                var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINLetter.pdf";

                // Wait for page to be fully loaded
                await WaitForPageLoadAsync(cancellationToken);

                string? blobUrl = null;

                // --- Primary PDF generation using Chrome CDP ---
                if (Driver is ChromeDriver chromeDriver)
                {
                    try
                    {
                        var printOptions = new Dictionary<string, object>();

                        var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printOptions);

                        if (result is Dictionary<string, object> resultDict && resultDict.ContainsKey("data"))
                        {
                            var pdfData = resultDict["data"]?.ToString();
                            if (!string.IsNullOrEmpty(pdfData))
                            {
                                var pdfBytes = Convert.FromBase64String(pdfData);
                                blobUrl = await _blobStorageService.UploadEinLetterPdf(
                                    pdfBytes,
                                    blobName,
                                    "application/pdf",
                                    data?.AccountId,
                                    data?.EntityId,
                                    data?.CaseId);
                                _logger.LogInformation($"Success PDF successfully uploaded to: {blobUrl}");
                            }
                        }
                    }
                    catch (Exception cdpEx)
                    {
                        _logger.LogWarning($"Chrome CDP success PDF generation failed, trying fallback: {cdpEx.Message}");
                    }
                }

                // --- Fallback if CDP method failed ---
                if (blobUrl == null)
                {
                    (blobUrl, bool fallbackSuccess) = await CapturePageAsPdfHtml2PdfFallback(data, cancellationToken);
                    if (!fallbackSuccess)
                    {
                        _logger.LogError("Both Chrome CDP and HTML2PDF fallback failed for success PDF.");
                        return (null, false);
                    }
                }

                // --- Notify Salesforce if blob upload succeeded ---
                if (!string.IsNullOrEmpty(blobUrl))
                {
                    bool notified = await _salesforceClient!.NotifyEinLetterToSalesforceAsync(
                        data?.RecordId,
                        blobUrl,
                        data?.EntityName,
                        data?.AccountId,
                        data?.EntityId,
                        data?.CaseId
                    );

                    if (!notified)
                    {
                        _logger.LogWarning("Salesforce notification for EIN Letter PDF upload failed.");
                    }

                    return (blobUrl, true);
                }

                return (null, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CaptureSuccessPageAsPdf failed.");
                return (null, false);
            }
        }
        public async Task<(string? BlobUrl, bool Success)> CaptureFailurePageAsPdf(CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot capture failure PDF - Driver is null");
                    return (null, false);
                }

                // Generate a clean filename
                var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINSubmissionFailure.pdf";

                // Wait for page to be fully loaded
                await WaitForPageLoadAsync(cancellationToken);

                string? blobUrl = null;

                // --- Primary PDF generation using Chrome CDP ---
                if (Driver is ChromeDriver chromeDriver)
                {
                    try
                    {
                        var printOptions = new Dictionary<string, object>();

                        var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printOptions);

                        if (result is Dictionary<string, object> resultDict && resultDict.ContainsKey("data"))
                        {
                            var pdfData = resultDict["data"]?.ToString();
                            if (!string.IsNullOrEmpty(pdfData))
                            {
                                var pdfBytes = Convert.FromBase64String(pdfData);
                                await SaveConfirmationPdf(pdfBytes, $"{cleanName}-ID-EINSubmissionFailure.pdf", data, cancellationToken);
                                blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                                _logger.LogInformation($"Failure PDF successfully uploaded to: {blobUrl}");
                            }
                        }
                    }
                    catch (Exception cdpEx)
                    {
                        _logger.LogWarning($"Chrome CDP failure PDF generation failed, trying fallback: {cdpEx.Message}");
                    }
                }

                // --- Fallback if CDP method failed ---
                if (blobUrl == null)
                {
                    (blobUrl, bool fallbackSuccess) = await CapturePageAsPdfHtml2PdfFallback(data, cancellationToken);
                    if (!fallbackSuccess)
                    {
                        _logger.LogError("Both Chrome CDP and HTML2PDF fallback failed (failure PDF).");
                        return (null, false);
                    }
                }

                // --- Notify Salesforce if blob upload succeeded ---
                if (!string.IsNullOrEmpty(blobUrl))
                {
                    bool notified = await _salesforceClient!.NotifyFailureScreenshotUploadToSalesforceAsync(
                        data?.RecordId,
                        blobUrl,
                        data?.EntityName,
                        data?.AccountId,
                        data?.EntityId,
                        data?.CaseId
                    );

                    if (!notified)
                    {
                        _logger.LogWarning("Salesforce notification for EINSubmissionFailure PDF upload failed.");
                    }

                    return (blobUrl, true);
                }

                return (null, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CaptureFailurePageAsPdf failed.");
                return (null, false);
            }
        }
        private async Task<(string? BlobUrl, bool Success)> CapturePageAsPdfHtml2PdfFallback(CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot capture PDF - Driver is null");
                    return (null, false);
                }

                // Generate a clean filename for the fallback method
                var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-Fallback.pdf";

                // Set script timeout
                Driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(30);

                string html2pdfScript = @"
                    var callback = arguments[arguments.length - 1];
                    
                    function loadHtml2Pdf() {
                        if (window.html2pdf) {
                            generatePdf();
                            return;
                        }
                        
                        var script = document.createElement('script');
                        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/html2pdf.js/0.10.1/html2pdf.bundle.min.js';
                        script.onload = function() {
                            setTimeout(generatePdf, 500);
                        };
                        script.onerror = function() {
                            callback(JSON.stringify({ success: false, error: 'Failed to load html2pdf.js' }));
                        };
                        document.head.appendChild(script);
                    }
                    
                    function generatePdf() {
                        try {
                            var element = document.body;
                            var opt = {
                                margin: 10,
                                filename: 'document.pdf',
                                image: { type: 'jpeg', quality: 0.98 },
                                html2canvas: { 
                                    scale: 1,
                                    logging: false,
                                    useCORS: true,
                                    allowTaint: true,
                                    letterRendering: true
                                },
                                jsPDF: { 
                                    unit: 'mm', 
                                    format: 'a4', 
                                    orientation: 'portrait' 
                                }
                            };
                            
                            html2pdf()
                                .set(opt)
                                .from(element)
                                .toPdf()
                                .get('pdf')
                                .then(function(pdf) {
                                    var pdfBlob = pdf.output('blob');
                                    var reader = new FileReader();
                                    reader.onloadend = function() {
                                        var base64data = reader.result.split(',')[1];
                                        callback(JSON.stringify({ success: true, data: base64data }));
                                    };
                                    reader.onerror = function() {
                                        callback(JSON.stringify({ success: false, error: 'Failed to convert to base64' }));
                                    };
                                    reader.readAsDataURL(pdfBlob);
                                })
                                .catch(function(error) {
                                    callback(JSON.stringify({ success: false, error: error.toString() }));
                                });
                        } catch (error) {
                            callback(JSON.stringify({ success: false, error: error.toString() }));
                        }
                    }
                    
                    loadHtml2Pdf();
                ";

                // Execute the script with proper error handling
                var result = ((IJavaScriptExecutor)Driver).ExecuteAsyncScript(html2pdfScript);
                var resultJson = result?.ToString();

                if (!string.IsNullOrEmpty(resultJson))
                {
                    try
                    {
                        var resultData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);

                        if (resultData != null && resultData.ContainsKey("success"))
                        {
                            var successElement = resultData["success"];
                            bool success = false;

                            // Handle different JSON element types
                            if (successElement is System.Text.Json.JsonElement jsonElement)
                            {
                                success = jsonElement.GetBoolean();
                            }
                            else if (successElement is bool boolValue)
                            {
                                success = boolValue;
                            }

                            if (success && resultData.ContainsKey("data"))
                            {
                                var dataElement = resultData["data"];
                                string? base64Pdf = null;

                                if (dataElement is System.Text.Json.JsonElement jsonDataElement)
                                {
                                    base64Pdf = jsonDataElement.GetString();
                                }
                                else if (dataElement is string stringValue)
                                {
                                    base64Pdf = stringValue;
                                }

                                if (!string.IsNullOrEmpty(base64Pdf))
                                {
                                    var pdfBytes = Convert.FromBase64String(base64Pdf);
                                    var blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                                    _logger.LogInformation($"PDF successfully uploaded to: {blobUrl}");
                                    return (blobUrl, true);
                                }
                            }
                            else
                            {
                                var errorElement = resultData.ContainsKey("error") ? resultData["error"] : null;
                                string? error = null;

                                if (errorElement is System.Text.Json.JsonElement jsonErrorElement)
                                {
                                    error = jsonErrorElement.GetString();
                                }
                                else if (errorElement is string stringErrorValue)
                                {
                                    error = stringErrorValue;
                                }

                                _logger.LogError($"PDF generation failed: {error ?? "Unknown error"}");
                            }
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        _logger.LogError($"Failed to parse JSON result: {jsonEx.Message}");
                    }
                }

                _logger.LogError("PDF generation failed - no valid result");
                return (null, false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HTML2PDF fallback: {ex.Message}");
                return (null, false);
            }
        }
        private async Task WaitForPageLoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogWarning("Cannot wait for page load - Driver is null");
                    return;
                }

                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(10));
                await Task.Run(() =>
                {
                    wait.Until(driver => ((IJavaScriptExecutor)driver).ExecuteScript("return document.readyState").Equals("complete"));
                }, cancellationToken);

                // Additional wait for dynamic content
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Page load wait failed: {ex.Message}");
            }
        }
        public void ScrollToElement(By locator, string description = "element", bool center = true)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogWarning("Cannot scroll to {Description} - Driver is null", description);
                    return;
                }

                var element = WaitHelper.WaitUntilExists(Driver, locator, Timeout);
                var block = center ? "center" : "start";
                ((IJavaScriptExecutor)Driver).ExecuteScript($"arguments[0].scrollIntoView({{block: '{block}', inline: 'nearest'}});", element);
                Task.Delay(300).Wait();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scroll to {Description}", description);
                try
                {
                    if (Driver != null)
                    {
                        new Actions(Driver).MoveToElement(Driver.FindElement(locator)).Perform();
                        Task.Delay(200).Wait();
                    }
                }
                catch (Exception inner)
                {
                    _logger.LogWarning(inner, "Fallback scroll to {Description} via Actions failed", description);
                }
            }
        }
        public void ScrollToBottom(int additionalOffset = 0, int maxAttempts = 3)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogWarning("Cannot scroll to bottom - Driver is null");
                    return;
                }

                long lastHeight = 0;
                long currentHeight = 0;
                int attempts = 0;

                do
                {
                    lastHeight = currentHeight;

                    // Try multiple scroll height sources
                    ((IJavaScriptExecutor)Driver).ExecuteScript(
                        "window.scrollTo(0, Math.max(document.body.scrollHeight, document.documentElement.scrollHeight));"
                    );

                    Task.Delay(500).Wait(); // Longer delay for dynamic content

                    currentHeight = (long)((IJavaScriptExecutor)Driver).ExecuteScript(
                        "return Math.max(document.body.scrollHeight, document.documentElement.scrollHeight);"
                    );

                    attempts++;
                }
                while (currentHeight > lastHeight && attempts < maxAttempts);

                if (additionalOffset != 0)
                {
                    ((IJavaScriptExecutor)Driver).ExecuteScript("window.scrollBy(0, arguments[0]);", additionalOffset);
                }

                Task.Delay(300).Wait();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scroll to bottom");
            }
        }
        public void CaptureBrowserLogs()
        {
            try
            {
                if (Driver != null)
                {
                    var logs = Driver.Manage().Logs.GetLog(LogType.Browser);
                    ConsoleLogs.AddRange(logs.Select(log => new Dictionary<string, object?>
                    {
                        {"level", log.Level.ToString()},
                        {"message", log.Message}
                    }));

                    foreach (var log in logs)
                    {
                        _logger.LogDebug("Browser console: {Level} - {Message}", log.Level, log.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture browser logs");
            }
        }
        public void LogSystemResources(string? referenceId = null, CancellationToken cancellationToken = default)
        {
            var currentProcess = Process.GetCurrentProcess();
            var memoryUsed = currentProcess.WorkingSet64;
            var cpuTime = currentProcess.TotalProcessorTime;

            _logger.LogInformation("System Resources - Ref: {referenceId}, Memory: {memoryUsed} bytes, CPU Time: {cpuTime}", referenceId ?? "N/A", memoryUsed, cpuTime);
        }
        public abstract Task NavigateAndFillForm(CaseData? data, Dictionary<string, object?>? jsonData);
        public abstract Task HandleTrusteeshipEntity(CaseData? data);
        public abstract Task<(bool Success, string? Message, string? AzureBlobUrl)> RunAutomation(CaseData? data, Dictionary<string, object> jsonData);
        public string NormalizeState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                _logger.LogError("State cannot be empty");
                throw new ArgumentException("State cannot be empty", nameof(state));
            }

            var stateClean = state.ToUpper().Trim();

            var stateMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"ALABAMA", "AL"}, {"ALASKA", "AK"}, {"ARIZONA", "AZ"}, {"ARKANSAS", "AR"},
                {"CALIFORNIA", "CA"}, {"COLORADO", "CO"}, {"CONNECTICUT", "CT"}, {"DELAWARE", "DE"},
                {"FLORIDA", "FL"}, {"GEORGIA", "GA"}, {"HAWAII", "HI"}, {"IDAHO", "ID"},
                {"ILLINOIS", "IL"}, {"INDIANA", "IN"}, {"IOWA", "IA"}, {"KANSAS", "KS"},
                {"KENTUCKY", "KY"}, {"LOUISIANA", "LA"}, {"MAINE", "ME"}, {"MARYLAND", "MD"},
                {"MASSACHUSETTS", "MA"}, {"MICHIGAN", "MI"}, {"MINNESOTA", "MN"}, {"MISSISSIPPI", "MS"},
                {"MISSOURI", "MO"}, {"MONTANA", "MT"}, {"NEBRASKA", "NE"}, {"NEVADA", "NV"},
                {"NEW HAMPSHIRE", "NH"}, {"NEW JERSEY", "NJ"}, {"NEW MEXICO", "NM"}, {"NEW YORK", "NY"},
                {"NORTH CAROLINA", "NC"}, {"NORTH DAKOTA", "ND"}, {"OHIO", "OH"}, {"OKLAHOMA", "OK"},
                {"OREGON", "OR"}, {"PENNSYLVANIA", "PA"}, {"RHODE ISLAND", "RI"}, {"SOUTH CAROLINA", "SC"},
                {"SOUTH DAKOTA", "SD"}, {"TENNESSEE", "TN"}, {"TEXAS", "TX"}, {"UTAH", "UT"},
                {"VERMONT", "VT"}, {"VIRGINIA", "VA"}, {"WASHINGTON", "WA"}, {"WEST VIRGINIA", "WV"},
                {"WISCONSIN", "WI"}, {"WYOMING", "WY"}, {"DISTRICT OF COLUMBIA", "DC"}, {"SONOMA", ""}
            };

            if (stateClean.Length == 2 && stateMapping.Values.Contains(stateClean))
                return stateClean;

            if (stateMapping.ContainsKey(stateClean))
                return stateMapping[stateClean];

            foreach (var kvp in stateMapping)
            {
                if (stateClean == kvp.Key.ToUpper())
                    return kvp.Value;
            }

            var reverseMapping = stateMapping.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            if (reverseMapping.ContainsKey(stateClean))
                return stateClean;

            return stateClean;
        }
        public (string? Month, int Year) ParseFormationDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                _logger.LogWarning("Invalid date format: null or empty, using default date");
                return (null, 0);
            }

            var formats = new[] { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd", "MM/dd/yyyy", "yyyy/MM/dd" };

            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(dateStr.Trim(), fmt, null, System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    return (parsed.Month.ToString(), parsed.Year);
                }
            }

            _logger.LogWarning("Invalid date format: {DateStr}, using default date", dateStr);
            return (null, 0);
        }
        public Dictionary<string, object?> GetDefaults(CaseData? data)
        {
            if (data == null)
            {
                _logger.LogError("Cannot get defaults - CaseData is null");
                return new Dictionary<string, object?>();
            }

            var rawMembers = data.EntityMembers ?? new Dictionary<string, string>();

            var entityMembersDict = new Dictionary<string, string?>
            {
                {"first_name_1", (rawMembers.GetValueOrDefault("first_name_1") ?? "").Trim()},
                {"last_name_1", (rawMembers.GetValueOrDefault("last_name_1") ?? "").Trim()},
                {"middle_name_1", (rawMembers.GetValueOrDefault("middle_name_1") ?? "").Trim()},
                {"phone_1", (rawMembers.GetValueOrDefault("phone_1") ?? "").Trim()}
            };

            // Fix for MailingAddress now being a list
            var mailingAddress = (data.MailingAddress != null && data.MailingAddress.Count > 0) ? data.MailingAddress[0] : new Dictionary<string, string>();

            return new Dictionary<string, object?>
            {
                {"first_name", entityMembersDict["first_name_1"]},
                {"last_name", entityMembersDict["last_name_1"]},
                {"middle_name", entityMembersDict["middle_name_1"]},
                {"phone", entityMembersDict["phone_1"]},
                {"ssn_decrypted", data.SsnDecrypted ?? ""},
                {"entity_name", data.EntityName ?? ""},
                {"business_address_1", data.BusinessAddress1 ?? ""},
                {"business_address_2", data.BusinessAddress2 ?? ""},

                {"city", data.City ?? ""},
                {"zip_code", ExtractFirstFiveDigits(data.ZipCode)},
                {"business_description", data.BusinessDescription ?? "Any and lawful business"},
                {"formation_date", data.FormationDate ?? ""},
                {"county", data.County ?? ""},
                {"trade_name", data.TradeName ?? ""},
                {"care_of_name", data.CareOfName ?? ""},
                {"mailing_address", mailingAddress},
                {"closing_month", data.ClosingMonth ?? ""},
                {"filing_requirement", data.FilingRequirement ?? ""}
            };
        }
        
        public async Task InitializeDriver(bool useProxy, string? proxyHost, int? proxyPort, string? proxyUsername, string? proxyPassword, string recordId)
        {
            try
            {
                LogSystemResources(recordId); // You need to implement this

                if (useProxy) 
                {
                    // For AKS, we'll let DriverInitializer create a unique profile directory
                    // The download directory will be within the unique profile
                    ChromeDownloadDirectory = "/tmp/chrome-home/Downloads";
                    Console.WriteLine($"AKS: Initial ChromeDownloadDirectory set to: {ChromeDownloadDirectory}");
                    Driver = DriverInitializer.InitializeAKS(ChromeDownloadDirectory, recordId);
                    
                    // For the new approach, we need to get the actual download directory from the driver
                    // Since each profile has its own downloads folder, we'll use a pattern to find it
                    var tempPath = Path.GetTempPath();
                    var chromeProfileDirs = Directory.GetDirectories(tempPath, "chrome-profile-*");
                    
                    if (chromeProfileDirs.Length > 0)
                    {
                        // Use the most recent profile directory
                        var latestProfileDir = chromeProfileDirs.OrderByDescending(d => Directory.GetCreationTime(d)).First();
                        ChromeDownloadDirectory = Path.Combine(latestProfileDir, "downloads");
                        Console.WriteLine($"AKS: Found Chrome profile directory: {latestProfileDir}");
                        Console.WriteLine($"AKS: Final ChromeDownloadDirectory set to: {ChromeDownloadDirectory}");
                    }
                    else
                    {
                        // Fallback to the original approach
                        ChromeDownloadDirectory = Path.Combine(ChromeDownloadDirectory, recordId);
                        Console.WriteLine($"AKS: Fallback ChromeDownloadDirectory set to: {ChromeDownloadDirectory}");
                    }
                    
                    // Verify AKS directory structure
                    try
                    {
                        Console.WriteLine($"AKS: Download directory exists: {Directory.Exists(ChromeDownloadDirectory)} - {ChromeDownloadDirectory}");
                        
                        // Test write permissions
                        var testFile = Path.Combine(ChromeDownloadDirectory, "aks-test.tmp");
                        File.WriteAllText(testFile, "AKS write test");
                        File.Delete(testFile);
                        Console.WriteLine($"AKS: Directory {ChromeDownloadDirectory} is writable");
                    }
                    catch (Exception aksEx)
                    {
                        Console.WriteLine($"AKS: WARNING - Directory verification failed: {aksEx.Message}");
                    }
                }
                else 
                {
                    // Store the download directory path for PDF capture methods
                    ChromeDownloadDirectory = Path.Combine(Path.GetTempPath(), $"chrome-downloads-{recordId}");                    
                    Driver = DriverInitializer.InitializeLocal(ChromeDownloadDirectory, recordId);
                    // Update ChromeDownloadDirectory to point to the actual download directory (with recordId subdirectory)
                    ChromeDownloadDirectory = Path.Combine(ChromeDownloadDirectory, recordId);
                }

                //todo: the webDriverWaiter is not initialize for the AKS, pls explain?
                Wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(Timeout));
                
                Console.WriteLine("- WebDriver initialized successfully");

                _logger.LogInformation("WebDriver initialized successfully for local testing - Download directory: {ChromeDownloadDirectory}", ChromeDownloadDirectory);
                Console.WriteLine($"Final ChromeDownloadDirectory set to: {ChromeDownloadDirectory}");
                
                // Verify download directory configuration
                await VerifyDownloadDirectoryConfiguration();
                
                // Force Chrome to use the correct download directory via CDP
                await ConfigureDownloadDirectoryViaCDP();
                
                // Additional verification and cleanup
                await EnsureDownloadDirectoryIsCorrect();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize WebDriver: {ex.Message}");
                throw;
            }
        }
        private void LogChromeDriverDiagnostics()
        {
            try
            {
                _logger.LogInformation("=== Chrome Driver Diagnostics ===");

                // Check if Chromium is installed
                if (File.Exists("/usr/bin/chromium"))
                {
                    _logger.LogInformation("Chromium found at /usr/bin/chromium");
                }
                else
                {
                    _logger.LogWarning("Chromium not found at /usr/bin/chromium");
                }

                // Check ChromeDriver
                if (File.Exists("/usr/bin/chromedriver"))
                {
                    _logger.LogInformation("ChromeDriver found at /usr/bin/chromedriver");

                    // Try to get version info
                    try
                    {
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = "/usr/bin/chromedriver",
                            Arguments = "--version",
                            RedirectStandardOutput = true,
                            UseShellExecute = false
                        };

                        using (var process = Process.Start(processInfo))
                        {
                            var output = process!.StandardOutput.ReadToEnd();
                            _logger.LogInformation($"ChromeDriver version: {output.Trim()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not get ChromeDriver version");
                    }
                }
                else
                {
                    _logger.LogWarning("ChromeDriver not found at /usr/bin/chromedriver");
                }

                // Check environment variables
                var chromeBin = Environment.GetEnvironmentVariable("CHROME_BIN");
                var chromeDriverPath = Environment.GetEnvironmentVariable("CHROMEDRIVER_PATH");

                _logger.LogInformation($"CHROME_BIN environment variable: {chromeBin ?? "Not set"}");
                _logger.LogInformation($"CHROMEDRIVER_PATH environment variable: {chromeDriverPath ?? "Not set"}");

                _logger.LogInformation("=== End Chrome Driver Diagnostics ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Chrome driver diagnostics");
            }
        }
        private void LogDownloadDirectoryState(string context)
        {
            try
            {
                if (string.IsNullOrEmpty(ChromeDownloadDirectory))
                {
                    _logger.LogWarning($"{context}: ChromeDownloadDirectory is not configured");
                    return;
                }

                _logger.LogInformation($"{context}: Checking download directory: {ChromeDownloadDirectory}");
                
                if (Directory.Exists(ChromeDownloadDirectory))
                {
                    var files = Directory.GetFiles(ChromeDownloadDirectory);
                    var pdfFiles = Directory.GetFiles(ChromeDownloadDirectory, "*.pdf");
                    var crdownloadFiles = Directory.GetFiles(ChromeDownloadDirectory, "*.crdownload");
                    
                    _logger.LogInformation($"{context}: Directory exists. Total files: {files.Length}, PDF files: {pdfFiles.Length}, Downloading files: {crdownloadFiles.Length}");
                    
                    if (files.Length > 0)
                    {
                        _logger.LogInformation($"{context}: Files in directory: {string.Join(", ", files.Select(Path.GetFileName))}");
                    }
                }
                else
                {
                    _logger.LogWarning($"{context}: Download directory does not exist: {ChromeDownloadDirectory}");
                }

                // Also check the default downloads directory to see if files are being downloaded there
                var defaultDownloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(defaultDownloadsPath))
                {
                    var defaultFiles = Directory.GetFiles(defaultDownloadsPath);
                    var defaultPdfFiles = Directory.GetFiles(defaultDownloadsPath, "*.pdf");
                    var defaultCrdownloadFiles = Directory.GetFiles(defaultDownloadsPath, "*.crdownload");
                    
                    if (defaultFiles.Length > 0 || defaultPdfFiles.Length > 0 || defaultCrdownloadFiles.Length > 0)
                    {
                        _logger.LogWarning($"{context}: Found files in default Downloads directory! Total: {defaultFiles.Length}, PDF: {defaultPdfFiles.Length}, Downloading: {defaultCrdownloadFiles.Length}");
                        if (defaultFiles.Length > 0)
                        {
                            _logger.LogWarning($"{context}: Files in default Downloads: {string.Join(", ", defaultFiles.Select(Path.GetFileName))}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{context}: Error checking download directory: {ex.Message}");
            }
        }

        private async Task EnsureDownloadDirectoryIsCorrect()
        {
            try
            {
                if (Driver == null || string.IsNullOrEmpty(ChromeDownloadDirectory))
                {
                    _logger.LogWarning("Cannot ensure download directory is correct - Driver or ChromeDownloadDirectory is null");
                    return;
                }

                // Clear any existing files in the download directory
                if (Directory.Exists(ChromeDownloadDirectory))
                {
                    foreach (var file in Directory.GetFiles(ChromeDownloadDirectory))
                    {
                        try
                        {
                            File.Delete(file);
                            _logger.LogDebug("Cleaned up existing file in download directory: {File}", Path.GetFileName(file));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Could not delete existing file {File}: {Error}", Path.GetFileName(file), ex.Message);
                        }
                    }
                }

                // Ensure the directory exists and is writable
                Directory.CreateDirectory(ChromeDownloadDirectory);
                
                // Test write access
                var testFile = Path.Combine(ChromeDownloadDirectory, "test.txt");
                await File.WriteAllTextAsync(testFile, "test");
                File.Delete(testFile);
                
                _logger.LogInformation("Download directory is ready: {Directory}", ChromeDownloadDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error ensuring download directory is correct: {Error}", ex.Message);
            }
        }

        private async Task ConfigureDownloadDirectoryViaCDP()
        {
            try
            {
                if (Driver == null || string.IsNullOrEmpty(ChromeDownloadDirectory))
                {
                    _logger.LogWarning("Cannot configure download directory via CDP - Driver or ChromeDownloadDirectory is null");
                    return;
                }

                if (Driver is ChromeDriver chromeDriver)
                {
                    try
                    {
                        // Configure download behavior via CDP
                        var downloadBehavior = new Dictionary<string, object>
                        {
                            ["downloadPath"] = ChromeDownloadDirectory,
                            ["promptForDownload"] = false
                        };

                        chromeDriver.ExecuteCdpCommand("Page.setDownloadBehavior", downloadBehavior);
                        _logger.LogInformation("Successfully configured Chrome download behavior via CDP for directory: {DownloadDir}", ChromeDownloadDirectory);
                    }
                    catch (Exception cdpEx)
                    {
                        _logger.LogWarning("Failed to configure download directory via CDP: {Error}", cdpEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error configuring download directory via CDP: {Error}", ex.Message);
            }
        }

        private async Task VerifyDownloadDirectoryConfiguration()
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogWarning("Cannot verify download directory - Driver is null");
                    return;
                }

                // Check if the download directory exists and is writable
                if (!string.IsNullOrEmpty(ChromeDownloadDirectory))
                {
                    if (Directory.Exists(ChromeDownloadDirectory))
                    {
                        _logger.LogInformation("Download directory exists: {Directory}", ChromeDownloadDirectory);
                        
                        // Test write access
                        var testFile = Path.Combine(ChromeDownloadDirectory, "test.txt");
                        try
                        {
                            await File.WriteAllTextAsync(testFile, "test");
                            File.Delete(testFile);
                            _logger.LogInformation("Download directory is writable: {Directory}", ChromeDownloadDirectory);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Download directory is not writable: {Directory}, Error: {Error}", ChromeDownloadDirectory, ex.Message);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Download directory does not exist: {Directory}", ChromeDownloadDirectory);
                    }
                }

                // Verify Chrome download settings via JavaScript
                try
                {
                    var downloadSettings = ((IJavaScriptExecutor)Driver).ExecuteScript(@"
                        return {
                            defaultDirectory: window.chrome?.downloads?.defaultDirectory || 'Not available',
                            promptForDownload: window.chrome?.downloads?.promptForDownload || 'Not available'
                        };
                    ");
                    
                    _logger.LogInformation("Chrome download settings: {Settings}", downloadSettings);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not verify Chrome download settings via JavaScript: {Error}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error verifying download directory configuration: {Error}", ex.Message);
            }
        }

        public virtual Task<byte[]?> GetBrowserLogsAsync(CaseData? data, CancellationToken ct)
        {
            if (data == null || Driver == null)
            {
                _logger.LogError("Cannot get browser logs - CaseData or Driver is null");
                return Task.FromResult<byte[]?>(null);
            }

            try
            {
                var logs = Driver.Manage().Logs.GetLog(LogType.Browser);
                if (logs == null)
                    return Task.FromResult<byte[]?>(null);

                var formatted = string.Join("\n", logs.Select(log => $"{log.Timestamp} [{log.Level}] {log.Message}"));
                return Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(formatted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBrowserLogsAsync failed");
                return Task.FromResult<byte[]?>(null);
            }
        }
        public virtual async Task CleanupAsync(CancellationToken ct)
        {
            Cleanup();
            await Task.CompletedTask;
        }
        protected async Task<byte[]> DownloadBlobAsync(string? blobUrl, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(blobUrl))
            {
                _logger.LogError("Cannot download blob - blobUrl is null or empty");
                throw new ArgumentException("Blob URL cannot be null or empty", nameof(blobUrl));
            }

            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(blobUrl, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        /// <summary>
        /// Saves PDF with method identifier using the new tagging system for EIN Letter PDFs
        /// </summary>
        protected async Task SaveEinLetterPdfWithMethodIdentifier(byte[] pdfBytes, string methodName, CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (data?.RecordId != null && _blobStorageService != null && !string.IsNullOrEmpty(data.EntityName))
                {
                    // Create the clean entity name exactly like in the original blob naming structure
                    var cleanName = Regex.Replace(data.EntityName, @"[^\w\-]", "").Replace(" ", "");

                    // Use the original blob naming structure: EntityProcess/{RecordId}/{cleanName}-ID-EINLetter.pdf
                    // But append the method identifier at the end before .pdf
                    var blobName = $"EntityProcess/{data.RecordId}/{cleanName}-ID-EINLetter-{methodName}.pdf";

                    var blobUrl = await _blobStorageService.UploadEinLetterPdf(
                        pdfBytes,
                        blobName,
                        "application/pdf",
                        data.AccountId,
                        data.EntityId,
                        data.CaseId,
                        cancellationToken);

                    if (!string.IsNullOrEmpty(blobUrl))
                    {
                        _logger.LogInformation("💾 EIN LETTER PDF SAVED WITH METHOD ID: {MethodName} - {BlobName} - {BlobUrl}", methodName, blobName, blobUrl);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to save EIN Letter PDF with method identifier for method: {MethodName}", methodName);
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping EIN Letter PDF save with method identifier - RecordId, BlobStorageService, or EntityName is null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error saving EIN Letter PDF with method identifier for method {MethodName}: {Message}", methodName, ex.Message);
            }
        }

        /// <summary>
        /// Saves confirmation PDF with the new tagging system
        /// </summary>
        protected async Task SaveConfirmationPdf(byte[] pdfBytes, string fileName, CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (data?.RecordId != null && _blobStorageService != null && !string.IsNullOrEmpty(data.EntityName))
                {
                    // Create the clean entity name
                    var cleanName = Regex.Replace(data.EntityName, @"[^\w\-]", "").Replace(" ", "");

                    // Use standard blob naming for confirmation PDFs
                    var blobName = $"EntityProcess/{data.RecordId}/{fileName}";

                    var blobUrl = await _blobStorageService.UploadConfirmationPdf(
                        pdfBytes,
                        blobName,
                        "application/pdf",
                        data.AccountId,
                        data.EntityId,
                        data.CaseId,
                        cancellationToken);

                    if (!string.IsNullOrEmpty(blobUrl))
                    {
                        _logger.LogInformation("💾 CONFIRMATION PDF SAVED: {FileName} - {BlobName} - {BlobUrl}", fileName, blobName, blobUrl);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to save confirmation PDF: {FileName}", fileName);
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping confirmation PDF save - RecordId, BlobStorageService, or EntityName is null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error saving confirmation PDF {FileName}: {Message}", fileName, ex.Message);
            }
        }
    }
}