using EinAutomation.Api.Services.Interfaces;
using EinAutomation.Api.TestClasses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace EinAutomation.Api.Controllers
{
    /// <summary>
    /// Example of how to add test endpoints to your existing EinController
    /// You can copy these methods to your EinController.cs file
    /// 
    /// IMPORTANT: This is just an example - copy the methods below to your actual EinController.cs
    /// Do NOT use this class directly as it will cause compilation errors.
    /// </summary>
    public class EinControllerTestExample : ControllerBase
    {
        // NOTE: When copying to your EinController, you'll have access to your existing fields:
        // private readonly IAutomationOrchestrator _orchestrator;
        // private readonly ILogger<EinController> _logger;
        
        /// <summary>
        /// Quick test endpoint for blob storage with sample data
        /// Copy this method to your EinController.cs and uncomment the implementation
        /// GET /api/ein/test-blob-storage
        /// </summary>
        [HttpGet("test-blob-storage")]
        public Task<IActionResult> TestBlobStorage()
        {
            // COPY THIS METHOD TO YOUR EinController.cs AND UNCOMMENT:
            /*
            try
            {
                // You'll need to inject IBlobStorageService or get it from your orchestrator
                var blobStorageService = _orchestrator.GetBlobStorageService(); // Adjust based on your implementation
                var testConsole = new BlobStorageConsoleTest(blobStorageService, _logger);
                await testConsole.RunQuickTest();

                return Ok(new
                {
                    Message = "Blob storage test completed successfully",
                    Instructions = "Check the application logs and Azure Storage Explorer to verify the blob index tags",
                    TestData = new
                    {
                        RecordId = "a2GUS00000Aw1w92AD",
                        EntityName = "Orchid Wellbeing LLC",
                        AccountId = "0013h00002CJUvkAAH",
                        EntityId = "a0EUS0000026Bnp2AF",
                        CaseId = "500US0000ZVLgYAP",
                        ExpectedBlobName = "EntityProcess/a2GUS00000Aw1w92AD/OrchidWellbeingLLC-ID-JsonPayload.json",
                        ExpectedTags = "HiddenFromClient=true"
                    }
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Blob storage test failed");
                return StatusCode(500, $"Test failed: {ex.Message}");
            }
            */
            
            return Task.FromResult<IActionResult>(Ok(new { Message = "This is just an example - copy the commented code to your EinController.cs" }));
        }

        /// <summary>
        /// Get test instructions and sample curl commands
        /// Copy this method to your EinController.cs
        /// GET /api/ein/test-instructions
        /// </summary>
        [HttpGet("test-instructions")]
        public IActionResult GetTestInstructions()
        {
            return Ok(new
            {
                Message = "Blob Storage Index Tag Testing Instructions",
                QuickTest = new
                {
                    Description = "Run a quick JSON payload test with sample data",
                    Endpoint = "GET /api/ein/test-blob-storage",
                    CurlCommand = "curl -X GET 'https://your-api-url/api/ein/test-blob-storage'"
                },
                ManualTests = new
                {
                    ConfirmationPdf = new
                    {
                        Description = "Upload a confirmation PDF to test blob tags",
                        Endpoint = "POST /api/test/BlobStorageTest/upload-confirmation-pdf",
                        ExpectedTags = "HiddenFromClient=true, AccountId, EntityId, CaseId",
                        CurlCommand = @"curl -X POST 'https://your-api-url/api/test/BlobStorageTest/upload-confirmation-pdf' \
  -F 'PdfFile=@/path/to/your/confirmation.pdf' \
  -F 'RecordId=a2GUS00000Aw1w92AD' \
  -F 'EntityName=Orchid Wellbeing LLC' \
  -F 'AccountId=0013h00002CJUvkAAH' \
  -F 'EntityId=a0EUS0000026Bnp2AF' \
  -F 'CaseId=500US0000ZVLgYAP'"
                    },
                    EinLetterPdf = new
                    {
                        Description = "Upload an EIN Letter PDF to test blob tags",
                        Endpoint = "POST /api/test/BlobStorageTest/upload-ein-letter-pdf",
                        ExpectedTags = "HiddenFromClient=false, AccountId, EntityId, CaseId",
                        CurlCommand = @"curl -X POST 'https://your-api-url/api/test/BlobStorageTest/upload-ein-letter-pdf' \
  -F 'PdfFile=@/path/to/your/ein-letter.pdf' \
  -F 'RecordId=a2GUS00000Aw1w92AD' \
  -F 'EntityName=Orchid Wellbeing LLC' \
  -F 'AccountId=0013h00002CJUvkAAH' \
  -F 'EntityId=a0EUS0000026Bnp2AF' \
  -F 'CaseId=500US0000ZVLgYAP'"
                    },
                    JsonPayload = new
                    {
                        Description = "Upload a JSON payload to test blob tags",
                        Endpoint = "POST /api/test/BlobStorageTest/upload-json-payload",
                        ExpectedTags = "HiddenFromClient=true",
                        CurlCommand = @"curl -X POST 'https://your-api-url/api/test/BlobStorageTest/upload-json-payload' \
  -H 'Content-Type: application/json' \
  -d '{
    ""RecordId"": ""a2GUS00000Aw1w92AD"",
    ""EntityName"": ""Orchid Wellbeing LLC"",
    ""AccountId"": ""0013h00002CJUvkAAH"",
    ""EntityId"": ""a0EUS0000026Bnp2AF"",
    ""CaseId"": ""500US0000ZVLgYAP""
  }'"
                    }
                },
                VerificationSteps = new[]
                {
                    "1. Run one of the test endpoints above",
                    "2. Open Azure Storage Explorer or Azure Portal",
                    "3. Navigate to your storage account and container",
                    "4. Find the uploaded blob (check the logs for the exact blob name)",
                    "5. Right-click on the blob and select 'Properties' or 'Manage Blob Index Tags'",
                    "6. Verify that the blob index tags match the expected values",
                    "7. For JSON files: HiddenFromClient=true",
                    "8. For Confirmation PDFs: HiddenFromClient=true + AccountId/EntityId/CaseId",
                    "9. For EIN Letter PDFs: HiddenFromClient=false + AccountId/EntityId/CaseId"
                }
            });
        }
    }
}
