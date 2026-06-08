using ImageMagick;
using MangaReader.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MangaReader.Views
{
    public partial class ReaderView : UserControl
    {
        // Define the event so MainWindow knows the Back button was clicked
        public delegate void BackRequestedHandler();
        public event BackRequestedHandler? OnBackRequested;

        public ObservableCollection<string> CurrentReaderTags { get; set; }
        private List<string> _readerImageFiles = new List<string>();
        private int _currentReaderIndex = 0;
        private string _currentReaderFolderPath = "";
        private readonly string[] _validExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        public ReaderView()
        {
            InitializeComponent();
            CurrentReaderTags = new ObservableCollection<string>();
            this.DataContext = this;
        }

        private bool IsImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            return _validExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        // Called by MainWindow when a manga is selected
        public void LoadManga(string folderPath)
        {
            _currentReaderFolderPath = folderPath;

            var files = Directory.EnumerateFiles(folderPath)
                .Where(f => IsImageFile(f))
                .ToList();

            files.Sort(new NaturalSortComparer());
            _readerImageFiles = files;

            if (_readerImageFiles.Count > 0)
            {
                _currentReaderIndex = 0;
                ShowImage(_currentReaderIndex);

                CurrentReaderTags.Clear();
                var dbData = DatabaseManager.GetAllMangaData();
                if (dbData.ContainsKey(folderPath) && !string.IsNullOrWhiteSpace(dbData[folderPath].Tags))
                {
                    var existingTags = dbData[folderPath].Tags.Split(',').Select(t => t.Trim());
                    foreach (var t in existingTags) CurrentReaderTags.Add(t);
                }
            }
            else
            {
                MessageBox.Show("No valid JPG or PNG files found in this folder.");
            }
        }

        private void ShowImage(int index)
        {
            if (_readerImageFiles.Count == 0 || index < 0 || index >= _readerImageFiles.Count) return;

            string filePath = _readerImageFiles[index];

            try
            {
                using (var magickImage = new MagickImage(filePath))
                {
                    MangaDisplay.Source = magickImage.ToBitmapSource();
                }
                PageIndicator.Text = $"Page {index + 1} / {_readerImageFiles.Count}";
            }
            catch
            {
                MangaDisplay.Source = null;
                PageIndicator.Text = $"Error loading page {index + 1}";
            }
        }

        public void GoToPreviousPage()
        {
            if (_currentReaderIndex > 0)
            {
                _currentReaderIndex--;
                ShowImage(_currentReaderIndex);
            }
        }

        public void GoToNextPage()
        {
            if (_currentReaderIndex < _readerImageFiles.Count - 1)
            {
                _currentReaderIndex++;
                ShowImage(_currentReaderIndex);
            }
            else
            {
                DatabaseManager.MarkAsRead(_currentReaderFolderPath);
                MessageBoxResult result = MessageBox.Show("You've reached the end of this manga! Mark as read and return to library?", "Finished", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    BackButton_Click(null, null);
                }
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e) => GoToPreviousPage();
        private void NextButton_Click(object sender, RoutedEventArgs e) => GoToNextPage();

        private void BackButton_Click(object sender, RoutedEventArgs? e)
        {
            MangaDisplay.Source = null;
            _readerImageFiles.Clear();

            // Tell the MainWindow to close this view
            OnBackRequested?.Invoke();
        }

        // --- TAG LOGIC ---
        private void SyncTagsToDatabase()
        {
            if (string.IsNullOrEmpty(_currentReaderFolderPath)) return;
            string finalTags = string.Join(", ", CurrentReaderTags);
            DatabaseManager.SaveTags(_currentReaderFolderPath, finalTags);
            this.Focus();
        }

        private void AddTagToCurrentManga(string tagInput)
        {
            if (string.IsNullOrWhiteSpace(tagInput)) return;
            var cleanTag = tagInput.Trim();
            cleanTag = char.ToUpper(cleanTag[0]) + cleanTag.Substring(1).ToLower();

            if (!CurrentReaderTags.Contains(cleanTag))
            {
                CurrentReaderTags.Add(cleanTag);
                SyncTagsToDatabase();
            }
        }

        private void AddNewTagButton_Click(object sender, RoutedEventArgs e)
        {
            AddTagToCurrentManga(NewTagTextBox.Text);
            NewTagTextBox.Text = "";
        }

        private void SuggestionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton && clickedButton.Content is string selectedTag)
            {
                AddTagToCurrentManga(selectedTag);
                TagSuggestionsPopup.IsOpen = false;
                NewTagTextBox.Text = "";
            }
        }

        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string tagToRemove)
            {
                CurrentReaderTags.Remove(tagToRemove);
                SyncTagsToDatabase();
            }
        }

        private void ShowTagsMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var existingTags = DatabaseManager.GetAllUniqueTags();
            if (existingTags.Count == 0)
            {
                MessageBox.Show("You haven't created any tags yet!", "No Tags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            TagSuggestionsControl.ItemsSource = existingTags;
            TagSuggestionsPopup.IsOpen = true;
        }
    }
}