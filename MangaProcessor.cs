using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MangaReader
{
    public static class MangaProcessor
    {
        public static async Task ProcessNewZipsAsync(string rootPath)
        {
            string newFolderPath = Path.Combine(rootPath, "0_new");

            // If the folder doesn't exist, create it
            if (!Directory.Exists(newFolderPath))
            {
                Directory.CreateDirectory(newFolderPath);
            }

            // Find ONLY .zip files
            var zipFiles = Directory.GetFiles(newFolderPath, "*.zip");
            if (zipFiles.Length == 0) return;

            // Run the heavy extraction on a background thread
            await Task.Run(() =>
            {
                foreach (var zipFile in zipFiles)
                {
                    try
                    {
                        // Use the zip file's name as the new manga folder name
                        string folderName = Path.GetFileNameWithoutExtension(zipFile);
                        string destinationPath = Path.Combine(rootPath, folderName);

                        // Only proceed if a folder with that name doesn't already exist
                        if (!Directory.Exists(destinationPath))
                        {
                            LogMessage(newFolderPath, $"Starting extraction: {folderName}.zip");

                            Directory.CreateDirectory(destinationPath);
                            ZipFile.ExtractToDirectory(zipFile, destinationPath);

                            LogMessage(newFolderPath, $"Successfully extracted: {folderName}");

                            // The Cleanup Phase
                            string finalJpg = Path.Combine(destinationPath, "final.jpg");
                            string readMe = Path.Combine(destinationPath, "ReadMe.txt");

                            if (File.Exists(finalJpg)) File.Delete(finalJpg);
                            if (File.Exists(readMe)) File.Delete(readMe);

                            LogMessage(newFolderPath, $"Cleaned up junk files for: {folderName}");
                        }

                        // Delete the original .zip to save hard drive space
                        File.Delete(zipFile);
                        LogMessage(newFolderPath, $"Deleted original zip: {folderName}.zip");
                        LogMessage(newFolderPath, new string('-', 40)); // Adds a nice divider line between jobs
                    }
                    catch
                    {
                        // If a zip is corrupted or currently downloading, skip it and try next time
                    }
                }
            });
        }

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

                            // NEW: Report back to the UI!
                            progress?.Report($"Unzipping ({currentFile}/{zipFiles.Length}): {folderName}");

                            Directory.CreateDirectory(destinationPath);
                            ZipFile.ExtractToDirectory(zipFile, destinationPath);

                            LogMessage(newFolderPath, $"Successfully extracted: {folderName}");

                            // NEW: Report cleanup
                            progress?.Report($"Cleaning: {folderName}");

                            string finalJpg = Path.Combine(destinationPath, "final.jpg");
                            string readMe = Path.Combine(destinationPath, "ReadMe.txt");

                            if (File.Exists(finalJpg)) File.Delete(finalJpg);
                            if (File.Exists(readMe)) File.Delete(readMe);

                            LogMessage(newFolderPath, $"Cleaned up junk files for: {folderName}");
                        }

                        File.Delete(zipFile);
                        LogMessage(newFolderPath, $"Deleted original zip: {folderName}.zip");
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