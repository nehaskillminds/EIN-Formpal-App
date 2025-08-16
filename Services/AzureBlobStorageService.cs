using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Configuration;

#nullable enable

namespace EinAutomation.Api.Services
{
    public class AzureBlobStorageService : IBlobStorageService
    {
        private readonly ILogger<AzureBlobStorageService> _logger;
        private readonly string _connectionString;
        private readonly string _containerName;

        public AzureBlobStorageService(
            ILogger<AzureBlobStorageService> logger,
            IOptions<EinAutomation.Api.Options.AzureBlobStorageOptions> options,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Try to get connection string from configuration (Key Vault or appsettings)
            _connectionString = configuration["Azure:Blob:ConnectionString"];

            // If not found, try environment variable (e.g., set in Dockerfile)
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                _logger.LogWarning("Azure:Blob:ConnectionString not found in configuration, checking environment variable...");

                _connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

                if (string.IsNullOrWhiteSpace(_connectionString))
                {
                    _logger.LogWarning("AZURE_STORAGE_CONNECTION_STRING not found in environment, using hardcoded fallback.");
                    _connectionString = "";
                }
                else
                {
                    _logger.LogInformation("Using Azure Blob connection string from environment variable.");
                }
            }
            else
            {
                _logger.LogInformation("Using Azure Blob connection string from configuration.");
            }

            // Get container name from configuration (Key Vault or appsettings)
            _containerName = configuration["Azure:Blob:Container"];

            // If not found, try environment variable
            if (string.IsNullOrWhiteSpace(_containerName))
            {
                _logger.LogWarning("Azure:Blob:Container not found in configuration, checking environment variable...");

                _containerName = Environment.GetEnvironmentVariable("AZURE_CONTAINER_NAME");

                if (string.IsNullOrWhiteSpace(_containerName))
                {
                    _logger.LogWarning("AZURE_CONTAINER_NAME not found in environment, using default fallback.");
                    _containerName = "default-container";
                }
                else
                {
                    _logger.LogInformation("Using Azure container name from environment variable.");
                }
            }
            else
            {
                _logger.LogInformation("Using Azure container name from configuration.");
            }

            // Log storage account name for debugging (from configuration)
            var storageAccountName = configuration["Azure:Storage:AccountName"];
            if (!string.IsNullOrWhiteSpace(storageAccountName))
            {
                _logger.LogInformation("Azure Storage Account Name: {StorageAccountName}", storageAccountName);
            }

            _logger.LogInformation("AzureBlobStorageService: ConnectionString={ConnectionString}, Container={Container}", _connectionString, _containerName);
        }

        // For confirmation PDFs - includes AccountId, EntityId, CaseId tags
        public async Task<string> UploadConfirmationPdf(byte[] dataBytes, string blobName, string contentType, string? accountId, string? entityId, string? caseId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Uploading confirmation PDF blob: {BlobName}", blobName);
            if (dataBytes == null)
                throw new ArgumentNullException(nameof(dataBytes));
            if (blobName == null)
                throw new ArgumentNullException(nameof(blobName));
            if (contentType == null)
                throw new ArgumentNullException(nameof(contentType));

            try
            {
                BlobServiceClient blobServiceClient = new(_connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

                // Upload the blob, overwriting if it exists
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(dataBytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                // Set content type separately after upload
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
                {
                    ContentType = contentType
                }, cancellationToken: cancellationToken);

                // Set blob tags for confirmation PDF
                var tags = new Dictionary<string, string>
                {
                    { "HiddenFromClient", "true" },
                    { "AccountId", accountId ?? "" },
                    { "EntityId", entityId ?? "" },
                    { "CaseId", caseId ?? "" }
                };
                
                _logger.LogInformation("🏷️ Attempting to set blob index tags for confirmation PDF: {Tags}", string.Join(", ", tags.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                
                bool tagsSetSuccessfully = false;
                try
                {
                    await blobClient.SetTagsAsync(tags, cancellationToken: cancellationToken);
                    tagsSetSuccessfully = true;
                    _logger.LogInformation("✅ Blob index tags set successfully for confirmation PDF");
                }
                catch (RequestFailedException ex) when (ex.Status == 403)
                {
                    _logger.LogWarning("⚠️ Permission denied when setting blob index tags. The storage account connection string/SAS token needs 'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/write' permission or 't' permission in SAS. Error: {ErrorMessage}", ex.Message);
                    // Continue without tags - the blob upload was successful
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogWarning("⚠️ Failed to set blob index tags (Status: {Status}): {ErrorMessage}", ex.Status, ex.Message);
                    // Continue without tags - the blob upload was successful
                }
                catch (Exception ex)
                {
                    _logger.LogError("❌ Unexpected error when setting blob index tags: {ErrorMessage}", ex.Message);
                    // Continue without tags - the blob upload was successful
                }

                string blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("✅ Confirmation PDF uploaded successfully. Tags {TagStatus}: HiddenFromClient=true, AccountId={AccountId}, EntityId={EntityId}, CaseId={CaseId} - {BlobUrl}", 
                    tagsSetSuccessfully ? "SET" : "NOT SET", accountId ?? "null", entityId ?? "null", caseId ?? "null", blobUrl);
                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure upload failed for confirmation PDF blob {BlobName}", blobName);
                throw;
            }
        }

        // Legacy method for backward compatibility - will be phased out
        public async Task<string> UploadBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Uploading blob: {BlobName}", blobName);
            if (dataBytes == null)
                throw new ArgumentNullException(nameof(dataBytes));
            if (blobName == null)
                throw new ArgumentNullException(nameof(blobName));
            if (contentType == null)
                throw new ArgumentNullException(nameof(contentType));

            try
            {
                BlobServiceClient blobServiceClient = new(_connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

                // Upload the blob, overwriting if it exists
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(dataBytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                // Set content type separately after upload
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
                {
                    ContentType = contentType
                }, cancellationToken: cancellationToken);

                // Set blob tags
                var tags = new Dictionary<string, string>
                {
                    { "HiddenFromClient", "true" }
                };
                
                _logger.LogInformation("🏷️ Attempting to set blob index tags for legacy upload: {Tags}", string.Join(", ", tags.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                
                bool tagsSetSuccessfully = false;
                try
                {
                    await blobClient.SetTagsAsync(tags, cancellationToken: cancellationToken);
                    tagsSetSuccessfully = true;
                    _logger.LogInformation("✅ Blob index tags set successfully for legacy upload");
                }
                catch (RequestFailedException ex) when (ex.Status == 403)
                {
                    _logger.LogWarning("⚠️ Permission denied when setting blob index tags. The storage account connection string/SAS token needs 'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/write' permission or 't' permission in SAS. Error: {ErrorMessage}", ex.Message);
                    // Continue without tags - the blob upload was successful
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogWarning("⚠️ Failed to set blob index tags (Status: {Status}): {ErrorMessage}", ex.Status, ex.Message);
                    // Continue without tags - the blob upload was successful
                }
                catch (Exception ex)
                {
                    _logger.LogError("❌ Unexpected error when setting blob index tags: {ErrorMessage}", ex.Message);
                    // Continue without tags - the blob upload was successful
                }

                string blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("✅ Legacy upload to Azure Blob Storage completed. Tags {TagStatus}: HiddenFromClient=true - {BlobUrl}", 
                    tagsSetSuccessfully ? "SET" : "NOT SET", blobUrl);
                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure upload failed for blob {BlobName}", blobName);
                throw;
            }
        }

        // For EIN Letter PDFs - includes AccountId, EntityId, CaseId tags with HiddenFromClient=false
        public async Task<string> UploadEinLetterPdf(byte[] dataBytes, string blobName, string contentType, string? accountId, string? entityId, string? caseId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Uploading EIN Letter PDF blob: {BlobName}", blobName);
            if (dataBytes == null)
                throw new ArgumentNullException(nameof(dataBytes));
            if (blobName == null)
                throw new ArgumentNullException(nameof(blobName));
            if (contentType == null)
                throw new ArgumentNullException(nameof(contentType));

            try
            {
                BlobServiceClient blobServiceClient = new(_connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

                // Upload the blob, overwriting if it exists
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(dataBytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                // Set content type separately after upload
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
                {
                    ContentType = contentType
                }, cancellationToken: cancellationToken);

                // Set blob tags for EIN Letter PDF - HiddenFromClient = false for client visibility
                var tags = new Dictionary<string, string>
                {
                    { "HiddenFromClient", "false" },
                    { "AccountId", accountId ?? "" },
                    { "EntityId", entityId ?? "" },
                    { "CaseId", caseId ?? "" }
                };
                
                _logger.LogInformation("🏷️ Attempting to set blob index tags for EIN Letter PDF: {Tags}", string.Join(", ", tags.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                
                bool tagsSetSuccessfully = false;
                try
                {
                    await blobClient.SetTagsAsync(tags, cancellationToken: cancellationToken);
                    tagsSetSuccessfully = true;
                    _logger.LogInformation("✅ Blob index tags set successfully for EIN Letter PDF");
                }
                catch (RequestFailedException ex) when (ex.Status == 403)
                {
                    _logger.LogWarning("⚠️ Permission denied when setting blob index tags. The storage account connection string/SAS token needs 'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/write' permission or 't' permission in SAS. Error: {ErrorMessage}", ex.Message);
                    // Continue without tags - the blob upload was successful
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogWarning("⚠️ Failed to set blob index tags (Status: {Status}): {ErrorMessage}", ex.Status, ex.Message);
                    // Continue without tags - the blob upload was successful
                }
                catch (Exception ex)
                {
                    _logger.LogError("❌ Unexpected error when setting blob index tags: {ErrorMessage}", ex.Message);
                    // Continue without tags - the blob upload was successful
                }

                string blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("✅ EIN Letter PDF uploaded successfully. Tags {TagStatus}: HiddenFromClient=false, AccountId={AccountId}, EntityId={EntityId}, CaseId={CaseId} - {BlobUrl}", 
                    tagsSetSuccessfully ? "SET" : "NOT SET", accountId ?? "null", entityId ?? "null", caseId ?? "null", blobUrl);
                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure upload failed for EIN Letter PDF blob {BlobName}", blobName);
                throw;
            }
        }

        // Legacy method for backward compatibility - will be phased out
        public async Task<string> UploadFinalBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Uploading blob: {BlobName}", blobName);
            if (dataBytes == null)
                throw new ArgumentNullException(nameof(dataBytes));
            if (blobName == null)
                throw new ArgumentNullException(nameof(blobName));
            if (contentType == null)
                throw new ArgumentNullException(nameof(contentType));

            try
            {
                BlobServiceClient blobServiceClient = new(_connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

                // Upload the blob, overwriting if it exists
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(dataBytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                // Set content type separately after upload
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
                {
                    ContentType = contentType
                }, cancellationToken: cancellationToken);

                
                string blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("Uploaded to Azure Blob Storage with tags: {BlobUrl}", blobUrl);
                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure upload failed for blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task<string?> UploadLogToBlob(string? recordId, string? logFilePath)
        {
            string blobName = $"logs/{recordId}/chromedriver_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.log";
            _logger.LogInformation("Uploading blob: {BlobName}", blobName);
            if (string.IsNullOrEmpty(recordId) || string.IsNullOrEmpty(logFilePath))
            {
                _logger.LogWarning("Invalid input parameters for UploadLogToBlob: recordId or logFilePath is null or empty");
                return null;
            }

            try
            {
                if (!File.Exists(logFilePath))
                {
                    _logger.LogWarning("No log file found at {LogFilePath}", logFilePath);
                    return null;
                }

                byte[] logData = await File.ReadAllBytesAsync(logFilePath);
                return await UploadBytesToBlob(logData, blobName, "text/plain");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload logs to Azure Blob for record ID {RecordId} from {LogFilePath}", recordId, logFilePath);
                return null;
            }
        }

// Consolidated JSON upload method - replaces both UploadJsonAsync and SaveJsonDataSync
public async Task<bool> UploadJsonData(Dictionary<string, object> data, CaseData? caseData = null, CancellationToken cancellationToken = default)
{
    if (data == null)
        throw new ArgumentNullException(nameof(data));
    if (!data.ContainsKey("record_id"))
    {
        _logger.LogWarning("Invalid input parameters for UploadJsonData: data does not contain record_id");
        return false;
    }

    try
    {
        string legalName = data.ContainsKey("entity_name") ? data["entity_name"]?.ToString() ?? "UnknownEntity" : "UnknownEntity";
        string cleanName = Regex.Replace(legalName, @"[^\w]", "");
        var blobName = $"EntityProcess/{data["record_id"]}/{cleanName}-ID-JsonPayload.json";
        
        _logger.LogInformation("Uploading JSON blob: {BlobName}", blobName);
        
        string jsonData = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        byte[] dataBytes = Encoding.UTF8.GetBytes(jsonData);
        
        BlobServiceClient blobServiceClient = new(_connectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
        BlobClient blobClient = containerClient.GetBlobClient(blobName);
        
        // Try upload first, create container only if needed
        await UploadWithContainerCreation(blobClient, new MemoryStream(dataBytes), containerClient, cancellationToken);
        
        // Set content type separately after upload
        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
        {
            ContentType = "application/json"
        }, cancellationToken: cancellationToken);
        
        // Set blob tags - JSON always has HiddenFromClient = true
        var tags = new Dictionary<string, string>
        {
            { "HiddenFromClient", "true" }
        };
        
        _logger.LogInformation("🏷️ Attempting to set blob index tags for JSON data: {Tags}", string.Join(", ", tags.Select(kvp => $"{kvp.Key}={kvp.Value}")));
        
        bool tagsSetSuccessfully = false;
        try
        {
            await blobClient.SetTagsAsync(tags, cancellationToken: cancellationToken);
            tagsSetSuccessfully = true;
            _logger.LogInformation("✅ Blob index tags set successfully for JSON data");
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogWarning("⚠️ Permission denied when setting blob index tags. The storage account connection string/SAS token needs 'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/write' permission or 't' permission in SAS. Error: {ErrorMessage}", ex.Message);
            // Continue without tags - the blob upload was successful
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning("⚠️ Failed to set blob index tags (Status: {Status}): {ErrorMessage}", ex.Status, ex.Message);
            // Continue without tags - the blob upload was successful
        }
        catch (Exception ex)
        {
            _logger.LogError("❌ Unexpected error when setting blob index tags: {ErrorMessage}", ex.Message);
            // Continue without tags - the blob upload was successful
        }
        
        string blobUrl = blobClient.Uri.AbsoluteUri;
        _logger.LogInformation("✅ JSON data uploaded successfully. Tags {TagStatus}: HiddenFromClient=true - {BlobUrl}", 
            tagsSetSuccessfully ? "SET" : "NOT SET", blobUrl);
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload JSON data to Azure Blob");
        return false;
    }
}

public async Task<string> UploadAsync(byte[] bytes, string blobName, string contentType, bool overwrite = true, CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Uploading blob: {BlobName}", blobName);
    if (bytes == null)
        throw new ArgumentNullException(nameof(bytes));
    if (blobName == null)
        throw new ArgumentNullException(nameof(blobName));
    if (contentType == null)
        throw new ArgumentNullException(nameof(contentType));
    try
    {
        BlobServiceClient blobServiceClient = new(_connectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
        BlobClient blobClient = containerClient.GetBlobClient(blobName);
        // Try upload first, create container only if needed
        await UploadWithContainerCreation(blobClient, new MemoryStream(bytes), containerClient, cancellationToken, overwrite);
        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
        _logger.LogInformation("Uploaded blob '{BlobName}' with content type '{ContentType}'", blobName, contentType);
        return blobClient.Uri.AbsoluteUri;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "UploadAsync failed for blob {BlobName}", blobName);
        throw;
    }
}
// Helper method to handle upload with container creation
private async Task UploadWithContainerCreation(BlobClient blobClient, MemoryStream stream, BlobContainerClient containerClient, CancellationToken cancellationToken, bool overwrite = true)
{
    try
    {
        await blobClient.UploadAsync(stream, overwrite: overwrite, cancellationToken: cancellationToken);
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        // Container doesn't exist, try to create it once
        _logger.LogInformation("Container does not exist. Creating container: {ContainerName}", containerClient.Name);
        try
        {
            await containerClient.CreateAsync(PublicAccessType.None, cancellationToken: CancellationToken.None);
        }
        catch (RequestFailedException createEx) when (createEx.Status == 409)
        {
            // Container already exists (race condition), ignore
            _logger.LogDebug("Container already exists (race condition)");
        }
        // Reset stream position and retry the upload
        stream.Position = 0;
        await blobClient.UploadAsync(stream, overwrite: overwrite, cancellationToken: cancellationToken);
    }
    finally
    {
        stream?.Dispose();
    }
}
    }
}



