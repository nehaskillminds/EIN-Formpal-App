using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EinAutomation.Api.TestClasses
{
    /// <summary>
    /// Test class for uploading different types of files to Azure Blob Storage
    /// to verify blob index tags are set correctly
    /// </summary>
    public class BlobStorageTestUploader
    {
        private readonly IBlobStorageService _blobStorageService;
        private readonly ILogger<BlobStorageTestUploader> _logger;

        public BlobStorageTestUploader(IBlobStorageService blobStorageService, ILogger<BlobStorageTestUploader> logger)
        {
            _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Upload a confirmation PDF with test parameters
        /// </summary>
        public async Task<string> UploadTestConfirmationPdf(
            string filePath,
            string recordId,
            string entityName,
            string? accountId = null,
            string? entityId = null,
            string? caseId = null)
        {
            try
            {
                _logger.LogInformation("=== UPLOADING TEST CONFIRMATION PDF ===");
                _logger.LogInformation("File Path: {FilePath}", filePath);
                _logger.LogInformation("Record ID: {RecordId}", recordId);
                _logger.LogInformation("Entity Name: {EntityName}", entityName);
                _logger.LogInformation("Account ID: {AccountId}", accountId ?? "null");
                _logger.LogInformation("Entity ID: {EntityId}", entityId ?? "null");
                _logger.LogInformation("Case ID: {CaseId}", caseId ?? "null");

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"PDF file not found at: {filePath}");
                }

                var pdfBytes = await File.ReadAllBytesAsync(filePath);
                _logger.LogInformation("PDF file size: {Size} bytes", pdfBytes.Length);

                // Clean entity name for blob naming
                var cleanName = System.Text.RegularExpressions.Regex.Replace(entityName, @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{recordId}/{cleanName}-ID-ConfirmationTest.pdf";

                _logger.LogInformation("Generated blob name: {BlobName}", blobName);
                _logger.LogInformation("Expected tags: HiddenFromClient=true, AccountId={AccountId}, EntityId={EntityId}, CaseId={CaseId}",
                    accountId ?? "empty", entityId ?? "empty", caseId ?? "empty");

                var blobUrl = await _blobStorageService.UploadConfirmationPdf(
                    pdfBytes,
                    blobName,
                    "application/pdf",
                    accountId,
                    entityId,
                    caseId);

                _logger.LogInformation("✅ TEST CONFIRMATION PDF UPLOADED SUCCESSFULLY!");
                _logger.LogInformation("Blob URL: {BlobUrl}", blobUrl);
                _logger.LogInformation("=== VERIFICATION INSTRUCTIONS ===");
                _logger.LogInformation("1. Go to Azure Storage Explorer or Azure Portal");
                _logger.LogInformation("2. Navigate to blob: {BlobName}", blobName);
                _logger.LogInformation("3. Check blob index tags - should have:");
                _logger.LogInformation("   - HiddenFromClient: true");
                _logger.LogInformation("   - AccountId: {AccountId}", accountId ?? "(empty)");
                _logger.LogInformation("   - EntityId: {EntityId}", entityId ?? "(empty)");
                _logger.LogInformation("   - CaseId: {CaseId}", caseId ?? "(empty)");

                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to upload test confirmation PDF");
                throw;
            }
        }

        /// <summary>
        /// Upload an EIN Letter PDF with test parameters
        /// </summary>
        public async Task<string> UploadTestEinLetterPdf(
            string filePath,
            string recordId,
            string entityName,
            string? accountId = null,
            string? entityId = null,
            string? caseId = null)
        {
            try
            {
                _logger.LogInformation("=== UPLOADING TEST EIN LETTER PDF ===");
                _logger.LogInformation("File Path: {FilePath}", filePath);
                _logger.LogInformation("Record ID: {RecordId}", recordId);
                _logger.LogInformation("Entity Name: {EntityName}", entityName);
                _logger.LogInformation("Account ID: {AccountId}", accountId ?? "null");
                _logger.LogInformation("Entity ID: {EntityId}", entityId ?? "null");
                _logger.LogInformation("Case ID: {CaseId}", caseId ?? "null");

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"PDF file not found at: {filePath}");
                }

                var pdfBytes = await File.ReadAllBytesAsync(filePath);
                _logger.LogInformation("PDF file size: {Size} bytes", pdfBytes.Length);

                // Clean entity name for blob naming
                var cleanName = System.Text.RegularExpressions.Regex.Replace(entityName, @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{recordId}/{cleanName}-ID-EINLetter-Test.pdf";

                _logger.LogInformation("Generated blob name: {BlobName}", blobName);
                _logger.LogInformation("Expected tags: HiddenFromClient=false, AccountId={AccountId}, EntityId={EntityId}, CaseId={CaseId}",
                    accountId ?? "empty", entityId ?? "empty", caseId ?? "empty");

                var blobUrl = await _blobStorageService.UploadEinLetterPdf(
                    pdfBytes,
                    blobName,
                    "application/pdf",
                    accountId,
                    entityId,
                    caseId);

                _logger.LogInformation("✅ TEST EIN LETTER PDF UPLOADED SUCCESSFULLY!");
                _logger.LogInformation("Blob URL: {BlobUrl}", blobUrl);
                _logger.LogInformation("=== VERIFICATION INSTRUCTIONS ===");
                _logger.LogInformation("1. Go to Azure Storage Explorer or Azure Portal");
                _logger.LogInformation("2. Navigate to blob: {BlobName}", blobName);
                _logger.LogInformation("3. Check blob index tags - should have:");
                _logger.LogInformation("   - HiddenFromClient: false");
                _logger.LogInformation("   - AccountId: {AccountId}", accountId ?? "(empty)");
                _logger.LogInformation("   - EntityId: {EntityId}", entityId ?? "(empty)");
                _logger.LogInformation("   - CaseId: {CaseId}", caseId ?? "(empty)");

                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to upload test EIN Letter PDF");
                throw;
            }
        }

        /// <summary>
        /// Upload a test JSON payload
        /// </summary>
        public async Task<bool> UploadTestJsonPayload(
            Dictionary<string, object> jsonData,
            string recordId,
            string entityName)
        {
            try
            {
                _logger.LogInformation("=== UPLOADING TEST JSON PAYLOAD ===");
                _logger.LogInformation("Record ID: {RecordId}", recordId);
                _logger.LogInformation("Entity Name: {EntityName}", entityName);

                // Ensure record_id is in the data
                if (!jsonData.ContainsKey("record_id"))
                {
                    jsonData["record_id"] = recordId;
                }

                // Ensure entity_name is in the data
                if (!jsonData.ContainsKey("entity_name"))
                {
                    jsonData["entity_name"] = entityName;
                }

                _logger.LogInformation("JSON payload contains {Count} fields", jsonData.Count);
                _logger.LogInformation("Sample JSON data: {Json}", JsonSerializer.Serialize(jsonData, new JsonSerializerOptions { WriteIndented = true }));

                // Clean entity name for blob naming
                var cleanName = System.Text.RegularExpressions.Regex.Replace(entityName, @"[^\w]", "");
                var expectedBlobName = $"EntityProcess/{recordId}/{cleanName}-ID-JsonPayload.json";

                _logger.LogInformation("Expected blob name: {BlobName}", expectedBlobName);
                _logger.LogInformation("Expected tags: HiddenFromClient=true");

                var result = await _blobStorageService.UploadJsonData(jsonData, null);

                if (result)
                {
                    _logger.LogInformation("✅ TEST JSON PAYLOAD UPLOADED SUCCESSFULLY!");
                    _logger.LogInformation("=== VERIFICATION INSTRUCTIONS ===");
                    _logger.LogInformation("1. Go to Azure Storage Explorer or Azure Portal");
                    _logger.LogInformation("2. Navigate to blob: {BlobName}", expectedBlobName);
                    _logger.LogInformation("3. Check blob index tags - should have:");
                    _logger.LogInformation("   - HiddenFromClient: true");
                    _logger.LogInformation("4. Check blob content to verify JSON structure");
                }
                else
                {
                    _logger.LogError("❌ Failed to upload test JSON payload");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to upload test JSON payload");
                throw;
            }
        }

        /// <summary>
        /// Create a sample JSON payload for testing
        /// </summary>
        public Dictionary<string, object> CreateSampleJsonPayload(
            string recordId,
            string entityName,
            string? accountId = null,
            string? entityId = null,
            string? caseId = null)
        {
            return new Dictionary<string, object>
            {
                { "record_id", recordId },
                { "entity_name", entityName },
                { "form_type", "EIN" },
                { "entity_type", "Limited Liability Company (LLC)" },
                { "formation_date", "2025-08-07T00:00:00" },
                { "business_category", "OTHER" },
                { "business_description", "Career Coaching and Team Effectiveness" },
                { "filing_state", "Delaware" },
                { "business_address_1", "1000 Longboat Key Unit 1104, Longboat Key, FL 34228" },
                { "entity_state", "Florida" },
                { "city", "Longboat Key" },
                { "zip_code", "34228" },
                { "ssn_decrypted", "148-74-1133" },
                { "proceed_flag", "true" },
                { "county", "Sarasota" },
                { "closing_month", "12" },
                { "account_id", accountId ?? "" },
                { "entity_id", entityId ?? "" },
                { "case_id", caseId ?? "" },
                { "test_timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "test_purpose", "Blob index tag verification" }
            };
        }

        /// <summary>
        /// Upload all test files in sequence
        /// </summary>
        public async Task UploadAllTestFiles(
            string confirmationPdfPath,
            string einLetterPdfPath,
            string recordId,
            string entityName,
            string? accountId = null,
            string? entityId = null,
            string? caseId = null)
        {
            _logger.LogInformation("=== STARTING COMPREHENSIVE BLOB STORAGE TEST ===");
            _logger.LogInformation("Test Parameters:");
            _logger.LogInformation("- Record ID: {RecordId}", recordId);
            _logger.LogInformation("- Entity Name: {EntityName}", entityName);
            _logger.LogInformation("- Account ID: {AccountId}", accountId ?? "null");
            _logger.LogInformation("- Entity ID: {EntityId}", entityId ?? "null");
            _logger.LogInformation("- Case ID: {CaseId}", caseId ?? "null");
            _logger.LogInformation("- Confirmation PDF: {ConfirmationPdf}", confirmationPdfPath);
            _logger.LogInformation("- EIN Letter PDF: {EinLetterPdf}", einLetterPdfPath);

            try
            {
                // 1. Upload JSON payload
                _logger.LogInformation("\n--- TEST 1: JSON PAYLOAD ---");
                var jsonData = CreateSampleJsonPayload(recordId, entityName, accountId, entityId, caseId);
                await UploadTestJsonPayload(jsonData, recordId, entityName);

                // 2. Upload Confirmation PDF
                _logger.LogInformation("\n--- TEST 2: CONFIRMATION PDF ---");
                await UploadTestConfirmationPdf(confirmationPdfPath, recordId, entityName, accountId, entityId, caseId);

                // 3. Upload EIN Letter PDF
                _logger.LogInformation("\n--- TEST 3: EIN LETTER PDF ---");
                await UploadTestEinLetterPdf(einLetterPdfPath, recordId, entityName, accountId, entityId, caseId);

                _logger.LogInformation("\n=== ALL TESTS COMPLETED SUCCESSFULLY! ===");
                _logger.LogInformation("Please verify the blob index tags in Azure Storage Explorer");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Test sequence failed");
                throw;
            }
        }
    }
}



