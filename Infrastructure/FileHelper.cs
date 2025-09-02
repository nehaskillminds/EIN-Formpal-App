
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EinAutomation.Api.Infrastructure
{
    public class FileHelper
    {
        internal static void DeleteFiles(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            if (!Directory.Exists(directory))
                return;

            try
            {
                var directoryInfo = new DirectoryInfo(directory);
                foreach (var file in directoryInfo.GetFiles())
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        // Log or handle individual file deletion errors
                        System.Diagnostics.Debug.WriteLine($"Failed to delete file {file.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log or handle directory access errors
                System.Diagnostics.Debug.WriteLine($"Failed to access directory {directory}: {ex.Message}");
            }
        }

        internal static async Task<string?> WaitForFileDownloadAsync(string directory, int maxWaitTime, int waitInterval, CancellationToken cancellationToken)
        {
            // Validate directory parameter
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));
            }

            // Ensure directory exists
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Directory does not exist: {directory}");
            }

            Console.WriteLine($"WaitForFileDownloadAsync: Starting to monitor directory: {directory}");
            Console.WriteLine($"WaitForFileDownloadAsync: Directory exists: {Directory.Exists(directory)}");
            
            var waited = 0;

            while (waited < maxWaitTime) 
            {
                if (cancellationToken.IsCancellationRequested) 
                    throw new TaskCanceledException();

                await Task.Delay(waitInterval, cancellationToken);
                waited += waitInterval;

                var pdfFiles = Directory.GetFiles(directory, "*.pdf");
                var crdownloadFiles = Directory.GetFiles(directory, "*.crdownload");
                var allFiles = Directory.GetFiles(directory);
                
                // Log every 5 seconds for debugging
                if (waited % 5000 == 0)
                {
                    Console.WriteLine($"WaitForFileDownloadAsync: Waited {waited}ms, Directory: {directory}");
                    Console.WriteLine($"WaitForFileDownloadAsync: All files: {string.Join(", ", allFiles)}");
                    Console.WriteLine($"WaitForFileDownloadAsync: PDF files: {pdfFiles.Length}, CRDownload files: {crdownloadFiles.Length}");
                }
                
                // If we have PDF files and no partial downloads, we're done
                if (pdfFiles.Length > 0 && crdownloadFiles.Length == 0)
                {
                    Console.WriteLine($"WaitForFileDownloadAsync: Found PDF file: {pdfFiles[0]}");
                    return pdfFiles[0];
                }
            }

            Console.WriteLine($"WaitForFileDownloadAsync: Timeout after {maxWaitTime}ms, no PDF file found in {directory}");
            return null; // Return null instead of empty string when no file is found
        }
    }
}