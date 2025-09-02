
using OpenQA.Selenium;
using EinAutomation.Api.Models;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface IEinFormFiller
    {
        // Sync interaction methods
        bool FillField(By locator, string value, string label = "field");
        bool ClickButton(By locator, string description = "button", int retries = 3);
        bool ClickButtonByAriaLabel(string ariaLabel, string description = "button", int retries = 3);

        bool SelectRadio(string? radioId, string description = "radio", int? timeoutSeconds = null, int maxRetries = 3);
        void ClearAndFill(By locator, string value, string description);
        void ScrollToElement(By locator, string description = "element", bool center = true);
        void ScrollToBottom(int additionalOffset = 0, int maxAttempts = 0);
        void Cleanup();

        // Async additions required by AutomationOrchestrator
        // Task<(bool Success, string? EinNumber, byte[]? ConfirmationPdfBytes, string? BlobName)> FillAsync(CaseData? data, CancellationToken ct);
        // Task<(bool Success, string? EinNumber, byte[]? EinLetterPdfBytes, string? BlobName)> FinalSubmitAsync(CaseData? data, CancellationToken ct);
        // Task<byte[]?> CaptureFailurePageAsync(CaseData? data, CancellationToken ct);
        Task<byte[]?> GetBrowserLogsAsync(CaseData? data, CancellationToken ct);
        Task CleanupAsync(CancellationToken ct);

        // PDF and logging
        Task<(string? BlobUrl, bool Success)> CapturePageAsPdf(CaseData? data, CancellationToken ct);
        Task<(string? BlobUrl, bool Success)> CaptureSuccessPageAsPdf(CaseData? data, CancellationToken ct);
        Task<(string? BlobUrl, bool Success)> CaptureFailurePageAsPdf(CaseData? data, CancellationToken ct);
        void CaptureBrowserLogs();
        void LogSystemResources(string? referenceId = null, CancellationToken cancellationToken = default);

        // EIN flow
        Task NavigateAndFillForm(CaseData? data, Dictionary<string, object?> jsonData);

        Task HandleTrusteeshipEntity(CaseData? data);
        Task<(bool Success, string? Message, string? AzureBlobUrl)> RunAutomation(CaseData? data, Dictionary<string, object> jsonData);
        // Task<(string? EinNumber, string? PdfAzureUrl, bool Success)> FinalSubmit(CaseData? data, Dictionary<string, object>? jsonData, CancellationToken cancellationToken);
        // Utilities
        string NormalizeState(string? state);
        (string? Month, int Year) ParseFormationDate(string? dateStr);
        Dictionary<string, object?> GetDefaults(CaseData? data);

        // Browser
        Task InitializeDriver(bool useProxy, string? proxyHost, int? proxyPort, string? proxyUsername, string? proxyPassword, string recordId);
    }
}
