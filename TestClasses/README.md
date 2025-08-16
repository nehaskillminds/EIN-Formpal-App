# Blob Storage Index Tag Testing

This directory contains test classes to verify that blob index tags are being set correctly for different types of uploads (JSON payloads, confirmation PDFs, and EIN Letter PDFs).

## Test Classes Overview

### 1. `BlobStorageTestUploader.cs`
Core test functionality for uploading files with proper tagging.

### 2. `BlobStorageTestController.cs`
Web API endpoints for testing via HTTP requests.

### 3. `BlobStorageConsoleTest.cs`
Console-based interactive testing.

### 4. `TestEndpointExample.cs`
Example of how to add test endpoints to your existing controller.

## How to Use

### Option 1: Quick API Test (Recommended)

1. **Add test endpoint to your EinController** (copy from `TestEndpointExample.cs`):
   ```csharp
   [HttpGet("test-blob-storage")]
   public async Task<IActionResult> TestBlobStorage()
   ```

2. **Run the quick test**:
   ```bash
   curl -X GET 'https://your-api-url/api/ein/test-blob-storage'
   ```

3. **Check the response** - it will show you exactly what was uploaded and where to verify the tags.

### Option 2: Upload Your Own Files

#### A. JSON Payload Test
```bash
curl -X POST 'https://your-api-url/api/test/BlobStorageTest/upload-json-payload' \
  -H 'Content-Type: application/json' \
  -d '{
    "RecordId": "a2GUS00000Aw1w92AD",
    "EntityName": "Orchid Wellbeing LLC",
    "AccountId": "0013h00002CJUvkAAH",
    "EntityId": "a0EUS0000026Bnp2AF",
    "CaseId": "500US0000ZVLgYAP"
  }'
```

**Expected Result:**
- Blob Name: `EntityProcess/a2GUS00000Aw1w92AD/OrchidWellbeingLLC-ID-JsonPayload.json`
- Tags: `HiddenFromClient=true`

#### B. Confirmation PDF Test
```bash
curl -X POST 'https://your-api-url/api/test/BlobStorageTest/upload-confirmation-pdf' \
  -F 'PdfFile=@/path/to/your/confirmation.pdf' \
  -F 'RecordId=a2GUS00000Aw1w92AD' \
  -F 'EntityName=Orchid Wellbeing LLC' \
  -F 'AccountId=0013h00002CJUvkAAH' \
  -F 'EntityId=a0EUS0000026Bnp2AF' \
  -F 'CaseId=500US0000ZVLgYAP'
```

**Expected Result:**
- Blob Name: `EntityProcess/a2GUS00000Aw1w92AD/OrchidWellbeingLLC-ID-ConfirmationTest.pdf`
- Tags: 
  - `HiddenFromClient=true`
  - `AccountId=0013h00002CJUvkAAH`
  - `EntityId=a0EUS0000026Bnp2AF`
  - `CaseId=500US0000ZVLgYAP`

#### C. EIN Letter PDF Test
```bash
curl -X POST 'https://your-api-url/api/test/BlobStorageTest/upload-ein-letter-pdf' \
  -F 'PdfFile=@/path/to/your/ein-letter.pdf' \
  -F 'RecordId=a2GUS00000Aw1w92AD' \
  -F 'EntityName=Orchid Wellbeing LLC' \
  -F 'AccountId=0013h00002CJUvkAAH' \
  -F 'EntityId=a0EUS0000026Bnp2AF' \
  -F 'CaseId=500US0000ZVLgYAP'
```

**Expected Result:**
- Blob Name: `EntityProcess/a2GUS00000Aw1w92AD/OrchidWellbeingLLC-ID-EINLetter-Test.pdf`
- Tags: 
  - `HiddenFromClient=false` ⭐ (Note: false for client visibility)
  - `AccountId=0013h00002CJUvkAAH`
  - `EntityId=a0EUS0000026Bnp2AF`
  - `CaseId=500US0000ZVLgYAP`

### Option 3: Console Testing

If you prefer interactive console testing, inject `BlobStorageConsoleTest` into your application and call:
```csharp
await consoleTest.RunInteractiveTest();
```

## Verification Steps

1. **Run one of the tests above**
2. **Open Azure Storage Explorer or Azure Portal**
3. **Navigate to your storage account and container**
4. **Find the uploaded blob** (check the API response or logs for exact blob name)
5. **Right-click on the blob → Properties → Blob Index Tags**
6. **Verify the tags match the expected values**

## Sample Test Data

The tests use this sample data structure:
```json
{
  "record_id": "a2GUS00000Aw1w92AD",
  "entity_name": "Orchid Wellbeing LLC",
  "form_type": "EIN",
  "entity_type": "Limited Liability Company (LLC)",
  "formation_date": "2025-08-07T00:00:00",
  "business_category": "OTHER",
  "business_description": "Career Coaching and Team Effectiveness",
  "filing_state": "Delaware",
  "business_address_1": "1000 Longboat Key Unit 1104, Longboat Key, FL 34228",
  "entity_state": "Florida",
  "city": "Longboat Key",
  "zip_code": "34228",
  "ssn_decrypted": "148-74-1133",
  "proceed_flag": "true",
  "county": "Sarasota",
  "closing_month": "12",
  "account_id": "0013h00002CJUvkAAH",
  "entity_id": "a0EUS0000026Bnp2AF",
  "case_id": "500US0000ZVLgYAP",
  "test_timestamp": "2025-01-27T10:30:00Z",
  "test_purpose": "Blob index tag verification"
}
```

## Tag Summary

| File Type | HiddenFromClient | AccountId | EntityId | CaseId |
|-----------|------------------|-----------|----------|--------|
| JSON Payload | `true` | ❌ | ❌ | ❌ |
| Confirmation PDF | `true` | ✅ | ✅ | ✅ |
| EIN Letter PDF | `false` | ✅ | ✅ | ✅ |

## Troubleshooting

- **File not found errors**: Make sure PDF file paths are correct and files exist
- **Blob upload failures**: Check Azure connection string and container permissions
- **Missing tags**: Check the logs for confirmation that tags were set
- **Wrong tag values**: Verify the case-sensitive tag names (e.g., `HiddenFromClient`, not `hiddenFromClient`)

## Integration with Existing Code

To add these test capabilities to your existing application:

1. **Register the test services** in your DI container:
   ```csharp
   services.AddTransient<BlobStorageTestUploader>();
   services.AddTransient<BlobStorageConsoleTest>();
   ```

2. **Add the test controller** by copying `BlobStorageTestController.cs` to your Controllers folder

3. **Add test endpoints** to your existing controller by copying methods from `TestEndpointExample.cs`

The test classes are designed to work with your existing `IBlobStorageService` implementation and will use the same Azure configuration.



