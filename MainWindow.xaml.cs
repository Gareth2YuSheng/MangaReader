using ImageMagick;
using MangaReader.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MangaReader
{
    public partial class MainWindow : Window
    {
        string _rootMangaPath = @"C:\Users\Gareth\Pictures\Doujins";

        // Pagination State
        private List<string> _allMangaFolders = new List<string>();
        private int _currentLibraryPage = 0;
        private int _itemsPerPage = 24;

        // Cache to store preloaded cover paths so we don't scan the hard drive twice
        private Dictionary<string, string?> _coverCache = new Dictionary<string, string?>();

        // Core Data Bindings
        public ObservableCollection<MangaSeries> MangaLibrary { get; set; }
        public ObservableCollection<string> CurrentReaderTags { get; set; }
        private List<string> _readerImageFiles = new List<string>();
        private int _currentReaderIndex = 0;
        private string _currentReaderFolderPath = "";
        private bool _isSortDescending = true; // "Recent" usually implies Newest First (Descending)

        // Tags
        private string _currentTagFilter = "All";
        private List<string> _masterFolderCache = new List<string>(); // Stores everything

        private readonly string[] _validExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        public MainWindow()
        {
            InitializeComponent();
            DatabaseManager.InitializeDatabase();

            MangaLibrary = new ObservableCollection<MangaSeries>();
            CurrentReaderTags = new ObservableCollection<string>();

            this.DataContext = this;
            // We don't call PopulateLibraryView directly anymore.
            // Instead, we wait for the Window to physically load on screen first.
            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // This runs AFTER the dark gray window appears, so it doesn't freeze.
            await PopulateLibraryViewAsync();
        }

        private bool IsImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            return _validExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private async Task PopulateLibraryViewAsync()
        {
            if (!Directory.Exists(_rootMangaPath)) return;

            _masterFolderCache = Directory.GetDirectories(_rootMangaPath)
                .Where(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Any(f => IsImageFile(f)))
                .ToList();

            // Sync all found folders to the database instantly so they get a DateAdded timestamp
            DatabaseManager.SyncNewFolders(_masterFolderCache);

            PopulateTagDropdown();
        }

        // Fills the dropdown with your unique tags
        private void PopulateTagDropdown()
        {
            TagFilterDropdown.Items.Clear();
            TagFilterDropdown.Items.Add(new ComboBoxItem { Content = "All", IsSelected = true });

            var uniqueTags = DatabaseManager.GetAllUniqueTags();
            foreach (var tag in uniqueTags)
            {
                TagFilterDropdown.Items.Add(new ComboBoxItem { Content = tag });
            }
        }

        private void FilterControls_Changed(object sender, RoutedEventArgs e)
        {
            // SAFETY SHIELD: Ignore events that fire while the UI is still being built
            if (TagFilterDropdown == null || SortDropdown == null || UnreadOnlyCheckbox == null)
                return;

            if (TagFilterDropdown.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                _currentTagFilter = item.Content.ToString()!;
                if (_masterFolderCache.Count > 0)
                {
                    ApplyFilterAndLoad();
                }
            }
        }

        // The actual filtering engine
        private void ApplyFilterAndLoad()
        {
            var dbData = DatabaseManager.GetAllMangaData();
            bool showUnreadOnly = UnreadOnlyCheckbox.IsChecked == true;

            // 1. Filter the Cache (Keep existing logic)
            var filteredFolders = _masterFolderCache.Where(dir =>
            {
                bool matchesTag = true;
                bool matchesReadStatus = true;

                if (dbData.TryGetValue(dir, out var data))
                {
                    if (_currentTagFilter != "All")
                    {
                        var tags = data.Tags.Split(',').Select(t => t.Trim());
                        matchesTag = tags.Contains(_currentTagFilter, StringComparer.OrdinalIgnoreCase);
                    }
                    if (showUnreadOnly) matchesReadStatus = !data.IsRead;
                }
                else if (showUnreadOnly) matchesReadStatus = true;

                return matchesTag && matchesReadStatus;
            });

            // 2. Read the UI Choices
            string sortOption = "Recent";
            if (SortDropdown != null && SortDropdown.SelectedItem is ComboBoxItem item)
            {
                sortOption = item.Content.ToString()!;
            }

            var naturalSorter = new NaturalSortComparer();

            // 3. APPLY PRIMARY SORT: Always pin Unread items to the top!
            var baseSort = filteredFolders.OrderBy(dir => dbData.ContainsKey(dir) ? dbData[dir].IsRead : false);

            // 4. APPLY SECONDARY SORT: User's choice (Name vs Recent, Asc vs Desc)
            if (sortOption == "Name")
            {
                if (_isSortDescending)
                    _allMangaFolders = baseSort.ThenByDescending(dir => new DirectoryInfo(dir).Name, naturalSorter).ToList();
                else
                    _allMangaFolders = baseSort.ThenBy(dir => new DirectoryInfo(dir).Name, naturalSorter).ToList();
            }
            else // "Recent"
            {
                if (_isSortDescending)
                    _allMangaFolders = baseSort.ThenByDescending(dir => dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.MinValue).ToList();
                else
                    _allMangaFolders = baseSort.ThenBy(dir => dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.MinValue).ToList();
            }

            // 5. Reload UI
            _currentLibraryPage = 0;
            _ = LoadLibraryPageAsync(0);
        }

        private async Task LoadLibraryPageAsync(int pageIndex)
        {
            MangaLibrary.Clear();

            int startIndex = pageIndex * _itemsPerPage;
            var foldersForThisPage = _allMangaFolders.Skip(startIndex).Take(_itemsPerPage).ToList();

            int totalPages = Math.Max(1, (int)Math.Ceiling((double)_allMangaFolders.Count / _itemsPerPage));
            LibPageIndicator.Text = $"Page {pageIndex + 1} / {totalPages}";

            var pageItems = await Task.Run(() =>
            {
                var temp = new List<MangaSeries>();
                var dbData = DatabaseManager.GetAllMangaData();

                foreach (var dir in foldersForThisPage)
                {
                    var directoryInfo = new DirectoryInfo(dir);
                    string? coverPath = GetOrFindCover(dir);

                    if (coverPath != null)
                    {
                        bool isRead = dbData.ContainsKey(dir) ? dbData[dir].IsRead : false;
                        string tags = dbData.ContainsKey(dir) ? dbData[dir].Tags : "";
                        DateTime dateAdded = dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.Now;

                        // NEW: Use Magick.NET to safely decode the cover image
                        ImageSource? safeCoverImage = null;
                        try
                        {
                            using (var magickImage = new MagickImage(coverPath))
                            {
                                var bmp = magickImage.ToBitmapSource();
                                bmp.Freeze(); // CRITICAL: This allows the background thread to share the image with the UI
                                safeCoverImage = bmp;
                            }
                        }
                        catch
                        {
                            // If the image is completely corrupted, safeCoverImage remains null and shows blank instead of crashing
                        }

                        temp.Add(new MangaSeries
                        {
                            Title = directoryInfo.Name,
                            FolderPath = dir,
                            CoverPath = coverPath,
                            CoverImage = safeCoverImage, // Assign the safely decoded image!
                            IsRead = isRead,
                            Tags = tags,
                            DateAdded = dateAdded
                        });
                    }
                }
                return temp;
            });

            foreach (var item in pageItems) MangaLibrary.Add(item);

            _ = PreloadNextPagesAsync(pageIndex + 1);
        }

        private string? GetOrFindCover(string dir)
        {
            if (_coverCache.ContainsKey(dir)) return _coverCache[dir];

            // Force a consistent search by ordering files by name
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                                 .OrderBy(f => f);

            var cover = files.FirstOrDefault(f => IsImageFile(f));

            _coverCache[dir] = cover;
            return cover;
        }

        private async Task PreloadNextPagesAsync(int startPage)
        {
            await Task.Run(() =>
            {
                // Calculate the next 3 pages (X items)
                int startIndex = startPage * _itemsPerPage;
                var foldersToPreload = _allMangaFolders.Skip(startIndex).Take(_itemsPerPage * 3).ToList();

                foreach (var dir in foldersToPreload)
                {
                    GetOrFindCover(dir); // This automatically saves to our dictionary cache
                }
            });
        }

        private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Flip the boolean
            _isSortDescending = !_isSortDescending;

            // Update the visual text on the button
            SortDirectionButton.Content = _isSortDescending ? "▼ Desc" : "▲ Asc";

            // Re-sort the library
            if (_masterFolderCache.Count > 0)
            {
                ApplyFilterAndLoad();
            }
        }

        // --- NAVIGATION ROUTING LOGIC ---
        private void LibraryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Capture the selected item template item from the grid double-click event
            if (LibraryListBox.SelectedItem is MangaSeries selectedManga)
            {
                OpenMangaFolder(selectedManga.FolderPath);
            }
        }

        private void OpenMangaFolder(string folderPath)
        {
            _currentReaderFolderPath = folderPath;

            // 1. Get the list of files without sorting yet
            var files = Directory.EnumerateFiles(folderPath)
                .Where(f => IsImageFile(f))
                .ToList();

            // 2. Sort them using the Windows Natural Sorter
            files.Sort(new NaturalSortComparer());

            _readerImageFiles = files;

            if (_readerImageFiles.Count > 0)
            {
                _currentReaderIndex = 0;
                ShowImage(_currentReaderIndex);

                // Load tags into the interactive pills
                CurrentReaderTags.Clear();
                var dbData = DatabaseManager.GetAllMangaData();
                if (dbData.ContainsKey(folderPath) && !string.IsNullOrWhiteSpace(dbData[folderPath].Tags))
                {
                    var existingTags = dbData[folderPath].Tags.Split(',').Select(t => t.Trim());
                    foreach (var t in existingTags)
                    {
                        CurrentReaderTags.Add(t);
                    }
                }

                LibraryView.Visibility = Visibility.Collapsed;
                ReaderView.Visibility = Visibility.Visible;
                this.Focus();
            }
            else
            {
                MessageBox.Show("No valid JPG or PNG files found in this folder.");
            }
        }

        private void BackToLibrary()
        {
            // Reset active views
            ReaderView.Visibility = Visibility.Collapsed;
            LibraryView.Visibility = Visibility.Visible;

            // Clean active memory cache context references from structural canvas frame bindings
            MangaDisplay.Source = null;
            _readerImageFiles.Clear();
        }

        private void ShowImage(int index)
        {
            if (_readerImageFiles.Count == 0 || index < 0 || index >= _readerImageFiles.Count) return;

            string filePath = _readerImageFiles[index];

            try
            {
                // Let Magick.NET handle everything. It ignores extensions and reads the true file header.
                using (var magickImage = new MagickImage(filePath))
                {
                    MangaDisplay.Source = magickImage.ToBitmapSource();
                }

                PageIndicator.Text = $"Page {index + 1} / {_readerImageFiles.Count}";
            }
            catch (Exception)
            {
                MangaDisplay.Source = null;
                PageIndicator.Text = $"Error loading page {index + 1}";
            }
        }

        // --- BUTTON BINDINGS INTERFACE CONTROL ACTIONS ---
        private async void LibPrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLibraryPage > 0)
            {
                _currentLibraryPage--;
                await LoadLibraryPageAsync(_currentLibraryPage);
            }
        }

        private async void LibNextButton_Click(object sender, RoutedEventArgs e)
        {
            int maxPages = (int)Math.Ceiling((double)_allMangaFolders.Count / _itemsPerPage);
            if (_currentLibraryPage < maxPages - 1)
            {
                _currentLibraryPage++;
                await LoadLibraryPageAsync(_currentLibraryPage);
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReaderIndex > 0)
            {
                _currentReaderIndex--;
                ShowImage(_currentReaderIndex);
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReaderIndex < _readerImageFiles.Count - 1)
            {
                // Go to next page normally
                _currentReaderIndex++;
                ShowImage(_currentReaderIndex);
            }
            else
            {
                // WE ARE ON THE LAST PAGE!
                // 1. Mark it in the database
                string currentFolderPath = Path.GetDirectoryName(_readerImageFiles[0]);
                DatabaseManager.MarkAsRead(currentFolderPath);

                // 2. Optional: Ask the user if they want to go back to the library
                MessageBoxResult result = MessageBox.Show(
                    "You've reached the end of this manga! Mark as read and return to library?",
                    "Finished", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    BackToLibrary();

                    // Refresh the current page to make the green READ badge appear
                    _ = LoadLibraryPageAsync(_currentLibraryPage);
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackToLibrary();
        }

        // TAG RELATED FUNCTIONS
        // --- TAG PILL LOGIC ---
        // 1. Core Logic: Automatically saves whatever is currently in the pill collection
        private void SyncTagsToDatabase()
        {
            if (string.IsNullOrEmpty(_currentReaderFolderPath)) return;

            string finalTags = string.Join(", ", CurrentReaderTags);
            DatabaseManager.SaveTags(_currentReaderFolderPath, finalTags);

            // Update the Library UI behind the scenes
            var currentManga = MangaLibrary.FirstOrDefault(m => m.FolderPath == _currentReaderFolderPath);
            if (currentManga != null) currentManga.Tags = finalTags;

            PopulateTagDropdown();
            this.Focus(); // Return keyboard focus for arrow keys
        }

        // 2. Helper Logic: Cleans input and adds the pill
        private void AddTagToCurrentManga(string tagInput)
        {
            if (string.IsNullOrWhiteSpace(tagInput)) return;

            var cleanTag = tagInput.Trim();
            cleanTag = char.ToUpper(cleanTag[0]) + cleanTag.Substring(1).ToLower();

            // Prevent duplicate pills
            if (!CurrentReaderTags.Contains(cleanTag))
            {
                CurrentReaderTags.Add(cleanTag);
                SyncTagsToDatabase();
            }
        }

        // 3. Button Click: Add from the text box
        private void AddNewTagButton_Click(object sender, RoutedEventArgs e)
        {
            AddTagToCurrentManga(NewTagTextBox.Text);
            NewTagTextBox.Text = ""; // Clear the box after adding
        }

        // 4. Button Click: Add from the floating dropdown menu
        private void SuggestionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton && clickedButton.Content is string selectedTag)
            {
                AddTagToCurrentManga(selectedTag);
                TagSuggestionsPopup.IsOpen = false;
                NewTagTextBox.Text = "";
            }
        }

        // 5. Button Click: The '✕' on the tag pill
        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            // The DataContext of the 'x' button is the string name of the tag itself!
            if (sender is Button btn && btn.DataContext is string tagToRemove)
            {
                CurrentReaderTags.Remove(tagToRemove);
                SyncTagsToDatabase();
            }
        }

        // 6. Opens the floating menu and loads the tags
        private void ShowTagsMenuButton_Click(object sender, RoutedEventArgs e)
        {
            // Fetch all unique tags from the database
            var existingTags = DatabaseManager.GetAllUniqueTags();

            if (existingTags.Count == 0)
            {
                MessageBox.Show("You haven't created any tags yet!", "No Tags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Bind them to the popup menu
            TagSuggestionsControl.ItemsSource = existingTags;

            // Open the popup directly below the text box
            TagSuggestionsPopup.IsOpen = true;
        }

        // --- GLOBAL KEY EVENT HOOK MONITORING DELEGATES ---
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Key mechanics processing route restrictions based on interface state
            if (ReaderView.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Left)
                {
                    PrevButton_Click(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Right)
                {
                    NextButton_Click(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape) // Map ESC to go back to the catalog index overview grid layout
                {
                    BackToLibrary();
                    e.Handled = true;
                }
            }
        }

        private async void ItemsPerPageDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ensure the UI has fully loaded before trying to read the dropdown
            if (ItemsPerPageDropdown != null && ItemsPerPageDropdown.SelectedItem is ComboBoxItem selectedItem)
            {
                if (int.TryParse(selectedItem.Content.ToString(), out int newValue))
                {
                    _itemsPerPage = newValue;

                    // If the library is already populated, reset to page 1 and reload
                    if (_allMangaFolders.Count > 0)
                    {
                        _currentLibraryPage = 0;
                        await LoadLibraryPageAsync(_currentLibraryPage);
                    }
                }
            }
        }
    }
}