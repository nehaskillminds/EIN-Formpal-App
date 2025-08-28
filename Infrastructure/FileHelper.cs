
namespace EinAutomation.Api.Infrastructure
{
    public class FileHelper
    {
        internal static void DeleteFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            var directoryInfo = new DirectoryInfo(directory);
            foreach (var file in directoryInfo.GetFiles())
            {
                file.Delete();
            }
        }

        internal static async Task<string> WaitForFileDownloadAsync(string directory, int maxWaitTime, int waitInterval, CancellationToken cancellationToken)
        {
            var waited = 0;
            var downloadedFilePath = "";

            while (waited < maxWaitTime) 
            {
                if (cancellationToken.IsCancellationRequested) 
                    throw new TaskCanceledException();

                await Task.Delay(waitInterval, cancellationToken);
                waited += waitInterval;

                var pdfFiles = Directory.GetFiles(directory, "*.pdf");
                var crdownloadFiles = Directory.GetFiles(directory, "*.crdownload");
                // If we have PDF files and no partial downloads, we're done
                if (pdfFiles.Length > 0 && crdownloadFiles.Length == 0)
                {
                    downloadedFilePath = pdfFiles[0];
                    break;
                }
            }

            return downloadedFilePath;
        }
    }
}