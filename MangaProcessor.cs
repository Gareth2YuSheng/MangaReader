using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MangaReader
{
    public static class MangaProcessor
    {
        // Only ONE ProcessNewZipsAsync method is needed!
        public static async Task ProcessNewZipsAsync(string rootPath, IProgress<string> progress = null)
        {
            string newFolderPath = Path.Combine(rootPath, "0_new");

            if (!Directory.Exists(newFolderPath)) Directory.CreateDirectory(newFolderPath);

            var zipFiles = Directory.GetFiles(newFolderPath, "*.zip");
            if (zipFiles.Length == 0) return;

            await Task.Run(() =>
            {
                // We can use a simple counter to show "1 of X"
                int currentFile = 1;

                foreach (var zipFile in zipFiles)
                {
                    try
                    {
                        string folderName = Path.GetFileNameWithoutExtension(zipFile);
                        string destinationPath = Path.Combine(rootPath, folderName);

                        if (!Directory.Exists(destinationPath))
                        {
                            LogMessage(newFolderPath, $"Starting extraction: {folderName}.zip");
                            progress?.Report($"Unzipping ({currentFile}/{zipFiles.Length}): {folderName}");

                            Directory.CreateDirectory(destinationPath);
                            ZipFile.ExtractToDirectory(zipFile, destinationPath);

                            LogMessage(newFolderPath, $"Successfully extracted: {folderName}");
                            progress?.Report($"Cleaning: {folderName}");

                            string finalJpg = Path.Combine(destinationPath, "final.jpg");
                            string readMe = Path.Combine(destinationPath, "ReadMe.txt");

                            if (File.Exists(finalJpg)) File.Delete(finalJpg);
                            if (File.Exists(readMe)) File.Delete(readMe);

                            LogMessage(newFolderPath, $"Cleaned up junk files for: {folderName}");
                        }
                        else
                        {
                            // Log that we skipped extraction because it's a duplicate
                            LogMessage(newFolderPath, $"Duplicate found: '{folderName}' already exists. Skipping extraction.");
                        }

                        // Because this is OUTSIDE the if-statement, it will delete the zip whether it extracted it or skipped it!
                        File.Delete(zipFile);
                        LogMessage(newFolderPath, $"Deleted zip: {folderName}.zip");
                        LogMessage(newFolderPath, new string('-', 40));
                    }
                    catch (Exception ex)
                    {
                        LogMessage(newFolderPath, $"ERROR processing {Path.GetFileName(zipFile)}: {ex.Message}");
                    }

                    currentFile++;
                }
            });
        }

        private static void LogMessage(string logDirectory, string message)
        {
            try
            {
                string logPath = Path.Combine(logDirectory, "processing_log.txt");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

                // AppendAllText creates the file if it doesn't exist, and just adds to the bottom if it does
                File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // We wrap this in a try-catch so that if the log file is locked for some reason, 
                // it quietly fails instead of crashing your entire background processor.
            }
        }
    }
}