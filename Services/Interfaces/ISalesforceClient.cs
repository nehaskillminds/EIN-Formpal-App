using System.Threading.Tasks;
using OpenQA.Selenium;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface ISalesforceClient
    {
        Task<bool> InitializeSalesforceAuthAsync();
        Task<bool> NotifySalesforceSuccessAsync(string? entityProcessId, string? einNumber);
        Task<bool> NotifySalesforceEINLetterFailureAsync(string? entityProcessId, string? einNumber);
        Task<bool> NotifySalesforceErrorCodeAsync(string? entityProcessId, string? errorCode, string? status = "fail", IWebDriver? driver = null, string? htmlContent = null);
        Task<bool> NotifyScreenshotUploadToSalesforceAsync(string? entityProcessId, string? blobUrl, string? entityName, string? accountId = null, string? entityId = null, string? caseId = null);
        Task<bool> NotifyFailureScreenshotUploadToSalesforceAsync(string? entityProcessId, string? blobUrl, string? entityName, string? accountId = null, string? entityId = null, string? caseId = null);

        Task<bool> NotifyEinLetterToSalesforceAsync(string? entityProcessId, string? blobUrl, string? entityName, string? accountId = null, string? entityId = null, string? caseId = null);
        
        bool IsAuthenticated { get; }
        string? InstanceUrl { get; }
    }
}