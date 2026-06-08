using Dapper;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MangaReader
{
    public static class DatabaseManager
    {
        private const string ConnectionString = "Data Source=manga_library.db";

        public static void InitializeDatabase()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            // Rebuilt Schema with DateAdded
            string createTableSql = @"
                CREATE TABLE IF NOT EXISTS MangaProgress (
                    FolderPath TEXT PRIMARY KEY,
                    IsRead INTEGER DEFAULT 0,
                    Tags TEXT DEFAULT '',
                    DateAdded TEXT DEFAULT CURRENT_TIMESTAMP
                )";
            connection.Execute(createTableSql);
        }

        // Fast batch-insert so 1,092 items don't freeze the app on first launch
        public static void SyncNewFolders(IEnumerable<string> folderPaths)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            // INSERT OR IGNORE means it only adds brand new folders, skipping ones it already knows
            string sql = "INSERT OR IGNORE INTO MangaProgress (FolderPath, DateAdded) VALUES (@Folder, CURRENT_TIMESTAMP)";
            connection.Execute(sql, folderPaths.Select(f => new { Folder = f }), transaction: transaction);

            transaction.Commit();
        }

        // Now returns DateAdded as well
        public static Dictionary<string, (bool IsRead, string Tags, DateTime DateAdded)> GetAllMangaData()
        {
            using var connection = new SqliteConnection(ConnectionString);
            var results = connection.Query("SELECT FolderPath, IsRead, Tags, DateAdded FROM MangaProgress");

            var dict = new Dictionary<string, (bool, string, DateTime)>();
            foreach (var row in results)
            {
                DateTime parsedDate = DateTime.TryParse((string)row.DateAdded, out var d) ? d : DateTime.Now;
                dict[(string)row.FolderPath] = ((long)row.IsRead == 1, (string)row.Tags ?? "", parsedDate);
            }
            return dict;
        }

        public static void MarkAsRead(string folderPath)
        {
            using var connection = new SqliteConnection(ConnectionString);
            string sql = @"
                INSERT INTO MangaProgress (FolderPath, IsRead) VALUES (@Folder, 1)
                ON CONFLICT(FolderPath) DO UPDATE SET IsRead = 1";
            connection.Execute(sql, new { Folder = folderPath });
        }

        public static void SaveTags(string folderPath, string tags)
        {
            using var connection = new SqliteConnection(ConnectionString);
            string sql = @"
                INSERT INTO MangaProgress (FolderPath, Tags) VALUES (@Folder, @Tags)
                ON CONFLICT(FolderPath) DO UPDATE SET Tags = @Tags";
            connection.Execute(sql, new { Folder = folderPath, Tags = tags });
        }

        public static List<string> GetAllUniqueTags()
        {
            using var connection = new SqliteConnection(ConnectionString);
            var allTags = connection.Query<string>("SELECT Tags FROM MangaProgress WHERE Tags IS NOT NULL AND Tags != ''");
            var uniqueTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tagString in allTags)
            {
                var split = tagString.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t));
                foreach (var t in split) uniqueTags.Add(t);
            }
            return uniqueTags.OrderBy(t => t).ToList();
        }

        public static void UpdateReadStatus(string folderPath, bool isRead)
        {
            using var connection = new SqliteConnection(ConnectionString);
            string sql = @"
                INSERT INTO MangaProgress (FolderPath, IsRead) VALUES (@Folder, @IsRead)
                ON CONFLICT(FolderPath) DO UPDATE SET IsRead = @IsRead";
            connection.Execute(sql, new { Folder = folderPath, IsRead = isRead ? 1 : 0 });
        }
    }
}