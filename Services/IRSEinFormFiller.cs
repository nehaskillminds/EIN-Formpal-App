using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using HtmlAgilityPack;
using EinAutomation.Api.Infrastructure;
using System.Net;
using System.Text;
using System.Reflection;
using OpenQA.Selenium.Interactions; // Optional, for Actions class

namespace EinAutomation.Api.Services
{
    public class IRSEinFormFiller : EinFormFiller
    {
        private readonly HttpClient _httpClient;
        private readonly IErrorMessageExtractionService _errorMessageExtractionService;

        public static readonly Dictionary<string, string> EntityTypeMapping = new Dictionary<string, string>
        {
            { "Sole Proprietorship", "Sole Proprietor" },
            { "Individual", "Sole Proprietor" },
            { "Partnership", "Partnership" },
            { "Joint venture", "Partnership" },
            { "Limited Partnership", "Partnership" },
            { "General partnership", "Partnership" },
            { "C-Corporation", "Corporations" },
            { "S-Corporation", "Corporations" },
            { "Professional Corporation", "Corporations" },
            { "Corporation", "Corporations" },
            { "Non-Profit Corporation", "View Additional Types, Including Tax-Exempt and Governmental Organizations" },
            { "Limited Liability", "Limited Liability Company (LLC)" },
            { "Company (LLC)", "Limited Liability Company (LLC)" },
            { "LLC", "Limited Liability Company (LLC)" },
            { "Limited Liability Company", "Limited Liability Company (LLC)" },
            { "Limited Liability Company (LLC)", "Limited Liability Company (LLC)" },
            { "Professional Limited Liability Company", "Limited Liability Company (LLC)" },
            { "Limited Liability Partnership", "Partnership" },
            { "LLP", "Partnership" },
            { "Professional Limited Liability Company (PLLC)", "Limited Liability Company (LLC)" },
            { "Association", "View Additional Types, Including Tax-Exempt and Governmental Organizations" },
            { "Co-ownership", "Partnership" },
            { "Doing Business As (DBA)", "Sole Proprietor" },
            { "Trusteeship", "Trusts" }
        };

        public static readonly Dictionary<string, string> RadioButtonMapping = new Dictionary<string, string>
        {
            { "Sole Proprietor", "sole" },
            { "Partnership", "partnerships" },
            { "Corporations", "corporations" },
            { "Limited Liability Company (LLC)", "limited" },
            { "Estate", "estate" },
            { "Trusts", "trusts" },
            { "View Additional Types, Including Tax-Exempt and Governmental Organizations", "viewadditional" }
        };

        public static readonly Dictionary<string, string> SubTypeMapping = new Dictionary<string, string>
        {
            { "Sole Proprietorship", "Sole Proprietor" },
            { "Individual", "Sole Proprietor" },
            { "Partnership", "Partnership" },
            { "Joint venture", "Joint Venture" },
            { "Limited Partnership", "Partnership" },
            { "General partnership", "Partnership" },
            { "C-Corporation", "Corporation" },
            { "S-Corporation", "S Corporation" },
            { "Professional Corporation", "Personal Service Corporation" },
            { "Corporation", "Corporation" },
            { "Non-Profit Corporation", "Non-Profit/Tax-Exempt Organization" },
            { "Limited Liability", "N/A" },
            { "Limited Liability Company (LLC)", "N/A" },
            { "LLC", "N/A" },
            { "Limited Liability Company", "N/A" },
            { "Professional Limited Liability Company", "N/A" },
            { "Limited Liability Partnership", "Partnership" },
            { "LLP", "Partnership" },
            { "Professional Limited Liability Company (PLLC)", "N/A" },
            { "Association", "N/A" },
            { "Co-ownership", "Partnership" },
            { "Doing Business As (DBA)", "N/A" },
            { "Trusteeship", "Irrevocable Trust" },
            { "Trusteeship-Revocable", "Revocable Trust" },
            { "Trusteeship-Irrevocable", "Irrevocable Trust" }
        };

        public static readonly Dictionary<string, string> SubTypeButtonMapping = new Dictionary<string, string>
        {
            { "Sole Proprietor", "sole" },
            { "Household Employer", "house" },
            { "Partnership", "parnership" },
            { "Joint Venture", "joint" },
            { "Corporation", "corp" },
            { "S Corporation", "scorp" },
            { "Personal Service Corporation", "personalservice" },
            { "Irrevocable Trust", "irrevocable" },
            { "Revocable Trust", "revocable" },
            { "Non-Profit/Tax-Exempt Organization", "othernonprofit" },
            { "Other", "other_option" }
        };

        public IRSEinFormFiller(
            ILogger<IRSEinFormFiller>? logger,
            IBlobStorageService? blobStorageService,
            ISalesforceClient? salesforceClient,
            HttpClient? httpClient,
            IErrorMessageExtractionService errorMessageExtractionService)
            : base(logger ?? throw new ArgumentNullException(nameof(logger)), 
                   blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService)),
                   salesforceClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _errorMessageExtractionService = errorMessageExtractionService ?? throw new ArgumentNullException(nameof(errorMessageExtractionService));
        }

        private async Task<bool> DetectAndHandleType2Failure(CaseData? data, Dictionary<string, object?>? jsonData)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot detect Type 2 failure - Driver is null");
                    return false;
                }

                var pageText = Driver?.PageSource?.ToLower() ?? string.Empty;
                if (pageText.Contains("we are unable to provide you with an ein"))
                {
                    string? referenceNumber = null;

                    // Primary attempt: Regex
                    var refMatch = Regex.Match(pageText, @"reference number\s+(\d+)");
                    if (refMatch.Success)
                    {
                        referenceNumber = refMatch.Groups[1].Value;
                        _logger.LogInformation("Extracted IRS Reference Number: {ReferenceNumber}", referenceNumber);
                    }
                    else
                    {
                        _logger.LogWarning("Primary reference number extraction failed. Attempting fallback with HtmlAgilityPack.");

                        try
                        {
                            var doc = new HtmlDocument();
                            doc.LoadHtml(Driver?.PageSource ?? string.Empty);

                            var textNodes = doc.DocumentNode.SelectNodes("//text()[contains(., 'reference number')]");
                            if (textNodes != null)
                            {
                                foreach (var node in textNodes)
                                {
                                    var match = Regex.Match(node.InnerText, @"reference number\s+(\d+)");
                                    if (match.Success)
                                    {
                                        referenceNumber = match.Groups[1].Value;
                                        _logger.LogInformation("Extracted IRS Reference Number via fallback: {ReferenceNumber}", referenceNumber);
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Fallback parsing of reference number failed: {Message}", ex.Message);
                        }
                    }

                    if (!string.IsNullOrEmpty(referenceNumber))
                    {
                        if (jsonData != null && Driver != null)
                        {
                            var errorMsg = _errorMessageExtractionService.ExtractErrorMessage(Driver);
                            jsonData["irs_reference_number"] = referenceNumber;
                            jsonData["error_message"] = errorMsg;
                            await _blobStorageService.UploadJsonData(jsonData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? new object()), data);
                        }
                        var (blobUrl, success) = await CaptureFailurePageAsPdf(data, CancellationToken.None);
                        if (success && !string.IsNullOrEmpty(data?.RecordId))
                        {
                            await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId!, referenceNumber, "fail", Driver);
                        }
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in DetectAndHandleType2Failure: {ex.Message}");
                return false;
            }
        }
        
        // Helper to remove special characters from addresses
        private static string CleanAddress(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            // Allow letters, numbers, spaces, hyphen (-), and forward slash (/)
            return Regex.Replace(input, @"[^a-zA-Z0-9\s\-\/]", "");
        }



        public override async Task NavigateAndFillForm(CaseData? data, Dictionary<string, object?>? jsonData)
        {
            try
            {
                LogSystemResources();
                if (data == null)
                {
                    throw new ArgumentNullException(nameof(data));
                }
                if (string.IsNullOrWhiteSpace(data.RecordId))
                {
                    throw new ArgumentNullException(nameof(data.RecordId), "RecordId is required");
                }
                if (string.IsNullOrWhiteSpace(data.FormType))
                {
                    throw new ArgumentNullException(nameof(data.FormType), "FormType is required");
                }
                _logger.LogInformation($"Navigating to IRS EIN form for record_id: {data.RecordId}");
                if (Driver == null)
                {
                    _logger.LogWarning("Driver was null; calling InitializeDriver()");
                    InitializeDriver();

                    if (Driver == null)
                    {
                        _logger.LogCritical("Driver still null after InitializeDriver()");
                        throw new InvalidOperationException("WebDriver is not initialized after InitializeDriver().");
                    }
                }
                Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                Driver.Navigate().GoToUrl("https://sa.www4.irs.gov/modiein/individual/index.jsp");
                _logger.LogInformation("Navigated to IRS EIN form");

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    var alert = new WebDriverWait(Driver, TimeSpan.FromSeconds(5)).Until(d =>
                    {
                        try { return d.SwitchTo().Alert(); } catch (NoAlertPresentException) { return null; }
                    });
                    if (alert != null)
                    {
                        var alertText = alert.Text ?? "Unknown alert";
                        alert.Accept();
                        _logger.LogInformation($"Handled alert popup: {alertText}");
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogDebug("No alert popup appeared");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), 10);
                    _logger.LogInformation("Page loaded successfully");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    var pageSource = GetTruncatedPageSource();
                    _logger.LogError($"Page load timeout. Current URL: {Driver?.Url ?? "N/A"}, Page source: {pageSource}");
                    throw new AutomationError("Page load timeout", "Failed to locate Begin Application button");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), "Begin Application"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to click Begin Application", "Button click unsuccessful after retries");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.Id("individual-leftcontent"), 10);
                    _logger.LogInformation("Main form content loaded");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to load main form content", "Element 'individual-leftcontent' not found");
                }

                var entityType = data.EntityType?.Trim() ?? string.Empty;
                var mappedType = EntityTypeMapping.GetValueOrDefault(entityType, string.Empty);
                var radioId = RadioButtonMapping.GetValueOrDefault(mappedType, string.Empty);
                if (string.IsNullOrEmpty(radioId) || !SelectRadio(radioId, $"Entity type: {mappedType}"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError($"Failed to select entity type: {mappedType}", $"Radio ID: {radioId}");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after entity type", "Continue button click unsuccessful");
                }

                if (!new[] { "Limited Liability Company (LLC)", "Estate" }.Contains(mappedType))
                {
                    var subType = SubTypeMapping.GetValueOrDefault(entityType, "Other");
                    
                    // Enhanced logic for non-profit organizations
                    if (entityType == "Non-Profit Corporation" || entityType == "Association")
                    {
                        var businessDesc = data.BusinessDescription?.ToLower() ?? string.Empty;
                        var nonprofitKeywords = new[] { "non-profit", "nonprofit", "charity", "charitable", "501(c)", "tax-exempt" };
                        
                        // For Non-Profit Corporation, always use Non-Profit/Tax-Exempt Organization
                        if (entityType == "Non-Profit Corporation")
                        {
                            subType = nonprofitKeywords.Any(keyword => businessDesc.Contains(keyword))
                                ? "Non-Profit/Tax-Exempt Organization"
                                : "Non-Profit/Tax-Exempt Organization"; // Default to non-profit for Non-Profit Corporation
                        }
                        // For Association, check business description to determine if it's non-profit
                        else if (entityType == "Association")
                        {
                            subType = nonprofitKeywords.Any(keyword => businessDesc.Contains(keyword))
                                ? "Non-Profit/Tax-Exempt Organization"
                                : "Other";
                        }
                        
                        _logger.LogInformation("Processing {EntityType} as non-profit organization. Business description: {BusinessDesc}, Selected sub-type: {SubType}", 
                            entityType, businessDesc, subType);
                    }
                    
                    var subTypeRadioId = SubTypeButtonMapping.GetValueOrDefault(subType, "other_option");
                    if (!SelectRadio(subTypeRadioId, $"Sub-type: {subType}"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to select sub-type: {subType}", $"Radio ID: {subTypeRadioId}");
                    }
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue sub-type (first click)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after sub-type selection (first click)");
                    }
                    await Task.Delay(500);
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue sub-type (second click)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after sub-type selection (second click)");
                    }
                }
                else
                {
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after entity type"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after entity type");
                    }
                }

                if (mappedType == "Limited Liability Company (LLC)")
                {
                    // --- numberOfMembers robust handling ---
                    int llcMembers = 1;
                    var llcMembersRaw = data.LlcDetails?.NumberOfMembers;
                    llcMembers = ParseFlexibleInt(llcMembersRaw, 1);
                    if (llcMembers < 1) llcMembers = 1;
                    try
                    {
                        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                        var field = WaitHelper.WaitUntilExists(Driver, By.XPath("//input[@id='numbermem' or @name='numbermem']"), 10);
                        ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", field);
                        field.Clear();
                        await Task.Delay(200);
                        field.SendKeys(llcMembers.ToString());
                        _logger.LogInformation($"Filled LLC members: {llcMembers}");
                    }
                    catch (WebDriverTimeoutException)
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill LLC members", "Timeout waiting for element");
                    }
                    catch (NoSuchElementException ex)
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill LLC members", ex.Message);
                    }
                    var stateValue = NormalizeState(data.EntityState ?? data.EntityStateRecordState ?? string.Empty);
                    if (!SelectDropdown(By.Id("state"), stateValue, "State"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to select state: {stateValue}");
                    }
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after LLC members and state");
                    }
                }

                var specificStates = new HashSet<string> { "AZ", "CA", "ID", "LA", "NV", "NM", "TX", "WA", "WI" };

                if (mappedType == "Limited Liability Company (LLC)" &&
                    specificStates.Contains(NormalizeState(data.EntityState ?? string.Empty)))
                {
                    try
                    {
                        var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(5));
                        var radioElement = wait.Until(driver =>
                        {
                            var elements = driver.FindElements(By.Id("radio_n"));
                            return elements.Count > 0 ? elements[0] : null;
                        });

                        if (radioElement != null)
                        {
                            if (!SelectRadio("radio_n", "Non-partnership LLC option"))
                            {
                                CaptureBrowserLogs();
                                throw new AutomationError("Failed to select non-partnership LLC option");
                            }

                            if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after radio_n"))
                            {
                                CaptureBrowserLogs();
                                throw new AutomationError("Failed to continue after non-partnership LLC option");
                            }
                        }
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("'radio_n' not found within 5 seconds. Skipping selection and continuing.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Unexpected error while handling non-partnership LLC option: {ex.Message}");
                    }

                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after confirmation"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after confirmation");
                    }
                }
                else
                {
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after LLC"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after LLC");
                    }
                }

                if (!SelectRadio("newbiz", "New Business"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select new business");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after business purpose");
                }


                var defaults = GetDefaults(data);
                var firstName = data.EntityMembers?.GetValueOrDefault<string, string>("first_name_1", (string?)defaults["first_name"]!) ?? (string?)defaults["first_name"] ?? string.Empty;
                var lastName = data.EntityMembers?.GetValueOrDefault<string, string>("last_name_1", (string?)defaults["last_name"]!) ?? (string?)defaults["last_name"] ?? string.Empty;
                var middleName = data.EntityMembers?.GetValueOrDefault<string, string>("middle_name_1", (string?)defaults["middle_name"]!) ?? (string?)defaults["middle_name"] ?? string.Empty;

                if (new[] { "Sole Proprietorship", "Individual" }.Contains(data.EntityType ?? string.Empty))
                {
                    if (!FillField(By.Id("applicantFirstName"), firstName, "First Name (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill First Name: {firstName}");
                    }
                    if (!string.IsNullOrEmpty(middleName) && !FillField(By.Id("applicantMiddleName"), middleName, "Middle Name (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Middle Name: {middleName}");
                    }
                    if (!FillField(By.Id("applicantLastName"), lastName, "Last Name (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Last Name: {lastName}");
                    }
                }
                else
                {
                    if (!FillField(By.Id("responsiblePartyFirstName"), firstName, "First Name"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill First Name: {firstName}");
                    }
                    if (!string.IsNullOrEmpty(middleName) && !FillField(By.Id("responsiblePartyMiddleName"), middleName, "Middle Name"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Middle Name: {middleName}");
                    }
                    if (!FillField(By.Id("responsiblePartyLastName"), lastName, "Last Name"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Last Name: {lastName}");
                    }
                }

                var ssn = (string?)defaults["ssn_decrypted"] ?? string.Empty;
                ssn = ssn.Replace("-", "");
                if (new[] { "Sole Proprietorship", "Individual" }.Contains(data.EntityType ?? string.Empty))
                {
                    if (!FillField(By.Id("applicantSSN3"), ssn.Substring(0, 3), "SSN First 3 (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN First 3");
                    }
                    if (!FillField(By.Id("applicantSSN2"), ssn.Substring(3, 2), "SSN Middle 2 (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Middle 2");
                    }
                    if (!FillField(By.Id("applicantSSN4"), ssn.Substring(5), "SSN Last 4 (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Last 4");
                    }
                }
                else
                {
                    if (!FillField(By.Id("responsiblePartySSN3"), ssn.Substring(0, 3), "SSN First 3"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN First 3");
                    }
                    if (!FillField(By.Id("responsiblePartySSN2"), ssn.Substring(3, 2), "SSN Middle 2"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Middle 2");
                    }
                    if (!FillField(By.Id("responsiblePartySSN4"), ssn.Substring(5), "SSN Last 4"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Last 4");
                    }
                }

                if (!SelectRadio("iamsole", "I Am Sole"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select I Am Sole");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after responsible party");
                }



                var address1 = CleanAddress(defaults["business_address_1"]?.ToString()?.Trim() ?? string.Empty);
                var address2 = CleanAddress(defaults["business_address_2"]?.ToString()?.Trim() ?? string.Empty);
                var fullAddress = string.Join(" ", new[] { address1, address2 }.Where(s => !string.IsNullOrEmpty(s)));

                if (!FillField(By.Id("physicalAddressStreet"), fullAddress, "Street"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical Street");
                }

                if (!FillField(By.Id("physicalAddressCity"), CleanAddress(defaults["city"]?.ToString()), "Physical City"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical City");
                }

                if (!SelectDropdown(By.Id("physicalAddressState"), NormalizeState(data.EntityState ?? string.Empty), "Physical State"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Physical State");
                }

                if (!FillField(By.Id("physicalAddressZipCode"), CleanAddress(defaults["zip_code"]?.ToString()), "Physical Zip"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical Zip");
                }


                var phone = defaults["phone"]?.ToString() ?? "2812173123";
                var phoneClean = Regex.Replace(phone, @"\D", "");
                if (phoneClean.Length == 10)
                {
                    if (!FillField(By.Id("phoneFirst3"), phoneClean.Substring(0, 3), "Phone First 3"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Phone First 3");
                    }
                    if (!FillField(By.Id("phoneMiddle3"), phoneClean.Substring(3, 3), "Phone Middle 3"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Phone Middle 3");
                    }
                    if (!FillField(By.Id("phoneLast4"), phoneClean.Substring(6), "Phone Last 4"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Phone Last 4");
                    }
                }

                var allowedEntityTypes = new[] { "C-Corporation", "S-Corporation", "Professional Corporation", "Corporation" };
                if (!string.IsNullOrEmpty(data.CareOfName) && allowedEntityTypes.Contains(data.EntityType ?? string.Empty))
                {
                    try
                    {
                        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                        WaitHelper.WaitUntilExists(Driver, By.Id("physicalAddressCareofName"), 10);
                        // Clean the care of name using the same regex rules as legal name
                        var careOfName = data.CareOfName?.Trim() ?? string.Empty;
                        var cleanedCareOfName = Regex.Replace(careOfName, @"[^a-zA-Z0-9\s\-&]", "");
                        
                        _logger.LogInformation("Original care of name: {Original}", careOfName);
                        _logger.LogInformation("Cleaned care of name: {Cleaned}", cleanedCareOfName);
                        
                        if (!FillField(By.Id("physicalAddressCareofName"), cleanedCareOfName, "Physical Care of Name"))
                        {
                            _logger.LogWarning("Failed to fill Physical Care of Name, proceeding");
                        }
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("Physical Care of Name field not found");
                    }
                    catch (NoSuchElementException ex)
                    {
                        _logger.LogInformation($"Physical Care of Name field not found: {ex.Message}");
                    }
                }

                // Support MailingAddress as array
                var mailingAddressDict = (data.MailingAddress != null && data.MailingAddress.Count > 0)
                    ? data.MailingAddress[0]
                    : new Dictionary<string, string>();

                var mailingStreet = mailingAddressDict.GetValueOrDefault("mailingStreet", "").Trim();
                var physicalStreet1 = defaults["business_address_1"]?.ToString()?.Trim() ?? string.Empty;
                var physicalStreet2 = defaults["business_address_2"]?.ToString()?.Trim() ?? string.Empty;
                var physicalFullAddress = string.Join(" ", new[] { physicalStreet1, physicalStreet2 }.Where(s => !string.IsNullOrEmpty(s))).Trim();

                var shouldFillMailing = !string.IsNullOrEmpty(mailingStreet) &&
                                        !string.Equals(mailingStreet, physicalFullAddress, StringComparison.OrdinalIgnoreCase);

                if (shouldFillMailing)
                {
                    if (!SelectRadio("radioAnotherAddress_y", "Address option (Yes)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to select Address option (Yes)");
                    }
                }
                else
                {
                    if (!SelectRadio("radioAnotherAddress_n", "Address option (No)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to select Address option (No)");
                    }
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after address option"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after address option");
                }

                try
                {
                    var shortWait = new WebDriverWait(Driver, TimeSpan.FromSeconds(20));
                    var element = WaitHelper.WaitUntilClickable(Driver, By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']"), 20);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                    element.Click();
                    _logger.LogInformation("Clicked Accept As Entered");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Accept As Entered button not found within 20 seconds, proceeding.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Unexpected error while clicking Accept As Entered: {ex.Message}");
                }

                if (shouldFillMailing)
                {
                    if (!FillField(By.Id("mailingAddressStreet"), CleanAddress(mailingStreet), "Mailing Street"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing Street");
                    }
                    if (!FillField(By.Id("mailingAddressCity"), CleanAddress(mailingAddressDict.GetValueOrDefault("mailingCity", "")), "Mailing City"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing City");
                    }
                    if (!FillField(By.Id("mailingAddressState"), CleanAddress(mailingAddressDict.GetValueOrDefault("mailingState", "")), "Mailing State"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to select Mailing State");
                    }
                    if (!FillField(By.Id("mailingAddressPostalCode"), CleanAddress(mailingAddressDict.GetValueOrDefault("mailingZip", "")), "Zip"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing Zip");
                    }
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after mailing address"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after mailing address");
                    }
                    try
                    {
                        var shortWait = new WebDriverWait(Driver, TimeSpan.FromSeconds(20));
                        var element = WaitHelper.WaitUntilClickable(Driver, By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']"), 20);
                        ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                        element.Click();
                        _logger.LogInformation("Clicked Accept As Entered");
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("Accept As Entered button not found within 20 seconds, proceeding.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Unexpected error while clicking Accept As Entered: {ex.Message}");
                    }
                }


                var suffixRulesByGroup = new Dictionary<string, string[]>
                {
                    {"sole", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"partnerships", new[] {"Corp", "LLC", "PLLC", "LC", "Inc", "PA"}},
                    {"corporations", new[] {"LLC", "PLLC", "LC"}},
                    {"limited", new[] {"Corp", "Inc", "PA"}},
                    {"trusts", new[] {"Corp", "LLC", "PLLC", "LC", "Inc", "PA"}},
                    {"estate", new[] {"Corp", "LLC", "PLLC", "LC", "Inc", "PA"}},
                    {"othernonprofit", new[] {"Corp", "Inc", "PA"}}
                };

                string? businessName;
                try
                {
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                    var originalName = businessName;

                    var entityTypeLabel = data.EntityType?.Trim() ?? string.Empty;
                    mappedType = EntityTypeMapping.GetValueOrDefault(entityTypeLabel, string.Empty).Trim();
                    var entityGroup = RadioButtonMapping.GetValueOrDefault(mappedType);

                    if (!string.IsNullOrEmpty(entityGroup))
                    {
                        var suffixes = suffixRulesByGroup.GetValueOrDefault(entityGroup, new string[] { });
                        foreach (var suffix in suffixes)
                        {
                            if (Regex.IsMatch(businessName, $@"\b{suffix}\s*$", RegexOptions.IgnoreCase))
                            {
                                businessName = Regex.Replace(businessName, $@"\b{suffix}\s*$", "", RegexOptions.IgnoreCase).Trim();
                                _logger.LogInformation($"Stripped suffix '{suffix}' from business name: '{originalName}' -> '{businessName}'");
                                break;
                            }
                        }
                    }

                    businessName = Regex.Replace(businessName, @"[^a-zA-Z0-9\s\-&]", "");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to process business name: {ex.Message}");
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                }

                try
                {
                    var entityGroup = RadioButtonMapping.GetValueOrDefault(mappedType);
                    if (entityGroup == "sole")
                    {
                        if (!FillField(By.Id("businessOperationalTradeName"), businessName, "Trade Name (Sole Prop/Individual)"))
                        {
                            _logger.LogInformation("Failed to fill business name in trade name field");
                        }
                    }
                    else
                    {
                        if (!FillField(By.Id("businessOperationalLegalName"), businessName, "Legal Business Name"))
                        {
                            _logger.LogInformation("Failed to fill business name in legal name field");
                        }
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Business name field not found");
                }
                catch (NoSuchElementException ex)
                {
                    _logger.LogInformation($"Business name field not found: {ex.Message}");
                }

                if (!FillField(By.Id("businessOperationalCounty"), data.County ?? string.Empty, "County"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill County");
                }

                try
                {
                    SelectDropdown(By.Id("businessOperationalState"), NormalizeState(data.EntityState ?? string.Empty), "Business Operational State");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Business Operational State dropdown not found");
                }
                catch (NoSuchElementException ex)
                {
                    _logger.LogInformation($"Business Operational State dropdown not found: {ex.Message}");
                }

                
                var filingState = data.FilingState;
                var entityTypesRequiringArticles = new[] { "C-Corporation", "S-Corporation", "Professional Corporation", "Corporation", "Limited Liability Company", "Professional Limited Liability Company", "Limited Liability Company (LLC)", "Professional Limited Liability Company (PLLC)", "LLC" };
                if (entityTypesRequiringArticles.Contains(data.EntityType ?? string.Empty))
                {
                    try
                    {   _logger.LogInformation("Attempting to select Articles Filed State. FilingState value: '{FilingState}'", filingState);
                        SelectDropdown(By.Id("articalsFiledState"), NormalizeState(data.FilingState ?? string.Empty), "Articles Filed State");
                        _logger.LogInformation("Selected Articles Filed State");
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("Articles Filed State dropdown not found");
                    }
                    catch (NoSuchElementException ex)
                    {
                        _logger.LogInformation($"Articles Filed State dropdown not found: {ex.Message}");
                    }
                }

                try
                {
                    if (!string.IsNullOrEmpty(data.TradeName))
                    {
                        var tradeName = data.TradeName?.Trim() ?? string.Empty;
                        var entityName = (string?)defaults["entity_name"] ?? string.Empty;

                        var entityTypeLabel = data.EntityType?.Trim() ?? string.Empty;
                        var localMappedType = EntityTypeMapping.GetValueOrDefault(entityTypeLabel, string.Empty).Trim();
                        var entityGroup = RadioButtonMapping.GetValueOrDefault(mappedType, "");

                        var suffixesByGroup = new Dictionary<string, string[]>
                {
                    {"sole", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"partnerships", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"corporations", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"limited", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"trusts", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"estate", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"othernonprofit", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}}
                };

                        string NormalizeName(string name, string group)
                        {
                            string result = Regex.Replace(name, @"[^a-zA-Z0-9\s\-&]", "").Trim();
                            var suffixes = suffixesByGroup.GetValueOrDefault(group, new string[] { });

                            foreach (var suffix in suffixes)
                            {
                                if (Regex.IsMatch(result, $@"\b{suffix}\s*$", RegexOptions.IgnoreCase))
                                {
                                    result = Regex.Replace(result, $@"\b{suffix}\s*$", "", RegexOptions.IgnoreCase).Trim();
                                    break;
                                }
                            }

                            return result;
                        }

                        var normalizedTrade = NormalizeName(tradeName, entityGroup);
                        var normalizedEntity = NormalizeName(entityName, entityGroup);

                        if (!string.Equals(normalizedTrade, normalizedEntity, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation($"Filling Trade Name since it differs from Entity Name: '{normalizedTrade}' != '{normalizedEntity}'");

                            if (!FillField(By.Id("businessOperationalTradeName"), normalizedTrade, "Trade Name"))
                            {
                                CaptureBrowserLogs();
                                throw new AutomationError("Failed to fill Trade Name");
                            }
                        }
                        else
                        {
                            _logger.LogInformation("Skipping Trade Name input as it's same as Entity Name after normalization.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not process or fill Trade Name: {ex.Message}");
                }


                // --- startDate robust handling ---
                var (month, year) = ParseFlexibleDate(data?.FormationDate ?? string.Empty);
                if (!SelectDropdown(By.Id("BUSINESS_OPERATIONAL_MONTH_ID"), month?.ToString(), "Formation Month"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Formation Month");
                }
                if (!FillField(By.Id("BUSINESS_OPERATIONAL_YEAR_ID"), year.ToString(), "Formation Year"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Formation Year");
                }

                // --- closingMonth robust handling ---
                string closingMonthRaw = data?.ClosingMonth?.ToString() ?? string.Empty;
                if (int.TryParse(closingMonthRaw, out var closingMonthInt))
                    closingMonthRaw = closingMonthInt.ToString();

                if (!string.IsNullOrEmpty(closingMonthRaw))
                {
                    var monthMapping = new Dictionary<string, string>
                    {
                        {"january", "JANUARY"}, {"jan", "JANUARY"}, {"1", "JANUARY"},
                        {"february", "FEBRUARY"}, {"feb", "FEBRUARY"}, {"2", "FEBRUARY"},
                        {"march", "MARCH"}, {"mar", "MARCH"}, {"3", "MARCH"},
                        {"april", "APRIL"}, {"apr", "APRIL"}, {"4", "APRIL"},
                        {"may", "MAY"}, {"5", "MAY"},
                        {"june", "JUNE"}, {"jun", "JUNE"}, {"6", "JUNE"},
                        {"july", "JULY"}, {"jul", "JULY"}, {"7", "JULY"},
                        {"august", "AUGUST"}, {"aug", "AUGUST"}, {"8", "AUGUST"},
                        {"september", "SEPTEMBER"}, {"sep", "SEPTEMBER"}, {"9", "SEPTEMBER"},
                        {"october", "OCTOBER"}, {"oct", "OCTOBER"}, {"10", "OCTOBER"},
                        {"november", "NOVEMBER"}, {"nov", "NOVEMBER"}, {"11", "NOVEMBER"},
                        {"december", "DECEMBER"}, {"dec", "DECEMBER"}, {"12", "DECEMBER"}
                    };

                    var entityTypesRequiringFiscalMonth = new[] { "Partnership", "Joint venture", "Limited Partnership", "General partnership", "C-Corporation", "Limited Liability Partnership", "LLP", "Corporation" };

                    if (entityTypesRequiringFiscalMonth.Contains(data.EntityType ?? string.Empty))
                    {
                        var normalizedMonth = monthMapping.GetValueOrDefault(closingMonthRaw.ToLower().Trim());
                        if (!string.IsNullOrEmpty(normalizedMonth))
                        {
                            const int retries = 2;
                            for (int attempt = 0; attempt < retries; attempt++)
                            {
                                try
                                {
                                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                                    var dropdown = WaitHelper.WaitUntilClickable(Driver, By.Id("fiscalMonth"), 10);
                                    new SelectElement(dropdown).SelectByText(normalizedMonth);
                                    _logger.LogInformation($"Selected Fiscal Month: {normalizedMonth}");
                                    break;
                                }
                                catch (WebDriverTimeoutException)
                                {
                                    if (attempt < retries - 1)
                                    {
                                        _logger.LogWarning($"Attempt {attempt + 1} to select Fiscal Month failed");
                                        await Task.Delay(1000);
                                    }
                                    else
                                    {
                                        CaptureBrowserLogs();
                                        throw new AutomationError($"Failed to select Fiscal Month {normalizedMonth}");
                                    }
                                }
                                catch (NoSuchElementException ex)
                                {
                                    if (attempt < retries - 1)
                                    {
                                        _logger.LogWarning($"Attempt {attempt + 1} to select Fiscal Month failed: {ex.Message}");
                                        await Task.Delay(1000);
                                    }
                                    else
                                    {
                                        CaptureBrowserLogs();
                                        throw new AutomationError($"Failed to select Fiscal Month {normalizedMonth}", ex.Message);
                                    }
                                }
                                catch (StaleElementReferenceException ex)
                                {
                                    if (attempt < retries - 1)
                                    {
                                        _logger.LogWarning($"Attempt {attempt + 1} to select Fiscal Month failed: {ex.Message}");
                                        await Task.Delay(1000);
                                    }
                                    else
                                    {
                                        CaptureBrowserLogs();
                                        throw new AutomationError($"Failed to select Fiscal Month {normalizedMonth}", ex.Message);
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Invalid closing_month: {closingMonthRaw}, skipping");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Skipping fiscal month selection for entity_type: {data.EntityType}");
                    }
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after formation date");
                }

                var activityRadios = new[] { "radioTrucking_n", "radioInvolveGambling_n", "radioExciseTax_n", "radioSellTobacco_n", "radioHasEmployees_n" };
                foreach (var radio in activityRadios)
                {
                    if (!SelectRadio(radio, radio))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to select {radio}");
                    }
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after activity options");
                }

                if (!SelectRadio("other", "Other activity"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Other activity");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after primary activity");
                }

                if (!SelectRadio("other", "Other service"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Other service");
                }
                // Clean the business description to remove special characters
                var businessDescription = defaults["business_description"]?.ToString();
                var cleanedBusinessDescription = CleanBusinessDescription(businessDescription);
                
                _logger.LogInformation("Original business description: {Original}", businessDescription);
                _logger.LogInformation("Cleaned business description: {Cleaned}", cleanedBusinessDescription);
                
                if (!FillField(By.Id("pleasespecify"), cleanedBusinessDescription, "Business Description"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Business Description");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after specify service");
                }

                if (!SelectRadio("receiveonline", "Receive Online"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Receive Online");
                }

                if (ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after receive EIN"))
                {
                    CaptureBrowserLogs();

                        // Capture confirmation page using the base CapturePageAsPdf method
                        var (blobUrl, success) = await CapturePageAsPdf(data, CancellationToken.None);

                    if (success && !string.IsNullOrEmpty(blobUrl))
                    {
                            _logger.LogInformation($"Confirmation page PDF uploaded to Azure: {blobUrl}");
                    }
                    else
                    {
                            _logger.LogWarning("Failed to capture and upload confirmation page PDF");
                    }
                }
                else
                {
                    throw new AutomationError("Failed to continue after receive EIN selection");
                }

                _logger.LogInformation("Form filled successfully");
            }
            catch (WebDriverTimeoutException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (NoSuchElementException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (ElementNotInteractableException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (StaleElementReferenceException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (WebDriverException ex)
            {
                CaptureBrowserLogs();
                if (File.Exists(DriverLogPath))
                {
                    try
                    {
                        var driverLogs = await File.ReadAllTextAsync(DriverLogPath);
                        _logger.LogError($"ChromeDriver logs: {driverLogs}");
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogError($"Failed to read ChromeDriver logs: {logEx.Message}");
                    }
                }
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"WebDriver error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("WebDriver error", ex.Message);
            }
            catch (Exception ex)
            {
                CaptureBrowserLogs();
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"Unexpected error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");

                try
                {
                    var handled = await DetectAndHandleType2Failure(data, jsonData);
                    if (handled)
                    {
                        _logger.LogWarning("Handled as Type 2 EIN failure during form fill. Skipping exception raise.");
                        return;
                    }
                }
                catch (Exception err)
                {
                    _logger.LogError($"Type 2 handler failed while processing EIN failure page: {err.Message}");
                }

                throw new AutomationError("Unexpected form filling error", ex.Message);
            }

        }

        public override async Task HandleTrusteeshipEntity(CaseData? data)
        {
            _logger.LogInformation("Handling Trusteeship entity type form flow");
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (string.IsNullOrWhiteSpace(data.EntityType))
            {
                throw new ArgumentNullException(nameof(data.EntityType), "EntityType is required");
            }
            var defaults = GetDefaults(data);

            try
            {
                LogSystemResources();
                _logger.LogInformation($"Navigating to IRS EIN form for record_id: {data.RecordId}");
                if (Driver == null)
                {
                    _logger.LogWarning("Driver was null; calling InitializeDriver()");
                    InitializeDriver();

                    if (Driver == null)
                    {
                        _logger.LogCritical("Driver still null after InitializeDriver()");
                        throw new InvalidOperationException("WebDriver is not initialized after InitializeDriver().");
                    }
                }
                Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                Driver.Navigate().GoToUrl("https://sa.www4.irs.gov/modiein/individual/index.jsp");
                _logger.LogInformation("Navigated to IRS EIN form");

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    var alert = new WebDriverWait(Driver, TimeSpan.FromSeconds(5)).Until(d =>
                    {
                        try { return d.SwitchTo().Alert(); } catch (NoAlertPresentException) { return null; }
                    });
                    if (alert != null)
                    {
                        var alertText = alert.Text ?? "Unknown alert";
                        alert.Accept();
                        _logger.LogInformation($"Handled alert popup: {alertText}");
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogDebug("No alert popup appeared");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), 10);
                    _logger.LogInformation("Page loaded successfully");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    var pageSource = GetTruncatedPageSource();
                    _logger.LogError($"Page load timeout. Current URL: {Driver?.Url ?? "N/A"}, Page source: {pageSource}");
                    throw new AutomationError("Page load timeout", "Failed to locate Begin Application button");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), "Begin Application"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to click Begin Application", "Button click unsuccessful after retries");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.Id("individual-leftcontent"), 10);
                    _logger.LogInformation("Main form content loaded");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to load main form content", "Element 'individual-leftcontent' not found");
                }

                var entityType = data.EntityType?.Trim() ?? string.Empty;
                var mappedType = EntityTypeMapping.GetValueOrDefault(entityType, "Trusts");
                _logger.LogInformation($"Mapped entity type: {entityType} -> {mappedType}");
                var radioId = RadioButtonMapping.GetValueOrDefault(mappedType);
                if (string.IsNullOrEmpty(radioId) || !SelectRadio(radioId, $"Entity type: {mappedType}"))
                {
                    throw new AutomationError("Failed to select Trusteeship entity type");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after entity type"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after entity type");
                }

                // Determine sub-type based on TrustType from payload
                string subType;
                if (entityType == "Trusteeship")
                {
                    var trustType = data.TrustType?.Trim() ?? string.Empty;
                    _logger.LogInformation($"TrustType from payload: {trustType}");
                    
                    if (trustType.Equals("Revocable", StringComparison.OrdinalIgnoreCase))
                    {
                        subType = "Revocable Trust";
                        _logger.LogInformation("Selected Revocable Trust sub-type based on TrustType: Revocable");
                    }
                    else if (trustType.Equals("Irrevocable", StringComparison.OrdinalIgnoreCase))
                    {
                        subType = "Irrevocable Trust";
                        _logger.LogInformation("Selected Irrevocable Trust sub-type based on TrustType: Irrevocable");
                    }
                    else
                    {
                        // Default to Irrevocable Trust if TrustType is not specified or invalid
                        subType = "Irrevocable Trust";
                        _logger.LogWarning($"Invalid or missing TrustType: '{trustType}'. Defaulting to Irrevocable Trust");
                    }
                }
                else
                {
                    subType = SubTypeMapping.GetValueOrDefault(entityType, "Other");
                }
                
                var subRadioId = SubTypeButtonMapping.GetValueOrDefault(subType, "other_option");
                if (!SelectRadio(subRadioId, $"Sub-type: {subType}"))
                {
                    throw new AutomationError("Failed to select Trusteeship sub-type");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after sub-type"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after sub-type");
                }
                CaptureBrowserLogs();
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after sub-type"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after sub-type");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='responsiblePartyFirstName']"), (string?)defaults["first_name"] ?? string.Empty, "Responsible First Name"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Responsible First Name");
                }
                CaptureBrowserLogs();

                if (!string.IsNullOrEmpty((string?)defaults["middle_name"]) && !FillField(By.XPath("//input[@id='responsiblePartyMiddleName']"), (string?)defaults["middle_name"] ?? string.Empty, "Responsible Middle Name"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Responsible Middle Name");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='responsiblePartyLastName']"), (string?)defaults["last_name"] ?? string.Empty, "Responsible Last Name"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Responsible Last Name");
                }
                CaptureBrowserLogs();

                var ssn = (string?)defaults["ssn_decrypted"] ?? string.Empty;
                ssn = ssn.Replace("-", "");
                if (!FillField(By.XPath("//input[@id='responsiblePartySSN3']"), ssn.Substring(0, 3), "SSN First 3"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill SSN First 3");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='responsiblePartySSN2']"), ssn.Substring(3, 2), "SSN Middle 2"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill SSN Middle 2");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='responsiblePartySSN4']"), ssn.Substring(5), "SSN Last 4"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill SSN Last 4");
                }
                CaptureBrowserLogs();

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit2' and contains(@value, 'Continue >>')]"), "Continue after SSN"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after SSN");
                }
                CaptureBrowserLogs();

                FillField(By.XPath("//input[@id='responsiblePartyFirstName']"), (string?)defaults["first_name"] ?? string.Empty, "Clear & Fill First Name");
                CaptureBrowserLogs();
                if (!string.IsNullOrEmpty((string?)defaults["middle_name"]))
                {
                    FillField(By.XPath("//input[@id='responsiblePartyMiddleName']"), (string?)defaults["middle_name"] ?? string.Empty, "Responsible Middle Name");
                }
                CaptureBrowserLogs();
                FillField(By.XPath("//input[@id='responsiblePartyLastName']"), (string?)defaults["last_name"] ?? string.Empty, "Clear & Fill Last Name");
                CaptureBrowserLogs();

                if (!SelectRadio("iamsole", "I Am Sole"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select I Am Sole");
                }
                CaptureBrowserLogs();
                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit' and contains(@value, 'Continue >>')]"), "Continue after I Am Sole"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after I Am Sole");
                }
                CaptureBrowserLogs();

                // Trusteeship entity: update mailing address usage
                var mailingAddressDict = (data.MailingAddress != null && data.MailingAddress.Count > 0)
                    ? data.MailingAddress[0]
                    : new Dictionary<string, string>();

                if (!FillField(By.XPath("//input[@id='mailingAddressStreet']"),
                    CleanAddress(mailingAddressDict.GetValueOrDefault("mailingStreet", "")),
                    "Mailing Street"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing Street");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='mailingAddressCity']"),
                    CleanAddress(mailingAddressDict.GetValueOrDefault("mailingCity", "")),
                    "Mailing City"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing City");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='mailingAddressState']"),
                    CleanAddress(mailingAddressDict.GetValueOrDefault("mailingState", "")),
                    "Mailing State"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing State");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='mailingAddressPostalCode']"),
                    CleanAddress(mailingAddressDict.GetValueOrDefault("mailingZip", "")),
                    "Zip"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Zip");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='internationalPhoneNumber']"), (string?)defaults["phone"] ?? string.Empty, "Phone Number"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Phone Number");
                }
                CaptureBrowserLogs();

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit' and contains(@value, 'Continue >>')]"), "Continue after Mailing"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after Mailing");
                }
                CaptureBrowserLogs();

                try
                {
                    var shortWait = new WebDriverWait(Driver, TimeSpan.FromSeconds(20));
                    var element = WaitHelper.WaitUntilClickable(Driver, By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']"), 20);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                    element.Click();
                    _logger.LogInformation("Clicked Accept As Entered");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Accept As Entered button not found within 20 seconds, proceeding.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Unexpected error while clicking Accept As Entered: {ex.Message}");
                }

                string? businessName;
                try
                {
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                    businessName = Regex.Replace(businessName, @"[^a-zA-Z0-9\s\-&]", "");
                    var suffixes = new[] { "Corp", "Inc", "LLC", "LC", "PLLC", "PA", "L.L.C.", "INC.", "CORPORATION", "LIMITED" };
                    var pattern = $@"\b(?:{string.Join("|", suffixes.Select(Regex.Escape))})\b\.?$";
                    businessName = Regex.Replace(businessName, pattern, "", RegexOptions.IgnoreCase).Trim();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to process business name: {ex.Message}");
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                }

                try
                {
                    if (!FillField(By.Id("businessOperationalLegalName"), businessName, "Legal Business Name"))
                    {
                        _logger.LogInformation("Failed to fill business name in appropriate field based on entity type");
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Business name field not found");
                }
                catch (NoSuchElementException ex)
                {
                    _logger.LogInformation($"Business name field not found: {ex.Message}");
                }

                if (!FillField(By.XPath("//input[@id='businessOperationalCounty']"), data.County ?? string.Empty, "County"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill County");
                }
                CaptureBrowserLogs();

                try
            {
                var normalizedState = NormalizeState(data.EntityState ?? string.Empty);
                var stateSelect = WaitHelper.WaitUntilClickable(Driver, By.XPath("//select[@id='businessOperationalState' and @name='businessOperationalState']"), 10);
                new SelectElement(stateSelect).SelectByValue(normalizedState);
                _logger.LogInformation($"Selected state: {normalizedState}");
            }
            catch (WebDriverTimeoutException)
            {
                CaptureBrowserLogs();
                throw new AutomationError("Failed to select state");
            }
            catch (NoSuchElementException ex)
            {
                CaptureBrowserLogs();
                throw new AutomationError($"Failed to select state: {ex.Message}");
            }
            CaptureBrowserLogs();


                var (month, year) = ParseFlexibleDate(data.FormationDate ?? string.Empty);
                if (!SelectDropdown(By.Id("BUSINESS_OPERATIONAL_MONTH_ID"), month.ToString(), "Formation Month"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Formation Month");
                }
                if (!FillField(By.Id("BUSINESS_OPERATIONAL_YEAR_ID"), year.ToString(), "Formation Year"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Formation Year");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit' and contains(@value, 'Continue >>')]"), "Continue after Business Info"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after Business Info");
                }
                CaptureBrowserLogs();

                if (!SelectRadio("radioHasEmployees_n", "radioHasEmployees_n"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select radioHasEmployees_n");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after activity options");
                }
                CaptureBrowserLogs();

                if (!SelectRadio("receiveonline", "Receive Online"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Receive Online");
                }

                if (ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after receive EIN"))
                {
                    CaptureBrowserLogs();

                        // Capture confirmation page using the base CapturePageAsPdf method
                        var (blobUrl, success) = await CapturePageAsPdf(data, CancellationToken.None);

                    if (success && !string.IsNullOrEmpty(blobUrl))
                    {
                            _logger.LogInformation($"Confirmation page PDF uploaded to Azure: {blobUrl}");
                    }
                    else
                    {
                            _logger.LogWarning("Failed to capture and upload confirmation page PDF");
                    }
                }
                else
                {
                    throw new AutomationError("Failed to continue after receive EIN selection");
                }

                _logger.LogInformation("Form filled successfully");
                _logger.LogInformation("Completed Trusteeship entity form successfully");
            }
            catch (WebDriverTimeoutException ex)
            {
                CaptureBrowserLogs();
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (NoSuchElementException ex)
            {
                CaptureBrowserLogs();
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (ElementNotInteractableException ex)
            {
                CaptureBrowserLogs();
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (StaleElementReferenceException ex)
            {
                CaptureBrowserLogs();
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (WebDriverException ex)
            {
                CaptureBrowserLogs();
                if (File.Exists(DriverLogPath))
                {
                    try
                    {
                        var driverLogs = await File.ReadAllTextAsync(DriverLogPath);
                        _logger.LogError($"ChromeDriver logs: {driverLogs}");
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogError($"Failed to read ChromeDriver logs: {logEx.Message}");
                    }
                }
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"WebDriver error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("WebDriver error", ex.Message);
            }
            catch (Exception ex)
            {
                CaptureBrowserLogs();
                var pageSource = GetTruncatedPageSource();
                _logger.LogError($"Unexpected error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");

                try
                {
                    var handled = await DetectAndHandleType2Failure(data, new Dictionary<string, object?>());
                    if (handled)
                    {
                        _logger.LogWarning("Handled as Type 2 EIN failure during form fill. Skipping exception raise.");
                        return;
                    }
                }
                catch (Exception err)
                {
                    _logger.LogError($"Type 2 handler failed while processing EIN failure page: {err.Message}");
                }

                throw new AutomationError("Unexpected form filling error", ex.Message);
            }
        }

        public override async Task<(bool Success, string? Message, string? AzureBlobUrl)> RunAutomation(CaseData? data, Dictionary<string, object> jsonData)
        {
            string? einNumber = string.Empty;
            string? pdfAzureUrl = null;
            bool success = false;

            try
            {
                await _salesforceClient.InitializeSalesforceAuthAsync();

                var missingFields = data?.GetType()
                    .GetProperties()
                    .Where(p => p.GetValue(data) == null && p.Name != "RecordId")
                    .Select(p => p.Name)
                    .ToList() ?? new List<string>();
                if (missingFields.Any())
                {
                    _logger.LogInformation($"Missing fields detected (using defaults): {string.Join(", ", missingFields)}");
                    jsonData["missing_fields"] = missingFields;
                }

                InitializeDriver();

                if (string.IsNullOrWhiteSpace(data?.EntityType))
                {
                    throw new ArgumentNullException(nameof(data.EntityType), "EntityType is required");
                }
                if (data?.EntityType == "Trusteeship")
                {
                    await HandleTrusteeshipEntity(data);
                }
                else
                {
                    await NavigateAndFillForm(data, jsonData.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));
                }

                // _______________________________________________final submit deployment ________________________________

                // // 5. Continue to EIN Letter
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Submit']"), 
                        "Final Continue before EIN download"))
                    {
                        throw new Exception("Failed to click final Continue button before EIN");
                    }


                (einNumber, pdfAzureUrl, success) = await FinalSubmit(data, jsonData, CancellationToken.None);
                

                 // Type "yes" to click final submit button____________________________local testing_____________________


                // Console.WriteLine("Type 'yes' to continue to the EIN letter step:");
                // string? input = Console.ReadLine();


                // if (input?.Trim().ToLower() == "yes")
                // {
                //     if (!ClickButton(By.XPath("//input[@type='submit' and @value='Submit']"),
                //         "Final Continue before EIN download"))
                //     {
                //         throw new Exception("Failed to click final Continue button before EIN");
                //     }
                // }
                // else
                // {
                //     throw new Exception("User did not confirm with 'yes'. Aborting EIN letter step.");
                // }
                
                // // Type "yes" to download the EIN Letter

                // Console.WriteLine("Type 'yes' to proceed with final EIN submission:");
                // input = Console.ReadLine(); 

                // if (input?.Trim().ToLower() == "yes")
                // {
                //     (einNumber, pdfAzureUrl, success) = await FinalSubmit(data, jsonData, CancellationToken.None);
                // }
                // else
                // {
                //     throw new Exception("User did not confirm with 'yes'. Aborting FinalSubmit.");
                // }

                if (success && !string.IsNullOrEmpty(data?.RecordId) && !string.IsNullOrEmpty(einNumber))
                {
                    await _salesforceClient.NotifySalesforceSuccessAsync(data.RecordId!, einNumber);
                }

                return (success, einNumber, pdfAzureUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automation failed");

                string errorMsg = string.Empty;
                if (Driver != null)
                    errorMsg = _errorMessageExtractionService.ExtractErrorMessage(Driver);
                if (string.IsNullOrWhiteSpace(errorMsg))
                    errorMsg = ex.Message ?? "Unknown failure";
                if (jsonData != null)
                    jsonData["error_message"] = errorMsg;

                try
                {
                    var (failureBlobUrl, failureSuccess) = await CaptureFailurePageAsPdf(data, CancellationToken.None);
                    if (failureSuccess)
                    {
                        if (!string.IsNullOrEmpty(data?.RecordId))
                        {
                            await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId!, "500", "fail", Driver);
                        }
                    }
                    // Always save JSON with error message
                    if (jsonData != null)
                        await _blobStorageService.UploadJsonData(jsonData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? new object()), data);
                }
                catch (Exception pdfError)
                {
                    _logger.LogWarning($"Failed to capture or upload failure PDF/JSON: {pdfError.Message}");
                }

                return (false, null, null);
            }
            finally
            {
                try
                {
                    var recordId = data?.RecordId ?? "unknown";
                    var logPath = DriverLogPath ?? string.Empty;
                    var logUrl = await _blobStorageService.UploadLogToBlob(recordId, logPath);
                    if (!string.IsNullOrEmpty(logUrl))
                    {
                        _logger.LogInformation($"Uploaded Chrome log to: {logUrl}");
                    }
                }
                catch (Exception logError)
                {
                    _logger.LogWarning($"Failed to upload Chrome logs: {logError.Message}");
                }

                // BROWSER KEPT OPEN FOR DEBUGGING - Cleanup commented out
                // Cleanup();
                _logger.LogInformation($"Browser instance kept open for debugging - Record ID: {data?.RecordId ?? "unknown"}");
            }
        }

private async Task<(string? EinNumber, string? PdfAzureUrl, bool Success)> FinalSubmit(CaseData? data, Dictionary<string, object>? jsonData, CancellationToken cancellationToken)
{
    string? einNumber = null;
    string? pdfAzureUrl = null;

    try
    {
        if (Driver == null)
        {
            _logger.LogError("Driver is null");
            return (null, null, false);
        }

        // Try to extract EIN
        try
        {
            var einElement = WaitHelper.WaitUntilExists(Driver, By.CssSelector("td[align='left'] > b"), 10);
            var einText = einElement?.Text?.Trim();

            if (!string.IsNullOrEmpty(einText) && Regex.IsMatch(einText, @"^\d{2}-\d{7}$"))
            {
                einNumber = einText;
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Primary EIN extraction failed. Attempting fallback with HtmlAgilityPack.");
            var doc = new HtmlDocument();
            doc.LoadHtml(Driver.PageSource);

            var einNode = doc.DocumentNode.SelectSingleNode("//td/b[contains(text(), '-')]");
            if (einNode != null)
            {
                var einText = einNode.InnerText.Trim();
                if (Regex.IsMatch(einText, @"^\d{2}-\d{7}$"))
                {
                    einNumber = einText;
                    _logger.LogInformation("Extracted EIN via fallback: {EinNumber}", einNumber);
                }
            }
        }

        if (!string.IsNullOrEmpty(einNumber) && jsonData != null)
        {
            jsonData["einNumber"] = einNumber;
        }

        // Handle failure page
        if (string.IsNullOrEmpty(einNumber))
        {
            string pageText = Driver?.PageSource?.ToLower() ?? string.Empty;

            if (pageText.Contains("we are unable to provide you with an ein"))
            {
                // Use optimized reference number extraction with early termination and page source caching
                string referenceNumber = ExtractReferenceNumberOptimized();

                if (!string.IsNullOrEmpty(referenceNumber) && jsonData != null)
                {
                    jsonData["irs_reference_number"] = referenceNumber;
                }

                await CaptureFailurePageAsPdf(data, cancellationToken);
                await _salesforceClient.NotifySalesforceErrorCodeAsync(data?.RecordId, referenceNumber ?? "fail", "fail", Driver);
                return (null, null, false);
            }
            else
            {
                await CaptureFailurePageAsPdf(data, cancellationToken);
                await _salesforceClient.NotifySalesforceErrorCodeAsync(data?.RecordId, "500", "fail", Driver);
                return (null, null, false);
            }
        }

        // Success path: EIN was extracted successfully, now capture the PDF
        try
        {
            _logger.LogInformation("EIN successfully extracted: {EinNumber}. Starting PDF capture...", einNumber);
            
            // Execute both PDF capture methods regardless of success/failure for comprehensive coverage
            _logger.LogInformation("🚀 Starting comprehensive EIN Letter PDF capture using both methods...");
            
            string? pdfAzureUrl2 = null;
            bool success1 = false;
            bool success2 = false;

            // Method 1: TryDownloadEinLetterPdfWithSelenium (All 18 methods)
            try
            {
                _logger.LogInformation("📥 METHOD SET 1: Executing TryDownloadEinLetterPdfWithSelenium (18 methods)...");
                var pdfBytes1 = await TryDownloadEinLetterPdfWithSelenium(einNumber, data, jsonData, cancellationToken);
                if (pdfBytes1 != null && pdfBytes1.Length > 0)
                {
                    success1 = true;
                    _logger.LogInformation("✅ METHOD SET 1 SUCCESS: TryDownloadEinLetterPdfWithSelenium returned {FileSize} bytes", pdfBytes1.Length);
                }
                else
                {
                    _logger.LogWarning("❌ METHOD SET 1 FAILED: TryDownloadEinLetterPdfWithSelenium returned null or empty");
                }
            }
            catch (Exception ex1)
            {
                _logger.LogError(ex1, "❌ METHOD SET 1 EXCEPTION: TryDownloadEinLetterPdfWithSelenium failed with exception");
            }

            // Method 2: CapturePageAsPdfEnhanced (Additional enhanced capture methods)
            try
            {
                _logger.LogInformation("📸 METHOD SET 2: Executing CapturePageAsPdfEnhanced...");
                var (blobUrl2, success2Result) = await CapturePageAsPdfEnhanced(data, cancellationToken);
                if (success2Result && !string.IsNullOrEmpty(blobUrl2))
                {
                    pdfAzureUrl2 = blobUrl2;
                    success2 = true;
                    _logger.LogInformation("✅ METHOD SET 2 SUCCESS: CapturePageAsPdfEnhanced - {BlobUrl}", blobUrl2);
                }
                else
                {
                    _logger.LogWarning("❌ METHOD SET 2 FAILED: CapturePageAsPdfEnhanced returned no valid result");
                }
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "❌ METHOD SET 2 EXCEPTION: CapturePageAsPdfEnhanced failed with exception");
            }

            // Determine overall success and primary PDF URL for response
            bool overallSuccess = success1 || success2;
            if (overallSuccess)
            {
                // Prefer the first successful method's result, or use the second if first failed
                pdfAzureUrl = pdfAzureUrl2 ?? "PDF captured via selenium methods";
                
                // Save JSON data with successful EIN
                if (jsonData != null)
                {
                    try
                    {
                        await _blobStorageService.UploadJsonData(jsonData, data);
                        _logger.LogInformation("JSON data saved successfully with EIN: {EinNumber}", einNumber);
                    }
                    catch (Exception jsonEx)
                    {
                        _logger.LogWarning("Failed to save JSON data: {Message}", jsonEx.Message);
                    }
                }

                // Notify Salesforce about successful EIN Letter
                try
                {
                    await _salesforceClient.NotifyEinLetterToSalesforceAsync(data?.RecordId, pdfAzureUrl, data?.EntityName, data?.AccountId, data?.EntityId, data?.CaseId);
                    _logger.LogInformation("Salesforce EIN Letter notification sent successfully");
                }
                catch (Exception sfEx)
                {
                    _logger.LogWarning("Failed to notify Salesforce about EIN Letter: {Message}", sfEx.Message);
                }

                // Notify Salesforce about overall success
                try
                {
                    await _salesforceClient.NotifySalesforceSuccessAsync(data?.RecordId, einNumber);
                    _logger.LogInformation("Salesforce success notification sent successfully");
                }
                catch (Exception sfEx)
                {
                    _logger.LogWarning("Failed to notify Salesforce about success: {Message}", sfEx.Message);
                }

                _logger.LogInformation("✅ EIN Letter PDF successfully captured and uploaded. Method 1 success: {Success1}, Method 2 success: {Success2}", success1, success2);
                return (einNumber, pdfAzureUrl, true);
            }
            else
            {
                _logger.LogError("Both PDF capture methods failed despite successful EIN extraction. Method 1 success: {Success1}, Method 2 success: {Success2}", success1, success2);
                throw new Exception("All PDF capture methods failed or returned empty content");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("PDF capture failed after successful EIN extraction: {Message}", ex.Message);

            // Try to save JSON data even on PDF capture failure
            if (jsonData != null)
            {
                try
                {
                    await _blobStorageService.UploadJsonData(jsonData, data);
                    _logger.LogInformation("JSON data saved despite PDF capture failure");
                }
                catch (Exception jsonEx)
                {
                    _logger.LogWarning("Failed to save JSON data after PDF capture failure: {Message}", jsonEx.Message);
                }
            }

            // Try to capture failure page
            try
            {
                await CaptureFailurePageAsPdf(data, cancellationToken);
            }
            catch (Exception captureEx)
            {
                _logger.LogWarning("Failed to capture failure page: {Message}", captureEx.Message);
            }

            // Notify Salesforce about the error
            try
            {
                await _salesforceClient.NotifySalesforceErrorCodeAsync(data?.RecordId, "PDF_CAPTURE_FAILED", "fail", Driver);
            }
            catch (Exception sfEx)
            {
                _logger.LogWarning("Failed to notify Salesforce about PDF capture error: {Message}", sfEx.Message);
            }

            return (einNumber, null, false); // Return EIN but indicate failure due to PDF issue
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error in FinalSubmit");
        
        // Try to capture failure page for debugging
        try
        {
            await CaptureFailurePageAsPdf(data, cancellationToken);
        }
        catch (Exception captureEx)
        {
            _logger.LogWarning("Failed to capture failure page in exception handler: {Message}", captureEx.Message);
        }
        
        return (null, null, false);
    }
}

private async Task<byte[]?> TryDownloadEinLetterPdfWithSelenium(string? einNumber, CaseData? data, Dictionary<string, object>? jsonData, CancellationToken cancellationToken)
// Note: einNumber and jsonData parameters are intentionally unused in this method but kept for interface compatibility
{
    string? downloadDir = null;
    string? downloadsFolder = null;
    byte[]? firstSuccessfulPdf = null; // Track the first successful PDF to return
    var successfulMethods = new List<string>(); // Track which methods succeeded
    
    try
    {
        // CRITICAL: Prepare page for clean PDF capture - remove navigation panels
        _logger.LogInformation("🔧 METHOD SET 1: Preparing page for PDF capture - removing navigation panels...");
        await PreparePageForFullCapture(cancellationToken);
        _logger.LogInformation("✅ METHOD SET 1: Page preparation complete");
        // Create a unique download directory for container deployment
        downloadDir = Path.Combine(Path.GetTempPath(), "ein_pdfs", DateTime.Now.Ticks.ToString());
        Directory.CreateDirectory(downloadDir);
        _logger.LogInformation("Created download directory: {DownloadDir}", downloadDir);

        // Also get the user's Downloads folder for local development
        downloadsFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        downloadsFolder = Path.Combine(downloadsFolder, "Downloads");
        _logger.LogInformation("Monitoring Downloads folder: {DownloadsFolder}", downloadsFolder);

        // Configure Chrome download preferences dynamically
        await ConfigureDownloadDirectory(downloadDir);

        // Wait for confirmation page to load and find the PDF link
        _logger.LogInformation("Waiting for confirmation page to load and PDF link to appear...");
        await Task.Delay(3000, cancellationToken); // Wait for page to fully load
        
        // Verify we're on the confirmation page
        var pageSource = Driver?.PageSource?.ToLower() ?? "";
        if (!pageSource.Contains("confirmation") && !pageSource.Contains("ein has been successfully assigned"))
        {
            _logger.LogWarning("Not on confirmation page, waiting longer...");
            await Task.Delay(5000, cancellationToken);
        }
        
        // Find and click the PDF link
        var pdfLinkElement = await FindPdfLinkElement();
        if (pdfLinkElement == null)
        {
            _logger.LogWarning("PDF link not found initially, waiting longer and retrying...");
            await Task.Delay(5000, cancellationToken); // Wait longer
            pdfLinkElement = await FindPdfLinkElement();
            
            if (pdfLinkElement == null)
            {
                throw new Exception("PDF link element not found after all attempts");
            }
        }
        
        _logger.LogInformation("Found PDF link element: {Href}", pdfLinkElement.GetAttribute("href"));
        _logger.LogInformation("PDF link onclick: {OnClick}", pdfLinkElement.GetAttribute("onclick"));
        _logger.LogInformation("PDF link text: {Text}", pdfLinkElement.Text);

        _logger.LogInformation("Starting simplified PDF download - Triggering downloads then scanning with Method 8 & 10a");
        
        // DOWNLOAD TRIGGERS: These methods trigger downloads to the downloads folder
        // The actual PDF processing will be done only by Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        
        // Download Trigger 1: Click PDF link to trigger download
        _logger.LogInformation("=== DOWNLOAD TRIGGER 1: Clicking PDF link ===");
        try
        {
            var clicked = await TryClickPdfLink(pdfLinkElement);
            if (clicked)
            {
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 1 SUCCESS: Successfully clicked PDF link");
                await Task.Delay(5000, cancellationToken); // Wait for download to start
            }
            else
            {
                _logger.LogWarning("❌ DOWNLOAD TRIGGER 1 FAILED: Could not click PDF link");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 1 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 2: Try keyboard shortcut Ctrl+S to trigger Save As dialog
        _logger.LogInformation("=== DOWNLOAD TRIGGER 2: Ctrl+S Save As ===");
        try
        {
            var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
            actions.KeyDown(Keys.Control).SendKeys("s").KeyUp(Keys.Control).Perform();
            await Task.Delay(3000, cancellationToken); // Wait for Save dialog
            
            // Try to press Enter to confirm save (if Save dialog opened)
            actions.SendKeys(Keys.Enter).Perform();
            await Task.Delay(2000, cancellationToken);
            _logger.LogInformation("✅ DOWNLOAD TRIGGER 2 SUCCESS: Triggered Ctrl+S Save As");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 2 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 3: Enhanced download button clicking with PDF-specific detection
        _logger.LogInformation("=== DOWNLOAD TRIGGER 3: Enhanced download button clicking ===");
        try
        {
            var downloadSelectors = new[]
            {
                // Standard download buttons
                "//button[@title='Download']",
                "//button[contains(@class, 'download')]", 
                "//a[@title='Download']",
                "//a[contains(@class, 'download')]",
                "//*[contains(text(), 'Download')]",
                "//*[@id='download']",
                "//button[contains(@aria-label, 'Download')]",
                "//a[contains(@aria-label, 'Download')]",
                
                // PDF viewer specific buttons
                "//button[contains(@class, 'print')]",
                "//button[@title='Print']",
                "//button[contains(@title, 'Save')]",
                "//button[contains(@class, 'save')]",
                "//*[@title='Save as PDF']",
                "//*[contains(text(), 'Save')]",
                "//*[contains(text(), 'Print')]",
                
                // PDF toolbar buttons
                "//button[contains(@class, 'toolbar')]//button[contains(@title, 'Download')]",
                "//div[contains(@class, 'pdf-toolbar')]//button",
                "//*[contains(@class, 'pdf-controls')]//*[contains(text(), 'Download')]",
                
                // Browser PDF viewer controls
                "//*[@id='download' or @class='download' or @title='Download']",
                "//*[contains(@onclick, 'download')]",
                "//*[contains(@onclick, 'print')]",
                "//*[contains(@onclick, 'save')]"
            };
            
            bool downloadClicked = false;
            foreach (var selector in downloadSelectors)
            {
                try
                {
                    var downloadBtns = Driver?.FindElements(By.XPath(selector));
                    if (downloadBtns != null && downloadBtns.Count > 0)
                    {
                        foreach (var downloadBtn in downloadBtns)
                        {
                            if (downloadBtn != null && downloadBtn.Displayed && downloadBtn.Enabled)
                            {
                                // Scroll element into view first
                                ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", downloadBtn);
                                await Task.Delay(500, cancellationToken);
                                
                                // Try multiple click methods
                                try
                                {
                                    downloadBtn.Click();
                                    _logger.LogInformation("✅ DOWNLOAD TRIGGER 3 SUCCESS: Clicked download button with selector: {Selector}", selector);
                                }
                                catch
                                {
                                    // Try JavaScript click as fallback
                                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].click();", downloadBtn);
                                    _logger.LogInformation("✅ DOWNLOAD TRIGGER 3 SUCCESS: JS-clicked download button with selector: {Selector}", selector);
                                }
                                
                                await Task.Delay(3000, cancellationToken);
                                downloadClicked = true;
                                break;
                            }
                        }
                        if (downloadClicked) break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Download button selector {Selector} failed: {Message}", selector, ex.Message);
                }
            }
            
            if (!downloadClicked)
            {
                _logger.LogInformation("🔍 No standard download buttons found, trying PDF-specific triggers...");
                await TryPdfSpecificDownloadTriggers(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 3 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 4: JavaScript openPDFNoticeWindow execution (STALE-ELEMENT-SAFE)
        _logger.LogInformation("=== DOWNLOAD TRIGGER 4: Execute openPDFNoticeWindow JavaScript ===");
        try
        {
            // Use fresh element detection to avoid stale element reference
            var jsExecutor = (IJavaScriptExecutor?)Driver;
            var openPDFScript = @"
                // Find PDF link with openPDFNoticeWindow
                var pdfLinks = document.querySelectorAll('a[onclick*=""openPDFNoticeWindow""]');
                var executed = false;
                
                for (var i = 0; i < pdfLinks.length; i++) {
                    var onclick = pdfLinks[i].getAttribute('onclick');
                    if (onclick && onclick.includes('openPDFNoticeWindow')) {
                        try {
                            eval(onclick);
                            executed = true;
                            break;
                        } catch(e) {
                            console.log('Failed to execute onclick:', e);
                        }
                    }
                }
                
                return executed ? 'EXECUTED' : 'NOT_FOUND';
            ";
            
            var result = jsExecutor?.ExecuteScript(openPDFScript);
            if (result?.ToString() == "EXECUTED")
            {
                await Task.Delay(3000, cancellationToken);
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 4 SUCCESS: Executed openPDFNoticeWindow JavaScript");
            }
            else
            {
                _logger.LogInformation("⏭️ DOWNLOAD TRIGGER 4 SKIPPED: No openPDFNoticeWindow onclick found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 4 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 5: Open PDF link in new window/tab (STALE-ELEMENT-SAFE)
        _logger.LogInformation("=== DOWNLOAD TRIGGER 5: Open PDF in new window ===");
        try
        {
            // Use JavaScript to find and open PDF links to avoid stale element reference
            var jsExecutor = (IJavaScriptExecutor?)Driver;
            var openNewWindowScript = @"
                // Find PDF links and open in new window
                var pdfSelectors = [
                    'a[href*="".pdf""]',
                    'a[href*=""CP575""]',
                    'a[onclick*=""openPDFNoticeWindow""]',
                    'a[onclick*=""pdf""]'
                ];
                
                var opened = false;
                var baseUrl = window.location.origin;
                
                pdfSelectors.forEach(function(selector) {
                    if (!opened) {
                        var links = document.querySelectorAll(selector);
                        for (var i = 0; i < links.length; i++) {
                            var href = links[i].href || links[i].getAttribute('href');
                            if (href) {
                                if (href.startsWith('/')) {
                                    href = baseUrl + href;
                                }
                                try {
                                    window.open(href, '_blank');
                                    opened = true;
                                    break;
                                } catch(e) {
                                    console.log('Failed to open window:', e);
                                }
                            }
                        }
                    }
                });
                
                return opened ? 'OPENED' : 'NOT_FOUND';
            ";
            
            var result = jsExecutor?.ExecuteScript(openNewWindowScript);
            if (result?.ToString() == "OPENED")
            {
                await Task.Delay(5000, cancellationToken);
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 5 SUCCESS: Opened PDF in new window");
            }
            else
            {
                _logger.LogWarning("❌ DOWNLOAD TRIGGER 5 FAILED: No PDF links found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 5 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 6: Direct navigation to PDF URL (STALE-ELEMENT-SAFE)
        _logger.LogInformation("=== DOWNLOAD TRIGGER 6: Direct navigation to PDF URL ===");
        try
        {
            // Use JavaScript to find PDF URL to avoid stale element reference
            var jsExecutor = (IJavaScriptExecutor?)Driver;
            var findUrlScript = @"
                // Find PDF URLs in the page
                var pdfSelectors = [
                    'a[href*="".pdf""]',
                    'a[href*=""CP575""]',
                    'a[onclick*=""openPDFNoticeWindow""]'
                ];
                
                var foundUrl = null;
                var baseUrl = window.location.origin;
                
                pdfSelectors.forEach(function(selector) {
                    if (!foundUrl) {
                        var links = document.querySelectorAll(selector);
                        for (var i = 0; i < links.length; i++) {
                            var href = links[i].href || links[i].getAttribute('href');
                            if (href) {
                                if (href.startsWith('/')) {
                                    href = baseUrl + href;
                                }
                                foundUrl = href;
                                break;
                            }
                        }
                    }
                });
                
                return foundUrl;
            ";
            
            var pdfUrl = jsExecutor?.ExecuteScript(findUrlScript)?.ToString();
            if (!string.IsNullOrEmpty(pdfUrl))
            {
                var currentUrl = Driver?.Url;
                Driver?.Navigate().GoToUrl(pdfUrl);
                await Task.Delay(8000, cancellationToken); // Wait for PDF to load/download
                
                // Navigate back to original page
                if (!string.IsNullOrEmpty(currentUrl))
                {
                    Driver?.Navigate().GoToUrl(currentUrl);
                    await Task.Delay(2000, cancellationToken);
                }
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 6 SUCCESS: Navigated to PDF URL and back");
            }
            else
            {
                _logger.LogWarning("❌ DOWNLOAD TRIGGER 6 FAILED: No PDF URL found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 6 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 7: Right-click context menu "Save As" (STALE-ELEMENT-SAFE)
        _logger.LogInformation("=== DOWNLOAD TRIGGER 7: Right-click Save As ===");
        try
        {
            // Find fresh PDF elements to avoid stale element reference
            var pdfElements = Driver?.FindElements(By.XPath("//a[contains(@href, '.pdf')] | //a[contains(@onclick, 'openPDFNoticeWindow')] | //a[contains(@href, 'CP575')]"));
            if (pdfElements != null && pdfElements.Count > 0)
            {
                var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
                var pdfElement = pdfElements.First();
                
                // Scroll element into view first
                ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", pdfElement);
                await Task.Delay(500, cancellationToken);
                
                actions.ContextClick(pdfElement).Perform();
                await Task.Delay(1000, cancellationToken);
                
                // Try to select "Save link as" option (key navigation)
                actions.SendKeys(Keys.ArrowDown).SendKeys(Keys.ArrowDown).SendKeys(Keys.Enter).Perform();
                await Task.Delay(3000, cancellationToken);
                
                // If save dialog opened, press Enter to confirm
                actions.SendKeys(Keys.Enter).Perform();
                await Task.Delay(2000, cancellationToken);
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 7 SUCCESS: Right-click Save As attempted");
            }
            else
            {
                _logger.LogWarning("❌ DOWNLOAD TRIGGER 7 FAILED: No PDF elements found for right-click");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 7 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 8: Multiple click strategies on PDF link
        _logger.LogInformation("=== DOWNLOAD TRIGGER 8: Multiple click strategies ===");
        try
        {
            var jsExecutor = (IJavaScriptExecutor?)Driver;
            
            // Strategy 1: JavaScript click with target modification
            try
            {
                jsExecutor?.ExecuteScript(@"
                    arguments[0].target = '_self';
                    arguments[0].click();
                ", pdfLinkElement);
                await Task.Delay(3000, cancellationToken);
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 8a SUCCESS: JavaScript click with target modification");
            }
            catch (Exception ex8a)
            {
                _logger.LogDebug("Download trigger 8a failed: {Message}", ex8a.Message);
            }
            
            // Strategy 2: Actions class click
            try
            {
                var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
                actions.Click(pdfLinkElement).Perform();
                await Task.Delay(3000, cancellationToken);
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 8b SUCCESS: Actions class click");
            }
            catch (Exception ex8b)
            {
                _logger.LogDebug("Download trigger 8b failed: {Message}", ex8b.Message);
            }
            
            // Strategy 3: Double click
            try
            {
                var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
                actions.DoubleClick(pdfLinkElement).Perform();
                await Task.Delay(3000, cancellationToken);
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 8c SUCCESS: Double click");
            }
            catch (Exception ex8c)
            {
                _logger.LogDebug("Download trigger 8c failed: {Message}", ex8c.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 8 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 9: Print to PDF triggers
        _logger.LogInformation("=== DOWNLOAD TRIGGER 9: Print to PDF triggers ===");
        try
        {
            var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
            
            // Trigger 9a: Ctrl+P (Print)
            try
            {
                actions.KeyDown(Keys.Control).SendKeys("p").KeyUp(Keys.Control).Perform();
                await Task.Delay(2000, cancellationToken);
                // If print dialog opens, try to select "Save as PDF" and save
                actions.SendKeys(Keys.Tab).SendKeys(Keys.Tab).SendKeys(Keys.Enter).Perform();
                await Task.Delay(3000, cancellationToken);
                _logger.LogInformation("✅ DOWNLOAD TRIGGER 9a SUCCESS: Ctrl+P Print attempted");
            }
            catch (Exception ex9a)
            {
                _logger.LogDebug("Download trigger 9a failed: {Message}", ex9a.Message);
            }
            
            // Trigger 9b: Look for print buttons
            try
            {
                var printSelectors = new[]
                {
                    "//button[@title='Print']",
                    "//button[contains(@class, 'print')]",
                    "//a[@title='Print']",
                    "//*[contains(text(), 'Print')]",
                    "//*[@id='print']"
                };
                
                foreach (var selector in printSelectors)
                {
                    try
                    {
                        var printBtn = Driver?.FindElement(By.XPath(selector));
                        if (printBtn != null && printBtn.Displayed && printBtn.Enabled)
                        {
                            printBtn.Click();
                            await Task.Delay(2000, cancellationToken);
                            _logger.LogInformation("✅ DOWNLOAD TRIGGER 9b SUCCESS: Clicked print button");
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // Continue to next selector
                    }
                }
            }
            catch (Exception ex9b)
            {
                _logger.LogDebug("Download trigger 9b failed: {Message}", ex9b.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 9 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 10: Alternative PDF link selectors
        _logger.LogInformation("=== DOWNLOAD TRIGGER 10: Alternative PDF link discovery and clicking ===");
        try
        {
            var pdfSelectors = new[]
            {
                "//a[contains(@href, '.pdf')]",
                "//a[contains(@href, 'CP575')]",
                "//a[contains(text(), 'PDF')]",
                "//a[contains(text(), 'Download')]",
                "//a[contains(@onclick, 'openPDFNoticeWindow')]",
                "//a[@target='pdf_popup']",
                "//*[contains(@class, 'pdf-link')]"
            };
            
            foreach (var selector in pdfSelectors)
            {
                try
                {
                    var pdfLinks = Driver?.FindElements(By.XPath(selector));
                    if (pdfLinks != null && pdfLinks.Count > 0)
                    {
                        foreach (var link in pdfLinks)
                        {
                            if (link.Displayed && link.Enabled)
                            {
                                link.Click();
                                await Task.Delay(3000, cancellationToken);
                                _logger.LogInformation("✅ DOWNLOAD TRIGGER 10 SUCCESS: Clicked alternative PDF link with selector: {Selector}", selector);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Continue to next selector
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 10 EXCEPTION: {Message}", ex.Message);
        }
        
        // Download Trigger 11: PDF download via form submission
        _logger.LogInformation("=== DOWNLOAD TRIGGER 11: Form submission triggers ===");
        try
        {
            // Look for forms that might contain the PDF link
            var forms = Driver?.FindElements(By.TagName("form"));
            if (forms != null && forms.Count > 0)
            {
                foreach (var form in forms)
                {
                    try
                    {
                        var submitButtons = form.FindElements(By.XPath(".//input[@type='submit'] | .//button[@type='submit']"));
                        foreach (var submitBtn in submitButtons)
                        {
                            var value = submitBtn.GetAttribute("value") ?? submitBtn.Text;
                            if (!string.IsNullOrEmpty(value) && 
                                (value.ToLower().Contains("download") || 
                                 value.ToLower().Contains("pdf") || 
                                 value.ToLower().Contains("continue")))
                            {
                                submitBtn.Click();
                                await Task.Delay(3000, cancellationToken);
                                _logger.LogInformation("✅ DOWNLOAD TRIGGER 11 SUCCESS: Clicked form submit button: {Value}", value);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Continue to next form
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ DOWNLOAD TRIGGER 11 EXCEPTION: {Message}", ex.Message);
        }
        
        // Wait for all downloads to complete
        _logger.LogInformation("Waiting 15 seconds for all downloads to complete...");
        await Task.Delay(15000, cancellationToken);
        
        // Method 1: Try window-based PDF capture FIRST (open PDF in new tab and extract content)
        // This is prioritized because it ensures the PDF is actually opened and loaded before capture
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 1: Window-based PDF capture ===");
        try
        {
        var pdfBytes = await TryWindowBasedPdfCapture(pdfLinkElement, cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("✅ METHOD 1 SUCCESS: Window-based capture - {FileSize} bytes", pdfBytes.Length);
                // Save this PDF with proper blob naming
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method1_WindowBased", data, cancellationToken);
                successfulMethods.Add("Method1_WindowBased");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
        }
        else
        {
            _logger.LogWarning("❌ METHOD 1 FAILED: Window-based capture returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 1 EXCEPTION: Window-based capture failed - {Message}", ex.Message);
        }
        */

        // Method 2: Try direct download as fallback
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 2: Direct download ===");
        try
        {
            var pdfBytes = await TryDirectDownload(pdfLinkElement, cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("✅ METHOD 2 SUCCESS: Direct download - {FileSize} bytes", pdfBytes.Length);
                // Save this PDF with proper blob naming
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method2_DirectDownload", data, cancellationToken);
                successfulMethods.Add("Method2_DirectDownload");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
        }
        else
        {
            _logger.LogWarning("❌ METHOD 2 FAILED: Direct download returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 2 EXCEPTION: Direct download failed - {Message}", ex.Message);
        }
        */

        // Method 3: Try clicking the PDF link and capturing from current window
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 3: Click and capture from current window ===");
        try
        {
            var pdfBytes = await TryClickAndCaptureFromCurrentWindow(pdfLinkElement, cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("✅ METHOD 3 SUCCESS: Click and capture - {FileSize} bytes", pdfBytes.Length);
                // Save this PDF with proper blob naming
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method3_ClickCapture", data, cancellationToken);
                successfulMethods.Add("Method3_ClickCapture");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
        }
        else
        {
            _logger.LogWarning("❌ METHOD 3 FAILED: Click and capture returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 3 EXCEPTION: Click and capture failed - {Message}", ex.Message);
        }
        */

        // Method 4: Try Chrome DevTools PDF capture
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 4: Chrome DevTools PDF capture ===");
        try
        {
            var pdfBytes = await TryChromeDevToolsPdfCapture(cancellationToken, data);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("✅ METHOD 4 SUCCESS: Chrome DevTools capture - {FileSize} bytes", pdfBytes.Length);
                // Save this PDF with proper blob naming
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method4_ChromeDevTools", data, cancellationToken);
                successfulMethods.Add("Method4_ChromeDevTools");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
        }
        else
        {
            _logger.LogWarning("❌ METHOD 4 FAILED: Chrome DevTools capture returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 4 EXCEPTION: Chrome DevTools capture failed - {Message}", ex.Message);
        }
        */

        // Method 5: Try ultimate fallback PDF download
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 5: Ultimate fallback PDF download ===");
        try
        {
            var pdfBytes = await TryUltimateFallbackPdfDownload(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("✅ METHOD 5 SUCCESS: Ultimate fallback - {FileSize} bytes", pdfBytes.Length);
                // Save this PDF with proper blob naming
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method5_UltimateFallback", data, cancellationToken);
                successfulMethods.Add("Method5_UltimateFallback");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
        }
        else
        {
            _logger.LogWarning("❌ METHOD 5 FAILED: Ultimate fallback returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 5 EXCEPTION: Ultimate fallback failed - {Message}", ex.Message);
        }
        */

        // Method 6: Click the PDF link first, then add delay and try multiple capture methods
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 6: Click PDF link with delay and multiple capture methods ===");
        try
        {
        var clicked = await TryClickPdfLink(pdfLinkElement);
            if (clicked)
        {
            _logger.LogInformation("✅ METHOD 6: Successfully clicked PDF link");
        
        // Add delay AFTER clicking the link to ensure PDF loads properly
        _logger.LogInformation("Adding 10-second delay after clicking PDF link to ensure proper loading...");
        await Task.Delay(10000, cancellationToken); // 10-second delay for PDF to load
        
        // Wait for PDF to be fully loaded by checking for PDF indicators
        _logger.LogInformation("Waiting for PDF to be fully loaded...");
        var maxWaitTime = 30000; // 30 seconds
        var waitInterval = 2000; // 2 seconds
        var totalWaited = 0;
        
        while (totalWaited < maxWaitTime && !cancellationToken.IsCancellationRequested)
        {
            var currentUrl = Driver?.Url ?? "";
            var currentPageSource = Driver?.PageSource ?? "";
            
            // Check if we're on a PDF page
            if (currentUrl.Contains(".pdf") || 
                currentPageSource.Contains("PDF") || 
                currentPageSource.Contains("application/pdf") ||
                currentUrl.Contains("CP575"))
            {
                _logger.LogInformation("PDF appears to be loaded. Current URL: {Url}", currentUrl);
                break;
            }
            
            await Task.Delay(waitInterval, cancellationToken);
            totalWaited += waitInterval;
            _logger.LogDebug("Waited {TotalWaited}ms for PDF to load...", totalWaited);
        }
        
        // Try multiple capture methods after clicking
        _logger.LogInformation("Attempting to capture PDF after clicking link...");
        
        // Try Chrome DevTools capture after clicking
        _logger.LogInformation("--- Method 6a: Chrome DevTools capture after clicking ---");
                try
                {
                    var pdfBytes = await TryChromeDevToolsPdfCapture(cancellationToken, data);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("✅ METHOD 6a SUCCESS: Chrome DevTools capture after clicking - {FileSize} bytes", pdfBytes.Length);
                        await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method6a_ChromeDevToolsAfterClick", data, cancellationToken);
                        successfulMethods.Add("Method6a_ChromeDevToolsAfterClick");
                        if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
        }
        else
        {
            _logger.LogWarning("❌ METHOD 6a FAILED: Chrome DevTools capture after clicking returned null or empty");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("❌ METHOD 6a EXCEPTION: Chrome DevTools capture after clicking failed - {Message}", ex.Message);
        }
        
        // Try ultimate fallback after clicking
        _logger.LogInformation("--- Method 6b: Ultimate fallback after clicking ---");
                try
                {
                    var pdfBytes = await TryUltimateFallbackPdfDownload(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("✅ METHOD 6b SUCCESS: Ultimate fallback after clicking - {FileSize} bytes", pdfBytes.Length);
                        await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method6b_UltimateFallbackAfterClick", data, cancellationToken);
                        successfulMethods.Add("Method6b_UltimateFallbackAfterClick");
                        if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
        }
        else
        {
            _logger.LogWarning("❌ METHOD 6b FAILED: Ultimate fallback after clicking returned null or empty");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("❌ METHOD 6b EXCEPTION: Ultimate fallback after clicking failed - {Message}", ex.Message);
                }
            }
            else
            {
                _logger.LogWarning("❌ METHOD 6 SETUP FAILED: Could not click PDF link");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 6 EXCEPTION: Click PDF link process failed - {Message}", ex.Message);
        }
        
        // Try to capture from any new tabs that might have opened (Method 6c)
        _logger.LogInformation("--- Method 6c: Checking for PDF in new tabs/windows ---");
        try
        {
        var currentWindows = Driver?.WindowHandles.ToList() ?? new List<string>();
            // Note: We can't compare with originalWindows here since it's not defined in this scope
            // So we'll check all windows for PDF content
        
            foreach (var windowHandle in currentWindows)
            {
                try
                {
                    Driver?.SwitchTo().Window(windowHandle);
                    await Task.Delay(3000, cancellationToken); // Wait for window to load
                    
                    var windowUrl = Driver?.Url ?? "";
                    _logger.LogInformation("Checking window: {Url}", windowUrl);
                    
                    // Try to capture PDF from this window (Method 6c1)
                    _logger.LogInformation("--- Method 6c1: Chrome DevTools capture from window ---");
                    try
                    {
                        var pdfBytes = await TryChromeDevToolsPdfCapture(cancellationToken, data);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                            _logger.LogInformation("✅ METHOD 6c1 SUCCESS: Captured PDF from window - {FileSize} bytes", pdfBytes.Length);
                            await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method6c1_WindowChromeDevTools", data, cancellationToken);
                            successfulMethods.Add("Method6c1_WindowChromeDevTools");
                            if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
                    }
                    else
                    {
                            _logger.LogWarning("❌ METHOD 6c1 FAILED: Chrome DevTools capture from window returned null or empty");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("❌ METHOD 6c1 EXCEPTION: Chrome DevTools capture from window failed - {Message}", ex.Message);
                    }
                    
                    // Try ultimate fallback on this window (Method 6c2)
                    _logger.LogInformation("--- Method 6c2: Ultimate fallback from window ---");
                    try
                    {
                        var pdfBytes = await TryUltimateFallbackPdfDownload(cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                            _logger.LogInformation("✅ METHOD 6c2 SUCCESS: Ultimate fallback succeeded on window - {FileSize} bytes", pdfBytes.Length);
                            await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method6c2_WindowUltimateFallback", data, cancellationToken);
                            successfulMethods.Add("Method6c2_WindowUltimateFallback");
                            if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
                    }
                    else
                    {
                            _logger.LogWarning("❌ METHOD 6c2 FAILED: Ultimate fallback from window returned null or empty");
                    }
                }
                    catch (Exception ex)
                {
                        _logger.LogWarning("❌ METHOD 6c2 EXCEPTION: Ultimate fallback from window failed - {Message}", ex.Message);
                }
            }
                catch (Exception windowEx)
            {
                    _logger.LogDebug("Error checking window {Window}: {Message}", windowHandle, windowEx.Message);
                }
                }
            }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 6c EXCEPTION: Window checking process failed - {Message}", ex.Message);
        }
        */

        // Method 7: Wait for download to complete in both directories with increased timeout
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 7: Wait for download to complete ===");
        try
        {
        var downloadedFile = await WaitForDownloadCompleteMultiLocation(downloadDir, downloadsFolder, 120000, cancellationToken); // Increased timeout to 120 seconds
        if (!string.IsNullOrEmpty(downloadedFile))
        {
            // Read and validate the downloaded PDF
            var downloadedBytes = await File.ReadAllBytesAsync(downloadedFile, cancellationToken);
            
            if (IsValidPdf(downloadedBytes))
            {
                _logger.LogInformation("✅ METHOD 7 SUCCESS: Click-based download - {FilePath}, {FileSize} bytes", 
                    downloadedFile, downloadedBytes.Length);
                    await SaveEinLetterPdfWithMethodIdentifier(downloadedBytes, "Method7_ClickBasedDownload", data, cancellationToken);
                    successfulMethods.Add("Method7_ClickBasedDownload");
                    if (firstSuccessfulPdf == null) firstSuccessfulPdf = downloadedBytes;
            }
            else
            {
                _logger.LogWarning("❌ METHOD 7 FAILED: Downloaded file is not a valid PDF");
            }
        }
        else
        {
            _logger.LogWarning("❌ METHOD 7 FAILED: No downloaded file found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 7 EXCEPTION: Click-based download failed - {Message}", ex.Message);
        }
        */
        
        // PRIMARY METHOD: Recent Downloads Scan (Method 10a moved to primary position)
        _logger.LogInformation("=== PRIMARY METHOD: Recent Downloads Scan ===");
        try
        {
        var fallbackDownloadedFile = await CheckDownloadsFolderForRecentPdf(downloadsFolder, cancellationToken);
        if (!string.IsNullOrEmpty(fallbackDownloadedFile))
        {
            var fallbackDownloadedBytes = await File.ReadAllBytesAsync(fallbackDownloadedFile, cancellationToken);
            if (IsValidPdf(fallbackDownloadedBytes))
            {
                    _logger.LogInformation("✅ PRIMARY METHOD SUCCESS: Found recently downloaded PDF in Downloads folder - {FilePath}, Size: {FileSize} bytes", 
                    fallbackDownloadedFile, fallbackDownloadedBytes.Length);
                    
                    // Save primary method PDF with standard blob naming (no method identifier)
                    await SavePrimaryEinLetterPdf(fallbackDownloadedBytes, data, cancellationToken);
                    successfulMethods.Add("Primary_RecentDownloadsScan");
                    if (firstSuccessfulPdf == null) firstSuccessfulPdf = fallbackDownloadedBytes;
                }
                else
                {
                    _logger.LogWarning("❌ PRIMARY METHOD FAILED: Found file but it's not a valid PDF");
                }
            }
            else
            {
                _logger.LogWarning("❌ PRIMARY METHOD FAILED: No recently downloaded PDF found in Downloads folder");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ PRIMARY METHOD EXCEPTION: Recent Downloads scan failed - {Message}", ex.Message);
        }

        // FALLBACK METHOD: Aggressive PDF scan (only if primary failed)
        if (firstSuccessfulPdf == null)
        {
            _logger.LogInformation("=== FALLBACK METHOD: Aggressive PDF scan ===");
            try
            {
            var scanResult = await PerformAggressivePdfScan(downloadDir, downloadsFolder, cancellationToken);
            if (scanResult != null && scanResult.Bytes.Length > 0)
            {
                _logger.LogInformation("✅ FALLBACK METHOD SUCCESS: Aggressive scan - {FileSize} bytes", scanResult.Bytes.Length);
                    
                    // Save fallback method PDF with standard blob naming (no method identifier)
                    await SavePrimaryEinLetterPdf(scanResult.Bytes, data, cancellationToken);
                    successfulMethods.Add("Fallback_AggressiveScan");
                    if (firstSuccessfulPdf == null) firstSuccessfulPdf = scanResult.Bytes;
            }
            else
            {
                _logger.LogWarning("❌ FALLBACK METHOD FAILED: Aggressive scan returned null or empty");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("❌ FALLBACK METHOD EXCEPTION: Aggressive scan failed - {Message}", ex.Message);
            }
        }
        else
        {
            _logger.LogInformation("⏭️ SKIPPING FALLBACK METHOD: Primary method already succeeded");
        }

        // Method 9: Additional comprehensive methods
        // COMMENTED OUT - Only keeping Method 8 (AggressiveScan) and Method 10a (RecentDownloadsScan)
        /*
        _logger.LogInformation("=== METHOD 9: Additional comprehensive browser capture methods ===");
        
        // Method 9a: Browser viewer capture
        try
        {
            var pdfBytes = await TryCapturePdfFromBrowserViewer(cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("✅ METHOD 9a SUCCESS: Browser viewer capture - {FileSize} bytes", pdfBytes.Length);
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method9a_BrowserViewer", data, cancellationToken);
                successfulMethods.Add("Method9a_BrowserViewer");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
            }
            else
            {
                _logger.LogWarning("❌ METHOD 9a FAILED: Browser viewer capture returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 9a EXCEPTION: Browser viewer capture failed - {Message}", ex.Message);
        }

        // Method 9b: Direct openPDFNoticeWindow execution
        try
        {
            var pdfBytes = await TryExecuteOpenPDFNoticeWindow(pdfLinkElement, cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                _logger.LogInformation("✅ METHOD 9b SUCCESS: openPDFNoticeWindow execution - {FileSize} bytes", pdfBytes.Length);
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method9b_OpenPDFNoticeWindow", data, cancellationToken);
                successfulMethods.Add("Method9b_OpenPDFNoticeWindow");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
            }
            else
            {
                _logger.LogWarning("❌ METHOD 9b FAILED: openPDFNoticeWindow execution returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 9b EXCEPTION: openPDFNoticeWindow execution failed - {Message}", ex.Message);
        }

        // Method 9c: Direct PDF URL extraction
        try
        {
            var pdfBytes = await TryDirectPdfUrlExtraction(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                _logger.LogInformation("✅ METHOD 9c SUCCESS: Direct PDF URL extraction - {FileSize} bytes", pdfBytes.Length);
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method9c_DirectUrlExtraction", data, cancellationToken);
                successfulMethods.Add("Method9c_DirectUrlExtraction");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
            }
            else
            {
                _logger.LogWarning("❌ METHOD 9c FAILED: Direct PDF URL extraction returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 9c EXCEPTION: Direct PDF URL extraction failed - {Message}", ex.Message);
        }

        // Method 9d: Open PDF tab capture
        try
        {
            var pdfBytes = await TryCapturePdfFromOpenTab(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                _logger.LogInformation("✅ METHOD 9d SUCCESS: Open tab capture - {FileSize} bytes", pdfBytes.Length);
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method9d_OpenTabCapture", data, cancellationToken);
                successfulMethods.Add("Method9d_OpenTabCapture");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
            }
            else
            {
                _logger.LogWarning("❌ METHOD 9d FAILED: Open tab capture returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 9d EXCEPTION: Open tab capture failed - {Message}", ex.Message);
        }

        // Method 9e: Print PDF from opened window
        try
        {
            var pdfBytes = await TryPrintPdfFromOpenedWindow(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                _logger.LogInformation("✅ METHOD 9e SUCCESS: Print PDF from opened window - {FileSize} bytes", pdfBytes.Length);
                await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Method9e_PrintFromWindow", data, cancellationToken);
                successfulMethods.Add("Method9e_PrintFromWindow");
                if (firstSuccessfulPdf == null) firstSuccessfulPdf = pdfBytes;
            }
            else
            {
                _logger.LogWarning("❌ METHOD 9e FAILED: Print PDF from opened window returned null or empty");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("❌ METHOD 9e EXCEPTION: Print PDF from opened window failed - {Message}", ex.Message);
        }
        */

        // NOTE: Method 10a (RecentDownloadsScan) moved to PRIMARY METHOD position above
        // NOTE: Method 10b (Final aggressive scan) removed as it duplicates FALLBACK METHOD above

        // Summary of results
        _logger.LogInformation("📊 DOWNLOAD SUMMARY: {SuccessfulMethodsCount} methods succeeded: {SuccessfulMethods}", 
            successfulMethods.Count, string.Join(", ", successfulMethods));

        // Return the first successful PDF, or null if none succeeded
        if (firstSuccessfulPdf != null)
        {
            _logger.LogInformation("✅ OVERALL SUCCESS: Returning PDF from first successful method ({FileSize} bytes)", firstSuccessfulPdf.Length);
            return firstSuccessfulPdf;
        }
        else
        {
            _logger.LogError("❌ OVERALL FAILURE: All PDF download methods failed");
        throw new Exception("All PDF download methods failed");
        }
    }
    catch (Exception ex)
    {
        _logger.LogError("Selenium PDF download failed: {Message}", ex.Message);
        return null;
    }
    finally
    {
        // Cleanup download directory
        if (!string.IsNullOrEmpty(downloadDir) && Directory.Exists(downloadDir))
        {
            try
            {
                Directory.Delete(downloadDir, true);
                _logger.LogDebug("Cleaned up download directory: {DownloadDir}", downloadDir);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning("Failed to cleanup download directory: {Message}", cleanupEx.Message);
            }
        }
    }
}

private async Task<byte[]?> TryWindowBasedPdfCapture(IWebElement pdfLinkElement, CancellationToken cancellationToken)
{
    try
    {
        // Get href using JavaScript to avoid stale element reference
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        var href = jsExecutor?.ExecuteScript(@"
            var pdfLinks = document.querySelectorAll('a[href*="".pdf""], a[href*=""CP575""], a[onclick*=""openPDFNoticeWindow""]');
            for (var i = 0; i < pdfLinks.length; i++) {
                var linkHref = pdfLinks[i].href || pdfLinks[i].getAttribute('href');
                if (linkHref) return linkHref;
            }
            return null;
        ")?.ToString();
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }
        _logger.LogInformation("Attempting window-based PDF capture from URL: {Url}", href);
        
        // Store current window handle
        var originalWindow = Driver?.CurrentWindowHandle;
        var originalWindows = Driver?.WindowHandles.ToList();
        
        // Handle relative URLs
        if (href.StartsWith("/"))
        {
            var baseUri = new Uri(Driver?.Url ?? "https://sa.www4.irs.gov");
            href = new Uri(baseUri, href).ToString();
        }
        
        // Open PDF in new tab using JavaScript
        jsExecutor = (IJavaScriptExecutor?)Driver;
        _logger.LogInformation("Opening PDF link in new tab: {Url}", href);
        jsExecutor?.ExecuteScript($"window.open('{href}', '_blank');");
        
        // Wait longer for the new tab to open and load
        await Task.Delay(8000, cancellationToken); // Increased delay to 8 seconds
        
        // Find the new window
        var newWindows = Driver?.WindowHandles.ToList();
        var pdfWindow = newWindows?.FirstOrDefault(w => !originalWindows.Contains(w));
        
        if (!string.IsNullOrEmpty(pdfWindow))
        {
            _logger.LogInformation("Successfully opened new window for PDF: {WindowHandle}", pdfWindow);
        }
        else
        {
            _logger.LogWarning("No new window found after opening PDF link. Attempting to retry...");
            // Retry opening the PDF link
            jsExecutor?.ExecuteScript($"window.open('{href}', '_blank');");
            await Task.Delay(8000, cancellationToken); // Increased delay to 8 seconds
            newWindows = Driver?.WindowHandles.ToList();
            pdfWindow = newWindows?.FirstOrDefault(w => !originalWindows.Contains(w));
            
            if (!string.IsNullOrEmpty(pdfWindow))
            {
                _logger.LogInformation("Successfully opened new window for PDF on retry: {WindowHandle}", pdfWindow);
            }
            else
            {
                _logger.LogError("Failed to open new window for PDF after retry");
                return null;
            }
        }
        
        if (!string.IsNullOrEmpty(pdfWindow))
        {
            try
            {
                Driver?.SwitchTo().Window(pdfWindow);
                
                // Wait for the page to fully load and check if it's a PDF
                await Task.Delay(8000, cancellationToken); // Increased delay to 8 seconds
                
                // Check if we're on a PDF page by examining the URL and content
                var currentUrl = Driver?.Url ?? "";
                var pageSource = Driver?.PageSource ?? "";
                
                _logger.LogInformation("Opened window URL: {Url}", currentUrl);
                _logger.LogInformation("Page source length: {Length}", pageSource.Length);
                
                // Wait for PDF to be fully loaded - check for PDF indicators
                var maxWaitTime = 30000; // 30 seconds
                var waitInterval = 2000; // 2 seconds
                var totalWaited = 0;
                var pdfDetected = false;
                
                while (totalWaited < maxWaitTime && !pdfDetected)
                {
                    // Check if page is still loading
                    var readyState = jsExecutor?.ExecuteScript("return document.readyState")?.ToString();
                    if (readyState == "complete")
                    {
                        // Check for PDF indicators
                        var hasPdfViewer = jsExecutor?.ExecuteScript("return document.querySelector('embed[type=\"application/pdf\"]') !== null")?.ToString() == "True";
                        var hasPdfObject = jsExecutor?.ExecuteScript("return document.querySelector('object[type=\"application/pdf\"]') !== null")?.ToString() == "True";
                        var hasPdfIframe = jsExecutor?.ExecuteScript("return document.querySelector('iframe[src*=\".pdf\"]') !== null")?.ToString() == "True";
                        var urlContainsPdf = currentUrl.ToLower().Contains(".pdf");
                        var hasPdfContent = jsExecutor?.ExecuteScript("return document.body.innerHTML.includes('PDF') || document.body.innerHTML.includes('pdf')")?.ToString() == "True";
                        
                        _logger.LogInformation("PDF detection check - Viewer: {HasViewer}, Object: {HasObject}, Iframe: {HasIframe}, URL: {UrlContains}, Content: {HasContent}", 
                            hasPdfViewer, hasPdfObject, hasPdfIframe, urlContainsPdf, hasPdfContent);
                        
                        if (hasPdfViewer || hasPdfObject || hasPdfIframe || urlContainsPdf || hasPdfContent)
                        {
                            _logger.LogInformation("PDF detected, proceeding with capture methods");
                            pdfDetected = true;
                            break;
                        }
                    }
                    
                    await Task.Delay(waitInterval, cancellationToken);
                    totalWaited += waitInterval;
                    _logger.LogInformation("Waiting for PDF to load... ({TotalWaited}ms/{MaxWaitTime}ms)", totalWaited, maxWaitTime);
                }
                
                if (!pdfDetected)
                {
                    _logger.LogWarning("PDF not detected after waiting {MaxWaitTime}ms, but proceeding with capture attempts", maxWaitTime);
                }
                
                // Additional wait to ensure PDF is fully rendered
                await Task.Delay(3000, cancellationToken);
                
                // Method 1: Try Chrome DevTools PDF capture (most reliable for PDFs)
                _logger.LogInformation("Attempting Method 1: Chrome DevTools PDF capture");
                var pdfBytes = await TryChromeDevToolsPdfCapture(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Successfully captured PDF using Chrome DevTools: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
                
                // Method 2: Try to get PDF content via JavaScript (if PDF is embedded)
                _logger.LogInformation("Attempting Method 2: JavaScript PDF extraction");
                pdfBytes = await TryExtractPdfFromPage(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Successfully extracted PDF from page: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
                
                // Method 3: Use print-to-PDF functionality
                _logger.LogInformation("Attempting Method 3: Print-to-PDF");
                pdfBytes = await TryPrintToPdf(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Successfully printed PDF to PDF: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
                
                // Method 4: Try to trigger download from the PDF viewer
                _logger.LogInformation("Attempting Method 4: PDF viewer download trigger");
                pdfBytes = await TryTriggerDownloadFromPdfViewer(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    return pdfBytes;
                }
                
                // Method 5: Try to capture using print dialog
                _logger.LogInformation("Attempting Method 5: Print dialog capture");
                pdfBytes = await TryCapturePdfFromPrintDialog(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Successfully captured PDF from print dialog: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
                
                // Method 6: Try to capture from PDF viewer controls
                _logger.LogInformation("Attempting Method 6: Browser viewer capture");
                pdfBytes = await TryCapturePdfFromBrowserViewer(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Successfully captured PDF from viewer controls: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
                
                // Method 7: Try to print PDF from opened window
                _logger.LogInformation("Attempting Method 7: Print from opened window");
                pdfBytes = await TryPrintPdfFromOpenedWindow(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Successfully printed PDF from opened window: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
                
                _logger.LogWarning("All PDF capture methods failed in window-based capture");
            }
            finally
            {
                // Close the PDF window and switch back
                try
                {
                    Driver?.Close();
                    Driver?.SwitchTo().Window(originalWindow);
                }
                catch (Exception switchEx)
                {
                    _logger.LogWarning("Failed to close PDF window: {Message}", switchEx.Message);
                }
            }
        }
        else
        {
            _logger.LogWarning("No new window found after opening PDF link");
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Window-based PDF capture failed: {Message}", ex.Message);
    }
    return null;
}

// New method to click PDF link and capture from current window
private async Task<byte[]?> TryClickAndCaptureFromCurrentWindow(IWebElement pdfLinkElement, CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Attempting to click PDF link and capture from current window");
        
        // Store current URL
        var originalUrl = Driver?.Url;
        
        // Find fresh PDF element to avoid stale element reference and click
        var pdfElements = Driver?.FindElements(By.XPath("//a[contains(@href, '.pdf')] | //a[contains(@onclick, 'openPDFNoticeWindow')] | //a[contains(@href, 'CP575')]"));
        if (pdfElements != null && pdfElements.Count > 0)
        {
            var freshPdfElement = pdfElements.First();
            // Scroll into view and click
            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", freshPdfElement);
            await Task.Delay(500, cancellationToken);
            freshPdfElement.Click();
        }
        else
        {
            _logger.LogWarning("No fresh PDF elements found for clicking");
            return null;
        }
        await Task.Delay(8000, cancellationToken); // Increased delay to 8 seconds for navigation
        
        // Check if we navigated to a new page
        var newUrl = Driver?.Url;
        if (newUrl != originalUrl)
        {
            _logger.LogInformation("Navigation detected to: {NewUrl}", newUrl);
            
            // Wait for page to load
            await Task.Delay(8000, cancellationToken); // Increased delay to 8 seconds for page loading
            
            // Try Chrome DevTools PDF capture
            var pdfBytes = await TryChromeDevToolsPdfCapture(cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("Successfully captured PDF from current window: {FileSize} bytes", pdfBytes.Length);
                return pdfBytes;
            }
            
            // Try other capture methods
            pdfBytes = await TryExtractPdfFromPage(cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                return pdfBytes;
            }
            
            pdfBytes = await TryPrintToPdf(cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                return pdfBytes;
            }
        }
        else
        {
            _logger.LogInformation("No navigation detected, PDF might have opened in new window or failed to load");
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Click and capture from current window failed: {Message}", ex.Message);
    }
    return null;
}

// New method specifically for printing PDF from opened window
private async Task<byte[]?> TryPrintPdfFromOpenedWindow(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Attempting to print PDF from opened window");
        
        // CRITICAL: Prepare page for clean PDF capture
        await PreparePageForFullCapture(cancellationToken);
        
        // Method 1: Use Chrome DevTools Protocol to print to PDF
        if (Driver is ChromeDriver chromeDriver)
        {
            var chromeDriverType = chromeDriver.GetType();
            var executeChromeCommandMethod = chromeDriverType.GetMethod("ExecuteChromeCommand",
                new Type[] { typeof(string), typeof(Dictionary<string, object>) });
            
            if (executeChromeCommandMethod != null)
            {
                var printParams = new Dictionary<string, object>
                {
                    ["landscape"] = false,
                    ["displayHeaderFooter"] = false,
                    ["printBackground"] = true,
                    ["preferCSSPageSize"] = true,
                    ["paperWidth"] = 8.5,
                    ["paperHeight"] = 11,
                    ["marginTop"] = 0.4,
                    ["marginBottom"] = 0.4,
                    ["marginLeft"] = 0.4,
                    ["marginRight"] = 0.4,
                    ["scale"] = 1.0
                };
                
                var result = executeChromeCommandMethod.Invoke(chromeDriver,
                    new object[] { "Page.printToPDF", printParams });
                
                if (result is Dictionary<string, object> printResult &&
                    printResult.TryGetValue("data", out var base64Data))
                {
                    var pdfBytes = Convert.FromBase64String(base64Data.ToString());
                    if (IsValidPdf(pdfBytes))
                    {
                        _logger.LogInformation("Successfully printed PDF from opened window: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                }
            }
        }
        
        // Method 2: Try keyboard shortcut Ctrl+P and capture
        var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
        actions.KeyDown(Keys.Control).SendKeys("p").KeyUp(Keys.Control).Perform();
        await Task.Delay(2000, cancellationToken);
        
        // Try to find and click "Save as PDF" option
        var saveAsPdfSelectors = new[]
        {
            "//button[contains(text(), 'Save as PDF')]",
            "//button[contains(text(), 'Save')]",
            "//*[contains(text(), 'Save as PDF')]",
            "//*[@id='save-as-pdf']",
            "//button[@aria-label='Save as PDF']",
            "//button[contains(@class, 'save-pdf')]"
        };
        
        foreach (var selector in saveAsPdfSelectors)
        {
            try
            {
                var saveBtn = Driver?.FindElement(By.XPath(selector));
                if (saveBtn != null && saveBtn.Displayed && saveBtn.Enabled)
                {
                    saveBtn.Click();
                    _logger.LogInformation("Clicked Save as PDF button using selector: {Selector}", selector);
                    await Task.Delay(3000, cancellationToken);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Save as PDF button selector {Selector} failed: {Message}", selector, ex.Message);
            }
        }
        
        // Method 3: Try to find and click print button in PDF viewer
        var printSelectors = new[]
        {
            "//button[@title='Print']",
            "//button[contains(@class, 'print')]",
            "//a[@title='Print']",
            "//a[contains(@class, 'print')]",
            "//*[contains(text(), 'Print')]",
            "//*[@id='print']",
            "//button[@aria-label='Print']"
        };
        
        foreach (var selector in printSelectors)
        {
            try
            {
                var printBtn = Driver?.FindElement(By.XPath(selector));
                if (printBtn != null && printBtn.Displayed && printBtn.Enabled)
                {
                    printBtn.Click();
                    _logger.LogInformation("Clicked Print button using selector: {Selector}", selector);
                    await Task.Delay(2000, cancellationToken);
                    
                    // After clicking print, try to capture the print dialog
                    var pdfBytes = await TryCapturePdfFromPrintDialog(cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        return pdfBytes;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Print button selector {Selector} failed: {Message}", selector, ex.Message);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Print PDF from opened window failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryExtractPdfFromPage(CancellationToken cancellationToken)
{
    try
    {
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        // Check if page contains PDF content
        var pageSource = Driver?.PageSource?.ToLower() ?? "";
        if (pageSource.Contains("application/pdf") || pageSource.Contains("%pdf"))
        {
            _logger.LogInformation("Page appears to contain PDF content");
            // Try to extract PDF data if it's embedded
            var script = @"
                var pdfData = null;
                var embeds = document.getElementsByTagName('embed');
                for (var i = 0; i < embeds.length; i++) {
                    if (embeds[i].type === 'application/pdf') {
                        pdfData = embeds[i].src;
                        break;
                    }
                }
                if (!pdfData) {
                    var objects = document.getElementsByTagName('object');
                    for (var i = 0; i < objects.length; i++) {
                        if (objects[i].type === 'application/pdf') {
                            pdfData = objects[i].data;
                            break;
                        }
                    }
                }
                return pdfData;
            ";
            var pdfSrc = jsExecutor?.ExecuteScript(script)?.ToString();
            if (!string.IsNullOrEmpty(pdfSrc))
            {
                _logger.LogInformation("Found embedded PDF source: {PdfSrc}", pdfSrc);
                // Download the PDF content directly
                return await DownloadPdfFromUrl(pdfSrc, cancellationToken);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("PDF extraction from page failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryTriggerDownloadFromPdfViewer(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("🔽 Enhanced PDF viewer download with full content support");
        
        // Prepare page for optimal PDF extraction
        await PreparePageForFullCapture(cancellationToken);
        
        // Method 1: Enhanced Chrome DevTools capture from viewer
        if (Driver is ChromeDriver chromeDriver)
        {
            _logger.LogInformation("📱 Viewer Strategy 1: Enhanced DevTools capture");
            var pdfBytes = await TryChromeDevToolsPdfCapture(cancellationToken, null);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("✅ Viewer Strategy 1 SUCCESS: {FileSize} bytes", pdfBytes.Length);
                return pdfBytes;
            }
        }
        
        // Method 2: Try to extract PDF content directly from viewer with enhanced detection
        _logger.LogInformation("🎯 Viewer Strategy 2: Enhanced content extraction");
        var pdfBytesExtracted = await TryExtractPdfContentFromViewerEnhanced(cancellationToken);
        if (pdfBytesExtracted != null && pdfBytesExtracted.Length > 0)
        {
            _logger.LogInformation("✅ Viewer Strategy 2 SUCCESS: {FileSize} bytes", pdfBytesExtracted.Length);
            return pdfBytesExtracted;
        }

        // Method 3: Enhanced download button detection with PDF-specific triggers
        _logger.LogInformation("🔘 Viewer Strategy 3: Enhanced download button detection");
        await TryPdfSpecificDownloadTriggers(cancellationToken);
        
        // Method 4: Comprehensive download selectors
        var downloadSelectors = new[]
        {
            // Standard download buttons
            "//button[@title='Download']",
            "//button[contains(@class, 'download')]",
            "//a[@title='Download']", 
            "//a[contains(@class, 'download')]",
            "//*[contains(text(), 'Download')]",
            "//*[@id='download']",
            "//button[contains(@aria-label, 'Download')]",
            "//a[contains(@aria-label, 'Download')]",
            
            // PDF viewer specific selectors
            "//button[contains(@class, 'toolbar-button') and contains(@title, 'Download')]",
            "//*[@class='download-button']",
            "//*[contains(@class, 'pdf-download')]",
            "//button[contains(@onclick, 'download')]",
            "//a[contains(@onclick, 'download')]",
            
            // Browser PDF viewer controls (Chrome, Firefox, etc.)
            "//*[@id='chrome-pdf-viewer']//button[contains(@title, 'Download')]",
            "//*[contains(@class, 'pdf-viewer-toolbar')]//button",
            "//button[contains(@class, 'pdf-toolbar')]",
            "//*[@role='toolbar']//button[contains(@title, 'Download')]"
        };
        
        foreach (var selector in downloadSelectors)
        {
            try
            {
                var downloadBtns = Driver?.FindElements(By.XPath(selector));
                if (downloadBtns != null && downloadBtns.Count > 0)
                {
                    foreach (var downloadBtn in downloadBtns)
                    {
                        if (downloadBtn != null && downloadBtn.Displayed && downloadBtn.Enabled)
                        {
                            // Scroll into view and click
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", downloadBtn);
                            await Task.Delay(500, cancellationToken);
                            
                            try
                            {
                                downloadBtn.Click();
                                _logger.LogInformation("✅ Viewer Strategy 3 SUCCESS: Clicked download button with selector: {Selector}", selector);
                            }
                            catch
                            {
                                // Try JavaScript click as fallback
                                ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].click();", downloadBtn);
                                _logger.LogInformation("✅ Viewer Strategy 3 SUCCESS: JS-clicked download button with selector: {Selector}", selector);
                            }
                            
                            await Task.Delay(3000, cancellationToken);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Download button selector {Selector} failed: {Message}", selector, ex.Message);
            }
        }

        // Method 4: Try to trigger print dialog and capture as PDF
        pdfBytes = await TryCapturePdfFromPrintDialog(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
            _logger.LogInformation("Successfully captured PDF from print dialog: {FileSize} bytes", pdfBytes.Length);
            return pdfBytes;
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("PDF viewer download trigger failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryExtractPdfContentFromViewer(CancellationToken cancellationToken)
{
    try
    {
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // Enhanced JavaScript to extract PDF content from various sources
        var script = @"
            function extractPdfContent() {
                // Method 1: Extract PDF URL from openPDFNoticeWindow function
                var pdfLinks = document.querySelectorAll('a[onclick*=""openPDFNoticeWindow""]');
                for (var i = 0; i < pdfLinks.length; i++) {
                    var onclick = pdfLinks[i].getAttribute('onclick');
                    if (onclick) {
                        var match = onclick.match(/openPDFNoticeWindow\('([^']+)'\)/);
                        if (match && match[1]) {
                            var pdfUrl = match[1];
                            // Handle relative URLs
                            if (pdfUrl.startsWith('/')) {
                                pdfUrl = window.location.origin + pdfUrl;
                            }
                            return { type: 'openPDFNoticeWindow', src: pdfUrl, element: pdfLinks[i].href };
                        }
                    }
                }
                
                // Method 2: Extract from CP575 links specifically
                var cp575Links = document.querySelectorAll('a[href*=""CP575""]');
                for (var i = 0; i < cp575Links.length; i++) {
                    var href = cp575Links[i].href;
                    if (href && (href.includes('.pdf') || href.includes('CP575'))) {
                        return { type: 'cp575', src: href };
                    }
                }
                
                // Method 3: Check for embedded PDF data
                var embeds = document.getElementsByTagName('embed');
                for (var i = 0; i < embeds.length; i++) {
                    if (embeds[i].type === 'application/pdf') {
                        return { type: 'embed', src: embeds[i].src, data: embeds[i].data };
                    }
                }
                
                // Method 4: Check for object tags
                var objects = document.getElementsByTagName('object');
                for (var i = 0; i < objects.length; i++) {
                    if (objects[i].type === 'application/pdf') {
                        return { type: 'object', src: objects[i].data, data: objects[i].data };
                    }
                }
                
                // Method 5: Check for iframe with PDF
                var iframes = document.getElementsByTagName('iframe');
                for (var i = 0; i < iframes.length; i++) {
                    var src = iframes[i].src;
                    if (src && (src.includes('.pdf') || src.includes('application/pdf'))) {
                        return { type: 'iframe', src: src };
                    }
                }
                
                // Method 6: Check for PDF viewer elements
                var pdfViewers = document.querySelectorAll('[data-pdf-url], [data-pdf-src]');
                for (var i = 0; i < pdfViewers.length; i++) {
                    var pdfUrl = pdfViewers[i].getAttribute('data-pdf-url') || pdfViewers[i].getAttribute('data-pdf-src');
                    if (pdfUrl) {
                        return { type: 'viewer', src: pdfUrl };
                    }
                }
                
                // Method 7: Check current URL for PDF
                if (window.location.href.includes('.pdf') || window.location.href.includes('application/pdf')) {
                    return { type: 'url', src: window.location.href };
                }
                
                // Method 8: Look for any PDF links in the page
                var allLinks = document.querySelectorAll('a[href*="".pdf""]');
                for (var i = 0; i < allLinks.length; i++) {
                    var href = allLinks[i].href;
                    if (href && href.includes('.pdf')) {
                        return { type: 'generic', src: href };
                    }
                }
                
                return null;
            }
            return extractPdfContent();
        ";
        
        var result = jsExecutor?.ExecuteScript(script);
        if (result != null)
        {
            _logger.LogInformation("Found PDF content source: {Result}", result);
            
            // If we found a PDF source, try to download it
            if (result is Dictionary<string, object> pdfInfo)
            {
                var src = pdfInfo.GetValueOrDefault("src")?.ToString();
                if (!string.IsNullOrEmpty(src))
                {
                    return await DownloadPdfFromUrl(src, cancellationToken);
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("PDF content extraction failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryCapturePdfFromPrintDialog(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("🖨️ Enhanced print dialog capture with full content support");
        
        // Prepare page for full content capture first
        await PreparePageForFullCapture(cancellationToken);
        
        // Method 1: Enhanced Chrome DevTools Protocol with full page parameters
        if (Driver is ChromeDriver chromeDriver)
        {
            try
            {
                _logger.LogInformation("📄 Print Dialog Strategy 1: Full page DevTools print");
                var printParams = new Dictionary<string, object>
                {
                    ["landscape"] = false,
                    ["displayHeaderFooter"] = false,
                    ["printBackground"] = true,
                    ["preferCSSPageSize"] = true,
                    ["paperWidth"] = 8.5,
                    ["paperHeight"] = 11,
                    ["marginTop"] = 0.0,      // Zero margins for full content
                    ["marginBottom"] = 0.0,
                    ["marginLeft"] = 0.0,
                    ["marginRight"] = 0.0,
                    ["scale"] = 1.0,
                    ["generateTaggedPDF"] = false
                };
                
                var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printParams);
                
                if (result is Dictionary<string, object> printResult &&
                    printResult.TryGetValue("data", out var base64Data) &&
                    base64Data?.ToString() is string base64String &&
                    !string.IsNullOrEmpty(base64String))
                {
                    var pdfBytes = Convert.FromBase64String(base64String);
                    if (IsValidPdf(pdfBytes))
                    {
                        _logger.LogInformation("✅ Print Dialog Strategy 1 SUCCESS: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Print Dialog Strategy 1 failed: {Message}", ex.Message);
            }
            
            // Method 2: PDF content focused print
            try
            {
                _logger.LogInformation("📋 Print Dialog Strategy 2: PDF content focused");
                var pdfBytes = await TryPdfContentAreaCapture(chromeDriver, cancellationToken);
                if (pdfBytes != null && IsValidPdf(pdfBytes))
                {
                    _logger.LogInformation("✅ Print Dialog Strategy 2 SUCCESS: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Print Dialog Strategy 2 failed: {Message}", ex.Message);
            }
        }
        
        // Method 2: Try keyboard shortcut Ctrl+P and capture
        var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
        actions.KeyDown(Keys.Control).SendKeys("p").KeyUp(Keys.Control).Perform();
        await Task.Delay(2000, cancellationToken);
        
        // Try to find and click "Save as PDF" option
        var saveAsPdfSelectors = new[]
        {
            "//button[contains(text(), 'Save as PDF')]",
            "//button[contains(text(), 'Save')]",
            "//*[contains(text(), 'Save as PDF')]",
            "//*[@id='save-as-pdf']"
        };
        
        foreach (var selector in saveAsPdfSelectors)
        {
            try
            {
                var saveBtn = Driver?.FindElement(By.XPath(selector));
                if (saveBtn != null && saveBtn.Displayed && saveBtn.Enabled)
                {
                    saveBtn.Click();
                    _logger.LogInformation("Clicked Save as PDF button");
                    await Task.Delay(3000, cancellationToken);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Save as PDF button selector {Selector} failed: {Message}", selector, ex.Message);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Print dialog PDF capture failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryPrintToPdf(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("🖨️ Enhanced print-to-PDF with full content capture");
        
        // Prepare page for full content capture first
        await PreparePageForFullCapture(cancellationToken);
        
        if (Driver is ChromeDriver chromeDriver)
        {
            // Strategy 1: High-quality print with optimized parameters
            try
            {
                _logger.LogInformation("📄 Print Strategy 1: High-quality full page print");
                var printParams = new Dictionary<string, object>
                {
                    ["landscape"] = false,
                    ["displayHeaderFooter"] = false,
                    ["printBackground"] = true,
                    ["scale"] = 1.0,
                    ["paperWidth"] = 8.5,
                    ["paperHeight"] = 11.0,
                    ["marginTop"] = 0.0,
                    ["marginBottom"] = 0.0,
                    ["marginLeft"] = 0.0,
                    ["marginRight"] = 0.0,
                    ["preferCSSPageSize"] = true
                };
                
                var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printParams);
                if (result is Dictionary<string, object> printResult &&
                    printResult.TryGetValue("data", out var base64Data) &&
                    base64Data?.ToString() is string base64String &&
                    !string.IsNullOrEmpty(base64String))
                {
                    var pdfBytes = Convert.FromBase64String(base64String);
                    if (IsValidPdf(pdfBytes))
                    {
                        _logger.LogInformation("✅ Print Strategy 1 SUCCESS: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Print Strategy 1 failed: {Message}", ex.Message);
            }
            
            // Strategy 2: Focus on PDF content area before printing
            try
            {
                _logger.LogInformation("📋 Print Strategy 2: PDF content focused print");
                var pdfBytes = await TryPdfContentAreaCapture(chromeDriver, cancellationToken);
                if (pdfBytes != null && IsValidPdf(pdfBytes))
                {
                    _logger.LogInformation("✅ Print Strategy 2 SUCCESS: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Print Strategy 2 failed: {Message}", ex.Message);
            }
            
            // Strategy 3: Original method as fallback
            try
            {
                _logger.LogInformation("📰 Print Strategy 3: Original print method");
                var printParams = new Dictionary<string, object>();
                var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printParams);
                if (result is Dictionary<string, object> printResult &&
                    printResult.TryGetValue("data", out var base64Data) &&
                    base64Data?.ToString() is string base64String &&
                    !string.IsNullOrEmpty(base64String))
                {
                    var pdfBytes = Convert.FromBase64String(base64String);
                    _logger.LogInformation("✅ Print Strategy 3 SUCCESS: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Print Strategy 3 failed: {Message}", ex.Message);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Enhanced print to PDF failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryCapturePdfFromOpenTab(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Searching for open PDF tabs");
        
        // CRITICAL: Prepare page for clean PDF capture on current tab first
        await PreparePageForFullCapture(cancellationToken);
        
        var originalWindow = Driver?.CurrentWindowHandle;
        var allWindows = Driver?.WindowHandles.ToList() ?? new List<string>();
        foreach (var windowHandle in allWindows)
        {
            try
            {
                Driver?.SwitchTo().Window(windowHandle);
                var currentUrl = Driver?.Url?.ToLower() ?? "";
                _logger.LogDebug("Checking window with URL: {Url}", currentUrl);
                if (currentUrl.Contains(".pdf") || currentUrl.Contains("cp575"))
                {
                    _logger.LogInformation("Found PDF tab: {Url}", currentUrl);
                    
                    // Method 1: Try to download from this URL directly
                    var pdfBytes = await DownloadPdfFromUrl(Driver?.Url, cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully downloaded PDF from URL: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                    
                    // Method 2: Try to extract PDF content from the page
                    pdfBytes = await TryExtractPdfContentFromViewer(cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully extracted PDF content from page: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                    
                    // Method 3: Try print to PDF using Chrome DevTools
                    pdfBytes = await TryPrintToPdf(cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully printed PDF to PDF: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                    
                    // Method 4: Try to capture from PDF viewer controls
                    pdfBytes = await TryTriggerDownloadFromPdfViewer(cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully captured PDF from viewer controls: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                    
                    // Method 5: Try to capture using print dialog
                    pdfBytes = await TryCapturePdfFromPrintDialog(cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully captured PDF from print dialog: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error checking window {WindowHandle}: {Message}", windowHandle, ex.Message);
            }
        }
        // Switch back to original window
        if (!string.IsNullOrEmpty(originalWindow))
        {
            try
            {
                Driver?.SwitchTo().Window(originalWindow);
            }
            catch (Exception switchEx)
            {
                _logger.LogWarning("Failed to switch back to original window: {Message}", switchEx.Message);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning("PDF tab capture failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryExecuteOpenPDFNoticeWindow(IWebElement pdfLinkElement, CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Attempting to execute openPDFNoticeWindow function directly");
        
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // Method 1: Extract PDF URL from onclick attribute
        var onclick = pdfLinkElement.GetAttribute("onclick");
        if (!string.IsNullOrEmpty(onclick))
        {
            var match = Regex.Match(onclick, @"openPDFNoticeWindow\('([^']+)'\)");
            if (match.Success)
            {
                var pdfUrl = match.Groups[1].Value;
                _logger.LogInformation("Extracted PDF URL from openPDFNoticeWindow: {PdfUrl}", pdfUrl);
                
                // Handle relative URLs
                if (pdfUrl.StartsWith("/"))
                {
                    var baseUri = new Uri(Driver?.Url ?? "https://sa.www4.irs.gov");
                    pdfUrl = new Uri(baseUri, pdfUrl).ToString();
                }
                
                // Try to download the PDF directly
                var pdfBytes = await DownloadPdfFromUrl(pdfUrl, cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Successfully downloaded PDF via openPDFNoticeWindow URL: {FileSize} bytes", pdfBytes.Length);
                    return pdfBytes;
                }
            }
        }
        
        // Method 2: Execute the openPDFNoticeWindow function directly
        var script = @"
            function executeOpenPDFNoticeWindow() {
                try {
                    // Find the PDF link with openPDFNoticeWindow
                    var pdfLink = document.querySelector('a[onclick*=""openPDFNoticeWindow""]');
                    if (pdfLink) {
                        var onclick = pdfLink.getAttribute('onclick');
                        if (onclick) {
                            var match = onclick.match(/openPDFNoticeWindow\('([^']+)'\)/);
                            if (match && match[1]) {
                                var pdfUrl = match[1];
                                // Handle relative URLs
                                if (pdfUrl.startsWith('/')) {
                                    pdfUrl = window.location.origin + pdfUrl;
                                }
                                return { success: true, url: pdfUrl };
                            }
                        }
                    }
                    return { success: false, error: 'No openPDFNoticeWindow found' };
                } catch (e) {
                    return { success: false, error: e.message };
                }
            }
            return executeOpenPDFNoticeWindow();
        ";
        
        var result = jsExecutor?.ExecuteScript(script);
        if (result != null && result is Dictionary<string, object> jsResult)
        {
            if (jsResult.TryGetValue("success", out var success) && success is bool isSuccess && isSuccess)
            {
                if (jsResult.TryGetValue("url", out var url) && url is string pdfUrl)
                {
                    _logger.LogInformation("Successfully extracted PDF URL via JavaScript: {PdfUrl}", pdfUrl);
                    
                    var pdfBytes = await DownloadPdfFromUrl(pdfUrl, cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully downloaded PDF via JavaScript extraction: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                }
            }
        }
        
        // Method 3: Try to execute the onclick function directly
        try
        {
            jsExecutor?.ExecuteScript("arguments[0].click();", pdfLinkElement);
            await Task.Delay(3000, cancellationToken);
            
            // Check if a new window/tab opened
            var newWindows = Driver?.WindowHandles.ToList();
            var originalWindows = Driver?.WindowHandles.ToList();
            
            if (newWindows != null && newWindows.Count > originalWindows?.Count)
            {
                var newWindow = newWindows.FirstOrDefault(w => !originalWindows.Contains(w));
                if (!string.IsNullOrEmpty(newWindow))
                {
                    Driver?.SwitchTo().Window(newWindow);
                    await Task.Delay(2000, cancellationToken);
                    
                    var pdfBytes = await TryExtractPdfContentFromViewer(cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully captured PDF from new window: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                    
                    // Close the new window and switch back
                    Driver?.Close();
                    Driver?.SwitchTo().Window(originalWindows.First());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Direct onclick execution failed: {Message}", ex.Message);
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("openPDFNoticeWindow execution failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryDirectPdfUrlExtraction(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Attempting direct PDF URL extraction from page");
        
        // CRITICAL: Prepare page for clean PDF capture first
        await PreparePageForFullCapture(cancellationToken);
        
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // JavaScript to extract the exact PDF URL from the page
        var script = @"
            function extractDirectPdfUrl() {
                try {
                    // Method 1: Look for the exact link with 'CLICK HERE for Your EIN Confirmation Letter'
                    var confirmationLinks = document.querySelectorAll('a');
                    for (var i = 0; i < confirmationLinks.length; i++) {
                        var link = confirmationLinks[i];
                        var text = link.textContent || link.innerText || '';
                        if (text.includes('CLICK HERE for Your EIN Confirmation Letter')) {
                            var onclick = link.getAttribute('onclick');
                            if (onclick) {
                                var match = onclick.match(/openPDFNoticeWindow\('([^']+)'\)/);
                                if (match && match[1]) {
                                    var pdfUrl = match[1];
                                    if (pdfUrl.startsWith('/')) {
                                        pdfUrl = window.location.origin + pdfUrl;
                                    }
                                    return { success: true, url: pdfUrl, method: 'confirmation_link' };
                                }
                            }
                        }
                    }
                    
                    // Method 2: Look for any link with CP575 in onclick
                    var cp575Links = document.querySelectorAll('a[onclick*=""CP575""]');
                    for (var i = 0; i < cp575Links.length; i++) {
                        var onclick = cp575Links[i].getAttribute('onclick');
                        if (onclick) {
                            var match = onclick.match(/openPDFNoticeWindow\('([^']+)'\)/);
                            if (match && match[1]) {
                                var pdfUrl = match[1];
                                if (pdfUrl.startsWith('/')) {
                                    pdfUrl = window.location.origin + pdfUrl;
                                }
                                return { success: true, url: pdfUrl, method: 'cp575_onclick' };
                            }
                        }
                    }
                    
                    // Method 3: Search page source for CP575 PDF URLs
                    var pageText = document.documentElement.outerHTML;
                    var pdfUrlMatches = pageText.match(/\/modein\/notices\/CP575_[^""\s]+\.pdf/g);
                    if (pdfUrlMatches && pdfUrlMatches.length > 0) {
                        var pdfUrl = pdfUrlMatches[0];
                        if (pdfUrl.startsWith('/')) {
                            pdfUrl = window.location.origin + pdfUrl;
                        }
                        return { success: true, url: pdfUrl, method: 'page_source' };
                    }
                    
                    // Method 4: Look for any PDF URL in the page
                    var allPdfMatches = pageText.match(/\/modein\/notices\/[^""\s]+\.pdf/g);
                    if (allPdfMatches && allPdfMatches.length > 0) {
                        var pdfUrl = allPdfMatches[0];
                        if (pdfUrl.startsWith('/')) {
                            pdfUrl = window.location.origin + pdfUrl;
                        }
                        return { success: true, url: pdfUrl, method: 'any_pdf' };
                    }
                    
                    return { success: false, error: 'No PDF URL found in page' };
                } catch (e) {
                    return { success: false, error: e.message };
                }
            }
            return extractDirectPdfUrl();
        ";
        
        var result = jsExecutor?.ExecuteScript(script);
        if (result != null && result is Dictionary<string, object> jsResult)
        {
            if (jsResult.TryGetValue("success", out var success) && success is bool isSuccess && isSuccess)
            {
                if (jsResult.TryGetValue("url", out var url) && url is string pdfUrl)
                {
                    var method = jsResult.TryGetValue("method", out var methodObj) ? methodObj?.ToString() : "unknown";
                    _logger.LogInformation("Successfully extracted PDF URL via {Method}: {Url}", method, pdfUrl);
                    
                    // Try to download the PDF directly
                    var pdfBytes = await DownloadPdfFromUrl(pdfUrl, cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully downloaded PDF via direct URL: {FileSize} bytes", pdfBytes.Length);
                        return pdfBytes;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to download PDF from extracted URL: {Url}", pdfUrl);
                    }
                }
            }
            else if (jsResult.TryGetValue("error", out var error))
            {
                _logger.LogDebug("Failed to extract PDF URL: {Error}", error);
            }
        }
        
        // Fallback: Try to construct the PDF URL based on the current page
        _logger.LogInformation("Attempting to construct PDF URL from page context");
        var currentUrl = Driver?.Url ?? "";
        var baseUrl = currentUrl.Substring(0, currentUrl.LastIndexOf('/'));
        
        // Try different PDF URL patterns
        var timestamp = DateTime.Now.Ticks;
        var urlPatterns = new[]
        {
            $"{baseUrl}/modein/notices/CP575_{timestamp}.pdf",
            $"{baseUrl}/notices/CP575_{timestamp}.pdf",
            $"{baseUrl}/modein/CP575_{timestamp}.pdf"
        };
        
        foreach (var pattern in urlPatterns)
        {
            _logger.LogInformation("Trying constructed PDF URL: {Url}", pattern);
            
            var pdfBytes = await DownloadPdfFromUrl(pattern, cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("Successfully downloaded PDF via constructed URL: {FileSize} bytes", pdfBytes.Length);
                return pdfBytes;
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Direct PDF URL extraction failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> TryChromeDevToolsPdfCapture(CancellationToken cancellationToken, CaseData? data = null)
{
    try
    {
        _logger.LogInformation("Attempting enhanced Chrome DevTools PDF capture with full-page support");
        
        if (Driver is ChromeDriver chromeDriver)
        {
            try
            {
                // Step 1: Pre-capture optimizations for full content
                await PreparePageForFullCapture(cancellationToken);
                
                // Step 2: Try multiple PDF capture strategies
                byte[]? pdfBytes = null;
                
                // Strategy 1: High-quality full page capture
                _logger.LogInformation("Strategy 1: High-quality full page PDF capture");
                pdfBytes = await TryFullPagePdfCapture(chromeDriver, cancellationToken);
                if (pdfBytes != null && IsValidPdf(pdfBytes))
                {
                    _logger.LogInformation("✅ Strategy 1 SUCCESS: Full page capture - {FileSize} bytes", pdfBytes.Length);
                    await SaveChromeDevToolsPdfToBlob(pdfBytes, data, cancellationToken);
                    return pdfBytes;
                }
                
                // Strategy 2: Focus on PDF content area
                _logger.LogInformation("Strategy 2: PDF content area focused capture");
                pdfBytes = await TryPdfContentAreaCapture(chromeDriver, cancellationToken);
                if (pdfBytes != null && IsValidPdf(pdfBytes))
                {
                    _logger.LogInformation("✅ Strategy 2 SUCCESS: PDF content area capture - {FileSize} bytes", pdfBytes.Length);
                    await SaveChromeDevToolsPdfToBlob(pdfBytes, data, cancellationToken);
                    return pdfBytes;
                }
                
                // Strategy 3: Original method as fallback
                _logger.LogInformation("Strategy 3: Original method fallback");
                var printParams = new Dictionary<string, object>();
                var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printParams);
                
                if (result is Dictionary<string, object> printResult &&
                    printResult.TryGetValue("data", out var base64Data) &&
                    base64Data?.ToString() is string base64String &&
                    !string.IsNullOrEmpty(base64String))
                {
                    pdfBytes = Convert.FromBase64String(base64String);
                    if (IsValidPdf(pdfBytes))
                    {
                        _logger.LogInformation("✅ Strategy 3 SUCCESS: Original method - {FileSize} bytes", pdfBytes.Length);
                        await SaveChromeDevToolsPdfToBlob(pdfBytes, data, cancellationToken);
                        return pdfBytes;
                    }
                }
                
                _logger.LogWarning("❌ All PDF capture strategies failed");
            }
            catch (Exception cdpEx)
            {
                _logger.LogWarning("Chrome DevTools PDF capture failed: {Message}", cdpEx.Message);
            }
        }
        else
        {
            _logger.LogWarning("Driver is not ChromeDriver, cannot use DevTools Protocol");
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Chrome DevTools PDF capture failed: {Message}", ex.Message);
    }
    return null;
}

private async Task PreparePageForFullCapture(CancellationToken cancellationToken)
{
    try
    {
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        if (jsExecutor == null) return;
        
        _logger.LogInformation("🔧 Preparing page for full PDF content capture - AGGRESSIVE navigation removal...");
        
        // PHASE 1: Aggressive Navigation Panel Removal
        var phase1Script = @"
            console.log('🔧 PHASE 1: Starting aggressive navigation panel removal...');
            
            // IRS.gov specific navigation panel selectors
            var irsNavigationSelectors = [
                // Main navigation containers
                '#main-navigation', '.main-navigation', '[class*=""main-nav""]',
                '#navigation', '.navigation', '[class*=""navigation""]',
                '#sidebar', '.sidebar', '[class*=""sidebar""]', '[id*=""sidebar""]',
                '#left-panel', '.left-panel', '#right-panel', '.right-panel',
                
                // IRS specific panels
                '#content-navigation', '.content-navigation',
                '.panel-left', '.panel-right', '.navigation-panel',
                '[class*=""panel-nav""]', '[id*=""panel-nav""]',
                
                // Common UI elements that cause issues
                '.breadcrumb', '#breadcrumb', '[class*=""breadcrumb""]',
                '.header', '#header', '.footer', '#footer',
                '.menu', '#menu', '[class*=""menu""]', '[id*=""menu""]',
                '.toolbar', '#toolbar', '[class*=""toolbar""]',
                
                // Canvas and overlay elements (critical for PDF view)
                'canvas:not([data-pdf-canvas])', '.canvas', '#canvas',
                '.overlay', '#overlay', '[class*=""overlay""]',
                '.modal', '#modal', '[class*=""modal""]'
            ];
            
            var hiddenCount = 0;
            irsNavigationSelectors.forEach(function(selector) {
                try {
                    var elements = document.querySelectorAll(selector);
                    elements.forEach(function(el) {
                        // Safety check: Don't hide if it's the actual PDF container
                        var isPdfContainer = el.querySelector('embed[type*=""pdf""]') || 
                                           el.querySelector('object[type*=""pdf""]') || 
                                           el.querySelector('iframe[src*="".pdf""]') ||
                                           el.id === 'pdf-isolation-container';
                        
                        if (!isPdfContainer) {
                            el.style.display = 'none !important';
                            el.style.visibility = 'hidden !important';
                            el.style.opacity = '0 !important';
                            el.style.position = 'absolute !important';
                            el.style.left = '-9999px !important';
                            hiddenCount++;
                            console.log('Hidden navigation element:', selector, el);
                        }
                    });
                } catch(e) { 
                    console.log('Error hiding selector:', selector, e);
                }
            });
            
            console.log('✅ PHASE 1 Complete: Hidden ' + hiddenCount + ' navigation elements');
            return 'PHASE1_COMPLETE';
        ";
        
        var phase1Result = jsExecutor.ExecuteScript(phase1Script);
        _logger.LogInformation("📄 Phase 1 result: {Result}", phase1Result);
        await Task.Delay(1000, cancellationToken);
        
        // PHASE 2: PDF Content Isolation
        var phase2Script = @"
            console.log('🔧 PHASE 2: PDF content isolation...');
            
            // Find PDF elements with comprehensive selectors
            var pdfSelectors = [
                'embed[type*=""pdf""]', 
                'object[type*=""pdf""]', 
                'object[data*="".pdf""]',
                'iframe[src*="".pdf""]',
                'embed[src*="".pdf""]',
                'embed:not([type])', // Some PDFs don't specify type
                'object[data*=""application/pdf""]',
                '#pdf-viewer', '.pdf-viewer',
                '#document-viewer', '.document-viewer',
                '#pdfViewer', '.pdfViewer',
                '[class*=""pdf""]', '[id*=""pdf""]'
            ];
            
            var pdfElement = null;
            var foundSelector = '';
            
            for (var i = 0; i < pdfSelectors.length; i++) {
                var elements = document.querySelectorAll(pdfSelectors[i]);
                if (elements.length > 0) {
                    pdfElement = elements[0];
                    foundSelector = pdfSelectors[i];
                    console.log('Found PDF element with selector:', foundSelector, pdfElement);
                    break;
                }
            }
            
            if (pdfElement) {
                console.log('✅ PDF element found, creating isolation container...');
                
                // Remove any existing isolation container
                var existingContainer = document.getElementById('pdf-isolation-container');
                if (existingContainer) {
                    existingContainer.remove();
                }
                
                // Create clean isolation container
                var cleanContainer = document.createElement('div');
                cleanContainer.id = 'pdf-isolation-container';
                cleanContainer.style.cssText = 
                    'position: fixed !important;' +
                    'top: 0 !important;' +
                    'left: 0 !important;' +
                    'width: 100vw !important;' +
                    'height: 100vh !important;' +
                    'background: white !important;' +
                    'z-index: 999999 !important;' +
                    'overflow: hidden !important;' +
                    'margin: 0 !important;' +
                    'padding: 0 !important;' +
                    'border: none !important;';
                
                // Clone PDF element with all attributes
                var pdfClone = pdfElement.cloneNode(true);
                pdfClone.style.cssText = 
                    'width: 100% !important;' +
                    'height: 100% !important;' +
                    'border: none !important;' +
                    'margin: 0 !important;' +
                    'padding: 0 !important;' +
                    'position: static !important;' +
                    'display: block !important;' +
                    'visibility: visible !important;' +
                    'opacity: 1 !important;';
                
                // If it's an embed or object, ensure src/data is preserved
                if (pdfElement.src) pdfClone.src = pdfElement.src;
                if (pdfElement.data) pdfClone.data = pdfElement.data;
                
                cleanContainer.appendChild(pdfClone);
                
                // Hide all body children except our container
                var bodyChildren = Array.from(document.body.children);
                bodyChildren.forEach(function(child) {
                    if (child.id !== 'pdf-isolation-container') {
                        child.style.display = 'none !important';
                    }
                });
                
                // Style body for clean PDF viewing
                document.body.style.overflow = 'hidden !important';
                document.body.style.background = 'white !important';
                document.body.style.margin = '0 !important';
                document.body.style.padding = '0 !important';
                
                // Add our clean container
                document.body.appendChild(cleanContainer);
                
                // Force focus on PDF
                try {
                    pdfClone.focus();
                    pdfClone.scrollIntoView();
                } catch(e) { /* ignore focus errors */ }
                
                console.log('✅ PDF isolation container created successfully');
                return 'PDF_ISOLATED_SUCCESS';
            } else {
                console.log('❌ No PDF element found - applying general cleanup');
                
                // If no PDF found, at least clean up the page
                document.body.style.background = 'white !important';
                document.body.style.overflow = 'visible !important';
                
                return 'PDF_NOT_FOUND_CLEANED';
            }
        ";
        
        var phase2Result = jsExecutor.ExecuteScript(phase2Script);
        _logger.LogInformation("📄 Phase 2 result: {Result}", phase2Result);
        await Task.Delay(2000, cancellationToken);
        
        // PHASE 3: Final cleanup and scroll reset
        var phase3Script = @"
            console.log('🔧 PHASE 3: Final cleanup...');
            
            // Reset scroll position
            window.scrollTo(0, 0);
            
            // Ensure document is ready for capture
            document.documentElement.style.overflow = 'hidden';
            
            // Remove any remaining popups or overlays
            var overlays = document.querySelectorAll('.popup, .modal, .overlay, .dialog, [class*=""popup""], [class*=""modal""], [class*=""overlay""], [class*=""dialog""]');
            overlays.forEach(function(overlay) {
                overlay.style.display = 'none !important';
            });
            
            console.log('✅ PHASE 3 Complete: Page ready for PDF capture');
            return 'PREPARATION_COMPLETE';
        ";
        
        var phase3Result = jsExecutor.ExecuteScript(phase3Script);
        _logger.LogInformation("📄 Phase 3 result: {Result}", phase3Result);
        
        // Final stabilization delay
        await Task.Delay(1500, cancellationToken);
        
        _logger.LogInformation("✅ PDF page preparation complete - navigation panels removed, PDF content isolated");
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Page preparation failed: {Message}", ex.Message);
    }
}

private Task<byte[]?> TryFullPagePdfCapture(ChromeDriver chromeDriver, CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("🖨️ Attempting full page PDF capture with optimized settings...");
        
        // Get page dimensions
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        var pageDimensions = jsExecutor?.ExecuteScript(@"
            return {
                scrollWidth: Math.max(document.body.scrollWidth, document.documentElement.scrollWidth),
                scrollHeight: Math.max(document.body.scrollHeight, document.documentElement.scrollHeight),
                clientWidth: document.documentElement.clientWidth,
                clientHeight: document.documentElement.clientHeight
            };
        ");
        
        // Enhanced print parameters for full content capture
        var printParams = new Dictionary<string, object>
        {
            ["landscape"] = false,
            ["displayHeaderFooter"] = false,
            ["printBackground"] = true,
            ["scale"] = 1.0,
            ["paperWidth"] = 8.5, // US Letter width in inches
            ["paperHeight"] = 11.0, // US Letter height in inches
            ["marginTop"] = 0.0,
            ["marginBottom"] = 0.0,
            ["marginLeft"] = 0.0,
            ["marginRight"] = 0.0,
            ["preferCSSPageSize"] = true,
            ["generateTaggedPDF"] = false,
            ["generateDocumentOutline"] = false
        };
        
        var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printParams);
        
        if (result is Dictionary<string, object> printResult &&
            printResult.TryGetValue("data", out var base64Data) &&
            base64Data?.ToString() is string base64String &&
            !string.IsNullOrEmpty(base64String))
        {
            return Task.FromResult<byte[]?>(Convert.FromBase64String(base64String));
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Full page PDF capture failed: {Message}", ex.Message);
    }
    return Task.FromResult<byte[]?>(null);
}

private async Task<byte[]?> TryPdfContentAreaCapture(ChromeDriver chromeDriver, CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("📋 Attempting PDF content area focused capture...");
        
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // First, try to identify and focus on the actual PDF content
        var focusScript = @"
            // Find PDF content elements
            var pdfElements = [
                ...document.querySelectorAll('embed[type*=""pdf""]'),
                ...document.querySelectorAll('object[type*=""pdf""]'),
                ...document.querySelectorAll('iframe[src*="".pdf""]'),
                ...document.querySelectorAll('[class*=""pdf-content""]'),
                ...document.querySelectorAll('[id*=""pdf-content""]'),
                ...document.querySelectorAll('.pdfobject'),
                ...document.querySelectorAll('#pdfobject')
            ];
            
            if (pdfElements.length > 0) {
                var pdfElement = pdfElements[0];
                
                // Hide everything else
                document.body.style.overflow = 'hidden';
                var allElements = document.body.children;
                for (var i = 0; i < allElements.length; i++) {
                    if (!allElements[i].contains(pdfElement)) {
                        allElements[i].style.display = 'none';
                    }
                }
                
                // Make PDF element visible and full screen
                var parent = pdfElement.parentElement;
                while (parent && parent !== document.body) {
                    parent.style.display = 'block';
                    parent.style.width = '100%';
                    parent.style.height = '100%';
                    parent = parent.parentElement;
                }
                
                pdfElement.style.width = '100vw';
                pdfElement.style.height = '100vh';
                pdfElement.style.position = 'fixed';
                pdfElement.style.top = '0';
                pdfElement.style.left = '0';
                pdfElement.style.zIndex = '999999';
                
                return 'PDF_ISOLATED';
            }
            return 'PDF_NOT_FOUND';
        ";
        
        var focusResult = jsExecutor?.ExecuteScript(focusScript);
        _logger.LogInformation("📌 PDF focus result: {Result}", focusResult);
        
        await Task.Delay(3000, cancellationToken); // Wait for changes to apply
        
        // Capture with A4 size optimized for documents
        var printParams = new Dictionary<string, object>
        {
            ["landscape"] = false,
            ["displayHeaderFooter"] = false,
            ["printBackground"] = true,
            ["scale"] = 1.0,
            ["paperWidth"] = 8.27, // A4 width
            ["paperHeight"] = 11.7, // A4 height
            ["marginTop"] = 0.0,
            ["marginBottom"] = 0.0,
            ["marginLeft"] = 0.0,
            ["marginRight"] = 0.0,
            ["preferCSSPageSize"] = false
        };
        
        var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printParams);
        
        if (result is Dictionary<string, object> printResult &&
            printResult.TryGetValue("data", out var base64Data) &&
            base64Data?.ToString() is string base64String &&
            !string.IsNullOrEmpty(base64String))
        {
            return Convert.FromBase64String(base64String);
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("PDF content area capture failed: {Message}", ex.Message);
    }
    return null;
}

private async Task TryPdfSpecificDownloadTriggers(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("🎯 Trying PDF-specific download triggers...");
        
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // Trigger 1: Look for hidden PDF download functions in the page
        var downloadFunctionScript = @"
            // Look for any download functions in the global scope
            var downloadFunctions = [];
            for (var prop in window) {
                if (prop.toLowerCase().includes('download') || prop.toLowerCase().includes('pdf')) {
                    if (typeof window[prop] === 'function') {
                        downloadFunctions.push(prop);
                    }
                }
            }
            
            // Try executing found functions
            downloadFunctions.forEach(function(funcName) {
                try {
                    window[funcName]();
                } catch(e) { /* ignore */ }
            });
            
            return downloadFunctions;
        ";
        
        var downloadFunctions = jsExecutor?.ExecuteScript(downloadFunctionScript);
        _logger.LogInformation("📋 Found and executed download functions: {Functions}", downloadFunctions);
        
        await Task.Delay(2000, cancellationToken);
        
        // Trigger 2: Try common PDF viewer keyboard shortcuts
        var actions = new OpenQA.Selenium.Interactions.Actions(Driver);
        
        // Ctrl+S (Save As)
        actions.KeyDown(Keys.Control).SendKeys("s").KeyUp(Keys.Control).Perform();
        await Task.Delay(1000, cancellationToken);
        actions.SendKeys(Keys.Escape).Perform(); // Close dialog if opened
        await Task.Delay(500, cancellationToken);
        
        // Ctrl+P (Print)
        actions.KeyDown(Keys.Control).SendKeys("p").KeyUp(Keys.Control).Perform();
        await Task.Delay(1000, cancellationToken);
        actions.SendKeys(Keys.Escape).Perform(); // Close dialog if opened
        await Task.Delay(500, cancellationToken);
        
        // Trigger 3: Try right-click context menu on PDF elements
        var pdfElements = Driver?.FindElements(By.XPath("//embed | //object | //iframe"));
        if (pdfElements != null && pdfElements.Count > 0)
        {
            foreach (var element in pdfElements)
            {
                try
                {
                    actions.ContextClick(element).Perform();
                    await Task.Delay(1000, cancellationToken);
                    
                    // Try to find and click "Save as" or "Download" in context menu
                    var contextMenuItems = Driver?.FindElements(By.XPath("//*[contains(text(), 'Save') or contains(text(), 'Download')]"));
                    if (contextMenuItems != null && contextMenuItems.Count > 0)
                    {
                        contextMenuItems.First().Click();
                        await Task.Delay(2000, cancellationToken);
                        _logger.LogInformation("✅ PDF TRIGGER SUCCESS: Context menu download triggered");
                    }
                    
                    actions.SendKeys(Keys.Escape).Perform(); // Close context menu
                    await Task.Delay(500, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Context click failed: {Message}", ex.Message);
                }
            }
        }
        
        // Trigger 4: Try browser-specific PDF download methods
        var browserDownloadScript = @"
            // Chrome PDF viewer specific
            if (window.chrome && window.chrome.runtime) {
                try {
                    // Try to trigger Chrome PDF download
                    document.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 's',
                        ctrlKey: true,
                        bubbles: true
                    }));
                } catch(e) { /* ignore */ }
            }
            
            // Look for PDF.js specific controls
            if (window.PDFViewerApplication) {
                try {
                    window.PDFViewerApplication.download();
                    return 'PDFJS_DOWNLOAD_TRIGGERED';
                } catch(e) { /* ignore */ }
            }
            
            // Try to find and click any hidden download links
            var hiddenLinks = document.querySelectorAll('a[href*="".pdf""][style*=""display:none""]');
            hiddenLinks.forEach(function(link) {
                link.style.display = 'block';
                link.click();
            });
            
            return 'BROWSER_SPECIFIC_TRIGGERS_EXECUTED';
        ";
        
        var browserResult = jsExecutor?.ExecuteScript(browserDownloadScript);
        _logger.LogInformation("🌐 Browser-specific triggers result: {Result}", browserResult);
        
        await Task.Delay(3000, cancellationToken);
        
    }
    catch (Exception ex)
    {
        _logger.LogDebug("PDF-specific download triggers failed: {Message}", ex.Message);
    }
}

private async Task<byte[]?> TryExtractPdfContentFromViewerEnhanced(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("🎯 Enhanced PDF content extraction from viewer");
        
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // Enhanced PDF extraction script with multiple detection methods
        var extractScript = @"
            function extractPdfContentEnhanced() {
                var results = { sources: [], methods: [] };
                
                // Method 1: Extract from PDF.js viewer
                if (window.PDFViewerApplication) {
                    try {
                        var pdfDocument = window.PDFViewerApplication.pdfDocument;
                        if (pdfDocument && pdfDocument.getData) {
                            var data = pdfDocument.getData();
                            results.sources.push({ type: 'pdfjs', data: data });
                            results.methods.push('PDF.js viewer data extraction');
                        }
                    } catch(e) { results.methods.push('PDF.js failed: ' + e.message); }
                }
                
                // Method 2: Extract from Chrome PDF viewer
                var chromeViewer = document.querySelector('#chrome-pdf-viewer');
                if (chromeViewer) {
                    try {
                        var src = chromeViewer.src || chromeViewer.getAttribute('src');
                        if (src && src.startsWith('blob:')) {
                            results.sources.push({ type: 'chrome-blob', url: src });
                            results.methods.push('Chrome PDF viewer blob URL');
                        }
                    } catch(e) { results.methods.push('Chrome viewer failed: ' + e.message); }
                }
                
                // Method 3: Extract from embed/object elements
                var pdfElements = [
                    ...document.querySelectorAll('embed[type*=""pdf""]'),
                    ...document.querySelectorAll('object[type*=""pdf""]'),
                    ...document.querySelectorAll('embed[src*="".pdf""]'),
                    ...document.querySelectorAll('object[data*="".pdf""]')
                ];
                
                pdfElements.forEach(function(element) {
                    var src = element.src || element.data || element.getAttribute('src') || element.getAttribute('data');
                    if (src) {
                        results.sources.push({ type: 'embed-object', url: src });
                        results.methods.push('Embed/Object element: ' + src);
                    }
                });
                
                // Method 4: Extract from iframes
                var iframes = document.querySelectorAll('iframe');
                iframes.forEach(function(iframe) {
                    try {
                        var src = iframe.src || iframe.getAttribute('src');
                        if (src && (src.includes('.pdf') || src.includes('application/pdf'))) {
                            results.sources.push({ type: 'iframe', url: src });
                            results.methods.push('Iframe PDF: ' + src);
                        }
                    } catch(e) { /* ignore cross-origin iframe errors */ }
                });
                
                // Method 5: Look for base64 PDF data in page
                var pageSource = document.documentElement.innerHTML;
                var base64Match = pageSource.match(/data:application\/pdf;base64,([A-Za-z0-9+\/=]+)/);
                if (base64Match) {
                    results.sources.push({ type: 'base64', data: base64Match[1] });
                    results.methods.push('Base64 PDF data found in page');
                }
                
                return results;
            }
            
            return extractPdfContentEnhanced();
        ";
        
        var extractResult = jsExecutor?.ExecuteScript(extractScript);
        if (extractResult is Dictionary<string, object> result)
        {
            if (result.TryGetValue("sources", out var sourcesObj) && 
                sourcesObj is System.Collections.IList sources && sources.Count > 0)
            {
                _logger.LogInformation("📋 Found {Count} PDF content sources", sources.Count);
                
                foreach (var sourceObj in sources)
                {
                    if (sourceObj is Dictionary<string, object> source)
                    {
                        if (source.TryGetValue("type", out var type) && source.TryGetValue("data", out var data))
                        {
                            // Handle base64 data
                            if (type.ToString() == "base64" && data is string base64Data)
                            {
                                try
                                {
                                    var pdfBytes = Convert.FromBase64String(base64Data);
                                    if (IsValidPdf(pdfBytes))
                                    {
                                        _logger.LogInformation("✅ Enhanced extraction SUCCESS: Base64 PDF - {FileSize} bytes", pdfBytes.Length);
                                        return pdfBytes;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug("Base64 conversion failed: {Message}", ex.Message);
                                }
                            }
                        }
                        
                        if (source.TryGetValue("url", out var url) && url is string pdfUrl)
                        {
                            // Try to download from the URL
                            try
                            {
                                var pdfBytes = await DownloadPdfFromUrl(pdfUrl, cancellationToken);
                                if (pdfBytes != null && pdfBytes.Length > 0 && IsValidPdf(pdfBytes))
                                {
                                    _logger.LogInformation("✅ Enhanced extraction SUCCESS: URL download - {FileSize} bytes", pdfBytes.Length);
                                    return pdfBytes;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug("URL download failed for {Url}: {Message}", pdfUrl, ex.Message);
                            }
                        }
                    }
                }
            }
            
            // Log methods tried for debugging
            if (result.TryGetValue("methods", out var methodsObj) && methodsObj is System.Collections.IList methods)
            {
                _logger.LogInformation("📝 PDF extraction methods attempted: {Methods}", 
                    string.Join(", ", methods.Cast<object>().Select(m => m.ToString())));
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Enhanced PDF content extraction failed: {Message}", ex.Message);
    }
    
    return null;
}

private async Task<byte[]?> TryUltimateFallbackPdfDownload(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Attempting ultimate fallback PDF download");
        
        // CRITICAL: Prepare page for clean PDF capture
        await PreparePageForFullCapture(cancellationToken);
        
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // Method 1: Extract all possible PDF URLs from the page
        var script = @"
            function extractAllPdfUrls() {
                var pdfUrls = [];
                
                // Extract from openPDFNoticeWindow functions
                var openPdfLinks = document.querySelectorAll('a[onclick*=""openPDFNoticeWindow""]');
                for (var i = 0; i < openPdfLinks.length; i++) {
                    var onclick = openPdfLinks[i].getAttribute('onclick');
                    if (onclick) {
                        var match = onclick.match(/openPDFNoticeWindow\('([^']+)'\)/);
                        if (match && match[1]) {
                            var url = match[1];
                            if (url.startsWith('/')) {
                                url = window.location.origin + url;
                            }
                            pdfUrls.push({ type: 'openPDFNoticeWindow', url: url });
                        }
                    }
                }
                
                // Extract from href attributes with dynamic CP575 patterns
                var hrefLinks = document.querySelectorAll('a[href*="".pdf""], a[href*=""CP575""]');
                for (var i = 0; i < hrefLinks.length; i++) {
                    var href = hrefLinks[i].href;
                    if (href && (href.includes('.pdf') || href.includes('CP575'))) {
                        pdfUrls.push({ type: 'href', url: href });
                    }
                }
                
                // Also look for any links containing CP575 in any format
                var allLinks = document.querySelectorAll('a[href]');
                for (var i = 0; i < allLinks.length; i++) {
                    var href = allLinks[i].href;
                    if (href && href.includes('CP575') && href.includes('.pdf')) {
                        pdfUrls.push({ type: 'cp575_dynamic', url: href });
                    }
                }
                
                // Try to construct URL from page context
                var currentUrl = window.location.href;
                var baseUrl = window.location.origin;
                
                // Common PDF URL patterns with dynamic timestamps
                var timestamp = Date.now();
                var randomId = Math.random().toString(36).substring(2, 15);
                var patterns = [
                    baseUrl + '/modein/notices/CP575_' + timestamp + '.pdf',
                    baseUrl + '/modein/notices/CP575_' + timestamp + '_' + randomId + '.pdf',
                    baseUrl + '/notices/CP575_' + timestamp + '.pdf',
                    baseUrl + '/modein/CP575_' + timestamp + '.pdf',
                    baseUrl + '/CP575_' + timestamp + '.pdf',
                    baseUrl + '/modein/notices/CP575_' + randomId + '.pdf',
                    baseUrl + '/modein/notices/CP575_' + timestamp + '_' + Date.now() + '.pdf'
                ];
                
                return { pdfUrls: pdfUrls, patterns: patterns, baseUrl: baseUrl };
            }
            return extractAllPdfUrls();
        ";
        
        var result = jsExecutor?.ExecuteScript(script);
        if (result != null && result is Dictionary<string, object> jsResult)
        {
            // Try each extracted URL
            if (jsResult.TryGetValue("pdfUrls", out var pdfUrls) && pdfUrls is IEnumerable<object> urls)
            {
                foreach (var urlObj in urls)
                {
                    if (urlObj is Dictionary<string, object> urlInfo)
                    {
                        if (urlInfo.TryGetValue("url", out var url) && url is string pdfUrl)
                        {
                            _logger.LogInformation("Trying ultimate fallback URL: {Url}", pdfUrl);
                            
                            var pdfBytes = await DownloadPdfFromUrl(pdfUrl, cancellationToken);
                            if (pdfBytes != null && pdfBytes.Length > 0)
                            {
                                _logger.LogInformation("Ultimate fallback succeeded with URL: {Url}, Size: {FileSize} bytes", 
                                    pdfUrl, pdfBytes.Length);
                                return pdfBytes;
                            }
                        }
                    }
                }
            }
            
            // Method 2: Try to construct URL from current page context
            if (jsResult.TryGetValue("baseUrl", out var baseUrl) && baseUrl is string baseUrlStr)
            {
                var currentUrl = Driver?.Url ?? "";
                var timestamp = DateTime.Now.Ticks;
                
                // Try different URL patterns with dynamic timestamps
                var urlTimestamp = DateTime.Now.Ticks;
                var randomId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var currentTime = DateTime.Now.Ticks;
                
                var urlPatterns = new[]
                {
                    $"{baseUrlStr}/modein/notices/CP575_{urlTimestamp}.pdf",
                    $"{baseUrlStr}/modein/notices/CP575_{urlTimestamp}_{randomId}.pdf",
                    $"{baseUrlStr}/notices/CP575_{urlTimestamp}.pdf",
                    $"{baseUrlStr}/modein/CP575_{urlTimestamp}.pdf",
                    $"{baseUrlStr}/CP575_{urlTimestamp}.pdf",
                    $"{baseUrlStr}/modein/notices/CP575_{randomId}.pdf",
                    $"{baseUrlStr}/modein/notices/CP575_{urlTimestamp}_{currentTime}.pdf",
                    $"{baseUrlStr}/modein/notices/CP575_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.pdf"
                };
                
                foreach (var pattern in urlPatterns)
                {
                    _logger.LogInformation("Trying constructed URL pattern: {Pattern}", pattern);
                    
                    var pdfBytes = await DownloadPdfFromUrl(pattern, cancellationToken);
                    if (pdfBytes != null && pdfBytes.Length > 0)
                    {
                        _logger.LogInformation("Ultimate fallback succeeded with constructed URL: {Url}, Size: {FileSize} bytes", 
                            pattern, pdfBytes.Length);
                        return pdfBytes;
                    }
                }
            }
        }
        
        // Method 3: Try to extract PDF URL from page source with dynamic patterns
        var pageSource = Driver?.PageSource ?? "";
        
        // Multiple regex patterns to catch different PDF URL formats
        var pdfUrlPatterns = new[]
        {
            @"/modein/notices/CP575_\d+\.pdf",                    // Standard format
            @"/modein/notices/CP575_\d+_\d+\.pdf",                // With additional timestamp
            @"/modein/notices/CP575_[a-zA-Z0-9_]+\.pdf",          // Alphanumeric variations
            @"/modein/notices/CP575_[a-zA-Z0-9_]+_[a-zA-Z0-9_]+\.pdf", // Multiple underscores
            @"/notices/CP575_\d+\.pdf",                            // Without modein
            @"/CP575_\d+\.pdf",                                    // Root level
            @"/modein/CP575_\d+\.pdf",                             // Direct modein
            @"/modein/notices/CP575_[^""\s]+\.pdf",                // Any characters except quotes/whitespace
            @"/modein/notices/[^""\s]*CP575[^""\s]*\.pdf",         // CP575 anywhere in filename
            @"/modein/notices/CP575_[a-zA-Z0-9_]{8,}\.pdf",       // At least 8 alphanumeric chars
            @"/modein/notices/CP575_\d{10,}\.pdf",                 // At least 10 digits
            @"/modein/notices/CP575_\d+[a-zA-Z0-9_]*\.pdf"        // Digits followed by optional chars
        };
        
        foreach (var pattern in pdfUrlPatterns)
        {
            var pdfUrlMatches = Regex.Matches(pageSource, pattern);
            
            foreach (Match match in pdfUrlMatches)
            {
                var pdfUrl = match.Value;
                if (pdfUrl.StartsWith("/"))
                {
                    var baseUri = new Uri(Driver?.Url ?? "https://sa.www4.irs.gov");
                    pdfUrl = new Uri(baseUri, pdfUrl).ToString();
                }
                
                _logger.LogInformation("Trying PDF URL from page source with pattern '{Pattern}': {Url}", pattern, pdfUrl);
                
                var pdfBytes = await DownloadPdfFromUrl(pdfUrl, cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("Ultimate fallback succeeded with page source URL: {Url}, Size: {FileSize} bytes", 
                        pdfUrl, pdfBytes.Length);
                    return pdfBytes;
                }
            }
        }
        
        // Method 4: Try to dynamically extract the actual PDF URL from the page
        _logger.LogInformation("Attempting to dynamically extract actual PDF URL from page");
        var dynamicPdfUrl = await TryExtractActualPdfUrlFromPage(cancellationToken);
        if (!string.IsNullOrEmpty(dynamicPdfUrl))
        {
            _logger.LogInformation("Found actual PDF URL: {Url}", dynamicPdfUrl);
            
            var pdfBytes = await DownloadPdfFromUrl(dynamicPdfUrl, cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("Ultimate fallback succeeded with actual PDF URL: {Url}, Size: {FileSize} bytes", 
                    dynamicPdfUrl, pdfBytes.Length);
                return pdfBytes;
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Ultimate fallback PDF download failed: {Message}", ex.Message);
    }
    return null;
}

private Task<string?> TryExtractActualPdfUrlFromPage(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Attempting to extract actual PDF URL from page");
        
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        // JavaScript to extract the actual PDF URL from the page
        var script = @"
            function extractActualPdfUrl() {
                try {
                    // Method 1: Look for the exact PDF link with openPDFNoticeWindow
                    var pdfLink = document.querySelector('a[onclick*=""openPDFNoticeWindow""]');
                    if (pdfLink) {
                        var onclick = pdfLink.getAttribute('onclick');
                        if (onclick) {
                            var match = onclick.match(/openPDFNoticeWindow\('([^']+)'\)/);
                            if (match && match[1]) {
                                var pdfUrl = match[1];
                                if (pdfUrl.startsWith('/')) {
                                    pdfUrl = window.location.origin + pdfUrl;
                                }
                                return { success: true, url: pdfUrl, method: 'openPDFNoticeWindow' };
                            }
                        }
                    }
                    
                    // Method 2: Look for any link with CP575 in href
                    var cp575Links = document.querySelectorAll('a[href*=""CP575""]');
                    for (var i = 0; i < cp575Links.length; i++) {
                        var href = cp575Links[i].href;
                        if (href && href.includes('CP575') && href.includes('.pdf')) {
                            return { success: true, url: href, method: 'href_cp575' };
                        }
                    }
                    
                    // Method 3: Look for any link with .pdf extension
                    var pdfLinks = document.querySelectorAll('a[href*="".pdf""]');
                    for (var i = 0; i < pdfLinks.length; i++) {
                        var href = pdfLinks[i].href;
                        if (href && href.includes('.pdf')) {
                            return { success: true, url: href, method: 'href_pdf' };
                        }
                    }
                    
                    // Method 4: Search in page source for PDF URLs
                    var pageText = document.documentElement.outerHTML;
                    var pdfUrlMatches = pageText.match(/\/modein\/notices\/CP575_[^""\s]+\.pdf/g);
                    if (pdfUrlMatches && pdfUrlMatches.length > 0) {
                        var pdfUrl = pdfUrlMatches[0];
                        if (pdfUrl.startsWith('/')) {
                            pdfUrl = window.location.origin + pdfUrl;
                        }
                        return { success: true, url: pdfUrl, method: 'page_source' };
                    }
                    
                    // Method 5: Look for any CP575 pattern in the page
                    var cp575Matches = pageText.match(/CP575_[^""\s]+\.pdf/g);
                    if (cp575Matches && cp575Matches.length > 0) {
                        var pdfUrl = '/modein/notices/' + cp575Matches[0];
                        if (pdfUrl.startsWith('/')) {
                            pdfUrl = window.location.origin + pdfUrl;
                        }
                        return { success: true, url: pdfUrl, method: 'cp575_pattern' };
                    }
                    
                    return { success: false, error: 'No PDF URL found' };
                } catch (e) {
                    return { success: false, error: e.message };
                }
            }
            return extractActualPdfUrl();
        ";
        
        var result = jsExecutor?.ExecuteScript(script);
        if (result != null && result is Dictionary<string, object> jsResult)
        {
            if (jsResult.TryGetValue("success", out var success) && success is bool isSuccess && isSuccess)
            {
                if (jsResult.TryGetValue("url", out var url) && url is string pdfUrl)
                {
                    var method = jsResult.TryGetValue("method", out var methodObj) ? methodObj?.ToString() : "unknown";
                    _logger.LogInformation("Successfully extracted actual PDF URL via {Method}: {Url}", method, pdfUrl);
                    return Task.FromResult<string?>(pdfUrl);
                }
            }
            else if (jsResult.TryGetValue("error", out var error))
            {
                _logger.LogDebug("Failed to extract actual PDF URL: {Error}", error);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Actual PDF URL extraction failed: {Message}", ex.Message);
    }
    return Task.FromResult<string?>(null);
}

private async Task<byte[]?> TryCapturePdfFromBrowserViewer(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Attempting comprehensive PDF capture from browser viewer");
        
        // CRITICAL: Prepare page for clean PDF capture
        await PreparePageForFullCapture(cancellationToken);
        
        // Method 1: Check if we're already on a PDF page
        var currentUrl = Driver?.Url?.ToLower() ?? "";
        if (currentUrl.Contains(".pdf") || currentUrl.Contains("cp575"))
        {
            _logger.LogInformation("Current page appears to be a PDF: {Url}", currentUrl);
            
            // Try all capture methods
            var pdfBytes = await TryExtractPdfContentFromViewer(cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("Successfully extracted PDF from current page: {FileSize} bytes", pdfBytes.Length);
                return pdfBytes;
            }
            
            pdfBytes = await TryPrintToPdf(cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("Successfully printed current page to PDF: {FileSize} bytes", pdfBytes.Length);
                return pdfBytes;
            }
            
            pdfBytes = await TryCapturePdfFromPrintDialog(cancellationToken);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("Successfully captured PDF from print dialog: {FileSize} bytes", pdfBytes.Length);
                return pdfBytes;
            }
        }
        
        // Method 2: Search for PDF elements on the page
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        var script = @"
            function findPdfElements() {
                var pdfElements = [];
                
                // Look for PDF links
                var links = document.querySelectorAll('a[href*="".pdf""], a[href*=""cp575""]');
                for (var i = 0; i < links.length; i++) {
                    pdfElements.push({ type: 'link', href: links[i].href, text: links[i].textContent });
                }
                
                // Look for PDF embeds
                var embeds = document.querySelectorAll('embed[type=""application/pdf""]');
                for (var i = 0; i < embeds.length; i++) {
                    pdfElements.push({ type: 'embed', src: embeds[i].src });
                }
                
                // Look for PDF objects
                var objects = document.querySelectorAll('object[type=""application/pdf""]');
                for (var i = 0; i < objects.length; i++) {
                    pdfElements.push({ type: 'object', data: objects[i].data });
                }
                
                // Look for iframes with PDFs
                var iframes = document.querySelectorAll('iframe[src*="".pdf""]');
                for (var i = 0; i < iframes.length; i++) {
                    pdfElements.push({ type: 'iframe', src: iframes[i].src });
                }
                
                return pdfElements;
            }
            return findPdfElements();
        ";
        
        var pdfElements = jsExecutor?.ExecuteScript(script);
        if (pdfElements != null && pdfElements is IEnumerable<object> elements)
        {
            foreach (var element in elements)
            {
                if (element is Dictionary<string, object> pdfElement)
                {
                    var href = pdfElement.GetValueOrDefault("href")?.ToString();
                    var src = pdfElement.GetValueOrDefault("src")?.ToString();
                    var data = pdfElement.GetValueOrDefault("data")?.ToString();
                    
                    var url = href ?? src ?? data;
                    if (!string.IsNullOrEmpty(url))
                    {
                        _logger.LogInformation("Found PDF element: {Type}, URL: {Url}", 
                            pdfElement.GetValueOrDefault("type"), url);
                        
                        var pdfBytes = await DownloadPdfFromUrl(url, cancellationToken);
                        if (pdfBytes != null && pdfBytes.Length > 0)
                        {
                            _logger.LogInformation("Successfully downloaded PDF from element: {FileSize} bytes", pdfBytes.Length);
                            return pdfBytes;
                        }
                    }
                }
            }
        }
        
        // Method 3: Try to trigger PDF download from any found elements
        var downloadSelectors = new[]
        {
            "//a[contains(@href, '.pdf')]",
            "//a[contains(@href, 'cp575')]",
            "//button[contains(text(), 'Download')]",
            "//button[contains(text(), 'View')]",
            "//*[contains(text(), 'EIN Confirmation Letter')]",
            "//*[contains(text(), 'confirmation letter')]"
        };
        
        foreach (var selector in downloadSelectors)
        {
            try
            {
                var element = Driver?.FindElement(By.XPath(selector));
                if (element != null && element.Displayed && element.Enabled)
                {
                    _logger.LogInformation("Found PDF element with selector: {Selector}", selector);
                    
                    // Try clicking the element
                    element.Click();
                    await Task.Delay(3000, cancellationToken);
                    
                    // Check if a new window/tab opened
                    var newWindows = Driver?.WindowHandles.ToList();
                    var originalWindows = Driver?.WindowHandles.ToList();
                    
                    if (newWindows != null && newWindows.Count > originalWindows?.Count)
                    {
                        var newWindow = newWindows.FirstOrDefault(w => !originalWindows.Contains(w));
                        if (!string.IsNullOrEmpty(newWindow))
                        {
                            Driver?.SwitchTo().Window(newWindow);
                            await Task.Delay(2000, cancellationToken);
                            
                            var pdfBytes = await TryExtractPdfContentFromViewer(cancellationToken);
                            if (pdfBytes != null && pdfBytes.Length > 0)
                            {
                                _logger.LogInformation("Successfully captured PDF from new window: {FileSize} bytes", pdfBytes.Length);
                                return pdfBytes;
                            }
                            
                            // Close the new window and switch back
                            Driver?.Close();
                            Driver?.SwitchTo().Window(originalWindows.First());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("PDF element selector {Selector} failed: {Message}", selector, ex.Message);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Comprehensive PDF capture from browser viewer failed: {Message}", ex.Message);
    }
    return null;
}

private async Task<byte[]?> DownloadPdfFromUrl(string? url, CancellationToken cancellationToken)
{
    try
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        _logger.LogInformation("Downloading PDF from URL: {Url}", url);

        // Get cookies from the current session
        var cookies = Driver?.Manage().Cookies.AllCookies;
        
        HttpClient httpClient;
        
        // Add cookies to HttpClient if available
        if (cookies != null && cookies.Count > 0)
        {
            var cookieContainer = new CookieContainer();
            foreach (var cookie in cookies)
            {
                try
                {
                    cookieContainer.Add(new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
                }
                catch (Exception cookieEx)
                {
                    _logger.LogDebug("Failed to add cookie {CookieName}: {Message}", cookie.Name, cookieEx.Message);
                }
            }
            
            var handler = new HttpClientHandler() { CookieContainer = cookieContainer };
            httpClient = new HttpClient(handler);
        }
        else
        {
            httpClient = new HttpClient();
        }
        
        using (httpClient)
        {
            // Set appropriate headers
            httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/pdf,*/*");
            httpClient.DefaultRequestHeaders.Add("Referer", Driver?.Url);

            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var response = await httpClient.GetAsync(url, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (IsValidPdf(content))
                {
                    _logger.LogInformation("PDF download successful: {FileSize} bytes", content.Length);
                    return content;
                }
                else
                {
                    _logger.LogWarning("Download returned non-PDF content");
                }
            }
            else
            {
                _logger.LogWarning("PDF download failed with status: {StatusCode}", response.StatusCode);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("PDF download from URL failed: {Message}", ex.Message);
    }

    return null;
}

private async Task<byte[]?> TryDirectDownload(IWebElement pdfLinkElement, CancellationToken cancellationToken)
{
    try
    {
        var href = pdfLinkElement.GetAttribute("href");
        if (string.IsNullOrEmpty(href))
        {
            _logger.LogDebug("No href attribute found on PDF link");
            return null;
        }

        _logger.LogInformation("Attempting direct download from URL: {Url}", href);

        // Get cookies from the current session
        var cookies = Driver?.Manage().Cookies.AllCookies;
        
        HttpClient httpClient;
        
        // Add cookies to HttpClient if available
        if (cookies != null && cookies.Count > 0)
        {
            var cookieContainer = new CookieContainer();
            foreach (var cookie in cookies)
            {
                try
                {
                    cookieContainer.Add(new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
                }
                catch (Exception cookieEx)
                {
                    _logger.LogDebug("Failed to add cookie {CookieName}: {Message}", cookie.Name, cookieEx.Message);
                }
            }
            
            var handler = new HttpClientHandler() { CookieContainer = cookieContainer };
            httpClient = new HttpClient(handler);
        }
        else
        {
            httpClient = new HttpClient();
        }
        
        using (httpClient)

        // Set appropriate headers
        httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/pdf,*/*");
        httpClient.DefaultRequestHeaders.Add("Referer", Driver?.Url);

        // Handle relative URLs
        if (href.StartsWith("/"))
        {
            var baseUri = new Uri(Driver?.Url ?? "https://sa.www4.irs.gov");
            href = new Uri(baseUri, href).ToString();
        }

        httpClient.Timeout = TimeSpan.FromSeconds(30);
        var response = await httpClient.GetAsync(href, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (IsValidPdf(content))
            {
                _logger.LogInformation("Direct download successful: {FileSize} bytes", content.Length);
                return content;
            }
            else
            {
                _logger.LogWarning("Direct download returned non-PDF content");
            }
        }
        else
        {
            _logger.LogWarning("Direct download failed with status: {StatusCode}", response.StatusCode);
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Direct download failed: {Message}", ex.Message);
    }

    return null;
}

private static bool IsValidPdf(byte[] content)
{
    try
    {
        // Basic size check
        if (content == null || content.Length < 100)
        {
            return false;
        }
        
        // Check PDF signature (%PDF)
        if (content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46)
        {
            return true;
        }
        
        // Check for PDF version header
        var contentStr = System.Text.Encoding.ASCII.GetString(content, 0, Math.Min(100, content.Length));
        if (contentStr.Contains("%PDF-"))
        {
            return true;
        }
        
        // Check for PDF keywords in content
        if (contentStr.Contains("PDF") && contentStr.Contains("obj") && contentStr.Contains("endobj"))
        {
            return true;
        }
        
        return false;
    }
    catch (Exception)
    {
        return false;
    }
}

        /// <summary>
        /// Check if filename matches the exact EIN Letter PDF pattern: CP575Notice_[numbers]
        /// </summary>
        private static bool IsLikelyEinLetterFilename(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return false;
                }

                var upperFileName = fileName.ToUpper();

                // PRIMARY RULE: Must start with "CP575NOTICE" followed by underscore and numbers
                // Pattern: CP575Notice_1754929009892 (exact match for the known EIN Letter format)
                if (upperFileName.StartsWith("CP575NOTICE_"))
                {
                    // Verify it follows the expected pattern: CP575Notice_[numbers]
                    var afterUnderscore = upperFileName.Substring("CP575NOTICE_".Length);
                    
                    // Check if what follows the underscore is numbers (the ID part)
                    if (!string.IsNullOrEmpty(afterUnderscore) && afterUnderscore.All(char.IsDigit))
                    {
                        return true; // Perfect match: CP575Notice_[numbers]
                    }
                    
                    // Allow some flexibility for variations like CP575Notice_123.pdf
                    if (afterUnderscore.Contains('.') && afterUnderscore.Split('.')[0].All(char.IsDigit))
                    {
                        return true; // Still valid: CP575Notice_[numbers].pdf
                    }
                }

                // FALLBACK RULES: Keep some of the original logic for edge cases
                // But these are now secondary - only if the primary rule doesn't match
                
                // High-confidence secondary patterns
                var highConfidencePatterns = new[]
                {
                    "CP575", // General CP575 documents
                    "EIN"    // Direct EIN reference
                };
                
                var highConfidenceMatches = highConfidencePatterns.Count(pattern => upperFileName.Contains(pattern));
                
                // Only return true for secondary patterns if they appear with additional indicators
                if (highConfidenceMatches > 0)
                {
                    var additionalIndicators = new[] { "NOTICE", "LETTER", "CONFIRMATION" };
                    var additionalMatches = additionalIndicators.Count(indicator => upperFileName.Contains(indicator));
                    
                    // Need both a high-confidence pattern AND additional indicators
                    return additionalMatches > 0;
                }

                return false; // Reject everything else
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Enhanced validation to check if PDF is actually an EIN Letter (CP575)
        /// </summary>
        private static bool IsValidEinLetterPdf(byte[] pdfBytes)
        {
            try
            {
                // First check if it's a valid PDF
                if (!IsValidPdf(pdfBytes))
                {
                    return false;
                }

                // Simple static text extraction for validation purposes
                var pdfText = ExtractSimpleTextFromPdf(pdfBytes);
                
                if (string.IsNullOrEmpty(pdfText))
                {
                    return false;
                }

                // Convert to uppercase for case-insensitive matching
                var upperText = pdfText.ToUpper();

                // Check for key EIN Letter identifiers
                var einLetterIndicators = new[]
                {
                    "DEPARTMENT OF THE TREASURY",
                    "INTERNAL REVENUE SERVICE", 
                    "EMPLOYER IDENTIFICATION NUMBER",
                    "CP 575", // EIN Letter notice number
                    "CP575",  // Alternative format
                    "WE ASSIGNED YOU AN EMPLOYER IDENTIFICATION NUMBER",
                    "FORM: SS-4", // EIN application form
                    "EIN WILL IDENTIFY YOU"
                };

                // Must have at least 3 key indicators to be considered a valid EIN Letter
                var matchedIndicators = einLetterIndicators.Count(indicator => upperText.Contains(indicator));
                
                return matchedIndicators >= 3;
            }
            catch (Exception)
            {
                // If content extraction fails, fall back to basic PDF validation
                return IsValidPdf(pdfBytes);
            }
        }

        /// <summary>
        /// Simple static text extraction for PDF validation (simplified version)
        /// </summary>
        private static string ExtractSimpleTextFromPdf(byte[] pdfBytes)
        {
            try
            {
                // Simple text extraction - look for readable text in PDF
                var pdfText = System.Text.Encoding.UTF8.GetString(pdfBytes);
                
                // Look for text in common PDF formats
                var extractedText = new StringBuilder();
                
                // Extract text in parentheses (common PDF text format)
                var textInParens = System.Text.RegularExpressions.Regex.Matches(pdfText, @"\(([^)]+)\)");
                foreach (System.Text.RegularExpressions.Match match in textInParens)
                {
                    extractedText.AppendLine(match.Groups[1].Value);
                }
                
                // Extract readable text patterns
                var readableTextMatches = System.Text.RegularExpressions.Regex.Matches(
                    pdfText,
                    @"[A-Z][A-Z\s]{10,}",
                    System.Text.RegularExpressions.RegexOptions.Multiline
                );

                foreach (System.Text.RegularExpressions.Match match in readableTextMatches)
                {
                    if (match.Value.Contains("TREASURY") || 
                        match.Value.Contains("INTERNAL REVENUE") ||
                        match.Value.Contains("EMPLOYER IDENTIFICATION") ||
                        match.Value.Contains("CP 575"))
                    {
                        extractedText.AppendLine(match.Value);
                    }
                }

                return extractedText.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Comprehensive EIN Letter validation combining filename and content checks
        /// </summary>
        private static bool IsValidEinLetterPdfWithFilename(byte[] pdfBytes, string fileName)
        {
            try
            {
                // Check filename first (quick check)
                var hasEinFilename = IsLikelyEinLetterFilename(fileName);
                
                // Check content validation
                var hasEinContent = IsValidEinLetterPdf(pdfBytes);
                
                // Return true if EITHER filename OR content suggests it's an EIN Letter
                // This allows for flexibility in case filename is generic but content is correct
                return hasEinFilename || hasEinContent;
            }
            catch (Exception)
            {
                // Fall back to content validation only
                return IsValidEinLetterPdf(pdfBytes);
            }
        }



private async Task ConfigureDownloadDirectory(string downloadDir)
{
    try
    {
        // Use JavaScript to disable PDF viewer and force download
        var jsExecutor = (IJavaScriptExecutor?)Driver;
        
        var script = @"
            // Disable Chrome's PDF viewer
            if (window.chrome && window.chrome.downloads) {
                window.chrome.downloads.setShelfEnabled(false);
            }
            
            // Override window.open for PDF links to force download
            window.originalOpen = window.open;
            window.open = function(url, target, features) {
                if (url && url.includes('.pdf')) {
                    var link = document.createElement('a');
                    link.href = url;
                    link.download = '';
                    link.target = '_blank';
                    document.body.appendChild(link);
                    link.click();
                    document.body.removeChild(link);
                    return null;
                } else {
                    return window.originalOpen(url, target, features);
                }
            };
        ";
        
        jsExecutor?.ExecuteScript(script);
        
        // Configure Chrome DevTools Protocol if available
        if (Driver is ChromeDriver chromeDriver)
        {
            await ConfigureChromeDownloadBehavior(chromeDriver, downloadDir);
        }

        await Task.Delay(1000);
        _logger.LogInformation("Download directory configured: {DownloadDir}", downloadDir);
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Download directory configuration failed: {Message}", ex.Message);
    }
}

private Task ConfigureChromeDownloadBehavior(ChromeDriver chromeDriver, string downloadDir)
{
    try
    {
        // Use reflection to access Chrome DevTools Protocol
        var chromeDriverType = chromeDriver.GetType();
        var executeChromeCommandMethod = chromeDriverType.GetMethod("ExecuteChromeCommand", 
            new Type[] { typeof(string), typeof(Dictionary<string, object>) });
        
        if (executeChromeCommandMethod != null)
        {
            // Set download behavior
            var downloadParams = new Dictionary<string, object>
            {
                ["behavior"] = "allow",
                ["downloadPath"] = downloadDir
            };
            executeChromeCommandMethod.Invoke(chromeDriver, new object[] { "Page.setDownloadBehavior", downloadParams });
            
            // Disable PDF viewer and configure download preferences
            var prefsParams = new Dictionary<string, object>
            {
                ["prefs"] = new Dictionary<string, object>
                {
                    ["plugins.always_open_pdf_externally"] = true,
                    ["profile.default_content_settings.popups"] = 0,
                    ["profile.default_content_setting_values.automatic_downloads"] = 1,
                    ["download.default_directory"] = downloadDir,
                    ["download.prompt_for_download"] = false,
                    ["download.directory_upgrade"] = true,
                    ["safebrowsing.enabled"] = false
                }
            };
            
            _logger.LogInformation("Successfully configured Chrome download behavior via CDP for directory: {DownloadDir}", downloadDir);
        }
        else
        {
            _logger.LogWarning("ExecuteChromeCommand method not available in this Selenium version");
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Chrome DevTools Protocol configuration failed: {Message}", ex.Message);
    }
    
    return Task.CompletedTask;
}

private async Task<IWebElement?> FindPdfLinkElement()
{
    var pdfLinkSelectors = new[]
    {
        // EXACT MATCHES for your HTML structure (highest priority)
        "//div[@id='confirmation-leftcontent']//a[contains(@href, 'CP575') and contains(@onclick, 'openPDFNoticeWindow')]",
        "//div[@id='confirmation-leftcontent']//a[contains(@href, 'CP575')]",
        "//div[@id='confirmation-leftcontent']//a[contains(@href, '.pdf')]",
        "//a[@onclick and contains(@onclick, 'openPDFNoticeWindow')]",
        "//a[@target='pdf_popup']",
        "//a[contains(@href, 'CP575') and contains(@onclick, 'openPDFNoticeWindow')]",
        
        // TEXT-BASED SELECTORS (medium priority)
        "//a[contains(text(), 'CLICK HERE for Your EIN Confirmation Letter')]",
        "//a[contains(text(), 'Click here for your EIN confirmation letter')]",
        "//a[contains(text(), 'EIN Confirmation Letter')]",
        "//a[contains(text(), 'confirmation letter')]",
        "//a[contains(text(), 'CP575')]",
        
        // BROAD SELECTORS (lowest priority)
        "//a[contains(@href, '.pdf')]",
        "//a[contains(@href, 'CP575') or contains(@href, 'cp575')]",
        "//a[contains(@onclick, 'openPDFNoticeWindow')]",
        "//a[contains(@target, 'pdf_popup')]",
        
        // FALLBACK SELECTORS (last resort)
        "//a[contains(@href, 'notices')]",
        "//a[contains(@href, 'pdf')]",
        "//a[contains(@href, 'CP575')]"
    };

    int maxRetries = 3;
    for (int retry = 0; retry < maxRetries; retry++)
    {
        _logger.LogInformation("PDF link search attempt {Attempt} of {MaxRetries}", retry + 1, maxRetries);
        
        foreach (var selector in pdfLinkSelectors)
        {
            try
            {
                if (Driver != null)
                {
                    var element = WaitHelper.WaitUntilVisible(Driver, By.XPath(selector), 5);
                    if (element != null && element.Displayed && element.Enabled)
                    {
                        _logger.LogInformation("Found PDF link using selector: {Selector}", selector);
                        _logger.LogDebug("PDF link href: {Href}", element.GetAttribute("href"));
                        _logger.LogDebug("PDF link text: {Text}", element.Text);
                        return element;
                    }
                }
            }
            catch (Exception selectorEx)
            {
                _logger.LogDebug("Selector {Selector} failed: {Message}", selector, selectorEx.Message);
            }
        }

        if (retry < maxRetries - 1)
        {
            _logger.LogWarning("PDF link not found, waiting and retrying...");
            await Task.Delay(3000);
            
            // Try to scroll to ensure the element is in view
            try
            {
                var jsExecutor = (IJavaScriptExecutor?)Driver;
                jsExecutor?.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                await Task.Delay(1000);
                jsExecutor?.ExecuteScript("window.scrollTo(0, 0);");
                await Task.Delay(1000);
            }
            catch (Exception scrollEx)
            {
                _logger.LogDebug("Scroll attempt failed: {Message}", scrollEx.Message);
            }
        }
    }

    return null;
}

private async Task<bool> TryClickPdfLink(IWebElement pdfLinkElement)
{
    var clickMethods = new Func<Task<bool>>[]
    {
        // Method 1: Handle the onclick JavaScript function directly
        async () => {
            try
            {
                var onclick = pdfLinkElement.GetAttribute("onclick");
                var href = pdfLinkElement.GetAttribute("href");
                
                if (!string.IsNullOrEmpty(onclick) && onclick.Contains("openPDFNoticeWindow"))
                {
                    // Extract the PDF URL from onclick
                    var match = Regex.Match(onclick, @"openPDFNoticeWindow\('([^']+)'\)");
                    if (match.Success)
                    {
                        var pdfUrl = match.Groups[1].Value;
                        var jsExecutor = (IJavaScriptExecutor?)Driver;
                        
                        // Navigate directly to the PDF URL
                        if (pdfUrl.StartsWith("/") && Driver != null)
                        {
                            var baseUri = new Uri(Driver.Url);
                            pdfUrl = new Uri(baseUri, pdfUrl).ToString();
                        }
                        
                        _logger.LogInformation("Extracted PDF URL from onclick: {PdfUrl}", pdfUrl);
                        Driver?.Navigate().GoToUrl(pdfUrl);
                        await Task.Delay(10000); // Increased delay to 10 seconds for PDF loading
                        return true;
                    }
                }
                
                // If no onclick, try direct href navigation
                if (!string.IsNullOrEmpty(href))
                {
                    var pdfUrl = href;
                    if (pdfUrl.StartsWith("/") && Driver != null)
                    {
                        var baseUri = new Uri(Driver.Url);
                        pdfUrl = new Uri(baseUri, pdfUrl).ToString();
                    }
                    
                    _logger.LogInformation("Navigating directly to PDF URL: {PdfUrl}", pdfUrl);
                    Driver?.Navigate().GoToUrl(pdfUrl);
                    await Task.Delay(10000); // Increased delay to 10 seconds for PDF loading
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Onclick handler method failed: {Message}", ex.Message);
                return false;
            }
        },
        
        // Method 2: JavaScript click with popup handling
        async () => {
            try
            {
                var jsExecutor = (IJavaScriptExecutor?)Driver;
                jsExecutor.ExecuteScript(@"
                    arguments[0].target = '_self';
                    arguments[0].click();
                ", pdfLinkElement);
                await Task.Delay(10000); // Increased delay to 10 seconds for PDF loading
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("JavaScript click with target modification failed: {Message}", ex.Message);
                return false;
            }
        },
        
        // Method 3: Standard click
        async () => {
            try
            {
                pdfLinkElement.Click();
                await Task.Delay(10000); // Increased delay to 10 seconds for PDF loading
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Standard click failed: {Message}", ex.Message);
                return false;
            }
        },
        
        // Method 4: Navigate directly to href
        async () => {
            try
            {
                var href = pdfLinkElement.GetAttribute("href");
                if (!string.IsNullOrEmpty(href) && Driver != null)
                {
                    if (href.StartsWith("/"))
                    {
                        var baseUri = new Uri(Driver.Url);
                        href = new Uri(baseUri, href).ToString();
                    }
                    Driver?.Navigate().GoToUrl(href);
                    await Task.Delay(10000); // Increased delay to 10 seconds for PDF loading
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Direct navigation failed: {Message}", ex.Message);
                return false;
            }
        }
    };

    foreach (var method in clickMethods)
    {
        try
        {
            if (await method())
            {
                _logger.LogInformation("Successfully triggered PDF download");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Click method failed: {Message}", ex.Message);
        }
    }

    return false;
}

private async Task<string?> WaitForDownloadCompleteMultiLocation(string downloadDir, string downloadsFolder, int timeoutMs, CancellationToken cancellationToken)
{
    var timeout = DateTime.Now.AddMilliseconds(timeoutMs);
    _logger.LogInformation("Waiting for PDF download to complete in directories: {DownloadDir}, {DownloadsFolder}", downloadDir, downloadsFolder);
    
    var lastFileCount = 0;
    var stableCount = 0;
    var startTime = DateTime.Now;
    
    while (DateTime.Now < timeout && !cancellationToken.IsCancellationRequested)
    {
        try
        {
            // Check both directories
            var directories = new List<string>();
            if (Directory.Exists(downloadDir))
                directories.Add(downloadDir);
            if (Directory.Exists(downloadsFolder))
                directories.Add(downloadsFolder);

            var allPdfFiles = new List<string>();
            var allPartFiles = new List<string>();
            var allTmpFiles = new List<string>();
            var allFiles = new List<string>();

            foreach (var dir in directories)
            {
                try
                {
                    var pdfFiles = Directory.GetFiles(dir, "*.pdf");
                    var partFiles = Directory.GetFiles(dir, "*.crdownload");
                    var tmpFiles = Directory.GetFiles(dir, "*.tmp");
                    var files = Directory.GetFiles(dir, "*.*");

                    allPdfFiles.AddRange(pdfFiles);
                    allPartFiles.AddRange(partFiles);
                    allTmpFiles.AddRange(tmpFiles);
                    allFiles.AddRange(files);
                    
                    // Log specific files found in each directory
                    if (pdfFiles.Length > 0)
                    {
                        _logger.LogInformation("Found PDF files in {Dir}: {Files}", dir, string.Join(", ", pdfFiles.Select(Path.GetFileName)));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error accessing directory {Dir}: {Message}", dir, ex.Message);
                }
            }
            
            _logger.LogDebug("Found files - PDF: {PdfCount}, Partial: {PartialCount}, Temp: {TempCount}, Total: {TotalCount}", 
                allPdfFiles.Count, allPartFiles.Count, allTmpFiles.Count, allFiles.Count);

            // Check for stable file count (no new files appearing)
            if (allFiles.Count == lastFileCount)
            {
                stableCount++;
            }
            else
            {
                stableCount = 0;
                lastFileCount = allFiles.Count;
                _logger.LogDebug("File count changed: {NewCount} (was {OldCount})", allFiles.Count, lastFileCount);
            }

            // If we have PDF files and no partial downloads, and file count is stable
            if (allPdfFiles.Count > 0 && allPartFiles.Count == 0 && allTmpFiles.Count == 0 && stableCount >= 2)
            {
                _logger.LogInformation("Found {PdfCount} PDF files, no partial downloads, stable count: {StableCount}", 
                    allPdfFiles.Count, stableCount);
                
                // Additional wait to ensure file is completely written
                await Task.Delay(2000, cancellationToken);
                
                // Verify the file is still there and has content
                var finalPdfFiles = new List<string>();
                foreach (var dir in directories)
                {
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            finalPdfFiles.AddRange(Directory.GetFiles(dir, "*.pdf"));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Error accessing directory {Dir} for final check: {Message}", dir, ex.Message);
                        }
                    }
                }

                if (finalPdfFiles.Count > 0)
                {
                    var filePath = finalPdfFiles[0];
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > 0)
                    {
                        _logger.LogInformation("Found PDF file: {FilePath}, Size: {FileSize} bytes", filePath, fileInfo.Length);
                        
                        // Verify it's a valid PDF
                        var testBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        if (IsValidPdf(testBytes))
                        {
                            _logger.LogInformation("PDF download completed: {FilePath}, Size: {FileSize} bytes, Directory: {Directory}", 
                                filePath, fileInfo.Length, Path.GetDirectoryName(filePath));
                            return filePath;
                        }
                        else
                        {
                            _logger.LogWarning("Downloaded file is not a valid PDF, continuing to wait...");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Downloaded file has zero size: {FilePath}", filePath);
                    }
                }
            }
            
            await Task.Delay(1000, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error while waiting for download: {Message}", ex.Message);
            await Task.Delay(1000, cancellationToken);
        }
    }

    if (cancellationToken.IsCancellationRequested)
    {
        _logger.LogWarning("PDF download wait cancelled");
    }
    else
    {
        _logger.LogError("PDF download timeout after {TimeoutMs}ms", timeoutMs);
        
        // Log current directory contents for debugging
        try
        {
            var directories = new List<string>();
            if (Directory.Exists(downloadDir))
                directories.Add(downloadDir);
            if (Directory.Exists(downloadsFolder))
                directories.Add(downloadsFolder);

            foreach (var dir in directories)
            {
                try
                {
                    var allFiles = Directory.GetFiles(dir, "*.*");
                    _logger.LogDebug("Files in directory {Dir} at timeout: {Files}", 
                        dir, string.Join(", ", allFiles.Select(f => $"{Path.GetFileName(f)} ({new FileInfo(f).Length} bytes)")));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error logging directory contents for {Dir}: {Message}", dir, ex.Message);
                }
            }
        }
        catch (Exception debugEx)
        {
            _logger.LogDebug("Error logging directory contents: {Message}", debugEx.Message);
        }
    }

    return null;
}

        // Helper to robustly parse date from string or DateTime
        private static (int? month, int? year) ParseFlexibleDate(object? dateObj)
        {
            if (dateObj == null) return (null, null);
            DateTime dt;
            if (dateObj is DateTime d)
                dt = d;
            else if (dateObj is string s && DateTime.TryParse(s, out var parsed))
                dt = parsed;
            else
                return (null, null);
            return (dt.Month, dt.Year);
        }

        // Helper to robustly parse int from string or number
        private static int ParseFlexibleInt(object? value, int defaultValue = 0)
        {
            if (value == null) return defaultValue;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (value is string s && int.TryParse(s, out var result)) return result;
            return defaultValue;
        }

        // Helper to robustly parse double from string or number
        private static double? ParseFlexibleDouble(object? value)
        {
            if (value == null) return null;
            if (value is double d) return d;
            if (value is float f) return (double)f;
            if (value is int i) return (double)i;
            if (value is long l) return (double)l;
            if (value is string s && double.TryParse(s, out var result)) return result;
            return null;
        }

        // Helper to robustly parse bool from string or bool
        private static bool ParseFlexibleBool(object? value, bool defaultValue = false)
        {
            if (value == null) return defaultValue;
            if (value is bool b) return b;
            if (value is string s)
            {
                if (bool.TryParse(s, out var result)) return result;
                if (s == "1" || s.ToLower() == "yes" || s.ToLower() == "y" || s.ToLower() == "true") return true;
                if (s == "0" || s.ToLower() == "no" || s.ToLower() == "n" || s.ToLower() == "false") return false;
            }
            if (value is int i) return i != 0;
            return defaultValue;
        }

        private async Task<string?> CheckDownloadsFolderForRecentPdf(string downloadsFolder, CancellationToken cancellationToken)
        {
            try
            {
                // Check if we're running in a container
                var isContainer = Environment.GetEnvironmentVariable("CONTAINER_ENV") == "true" || 
                                 Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
                                 File.Exists("/.dockerenv");
                
                if (isContainer)
                {
                    _logger.LogInformation("Running in container - checking container-specific locations for recent PDFs");
                    
                    // In containers, check temp directories instead of Downloads folder
                    var containerLocations = new[]
                    {
                        "/tmp",
                        "/app/temp", 
                        "/var/tmp",
                Path.GetTempPath(),
                "/home/Downloads",
                "/root/Downloads",
                "/home/app/Downloads", // common non-root user in containers
                "/app/Downloads",
                "/app", // current working directory of your app
                "/downloads", // common mounted volume location
                "/mnt/downloads",
                "/data/downloads",
                "/usr/tmp",
                "/var/tmp/downloads",
                "/tmp/downloads",
                "/tmp/playwright-downloads",
                "/home/.config/google-chrome/Default/Downloads",
                "/root/.config/google-chrome/Default/Downloads",
                "/home/.mozilla/firefox",
                "/root/.mozilla/firefox",
                ".", // current directory (relative path)
                "./downloads",
                // Additional AKS-specific locations
                "/tmp/chrome-home",
                "/tmp/chrome-home/Downloads",
                "/tmp/ein_pdfs",
                "/app/temp",
                "/app/downloads",
                "/home",
                "/home/chrome",
                "/home/chrome/Downloads",
                "/root",
                "/root/Downloads",
                "/usr/local",
                "/usr/local/tmp",
                "/opt",
                "/opt/tmp",
                "/var/cache",
                "/var/cache/chrome",
                "/var/cache/downloads",
                "/tmp/cache",
                "/tmp/cache/chrome",
                "/tmp/cache/downloads",
                Environment.GetEnvironmentVariable("TEMP") ?? "",
                Environment.GetEnvironmentVariable("TMP") ?? "",
                Environment.GetEnvironmentVariable("TMPDIR") ?? "",
                Environment.GetEnvironmentVariable("HOME") ?? "",
                Environment.GetEnvironmentVariable("USERPROFILE") ?? "",
                // Kubernetes-specific paths
                "/var/run/secrets/kubernetes.io",
                "/var/lib/kubelet/pods",
                "/var/log/pods",
                "/var/lib/docker",
                "/var/lib/containerd"
                    };
                    
                    foreach (var location in containerLocations)
                    {
                        if (Directory.Exists(location))
                        {
                            _logger.LogInformation("🔍 Searching for PDFs in: {Location}", location);
                            var result = await CheckLocationForRecentPdfs(location, cancellationToken);
                            if (!string.IsNullOrEmpty(result))
                            {
                                return result;
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Directory does not exist: {Location}", location);
                        }
                    }
                    
                    _logger.LogWarning("No recently created valid PDF files found in container locations");
                    return null;
                }
                else
                {
                    // Local environment - use the provided Downloads folder
                    if (!Directory.Exists(downloadsFolder))
                    {
                        _logger.LogWarning("Downloads folder does not exist: {DownloadsFolder}", downloadsFolder);
                        return null;
                    }
                    
                    return await CheckLocationForRecentPdfs(downloadsFolder, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking for recent PDFs: {Message}", ex.Message);
                return null;
            }
        }
        
        private async Task<string?> CheckLocationForRecentPdfs(string location, CancellationToken cancellationToken)
        {
            try
            {
                // Get all PDF files in the location (including subdirectories)
                var pdfFiles = Directory.GetFiles(location, "*.pdf", SearchOption.AllDirectories);
                _logger.LogInformation("Found {Count} PDF files in {Location} (including subdirectories)", pdfFiles.Length, location);

                if (pdfFiles.Length == 0)
                {
                    _logger.LogDebug("No PDF files found in {Location}", location);
                    return null;
                }

                // Sort by creation time (most recent first)
                var recentPdfFiles = pdfFiles
                    .Select(f => new FileInfo(f))
                    .Where(f => f.Length > 0) // Only files with content
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                _logger.LogInformation("Found {Count} non-empty PDF files in {Location}", recentPdfFiles.Count, location);

                foreach (var fileInfo in recentPdfFiles)
                {
                    try
                    {
                        // Check if file was created in the last 5 minutes (likely our download)
                        var timeSinceCreation = DateTime.Now - fileInfo.CreationTime;
                        if (timeSinceCreation.TotalMinutes <= 5)
                        {
                            _logger.LogInformation("Found recently created PDF file: {FileName}, Created: {CreationTime}, Size: {Size} bytes", 
                                fileInfo.Name, fileInfo.CreationTime, fileInfo.Length);
                            
                            // Verify it's a valid PDF and check if it's an EIN Letter
                            var testBytes = await File.ReadAllBytesAsync(fileInfo.FullName, cancellationToken);
                            
                            // First check basic PDF validity
                            if (!IsValidPdf(testBytes))
                            {
                                _logger.LogWarning("File is not a valid PDF: {FilePath}", fileInfo.FullName);
                                continue;
                            }
                            
                            // Enhanced check: Verify filename and content for EIN Letter
                            var hasEinFilename = IsLikelyEinLetterFilename(fileInfo.Name);
                            var hasEinContent = IsValidEinLetterPdf(testBytes);
                            
                            _logger.LogInformation("PDF validation results for {FileName}: Filename check = {FilenameResult}, Content check = {ContentResult}", 
                                fileInfo.Name, hasEinFilename, hasEinContent);
                                
                            // Log specific filename patterns found for debugging
                            if (hasEinFilename)
                            {
                                var upperFileName = fileInfo.Name.ToUpper();
                                var foundPatterns = new List<string>();
                                
                                // Check for primary pattern match
                                if (upperFileName.StartsWith("CP575NOTICE_"))
                                {
                                    foundPatterns.Add("PRIMARY: CP575Notice_[numbers] ✅");
                                }
                                else
                                {
                                    // Check secondary patterns
                                    if (upperFileName.Contains("CP575")) foundPatterns.Add("SECONDARY: CP575");
                                    if (upperFileName.Contains("EIN")) foundPatterns.Add("SECONDARY: EIN");
                                    if (upperFileName.Contains("NOTICE")) foundPatterns.Add("SECONDARY: NOTICE");
                                    if (upperFileName.Contains("LETTER")) foundPatterns.Add("SECONDARY: LETTER");
                                }
                                
                                _logger.LogInformation("📋 Filename patterns detected: {Patterns}", string.Join(", ", foundPatterns));
                            }
                            
                            if (IsValidEinLetterPdfWithFilename(testBytes, fileInfo.Name))
                            {
                                _logger.LogInformation("✅ VALID EIN LETTER PDF FOUND: {FilePath} - Filename: {FilenameValid}, Content: {ContentValid}", 
                                    fileInfo.FullName, hasEinFilename ? "✅" : "❌", hasEinContent ? "✅" : "❌");
                                return fileInfo.FullName;
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ PDF found but validation failed - not an EIN Letter: {FilePath} - Filename: {FilenameValid}, Content: {ContentValid}", 
                                    fileInfo.FullName, hasEinFilename ? "✅" : "❌", hasEinContent ? "✅" : "❌");
                                // Continue searching for a real EIN Letter PDF
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Skipping old PDF file: {FileName}, Created: {CreationTime}", 
                                fileInfo.Name, fileInfo.CreationTime);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Error checking PDF file {FileName}: {Message}", fileInfo.Name, ex.Message);
                    }
                }

                _logger.LogDebug("No recently created valid PDF files found in {Location}", location);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error checking location {Location} for recent PDFs: {Message}", location, ex.Message);
                return null;
            }
        }

        private class PdfScanResult
        {
            public string FilePath { get; set; } = string.Empty;
            public long FileSize { get; set; }
            public byte[] Bytes { get; set; } = Array.Empty<byte>();
        }

        private async Task<PdfScanResult?> PerformAggressivePdfScan(string downloadDir, string downloadsFolder, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting aggressive PDF scan...");
                
                var scanLocations = new List<string>();
                
                // Add configured download directory
                if (!string.IsNullOrEmpty(downloadDir) && Directory.Exists(downloadDir))
                {
                    scanLocations.Add(downloadDir);
                }
                
                // Add Downloads folder (only if it exists and we're not in a container)
                if (!string.IsNullOrEmpty(downloadsFolder) && Directory.Exists(downloadsFolder))
                {
                    scanLocations.Add(downloadsFolder);
                }
                
                // Add container-friendly locations
                var commonLocations = new List<string>();
                
                // Check if we're running in a container or Kubernetes
                var isContainer = Environment.GetEnvironmentVariable("CONTAINER_ENV") == "true" || 
                                 Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
                                 Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") != null ||
                                 File.Exists("/.dockerenv");
                
                if (isContainer)
                {
                    _logger.LogInformation("Running in container environment, using container-specific paths");
                    // Container-friendly locations - expanded for AKS
                    commonLocations.AddRange(new[]
                    {
                        "/tmp",
                        "/app/temp",
                        "/var/tmp",
                        "/tmp/chrome-home",
                        "/tmp/chrome-home/Downloads",
                        "/tmp/ein_pdfs",
                        "/app",
                        "/app/temp",
                        "/app/downloads",
                        "/home",
                        "/home/chrome",
                        "/home/chrome/Downloads",
                        "/root",
                        "/root/Downloads",
                        "/usr/local",
                        "/usr/local/tmp",
                        "/opt",
                        "/opt/tmp",
                        Path.GetTempPath(), // This should work in containers too
                        Environment.GetEnvironmentVariable("TEMP") ?? "",
                        Environment.GetEnvironmentVariable("TMP") ?? "",
                        Environment.GetEnvironmentVariable("TMPDIR") ?? "",
                        // Kubernetes-specific paths
                        "/var/run/secrets/kubernetes.io",
                        "/var/lib/kubelet/pods",
                        "/var/log/pods",
                        "/var/lib/docker",
                        "/var/lib/containerd"
                    });
                }
                else
                {
                    _logger.LogInformation("Running in local environment, using Windows-specific paths");
                    // Windows-specific locations
                    commonLocations.AddRange(new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
                        Path.GetTempPath()
                    });
                }
                
                foreach (var location in commonLocations)
                {
                    if (Directory.Exists(location) && !scanLocations.Contains(location))
                    {
                        scanLocations.Add(location);
                    }
                }
                
                _logger.LogInformation("Scanning {Count} locations for PDF files: {Locations}", 
                    scanLocations.Count, string.Join(", ", scanLocations));
                
                var allPdfFiles = new List<FileInfo>();
                
                foreach (var location in scanLocations)
                {
                    try
                    {
                        _logger.LogInformation("🔍 Aggressive scan searching in: {Location}", location);
                        var pdfFiles = Directory.GetFiles(location, "*.pdf", SearchOption.AllDirectories);
                        _logger.LogInformation("Found {Count} PDF files in {Location}", pdfFiles.Length, location);
                        foreach (var pdfFile in pdfFiles)
                        {
                            try
                            {
                                var fileInfo = new FileInfo(pdfFile);
                                if (fileInfo.Length > 0) // Only consider non-empty files
                                {
                                    allPdfFiles.Add(fileInfo);
                                    _logger.LogDebug("Found PDF: {FilePath}, Size: {Size}, Created: {Created}", 
                                        fileInfo.FullName, fileInfo.Length, fileInfo.CreationTime);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug("Error processing PDF file {File}: {Message}", pdfFile, ex.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Error scanning location {Location}: {Message}", location, ex.Message);
                    }
                }
                
                _logger.LogInformation("Found {Count} total PDF files across all locations", allPdfFiles.Count);
                
                if (allPdfFiles.Count == 0)
                {
                    _logger.LogWarning("No PDF files found in any location");
                    return null;
                }
                
                // Sort by creation time (most recent first) and file size (larger first)
                var sortedPdfFiles = allPdfFiles
                    .OrderByDescending(f => f.CreationTime)
                    .ThenByDescending(f => f.Length)
                    .ToList();
                
                // Try each PDF file, starting with the most recent
                foreach (var fileInfo in sortedPdfFiles)
                {
                    try
                    {
                        _logger.LogInformation("Testing PDF file: {FileName}, Size: {Size} bytes, Created: {Created}", 
                            fileInfo.Name, fileInfo.Length, fileInfo.CreationTime);
                        
                        // Read the file
                        var fileBytes = await File.ReadAllBytesAsync(fileInfo.FullName, cancellationToken);
                        
                        // First validate it's a basic PDF
                        if (!IsValidPdf(fileBytes))
                        {
                            _logger.LogDebug("File is not a valid PDF: {FilePath}", fileInfo.FullName);
                            continue;
                        }
                        
                        // Enhanced validation: Check filename and content for EIN Letter
                        var hasEinFilename = IsLikelyEinLetterFilename(fileInfo.Name);
                        var hasEinContent = IsValidEinLetterPdf(fileBytes);
                        
                        _logger.LogInformation("PDF validation results for {FileName}: Filename check = {FilenameResult}, Content check = {ContentResult}", 
                            fileInfo.Name, hasEinFilename, hasEinContent);
                            
                        // Log specific filename patterns found for debugging
                        if (hasEinFilename)
                        {
                            var upperFileName = fileInfo.Name.ToUpper();
                            var foundPatterns = new List<string>();
                            
                            // Check for primary pattern match
                            if (upperFileName.StartsWith("CP575NOTICE_"))
                            {
                                foundPatterns.Add("PRIMARY: CP575Notice_[numbers] ✅");
                            }
                            else
                            {
                                // Check secondary patterns
                                if (upperFileName.Contains("CP575")) foundPatterns.Add("SECONDARY: CP575");
                                if (upperFileName.Contains("EIN")) foundPatterns.Add("SECONDARY: EIN");
                                if (upperFileName.Contains("NOTICE")) foundPatterns.Add("SECONDARY: NOTICE");
                                if (upperFileName.Contains("LETTER")) foundPatterns.Add("SECONDARY: LETTER");
                            }
                            
                            _logger.LogInformation("📋 Filename patterns detected: {Patterns}", string.Join(", ", foundPatterns));
                        }
                        
                        if (IsValidEinLetterPdfWithFilename(fileBytes, fileInfo.Name))
                        {
                            _logger.LogInformation("✅ VALID EIN LETTER PDF FOUND via aggressive scan: {FilePath}, Size: {Size} bytes - Filename: {FilenameValid}, Content: {ContentValid}", 
                                fileInfo.FullName, fileBytes.Length, hasEinFilename ? "✅" : "❌", hasEinContent ? "✅" : "❌");
                            
                            return new PdfScanResult
                            {
                                FilePath = fileInfo.FullName,
                                FileSize = fileBytes.Length,
                                Bytes = fileBytes
                            };
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ PDF found but validation failed - not an EIN Letter: {FilePath} - Filename: {FilenameValid}, Content: {ContentValid}", 
                                fileInfo.FullName, hasEinFilename ? "✅" : "❌", hasEinContent ? "✅" : "❌");
                            // Continue searching for a real EIN Letter PDF
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Error testing PDF file {FileName}: {Message}", fileInfo.Name, ex.Message);
                    }
                }
                
                _logger.LogWarning("No valid PDF files found in aggressive scan");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during aggressive PDF scan: {Message}", ex.Message);
                return null;
            }
        }

        private static string GetAncestorText(HtmlAgilityPack.HtmlNode node, int maxLevels)
        {
            var text = new StringBuilder();
            var current = node.ParentNode;
            var level = 0;
            
            while (current != null && level < maxLevels)
            {
                text.Append(current.InnerText).Append(" ");
                current = current.ParentNode;
                level++;
            }
            
            return text.ToString().Trim();
        }

        private static string CleanBusinessDescription(string? businessDescription)
        {
            if (string.IsNullOrWhiteSpace(businessDescription))
            {
                return "Any and lawful business";
            }

            // Remove all special characters except letters, numbers, and spaces
            var cleaned = Regex.Replace(businessDescription, @"[^a-zA-Z0-9\s]", "");
            
            // Remove multiple spaces and trim
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            // If the cleaned result is empty or too short, use default
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length < 3)
            {
                return "Any and lawful business";
            }

            return cleaned;
        }
        
        /// <summary>
        /// Saves PDF bytes to blob storage for debugging purposes
        /// </summary>
        private async Task SavePdfForDebugging(byte[] pdfBytes, string methodName, CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (data?.RecordId != null && _blobStorageService != null)
                {
                    var fileName = $"{data.RecordId}_EINLetter_Debug_{methodName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                    var blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, fileName, "application/pdf", cancellationToken);
                    if (!string.IsNullOrEmpty(blobUrl))
                    {
                        _logger.LogInformation("📁 DEBUG PDF SAVED: {MethodName} - {BlobUrl}", methodName, blobUrl);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to save debug PDF for method: {MethodName}", methodName);
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping debug PDF save - RecordId or BlobStorageService is null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error saving debug PDF for method {MethodName}: {Message}", methodName, ex.Message);
            }
        }

        /// <summary>
        /// Saves the primary EIN Letter PDF with the standard blob naming structure
        /// </summary>
        private async Task SavePrimaryEinLetterPdf(byte[] pdfBytes, CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (_blobStorageService != null && !string.IsNullOrEmpty(data?.EntityName))
                {
                    // Create the clean entity name exactly like requested
                    var cleanName = Regex.Replace(data.EntityName, @"[^\w\-]", "").Replace(" ", "");
                    
                    // Use the requested blob naming structure: EntityProcess/{RecordId}/{cleanName}-ID-EINLetter.pdf
                    var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINLetter.pdf";
                    
                    var blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf", cancellationToken);
                    if (!string.IsNullOrEmpty(blobUrl))
                    {
                        _logger.LogInformation("💾 PRIMARY EIN LETTER PDF SAVED: {BlobName} - {BlobUrl}", blobName, blobUrl);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to save primary EIN Letter PDF");
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping primary PDF save - BlobStorageService or EntityName is null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error saving primary EIN Letter PDF: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Saves Chrome DevTools captured PDF to blob storage with ChromeDevTools identifier
        /// Always saves regardless of validation to ensure we get both PDFs
        /// </summary>
        private async Task SaveChromeDevToolsPdfToBlob(byte[] pdfBytes, CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (_blobStorageService != null && data != null && !string.IsNullOrEmpty(data.EntityName))
                {
                    // Always save Chrome DevTools PDF regardless of validation
                    // Create the clean entity name
                    var cleanName = Regex.Replace(data.EntityName, @"[^\w\-]", "").Replace(" ", "");
                    
                    // Use standard blob naming for Chrome DevTools capture
                    var blobName = $"EntityProcess/{data.RecordId ?? "unknown"}/{cleanName}-ID-EINLetter-ChromeDevTools.pdf";
                    
                    var blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf", cancellationToken);
                    if (!string.IsNullOrEmpty(blobUrl))
                    {
                        _logger.LogInformation("🔧 CHROME DEVTOOLS PDF SAVED: {BlobName} - {BlobUrl}", blobName, blobUrl);
                        
                        // Also notify Salesforce about this capture
                        if (_salesforceClient != null)
                        {
                            await _salesforceClient.NotifyEinLetterToSalesforceAsync(
                                data.RecordId, 
                                blobUrl, 
                                data.EntityName,
                                data.AccountId,
                                data.EntityId,
                                data.CaseId
                            );
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to save Chrome DevTools PDF to blob");
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping Chrome DevTools PDF save - BlobStorageService or CaseData is null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error saving Chrome DevTools PDF to blob: {Message}", ex.Message);
            }
        }



        /// <summary>
        /// Enhanced CapturePageAsPdf method with multiple download methods
        /// </summary>
        public async Task<(string? BlobUrl, bool Success)> CapturePageAsPdfEnhanced(CaseData? data, CancellationToken cancellationToken)
        {
            _logger.LogInformation("=== ENHANCED PDF CAPTURE: Starting comprehensive enhanced PDF download methods ===");
            
            string? firstSuccessfulBlobUrl = null;
            bool anyMethodSucceeded = false;
            var successfulMethods = new List<string>();
            
            try
            {
                // Method Enhanced-0: Container-safe Base64 PDF extraction (AKS optimized)
                _logger.LogInformation("=== ENHANCED METHOD 0: Container-safe Base64 PDF extraction ===");
                var pdfBytes = await TryBase64PdfExtractionForContainer(cancellationToken);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    _logger.LogInformation("✅ ENHANCED METHOD 0 SUCCESS: Container Base64 extraction - {FileSize} bytes", pdfBytes.Length);
                    
                    // Validate that this is actually a real EIN Letter PDF
                    var extractedFileName = ExtractCp575Filename();
                    var (isValidEinLetter, validationResult) = await ValidateRealEinLetterPdf(pdfBytes, null, extractedFileName);
                    _logger.LogInformation("🔍 ENHANCED METHOD 0 Validation: {IsValid} - {Result}", isValidEinLetter, validationResult);
                    
                    if (isValidEinLetter)
                    {
                        await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method0_ContainerBase64_VALID", data, cancellationToken);
                        _logger.LogInformation("✅ ENHANCED METHOD 0 CONTAINER SUCCESS: Valid EIN Letter captured and saved");
                        await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method0_ContainerBase64", data, cancellationToken);
                        return ("Enhanced_Method0_ContainerBase64_Success", true);
                    }
                    else
                    {
                        await SavePdfForDebugging(pdfBytes, "Enhanced_Method0_ContainerBase64_INVALID", data, cancellationToken);
                        _logger.LogWarning("⚠️ ENHANCED METHOD 0: Container extraction succeeded but PDF validation failed - trying next method");
                    }
                }
                else
                {
                    _logger.LogWarning("❌ ENHANCED METHOD 0 FAILED: Container Base64 extraction returned null or empty");
                }

                // Method Enhanced-1: Try Chrome DevTools PDF capture
                _logger.LogInformation("=== ENHANCED METHOD 1: Chrome DevTools PDF capture ===");
                        pdfBytes = await TryChromeDevToolsPdfCapture(cancellationToken, data);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                    _logger.LogInformation("✅ ENHANCED METHOD 1 SUCCESS: Chrome DevTools capture - {FileSize} bytes", pdfBytes.Length);
            
            // Validate that this is actually a real EIN Letter PDF
            var extractedFileName = ExtractCp575Filename();
            var (isValidEinLetter, validationResult) = await ValidateRealEinLetterPdf(pdfBytes, null, extractedFileName);
                    _logger.LogInformation("🔍 ENHANCED METHOD 1 Validation: {IsValid} - {Result}", isValidEinLetter, validationResult);
                    
                    await SavePdfForDebugging(pdfBytes, isValidEinLetter ? "Enhanced_Method1_ChromeDevTools_VALID" : "Enhanced_Method1_ChromeDevTools_INVALID", data, cancellationToken);
                    
                    // Save using the same blob naming structure as TryDownloadEinLetterPdfWithSelenium
                    await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method1_ChromeDevTools", data, cancellationToken);
                    successfulMethods.Add("Enhanced_Method1_ChromeDevTools");
                    
                    if (isValidEinLetter && firstSuccessfulBlobUrl == null)
                    {
                        anyMethodSucceeded = true;
                        firstSuccessfulBlobUrl = "Enhanced_Method1_ChromeDevTools_Success";
            }
        }
        else
        {
                    _logger.LogWarning("❌ ENHANCED METHOD 1 FAILED: Chrome DevTools capture returned null or empty");
        }

                // Method Enhanced-2: Try ultimate fallback PDF download
                _logger.LogInformation("=== ENHANCED METHOD 2: Ultimate fallback PDF download ===");
        pdfBytes = await TryUltimateFallbackPdfDownload(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                    _logger.LogInformation("✅ ENHANCED METHOD 2 SUCCESS: Ultimate fallback - {FileSize} bytes", pdfBytes.Length);
            
            // Validate that this is actually a real EIN Letter PDF
            var extractedFileName = ExtractCp575Filename();
            var (isValidEinLetter, validationResult) = await ValidateRealEinLetterPdf(pdfBytes, null, extractedFileName);
                    _logger.LogInformation("🔍 ENHANCED METHOD 2 Validation: {IsValid} - {Result}", isValidEinLetter, validationResult);
                    
                    await SavePdfForDebugging(pdfBytes, isValidEinLetter ? "Enhanced_Method2_UltimateFallback_VALID" : "Enhanced_Method2_UltimateFallback_INVALID", data, cancellationToken);
                    
                    // Save using the same blob naming structure as TryDownloadEinLetterPdfWithSelenium
                    await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method2_UltimateFallback", data, cancellationToken);
                    successfulMethods.Add("Enhanced_Method2_UltimateFallback");
                    
                    if (isValidEinLetter && firstSuccessfulBlobUrl == null)
                    {
                        anyMethodSucceeded = true;
                        firstSuccessfulBlobUrl = "Enhanced_Method2_UltimateFallback_Success";
            }
        }
        else
        {
                    _logger.LogWarning("❌ ENHANCED METHOD 2 FAILED: Ultimate fallback returned null or empty");
        }

                // Method Enhanced-3: Try to capture from browser viewer
                _logger.LogInformation("=== ENHANCED METHOD 3: Capture from browser viewer ===");
        pdfBytes = await TryCapturePdfFromBrowserViewer(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                    _logger.LogInformation("✅ ENHANCED METHOD 3 SUCCESS: Browser viewer capture - {FileSize} bytes", pdfBytes.Length);
                    await SavePdfForDebugging(pdfBytes, "Enhanced_Method3_BrowserViewer", data, cancellationToken);
                    
                    // Save using the same blob naming structure as TryDownloadEinLetterPdfWithSelenium
                    await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method3_BrowserViewer", data, cancellationToken);
                    successfulMethods.Add("Enhanced_Method3_BrowserViewer");
                    
                    if (firstSuccessfulBlobUrl == null)
                    {
                        anyMethodSucceeded = true;
                        firstSuccessfulBlobUrl = "Enhanced_Method3_BrowserViewer_Success";
                    }
        }
        else
        {
                    _logger.LogWarning("❌ ENHANCED METHOD 3 FAILED: Browser viewer capture returned null or empty");
        }

                // Method Enhanced-4: Try to capture from open tab
                _logger.LogInformation("=== ENHANCED METHOD 4: Capture from open tab ===");
        pdfBytes = await TryCapturePdfFromOpenTab(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                    _logger.LogInformation("✅ ENHANCED METHOD 4 SUCCESS: Open tab capture - {FileSize} bytes", pdfBytes.Length);
                    await SavePdfForDebugging(pdfBytes, "Enhanced_Method4_OpenTab", data, cancellationToken);
                    
                    // Save using the same blob naming structure as TryDownloadEinLetterPdfWithSelenium
                    await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method4_OpenTab", data, cancellationToken);
                    successfulMethods.Add("Enhanced_Method4_OpenTab");
                    
                    if (firstSuccessfulBlobUrl == null)
                    {
                        anyMethodSucceeded = true;
                        firstSuccessfulBlobUrl = "Enhanced_Method4_OpenTab_Success";
                    }
        }
        else
        {
                    _logger.LogWarning("❌ ENHANCED METHOD 4 FAILED: Open tab capture returned null or empty");
        }

                // Method Enhanced-5: Try to print PDF from opened window
                _logger.LogInformation("=== ENHANCED METHOD 5: Print PDF from opened window ===");
        pdfBytes = await TryPrintPdfFromOpenedWindow(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                    _logger.LogInformation("✅ ENHANCED METHOD 5 SUCCESS: Print PDF from opened window - {FileSize} bytes", pdfBytes.Length);
                    await SavePdfForDebugging(pdfBytes, "Enhanced_Method5_PrintFromWindow", data, cancellationToken);
                    
                    // Save using the same blob naming structure as TryDownloadEinLetterPdfWithSelenium
                    await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method5_PrintFromWindow", data, cancellationToken);
                    successfulMethods.Add("Enhanced_Method5_PrintFromWindow");
                    
                    if (firstSuccessfulBlobUrl == null)
                    {
                        anyMethodSucceeded = true;
                        firstSuccessfulBlobUrl = "Enhanced_Method5_PrintFromWindow_Success";
                    }
        }
        else
        {
                    _logger.LogWarning("❌ ENHANCED METHOD 5 FAILED: Print PDF from opened window returned null or empty");
        }

                // Method Enhanced-6: Try direct PDF URL extraction
                _logger.LogInformation("=== ENHANCED METHOD 6: Direct PDF URL extraction ===");
        pdfBytes = await TryDirectPdfUrlExtraction(cancellationToken);
        if (pdfBytes != null && pdfBytes.Length > 0)
        {
                    _logger.LogInformation("✅ ENHANCED METHOD 6 SUCCESS: Direct PDF URL extraction - {FileSize} bytes", pdfBytes.Length);
                    await SavePdfForDebugging(pdfBytes, "Enhanced_Method6_DirectUrlExtraction", data, cancellationToken);
                    
                    // Save using the same blob naming structure as TryDownloadEinLetterPdfWithSelenium
                    await SaveEinLetterPdfWithMethodIdentifier(pdfBytes, "Enhanced_Method6_DirectUrlExtraction", data, cancellationToken);
                    successfulMethods.Add("Enhanced_Method6_DirectUrlExtraction");
                    
                    if (firstSuccessfulBlobUrl == null)
                    {
                        anyMethodSucceeded = true;
                        firstSuccessfulBlobUrl = "Enhanced_Method6_DirectUrlExtraction_Success";
                    }
        }
        else
        {
                    _logger.LogWarning("❌ ENHANCED METHOD 6 FAILED: Direct PDF URL extraction returned null or empty");
                }

                // Summary of enhanced PDF capture results
                _logger.LogInformation("🏁 ENHANCED PDF CAPTURE COMPLETE: Successful methods: [{MethodsList}], Any success: {AnySuccess}", 
                    string.Join(", ", successfulMethods), anyMethodSucceeded);

                if (anyMethodSucceeded)
                {
                    return (firstSuccessfulBlobUrl, true);
        }
        else
        {
                    _logger.LogError("❌ ALL ENHANCED METHODS FAILED: No enhanced PDF capture method succeeded");
                return (null, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in enhanced CapturePageAsPdf method");
                return (null, false);
            }
        }

        /// <summary>
        /// Comprehensive validation to detect authentic EIN Letter PDF based on real EIN Letter content
        /// Uses patterns from actual IRS EIN Letter with filename pattern: "CP575Notice_1753375123337%20.pdf"
        /// </summary>
        private async Task<(bool IsValid, string ValidationResult)> ValidateRealEinLetterPdf(byte[] pdfData, string? expectedEinNumber = null, string? fileName = null)
        {
            try
            {
                _logger.LogInformation("🔍 Starting real EIN Letter PDF validation based on actual IRS content patterns...");
                
                if (pdfData == null || pdfData.Length == 0)
                {
                    return (false, "PDF data is null or empty");
                }

                // 1. Basic PDF validation
                if (!IsPdfContent(pdfData))
                {
                    return (false, "Data is not a valid PDF file (missing PDF magic bytes)");
                }

                // 2. File size validation based on user feedback (14-16 KB typical)
                var fileSizeKb = pdfData.Length / 1024;
                _logger.LogInformation("📏 PDF file size: {SizeKB} KB (expected: 14-16 KB)", fileSizeKb);

                // 3. Extract text content from PDF
                var pdfText = ExtractTextFromPdf(pdfData);
                if (string.IsNullOrWhiteSpace(pdfText))
                {
                    return (false, "Could not extract any text from PDF - likely a screenshot/image");
                }

                _logger.LogInformation("📄 Extracted {TextLength} characters from PDF for validation", pdfText.Length);

                // 4. Real EIN Letter validation based on actual content patterns
                var score = 0;
                var validationResults = new List<string>();

                // File size scoring (more flexible for Chrome DevTools captures)
                if (fileSizeKb >= 14 && fileSizeKb <= 16)
                {
                    score += 15; // Perfect size match
                    validationResults.Add($"✅ Perfect file size ({fileSizeKb} KB) matches real EIN Letters (14-16 KB)");
                }
                else if (fileSizeKb >= 10 && fileSizeKb <= 50) // Expanded range for Chrome DevTools
                {
                    score += 10; // Close to expected size
                    validationResults.Add($"✅ Good file size ({fileSizeKb} KB) acceptable for EIN Letters (10-50 KB)");
                }
                else if (fileSizeKb > 200)
                {
                    score -= 20; // Likely screenshot
                    validationResults.Add($"❌ File size ({fileSizeKb} KB) too large - likely screenshot (expected 10-50 KB)");
                }
                else
                {
                    validationResults.Add($"⚠️ File size ({fileSizeKb} KB) outside typical range (10-50 KB)");
                }

                // Real EIN Letter content validation
                score += ValidateRealEinLetterContent(pdfText, validationResults);

                // Filename validation (if provided)
                if (!string.IsNullOrEmpty(fileName))
                {
                    var filenameValidation = ValidateEinLetterFilename(fileName);
                    score += filenameValidation.Score;
                    validationResults.Add(filenameValidation.Result);
                }

                // EIN number validation
                if (!string.IsNullOrEmpty(expectedEinNumber))
                {
                    var einValidation = ValidateEinNumberInText(pdfText, expectedEinNumber);
                    score += einValidation.Score;
                    validationResults.Add(einValidation.Result);
                }

                // Final validation
                var isValidEinLetter = score >= 25; // Higher threshold for authentic validation
                var resultSummary = $"Real EIN Letter Validation Score: {score}/50. " + string.Join(" | ", validationResults);

                _logger.LogInformation("🎯 Real EIN Letter validation result: {IsValid} (Score: {Score}/50)", isValidEinLetter, score);
                foreach (var result in validationResults)
                {
                    _logger.LogInformation("  {Result}", result);
                }

                return (isValidEinLetter, resultSummary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating real EIN Letter PDF");
                return (false, $"Validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate EIN Letter content based on real IRS EIN Letter patterns
        /// </summary>
        private int ValidateRealEinLetterContent(string text, List<string> validationResults)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            
            var lowerText = text.ToLower();
            var score = 0;

            // CRITICAL PATTERNS from real EIN Letter (high score)
            var criticalPatterns = new Dictionary<string, int>
            {
                ["department of the treasury"] = 8,
                ["internal revenue service"] = 8,
                ["we assigned you an employer identification number"] = 10,
                ["cp 575"] = 8,
                ["keep this notice in your permanent records"] = 8,
                ["if you did not apply for this ein, contact the irs immediately"] = 10,
                ["keep a copy of this notice — it is issued only once"] = 8,
                ["your irs name control is"] = 6,
                ["safeguard your ein as per publication 4557"] = 6,
                ["cincinnati, oh 45999"] = 6
            };

            // SUPPORTING PATTERNS from real EIN Letter (medium score)
            var supportingPatterns = new Dictionary<string, int>
            {
                ["date of this notice:"] = 4,
                ["employer identification number:"] = 4,
                ["number of this notice:"] = 4,
                ["the irs assigned ein"] = 5,
                ["for assistance call: 1-800-829-4933"] = 3,
                ["when filing tax documents or making payments"] = 3,
                ["based on your information, you must file:"] = 4,
                ["form 1120"] = 2,
                ["form 2553"] = 2,
                ["www.irs.gov/forms-pubs"] = 2
            };

            // Check critical patterns
            foreach (var pattern in criticalPatterns)
            {
                if (lowerText.Contains(pattern.Key))
                {
                    score += pattern.Value;
                    validationResults.Add($"✅ CRITICAL: Found '{pattern.Key}' (+{pattern.Value})");
                }
            }

            // Check supporting patterns
            foreach (var pattern in supportingPatterns)
            {
                if (lowerText.Contains(pattern.Key))
                {
                    score += pattern.Value;
                    validationResults.Add($"✅ SUPPORT: Found '{pattern.Key}' (+{pattern.Value})");
                }
            }

            // Check for negative indicators (screenshot/webpage elements)
            var negativePatterns = new[]
            {
                "<html", "</html>", "<body", "</body>", "<div", "</div>",
                "javascript", "window.", "document.", "onclick=",
                "navigation", "breadcrumb", "menu", "button",
                "continue >>", "begin application", "back to"
            };

            var negativeCount = negativePatterns.Count(pattern => lowerText.Contains(pattern));
            if (negativeCount > 0)
            {
                var penalty = negativeCount * 5;
                score -= penalty;
                validationResults.Add($"❌ Found {negativeCount} webpage indicators (-{penalty})");
            }

            return score;
        }

        /// <summary>
        /// Validate EIN Letter filename based on real IRS naming pattern
        /// Real example: "CP575Notice_1753375123337%20.pdf"
        /// </summary>
        private (int Score, string Result) ValidateEinLetterFilename(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return (0, "No filename provided");

            var lowerFileName = fileName.ToLower();
            var score = 0;
            var indicators = new List<string>();

            // Perfect match patterns (high score)
            if (Regex.IsMatch(lowerFileName, @"cp575notice_\d+.*\.pdf", RegexOptions.IgnoreCase))
            {
                score += 15;
                indicators.Add("✅ Perfect CP575Notice pattern match");
            }
            else if (lowerFileName.Contains("cp575notice"))
            {
                score += 12;
                indicators.Add("✅ Contains CP575Notice");
            }
            else if (lowerFileName.Contains("cp575"))
            {
                score += 8;
                indicators.Add("✅ Contains CP575");
            }

            // Additional IRS filename indicators
            if (lowerFileName.Contains("notice"))
            {
                score += 3;
                indicators.Add("✅ Contains 'notice'");
            }

            // Check for numeric ID pattern (timestamp-like)
            if (Regex.IsMatch(lowerFileName, @"\d{10,}", RegexOptions.IgnoreCase))
            {
                score += 5;
                indicators.Add("✅ Contains long numeric ID (timestamp-like)");
            }

            // URL encoding indicators (like %20 for space)
            if (lowerFileName.Contains("%20") || lowerFileName.Contains("%"))
            {
                score += 2;
                indicators.Add("✅ Contains URL encoding (%20, etc.)");
            }

            // PDF extension
            if (lowerFileName.EndsWith(".pdf"))
            {
                score += 2;
                indicators.Add("✅ PDF extension");
            }

            // Negative indicators (custom/user-generated names)
            var negativePatterns = new[]
            {
                "download", "screenshot", "capture", "image", "scan",
                "desktop", "documents", "temp", "test", "sample"
            };

            var negativeCount = negativePatterns.Count(pattern => lowerFileName.Contains(pattern));
            if (negativeCount > 0)
            {
                var penalty = negativeCount * 3;
                score -= penalty;
                indicators.Add($"❌ Contains user-generated filename indicators (-{penalty})");
            }

            var result = $"Filename validation: {score} points. " + string.Join(", ", indicators);
            return (score, result);
        }

        /// <summary>
        /// Validate EIN number in text content
        /// </summary>
        private (int Score, string Result) ValidateEinNumberInText(string text, string expectedEinNumber)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(expectedEinNumber))
                return (0, "No EIN to validate");

            var lowerText = text.ToLower();
            var cleanExpectedEin = expectedEinNumber.Replace("-", "").Replace(" ", "");

            // Check for exact EIN match
            if (lowerText.Contains(expectedEinNumber.ToLower()))
            {
                return (10, $"✅ Found exact EIN match: {expectedEinNumber}");
            }

            // Check for EIN without hyphens
            if (lowerText.Replace("-", "").Replace(" ", "").Contains(cleanExpectedEin))
            {
                return (8, $"✅ Found EIN without formatting: {expectedEinNumber}");
            }

            // Check for "The IRS assigned EIN XX-XXXXXXX" pattern
            var einPattern = @"(?:the irs assigned ein|ein assigned|ein:?\s*)" + Regex.Escape(expectedEinNumber);
            if (Regex.IsMatch(lowerText, einPattern, RegexOptions.IgnoreCase))
            {
                return (12, $"✅ Found EIN in assignment context: {expectedEinNumber}");
            }

            return (0, $"❌ Expected EIN {expectedEinNumber} not found");
        }

        /// <summary>
        /// Extract text content from PDF using simple text extraction
        /// </summary>
        private string ExtractTextFromPdf(byte[] pdfData)
        {
            try
            {
                // Convert PDF to text using simple string extraction
                var pdfText = System.Text.Encoding.UTF8.GetString(pdfData);
                
                // Extract readable text between PDF objects
                var textPattern = @"(?:BT\s+.*?ET)|(?:\(([^)]+)\))|(?:<([^>]+)>)";
                var matches = Regex.Matches(pdfText, textPattern, RegexOptions.Singleline);
                
                var extractedText = new StringBuilder();
                foreach (Match match in matches)
                {
                    if (match.Groups[1].Success)
                        extractedText.AppendLine(match.Groups[1].Value);
                    else if (match.Groups[2].Success)
                        extractedText.AppendLine(match.Groups[2].Value);
                }

                var result = extractedText.ToString();
                
                // If simple extraction didn't work, try binary search for readable text
                if (string.IsNullOrWhiteSpace(result))
                {
                    result = ExtractReadableTextFromBinary(pdfData);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting text from PDF");
                return string.Empty;
            }
        }

        /// <summary>
        /// Extract readable text from binary PDF data
        /// </summary>
        private string ExtractReadableTextFromBinary(byte[] pdfData)
        {
            try
            {
                var text = new StringBuilder();
                var currentWord = new StringBuilder();
                
                for (int i = 0; i < pdfData.Length; i++)
                {
                    var b = pdfData[i];
                    
                    // Check if it's a printable ASCII character
                    if (b >= 32 && b <= 126)
                    {
                        currentWord.Append((char)b);
                    }
                    else
                    {
                        // End of word, add it if it's long enough to be meaningful
                        if (currentWord.Length >= 3)
                        {
                            text.Append(currentWord.ToString()).Append(" ");
                        }
                        currentWord.Clear();
                    }
                }
                
                // Add final word if any
                if (currentWord.Length >= 3)
                {
                    text.Append(currentWord.ToString());
                }
                
                return text.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting readable text from binary");
                return string.Empty;
            }
        }

        /// <summary>
        /// Extract the CP575 PDF filename from page source or URL
        /// </summary>
        private string? ExtractCp575Filename()
        {
            try
            {
                // First try to get from current URL if we're on a PDF page
                var currentUrl = Driver?.Url;
                if (!string.IsNullOrEmpty(currentUrl) && currentUrl.ToLower().Contains(".pdf"))
                {
                    var uri = new Uri(currentUrl);
                    var fileName = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrEmpty(fileName) && fileName.ToLower().Contains("cp575"))
                    {
                        _logger.LogInformation("📁 Extracted filename from URL: {FileName}", fileName);
                        return fileName;
                    }
                }

                // Try to extract from page source
                var pageSource = Driver?.PageSource ?? "";
                
                // Look for CP575 PDF URLs in various patterns
                var pdfUrlPatterns = new[]
                {
                    @"href=[""']([^""']*CP575[^""']*\.pdf)[""']",
                    @"href=[""']([^""']*/modiein/notices/[^""']*\.pdf)[""']",
                    @"openPDFNoticeWindow\([""']([^""']*\.pdf)[""']\)",
                    @"[""']([^""']*CP575Notice_[^""']*\.pdf)[""']"
                };

                foreach (var pattern in pdfUrlPatterns)
                {
                    var match = Regex.Match(pageSource, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var fullUrl = match.Groups[1].Value;
                        var fileName = Path.GetFileName(fullUrl);
                        
                        // Decode URL encoding if present
                        fileName = Uri.UnescapeDataString(fileName);
                        
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            _logger.LogInformation("📁 Extracted filename from page source: {FileName}", fileName);
                            return fileName;
                        }
                    }
                }

                _logger.LogInformation("📁 No CP575 filename found in page source or URL");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting CP575 filename");
                return null;
            }
        }

        /// <summary>
        /// Helper method to validate PDF content (check PDF magic bytes)
        /// </summary>
        private bool IsPdfContent(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            
            // Check for PDF magic bytes
            return data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46; // %PDF
        }

        /// <summary>
        /// Safely gets a truncated version of the page source for logging purposes
        /// </summary>
        private string GetTruncatedPageSource(int maxLength = 1000)
        {
            try
            {
                var pageSource = Driver?.PageSource;
                if (string.IsNullOrEmpty(pageSource))
                    return "N/A";
                
                return pageSource.Length <= maxLength 
                    ? pageSource 
                    : pageSource.Substring(0, maxLength);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to get page source: {Message}", ex.Message);
                return "N/A";
            }
        }

        /// <summary>
        /// Safely gets alert text with null checking
        /// </summary>
        private string GetAlertText(IAlert alert)
        {
            try
            {
                return alert?.Text ?? "Unknown alert";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to get alert text: {Message}", ex.Message);
                return "Unknown alert";
            }
        }

        /// <summary>
        /// Optimized reference number extraction with early termination and page source caching
        /// </summary>
        private string? ExtractReferenceNumberOptimized()
        {
            _logger.LogInformation("Starting optimized reference number extraction with early termination...");
            
            // Cache page source once at the beginning
            var pageSource = Driver?.PageSource ?? string.Empty;
            var pageTextLower = pageSource.ToLower();
            
            // Early check: if page doesn't contain reference number text, return null immediately
            if (!pageTextLower.Contains("reference number"))
            {
                _logger.LogDebug("Page does not contain 'reference number' text, skipping extraction");
                return null;
            }

            string? referenceNumber = null;

            // Method 1: Fast Selenium-based extraction (most reliable)
            try
            {
                var refElement = Driver != null ? WaitHelper.WaitUntilExists(Driver, By.XPath("//p[contains(text(), 'reference number')]"), 3) : null;
                if (refElement != null)
                {
                    var refText = refElement.Text;
                    var refMatch = Regex.Match(refText, @"reference number\s+(\d+)", RegexOptions.IgnoreCase);
                    if (refMatch.Success)
                    {
                        referenceNumber = refMatch.Groups[1].Value;
                        _logger.LogInformation("✅ FAST EXTRACTION: Reference number via Selenium: {ReferenceNumber}", referenceNumber);
                        return referenceNumber; // Early termination
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Fast Selenium extraction failed: {Message}", ex.Message);
            }

            // Method 2: Quick regex on cached page source
            if (string.IsNullOrEmpty(referenceNumber))
            {
                var patterns = new[]
                {
                    @"reference number\s+(\d+)",
                    @"reference number[:\s]*(\d+)",
                    @"mention reference number\s+(\d+)"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(pageSource, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        referenceNumber = match.Groups[1].Value;
                        _logger.LogInformation("✅ FAST EXTRACTION: Reference number via regex pattern '{Pattern}': {ReferenceNumber}", pattern, referenceNumber);
                        return referenceNumber; // Early termination
                    }
                }
            }

            // Method 3: Bold element extraction (common pattern)
            if (string.IsNullOrEmpty(referenceNumber))
            {
                try
                {
                    var boldElements = Driver?.FindElements(By.XPath("//p[contains(text(), 'reference number')]//b"));
                    if (boldElements != null && boldElements.Count > 0)
                    {
                        foreach (var boldElement in boldElements)
                        {
                            var boldText = boldElement.Text?.Trim();
                            if (!string.IsNullOrEmpty(boldText) && Regex.IsMatch(boldText, @"^\d+$"))
                            {
                                referenceNumber = boldText;
                                _logger.LogInformation("✅ FAST EXTRACTION: Reference number from bold element: {ReferenceNumber}", referenceNumber);
                                return referenceNumber; // Early termination
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Bold element extraction failed: {Message}", ex.Message);
                }
            }

            // Method 4: JavaScript extraction (fast DOM access)
            if (string.IsNullOrEmpty(referenceNumber))
            {
                try
                {
                    var jsExecutor = (IJavaScriptExecutor?)Driver;
                    var script = @"
                        function findReferenceNumberFast() {
                            // Look for bold elements with numbers near reference text
                            var boldElements = document.querySelectorAll('b');
                            for (var i = 0; i < boldElements.length; i++) {
                                var boldText = boldElements[i].textContent || boldElements[i].innerText || '';
                                if (/^\d+$/.test(boldText)) {
                                    var parent = boldElements[i].parentElement;
                                    var parentText = parent ? (parent.textContent || parent.innerText || '') : '';
                                    if (parentText.toLowerCase().includes('reference number')) {
                                        return boldText;
                                    }
                                }
                            }
                            
                            // Look for any text containing reference number pattern
                            var allText = document.body.textContent || document.body.innerText || '';
                            var match = allText.match(/reference number\s+(\d+)/i);
                            return match ? match[1] : null;
                        }
                        return findReferenceNumberFast();
                    ";

                    var result = jsExecutor?.ExecuteScript(script)?.ToString();
                    if (!string.IsNullOrEmpty(result))
                    {
                        referenceNumber = result;
                        _logger.LogInformation("✅ FAST EXTRACTION: Reference number via JavaScript: {ReferenceNumber}", referenceNumber);
                        return referenceNumber; // Early termination
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("JavaScript extraction failed: {Message}", ex.Message);
                }
            }

            // Method 5: HtmlAgilityPack on cached page source (robust but slower)
            if (string.IsNullOrEmpty(referenceNumber))
            {
                try
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(pageSource);

                    // Search text nodes containing "reference number"
                    var textNodes = doc.DocumentNode.SelectNodes("//text()[contains(., 'reference number')]");
                    if (textNodes != null)
                    {
                        foreach (var node in textNodes)
                        {
                            var match = Regex.Match(node.InnerText, @"reference number\s+(\d+)", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                referenceNumber = match.Groups[1].Value;
                                _logger.LogInformation("✅ ROBUST EXTRACTION: Reference number via HtmlAgilityPack: {ReferenceNumber}", referenceNumber);
                                return referenceNumber; // Early termination
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("HtmlAgilityPack extraction failed: {Message}", ex.Message);
                }
            }

            _logger.LogWarning("❌ All optimized reference number extraction methods failed");
            return null;
        }

        /// <summary>
        /// Container-safe Base64 PDF extraction - bypasses file system completely
        /// Designed specifically for AKS containers without Downloads directory
        /// </summary>
        private async Task<byte[]?> TryBase64PdfExtractionForContainer(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("🐳 CONTAINER-SAFE: Attempting Base64 PDF extraction (no file system access)");
                
                var jsExecutor = (IJavaScriptExecutor?)Driver;
                if (jsExecutor == null)
                {
                    _logger.LogWarning("❌ JavaScript executor not available");
                    return null;
                }

                // Container-optimized Base64 extraction script
                var containerExtractionScript = @"
                    function extractPdfAsBase64ForContainer() {
                        var result = {
                            success: false,
                            method: null,
                            data: null,
                            size: 0,
                            errors: []
                        };

                        // Method 1: Chrome PDF viewer direct blob extraction (most reliable in containers)
                        try {
                            var chromeViewer = document.querySelector('#plugin, embed[type*=""pdf""], object[type*=""pdf""]');
                            if (chromeViewer && chromeViewer.src) {
                                // For Chrome PDF viewer, try to extract blob data
                                fetch(chromeViewer.src)
                                    .then(response => response.blob())
                                    .then(blob => {
                                        var reader = new FileReader();
                                        reader.onload = function() {
                                            var base64 = reader.result.split(',')[1];
                                            window.extractedPdfBase64 = base64;
                                            window.extractedPdfSize = blob.size;
                                        };
                                        reader.readAsDataURL(blob);
                                    });
                                result.method = 'chrome-blob-fetch';
                            }
                        } catch(e) {
                            result.errors.push('Chrome blob extraction failed: ' + e.message);
                        }

                        // Method 2: PDF.js application data extraction
                        try {
                            if (window.PDFViewerApplication && window.PDFViewerApplication.pdfDocument) {
                                var pdfDoc = window.PDFViewerApplication.pdfDocument;
                                if (pdfDoc._transport && pdfDoc._transport._rawData) {
                                    var uint8Array = pdfDoc._transport._rawData;
                                    var binary = '';
                                    for (var i = 0; i < uint8Array.length; i++) {
                                        binary += String.fromCharCode(uint8Array[i]);
                                    }
                                    result.data = btoa(binary);
                                    result.size = uint8Array.length;
                                    result.success = true;
                                    result.method = 'pdfjs-rawdata';
                                    return result;
                                }
                                
                                // Alternative PDF.js method
                                if (pdfDoc.getData) {
                                    pdfDoc.getData().then(function(data) {
                                        var binary = '';
                                        for (var i = 0; i < data.length; i++) {
                                            binary += String.fromCharCode(data[i]);
                                        }
                                        window.extractedPdfBase64 = btoa(binary);
                                        window.extractedPdfSize = data.length;
                                    });
                                    result.method = 'pdfjs-getdata';
                                }
                            }
                        } catch(e) {
                            result.errors.push('PDF.js extraction failed: ' + e.message);
                        }

                        // Method 3: DOM-based Base64 search (fallback)
                        try {
                            var pageHtml = document.documentElement.outerHTML;
                            var base64Patterns = [
                                /data:application\/pdf;base64,([A-Za-z0-9+\/=]{1000,})/g,
                                /application\/pdf.*?([A-Za-z0-9+\/=]{1000,})/g,
                                /%PDF.*?([A-Za-z0-9+\/=]{1000,})/g
                            ];
                            
                            for (var i = 0; i < base64Patterns.length; i++) {
                                var matches = pageHtml.match(base64Patterns[i]);
                                if (matches && matches.length > 0) {
                                    var base64Data = matches[0].split(',').pop() || matches[0];
                                    // Validate it looks like base64 PDF data
                                    if (base64Data.length > 1000 && base64Data.match(/^[A-Za-z0-9+\/=]+$/)) {
                                        result.data = base64Data;
                                        result.size = Math.floor(base64Data.length * 3 / 4); // Approximate binary size
                                        result.success = true;
                                        result.method = 'dom-base64-pattern-' + i;
                                        return result;
                                    }
                                }
                            }
                        } catch(e) {
                            result.errors.push('DOM Base64 search failed: ' + e.message);
                        }

                        // Method 4: Direct PDF document object access
                        try {
                            var pdfDocuments = document.querySelectorAll('embed[src*="".pdf""], object[data*="".pdf""]');
                            for (var i = 0; i < pdfDocuments.length; i++) {
                                var pdfDoc = pdfDocuments[i];
                                var src = pdfDoc.src || pdfDoc.data;
                                if (src && src.startsWith('blob:')) {
                                    // Try to fetch blob directly
                                    fetch(src).then(response => response.arrayBuffer())
                                        .then(buffer => {
                                            var uint8Array = new Uint8Array(buffer);
                                            var binary = '';
                                            for (var j = 0; j < uint8Array.length; j++) {
                                                binary += String.fromCharCode(uint8Array[j]);
                                            }
                                            window.extractedPdfBase64 = btoa(binary);
                                            window.extractedPdfSize = buffer.byteLength;
                                        });
                                    result.method = 'direct-blob-fetch';
                                    break;
                                }
                            }
                        } catch(e) {
                            result.errors.push('Direct PDF document access failed: ' + e.message);
                        }

                        return result;
                    }

                    // Execute extraction and return immediate result
                    return extractPdfAsBase64ForContainer();
                ";

                var extractionResult = jsExecutor.ExecuteScript(containerExtractionScript);
                _logger.LogInformation("📋 Container extraction initial result: {Result}", extractionResult);

                // Check for immediate success
                if (extractionResult is Dictionary<string, object> result)
                {
                    if (result.TryGetValue("success", out var success) && (bool)success &&
                        result.TryGetValue("data", out var data) && data is string base64Data)
                    {
                        try
                        {
                            var pdfBytes = Convert.FromBase64String(base64Data);
                            if (IsValidPdf(pdfBytes))
                            {
                                var method = result.TryGetValue("method", out var methodObj) ? methodObj.ToString() : "unknown";
                                _logger.LogInformation("✅ CONTAINER SUCCESS: {Method} extracted {Size} bytes", method, pdfBytes.Length);
                                return pdfBytes;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Base64 conversion failed: {Message}", ex.Message);
                        }
                    }
                }

                // Wait for async extractions to complete
                _logger.LogInformation("⏳ Waiting for async PDF extraction to complete...");
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(1000, cancellationToken);
                    
                    var checkScript = @"
                        if (window.extractedPdfBase64 && window.extractedPdfSize) {
                            return {
                                data: window.extractedPdfBase64,
                                size: window.extractedPdfSize,
                                ready: true
                            };
                        }
                        return { ready: false };
                    ";
                    
                    var checkResult = jsExecutor.ExecuteScript(checkScript);
                    if (checkResult is Dictionary<string, object> check &&
                        check.TryGetValue("ready", out var ready) && (bool)ready &&
                        check.TryGetValue("data", out var asyncData) && asyncData is string asyncBase64)
                    {
                        try
                        {
                            var pdfBytes = Convert.FromBase64String(asyncBase64);
                            if (IsValidPdf(pdfBytes))
                            {
                                _logger.LogInformation("✅ CONTAINER ASYNC SUCCESS: Extracted {Size} bytes after {Attempts} attempts", 
                                    pdfBytes.Length, attempt + 1);
                                return pdfBytes;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Async Base64 conversion failed: {Message}", ex.Message);
                        }
                    }
                }

                // Final attempt: Use Chrome DevTools to capture PDF as Base64
                if (Driver is ChromeDriver chromeDriver)
                {
                    try
                    {
                        _logger.LogInformation("🔧 Container fallback: Chrome DevTools Base64 capture");
                        
                        // Focus on PDF content first
                        await PreparePageForFullCapture(cancellationToken);
                        
                        var printParams = new Dictionary<string, object>
                        {
                            ["format"] = "A4",
                            ["printBackground"] = true,
                            ["marginTop"] = 0,
                            ["marginBottom"] = 0,
                            ["marginLeft"] = 0,
                            ["marginRight"] = 0,
                            ["preferCSSPageSize"] = true
                        };

                        var cdpResult = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printParams);
                        if (cdpResult is Dictionary<string, object> printResult &&
                            printResult.TryGetValue("data", out var printData) && printData is string printBase64)
                        {
                            var pdfBytes = Convert.FromBase64String(printBase64);
                            if (IsValidPdf(pdfBytes))
                            {
                                _logger.LogInformation("✅ CONTAINER CDP SUCCESS: DevTools captured {Size} bytes", pdfBytes.Length);
                                return pdfBytes;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Chrome DevTools fallback failed: {Message}", ex.Message);
                    }
                }

                _logger.LogWarning("❌ CONTAINER EXTRACTION FAILED: All Base64 methods exhausted");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Container Base64 extraction error: {Message}", ex.Message);
                return null;
            }
        }
    }
}

