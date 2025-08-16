using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EinAutomation.Api.TestClasses
{
    /// <summary>
    /// Console-based test class for blob storage testing
    /// Run this directly from your application to test blob uploads
    /// </summary>
    public class BlobStorageConsoleTest
    {
        private readonly IBlobStorageService _blobStorageService;
        private readonly ILogger<BlobStorageConsoleTest> _logger;

        public BlobStorageConsoleTest(IBlobStorageService blobStorageService, ILogger<BlobStorageConsoleTest> logger)
        {
            _blobStorageService = blobStorageService;
            _logger = logger;
        }

        /// <summary>
        /// Interactive console test - prompts user for input
        /// </summary>
        public async Task RunInteractiveTest()
        {
            Console.WriteLine("=== AZURE BLOB STORAGE INDEX TAG TEST ===");
            Console.WriteLine();

            try
            {
                // Get test parameters from user
                var testParams = GetTestParametersFromUser();

                // Create a logger factory to get the correct logger type
                using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var uploaderLogger = loggerFactory.CreateLogger<BlobStorageTestUploader>();
                var testUploader = new BlobStorageTestUploader(_blobStorageService, uploaderLogger);

                // Ask what type of test to run
                Console.WriteLine("\nWhich test would you like to run?");
                Console.WriteLine("1. JSON Payload only");
                Console.WriteLine("2. Confirmation PDF only (requires PDF file)");
                Console.WriteLine("3. EIN Letter PDF only (requires PDF file)");
                Console.WriteLine("4. All tests (requires both PDF files)");
                Console.Write("Enter your choice (1-4): ");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await RunJsonTest(testUploader, testParams);
                        break;
                    case "2":
                        await RunConfirmationPdfTest(testUploader, testParams);
                        break;
                    case "3":
                        await RunEinLetterPdfTest(testUploader, testParams);
                        break;
                    case "4":
                        await RunAllTests(testUploader, testParams);
                        break;
                    default:
                        Console.WriteLine("Invalid choice. Running JSON test only.");
                        await RunJsonTest(testUploader, testParams);
                        break;
                }

                Console.WriteLine("\n=== TEST COMPLETED ===");
                Console.WriteLine("Please check Azure Storage Explorer to verify the blob index tags.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                _logger.LogError(ex, "Console test failed");
            }
        }

        private TestParameters GetTestParametersFromUser()
        {
            var testParams = new TestParameters();

            Console.WriteLine("Please provide the following test parameters:");
            Console.WriteLine();

            Console.Write("Record ID (required): ");
            testParams.RecordId = Console.ReadLine() ?? throw new ArgumentException("Record ID is required");

            Console.Write("Entity Name (required): ");
            testParams.EntityName = Console.ReadLine() ?? throw new ArgumentException("Entity Name is required");

            Console.Write("Account ID (optional, press Enter to skip): ");
            testParams.AccountId = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(testParams.AccountId))
                testParams.AccountId = null;

            Console.Write("Entity ID (optional, press Enter to skip): ");
            testParams.EntityId = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(testParams.EntityId))
                testParams.EntityId = null;

            Console.Write("Case ID (optional, press Enter to skip): ");
            testParams.CaseId = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(testParams.CaseId))
                testParams.CaseId = null;

            return testParams;
        }

        private async Task RunJsonTest(BlobStorageTestUploader testUploader, TestParameters testParams)
        {
            Console.WriteLine("\n--- RUNNING JSON PAYLOAD TEST ---");
            
            var jsonData = testUploader.CreateSampleJsonPayload(
                testParams.RecordId,
                testParams.EntityName,
                testParams.AccountId,
                testParams.EntityId,
                testParams.CaseId);

            await testUploader.UploadTestJsonPayload(jsonData, testParams.RecordId, testParams.EntityName);
        }

        private async Task RunConfirmationPdfTest(BlobStorageTestUploader testUploader, TestParameters testParams)
        {
            Console.WriteLine("\n--- RUNNING CONFIRMATION PDF TEST ---");
            
            Console.Write("Enter full path to confirmation PDF file: ");
            var pdfPath = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfPath}");
            }

            await testUploader.UploadTestConfirmationPdf(
                pdfPath,
                testParams.RecordId,
                testParams.EntityName,
                testParams.AccountId,
                testParams.EntityId,
                testParams.CaseId);
        }

        private async Task RunEinLetterPdfTest(BlobStorageTestUploader testUploader, TestParameters testParams)
        {
            Console.WriteLine("\n--- RUNNING EIN LETTER PDF TEST ---");
            
            Console.Write("Enter full path to EIN Letter PDF file: ");
            var pdfPath = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfPath}");
            }

            await testUploader.UploadTestEinLetterPdf(
                pdfPath,
                testParams.RecordId,
                testParams.EntityName,
                testParams.AccountId,
                testParams.EntityId,
                testParams.CaseId);
        }

        private async Task RunAllTests(BlobStorageTestUploader testUploader, TestParameters testParams)
        {
            Console.WriteLine("\n--- RUNNING ALL TESTS ---");
            
            Console.Write("Enter full path to confirmation PDF file: ");
            var confirmationPdfPath = Console.ReadLine();
            
            Console.Write("Enter full path to EIN Letter PDF file: ");
            var einLetterPdfPath = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(confirmationPdfPath) || !File.Exists(confirmationPdfPath))
            {
                throw new FileNotFoundException($"Confirmation PDF file not found: {confirmationPdfPath}");
            }
            
            if (string.IsNullOrWhiteSpace(einLetterPdfPath) || !File.Exists(einLetterPdfPath))
            {
                throw new FileNotFoundException($"EIN Letter PDF file not found: {einLetterPdfPath}");
            }

            await testUploader.UploadAllTestFiles(
                confirmationPdfPath,
                einLetterPdfPath,
                testParams.RecordId,
                testParams.EntityName,
                testParams.AccountId,
                testParams.EntityId,
                testParams.CaseId);
        }

        /// <summary>
        /// Quick test with predefined parameters (for development/debugging)
        /// </summary>
        public async Task RunQuickTest()
        {
            Console.WriteLine("=== RUNNING QUICK TEST WITH SAMPLE DATA ===");

            var testParams = new TestParameters
            {
                RecordId = "a2GUS00000Aw1w92AD",
                EntityName = "Orchid Wellbeing LLC",
                AccountId = "0013h00002CJUvkAAH",
                EntityId = "a0EUS0000026Bnp2AF",
                CaseId = "500US0000ZVLgYAP"
            };

            // Create a logger factory to get the correct logger type
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var uploaderLogger = loggerFactory.CreateLogger<BlobStorageTestUploader>();
            var testUploader = new BlobStorageTestUploader(_blobStorageService, uploaderLogger);

            // Run JSON test only (no PDF files needed)
            await RunJsonTest(testUploader, testParams);

            Console.WriteLine("\n=== QUICK TEST COMPLETED ===");
            Console.WriteLine("Check Azure Storage Explorer for the uploaded JSON file with tags.");
        }
    }

    public class TestParameters
    {
        public string RecordId { get; set; } = null!;
        public string EntityName { get; set; } = null!;
        public string? AccountId { get; set; }
        public string? EntityId { get; set; }
        public string? CaseId { get; set; }
    }
}
