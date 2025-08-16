using EinAutomation.Api.Services.Interfaces;
using EinAutomation.Api.TestClasses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EinAutomation.Api.Controllers
{
    /// <summary>
    /// Test controller for uploading files to Azure Blob Storage to verify index tags
    /// </summary>
    [ApiController]
    [Route("api/test/[controller]")]
    public class BlobStorageTestController : ControllerBase
    {
        private readonly IBlobStorageService _blobStorageService;
        private readonly ILogger<BlobStorageTestController> _logger;

        public BlobStorageTestController(
            IBlobStorageService blobStorageService,
            ILogger<BlobStorageTestController> logger)
        {
            _blobStorageService = blobStorageService;
            _logger = logger;
        }

        /// <summary>
        /// Upload a confirmation PDF for testing blob index tags
        /// </summary>
        [HttpPost("upload-confirmation-pdf")]
        public async Task<IActionResult> UploadConfirmationPdf([FromForm] ConfirmationPdfTestRequest request)
        {
            try
            {
                if (request.PdfFile == null || request.PdfFile.Length == 0)
                {
                    return BadRequest("PDF file is required");
                }

                if (string.IsNullOrEmpty(request.RecordId) || string.IsNullOrEmpty(request.EntityName))
                {
                    return BadRequest("RecordId and EntityName are required");
                }

                // Save uploaded file temporarily
                var tempFilePath = Path.GetTempFileName();
                try
                {
                    using (var stream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        await request.PdfFile.CopyToAsync(stream);
                    }

                    var loggerFactory = HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                    var uploaderLogger = loggerFactory.CreateLogger<BlobStorageTestUploader>();
                    var testUploader = new BlobStorageTestUploader(_blobStorageService, uploaderLogger);
                    var blobUrl = await testUploader.UploadTestConfirmationPdf(
                        tempFilePath,
                        request.RecordId,
                        request.EntityName,
                        request.AccountId,
                        request.EntityId,
                        request.CaseId);

                    return Ok(new
                    {
                        Success = true,
                        Message = "Confirmation PDF uploaded successfully",
                        BlobUrl = blobUrl,
                        ExpectedTags = new
                        {
                            HiddenFromClient = "true",
                            AccountId = request.AccountId ?? "(empty)",
                            EntityId = request.EntityId ?? "(empty)",
                            CaseId = request.CaseId ?? "(empty)"
                        },
                        Instructions = "Check Azure Storage Explorer to verify the blob index tags"
                    });
                }
                finally
                {
                    if (System.IO.File.Exists(tempFilePath))
                    {
                        System.IO.File.Delete(tempFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload confirmation PDF test");
                return StatusCode(500, $"Upload failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Upload an EIN Letter PDF for testing blob index tags
        /// </summary>
        [HttpPost("upload-ein-letter-pdf")]
        public async Task<IActionResult> UploadEinLetterPdf([FromForm] EinLetterPdfTestRequest request)
        {
            try
            {
                if (request.PdfFile == null || request.PdfFile.Length == 0)
                {
                    return BadRequest("PDF file is required");
                }

                if (string.IsNullOrEmpty(request.RecordId) || string.IsNullOrEmpty(request.EntityName))
                {
                    return BadRequest("RecordId and EntityName are required");
                }

                // Save uploaded file temporarily
                var tempFilePath = Path.GetTempFileName();
                try
                {
                    using (var stream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        await request.PdfFile.CopyToAsync(stream);
                    }

                    var loggerFactory = HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                    var uploaderLogger = loggerFactory.CreateLogger<BlobStorageTestUploader>();
                    var testUploader = new BlobStorageTestUploader(_blobStorageService, uploaderLogger);
                    var blobUrl = await testUploader.UploadTestEinLetterPdf(
                        tempFilePath,
                        request.RecordId,
                        request.EntityName,
                        request.AccountId,
                        request.EntityId,
                        request.CaseId);

                    return Ok(new
                    {
                        Success = true,
                        Message = "EIN Letter PDF uploaded successfully",
                        BlobUrl = blobUrl,
                        ExpectedTags = new
                        {
                            HiddenFromClient = "false",
                            AccountId = request.AccountId ?? "(empty)",
                            EntityId = request.EntityId ?? "(empty)",
                            CaseId = request.CaseId ?? "(empty)"
                        },
                        Instructions = "Check Azure Storage Explorer to verify the blob index tags"
                    });
                }
                finally
                {
                    if (System.IO.File.Exists(tempFilePath))
                    {
                        System.IO.File.Delete(tempFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload EIN Letter PDF test");
                return StatusCode(500, $"Upload failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Upload a JSON payload for testing blob index tags
        /// </summary>
        [HttpPost("upload-json-payload")]
        public async Task<IActionResult> UploadJsonPayload([FromBody] JsonPayloadTestRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.RecordId) || string.IsNullOrEmpty(request.EntityName))
                {
                    return BadRequest("RecordId and EntityName are required");
                }

                var loggerFactory = HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                var uploaderLogger = loggerFactory.CreateLogger<BlobStorageTestUploader>();
                var testUploader = new BlobStorageTestUploader(_blobStorageService, uploaderLogger);
                
                Dictionary<string, object> jsonData;
                if (request.CustomJsonData != null && request.CustomJsonData.Count > 0)
                {
                    jsonData = request.CustomJsonData;
                    // Ensure required fields are present
                    jsonData["record_id"] = request.RecordId;
                    jsonData["entity_name"] = request.EntityName;
                }
                else
                {
                    // Use sample data
                    jsonData = testUploader.CreateSampleJsonPayload(
                        request.RecordId,
                        request.EntityName,
                        request.AccountId,
                        request.EntityId,
                        request.CaseId);
                }

                var success = await testUploader.UploadTestJsonPayload(jsonData, request.RecordId, request.EntityName);

                if (success)
                {
                    var cleanName = System.Text.RegularExpressions.Regex.Replace(request.EntityName, @"[^\w]", "");
                    var expectedBlobName = $"EntityProcess/{request.RecordId}/{cleanName}-ID-JsonPayload.json";

                    return Ok(new
                    {
                        Success = true,
                        Message = "JSON payload uploaded successfully",
                        ExpectedBlobName = expectedBlobName,
                        ExpectedTags = new
                        {
                            HiddenFromClient = "true"
                        },
                        JsonDataSample = jsonData,
                        Instructions = "Check Azure Storage Explorer to verify the blob index tags and JSON content"
                    });
                }
                else
                {
                    return StatusCode(500, "Failed to upload JSON payload");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload JSON payload test");
                return StatusCode(500, $"Upload failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get sample request formats for testing
        /// </summary>
        [HttpGet("sample-requests")]
        public IActionResult GetSampleRequests()
        {
            return Ok(new
            {
                ConfirmationPdfRequest = new
                {
                    Endpoint = "POST /api/test/BlobStorageTest/upload-confirmation-pdf",
                    ContentType = "multipart/form-data",
                    Fields = new
                    {
                        PdfFile = "(file) - PDF file to upload",
                        RecordId = "(string, required) - e.g., 'a2GUS00000Aw1w92AD'",
                        EntityName = "(string, required) - e.g., 'Orchid Wellbeing LLC'",
                        AccountId = "(string, optional) - e.g., '0013h00002CJUvkAAH'",
                        EntityId = "(string, optional) - e.g., 'a0EUS0000026Bnp2AF'",
                        CaseId = "(string, optional) - e.g., '500US0000ZVLgYAP'"
                    }
                },
                EinLetterPdfRequest = new
                {
                    Endpoint = "POST /api/test/BlobStorageTest/upload-ein-letter-pdf",
                    ContentType = "multipart/form-data",
                    Fields = new
                    {
                        PdfFile = "(file) - PDF file to upload",
                        RecordId = "(string, required) - e.g., 'a2GUS00000Aw1w92AD'",
                        EntityName = "(string, required) - e.g., 'Orchid Wellbeing LLC'",
                        AccountId = "(string, optional) - e.g., '0013h00002CJUvkAAH'",
                        EntityId = "(string, optional) - e.g., 'a0EUS0000026Bnp2AF'",
                        CaseId = "(string, optional) - e.g., '500US0000ZVLgYAP'"
                    }
                },
                JsonPayloadRequest = new
                {
                    Endpoint = "POST /api/test/BlobStorageTest/upload-json-payload",
                    ContentType = "application/json",
                    Body = new
                    {
                        RecordId = "(string, required) - e.g., 'a2GUS00000Aw1w92AD'",
                        EntityName = "(string, required) - e.g., 'Orchid Wellbeing LLC'",
                        AccountId = "(string, optional) - e.g., '0013h00002CJUvkAAH'",
                        EntityId = "(string, optional) - e.g., 'a0EUS0000026Bnp2AF'",
                        CaseId = "(string, optional) - e.g., '500US0000ZVLgYAP'",
                        CustomJsonData = "(object, optional) - Custom JSON data, or leave null for sample data"
                    }
                }
            });
        }
    }

    // Request models
    public class ConfirmationPdfTestRequest
    {
        public IFormFile PdfFile { get; set; } = null!;
        public string RecordId { get; set; } = null!;
        public string EntityName { get; set; } = null!;
        public string? AccountId { get; set; }
        public string? EntityId { get; set; }
        public string? CaseId { get; set; }
    }

    public class EinLetterPdfTestRequest
    {
        public IFormFile PdfFile { get; set; } = null!;
        public string RecordId { get; set; } = null!;
        public string EntityName { get; set; } = null!;
        public string? AccountId { get; set; }
        public string? EntityId { get; set; }
        public string? CaseId { get; set; }
    }

    public class JsonPayloadTestRequest
    {
        public string RecordId { get; set; } = null!;
        public string EntityName { get; set; } = null!;
        public string? AccountId { get; set; }
        public string? EntityId { get; set; }
        public string? CaseId { get; set; }
        public Dictionary<string, object>? CustomJsonData { get; set; }
    }
}
