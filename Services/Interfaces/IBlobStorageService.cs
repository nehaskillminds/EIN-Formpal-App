using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EinAutomation.Api.Models;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface IBlobStorageService
    {
        /// <summary>
        /// Uploads confirmation PDF with specific tags (HiddenFromClient=true, AccountId, EntityId, CaseId)
        /// </summary>
        /// <param name="dataBytes">The PDF data to upload</param>
        /// <param name="blobName">The blob name/path</param>
        /// <param name="contentType">The content type</param>
        /// <param name="accountId">Account ID from payload</param>
        /// <param name="entityId">Entity ID from payload</param>
        /// <param name="caseId">Case ID from payload</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The URL of the uploaded blob</returns>
        Task<string> UploadConfirmationPdf(byte[] dataBytes, string blobName, string contentType, string? accountId, string? entityId, string? caseId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads EIN Letter PDF with specific tags (HiddenFromClient=false, AccountId, EntityId, CaseId)
        /// </summary>
        /// <param name="dataBytes">The PDF data to upload</param>
        /// <param name="blobName">The blob name/path</param>
        /// <param name="contentType">The content type</param>
        /// <param name="accountId">Account ID from payload</param>
        /// <param name="entityId">Entity ID from payload</param>
        /// <param name="caseId">Case ID from payload</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The URL of the uploaded blob</returns>
        Task<string> UploadEinLetterPdf(byte[] dataBytes, string blobName, string contentType, string? accountId, string? entityId, string? caseId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Consolidated JSON upload method with standard naming and tags
        /// </summary>
        /// <param name="data">The data to save as dictionary</param>
        /// <param name="caseData">Optional case data for additional context</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if successful</returns>
        Task<bool> UploadJsonData(Dictionary<string, object> data, CaseData? caseData = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads log files to Azure Blob Storage
        /// </summary>
        /// <param name="recordId">The associated record ID</param>
        /// <param name="logFilePath">Path to the log file</param>
        /// <returns>The URL of the uploaded log file or null if failed</returns>
        Task<string?> UploadLogToBlob(string? recordId, string? logFilePath);

        // Legacy methods - kept for backward compatibility but will be phased out
        Task<string> UploadBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default);
        Task<string> UploadFinalBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default);
        Task<string> UploadAsync(byte[] bytes, string blobName, string contentType, bool overwrite = true, CancellationToken cancellationToken = default);
    }
}