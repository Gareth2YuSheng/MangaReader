using ImageMagick;
using MangaReader.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MangaReader.Views
{
    public partial class LibraryView : UserControl
    {
        // Define the event so MainWindow knows a manga was clicked
        public delegate void MangaSelectedHandler(string folderPath);
        public event MangaSelectedHandler? OnMangaSelected;

        private string _rootMangaPath = @"C:\Users\Gareth\Pictures\Doujins";
        private List<string> _allMangaFolders = new List<string>();
        private int _currentLibraryPage = 0;
        private int _itemsPerPage = 24;
        private Dictionary<string, string?> _coverCache = new Dictionary<string, string?>();
        private string _currentTagFilter = "All";
        private List<string> _masterFolderCache = new List<string>();
        private bool _isSortDescending = true;
        private readonly string[] _validExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private string _currentSearchQuery = "";

        public ObservableCollection<MangaSeries> MangaLibrary { get; set; }

        public LibraryView()
        {
            InitializeComponent();
            MangaLibrary = new ObservableCollection<MangaSeries>();
            this.DataContext = this;
            this.Loaded += LibraryView_Loaded;
        }

        private async void LibraryView_Loaded(object sender, RoutedEventArgs e)
        {
            await PopulateLibraryViewAsync();
        }

        public void RefreshLibrary()
        {
            // Forces the UI to fetch fresh read/tag states from the DB when the reader closes
            _ = LoadLibraryPageAsync(_currentLibraryPage);
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

            DatabaseManager.SyncNewFolders(_masterFolderCache);
            PopulateTagDropdown();
        }

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
            if (TagFilterDropdown == null || SortDropdown == null || UnreadOnlyCheckbox == null) return;

            if (TagFilterDropdown.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                _currentTagFilter = item.Content.ToString()!;
                if (_masterFolderCache.Count > 0)
                {
                    ApplyFilterAndLoad();
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Grab the text and convert to lowercase for easy searching
            _currentSearchQuery = SearchBox.Text.Trim();

            if (_masterFolderCache.Count > 0)
            {
                ApplyFilterAndLoad();
            }
        }

        private void ApplyFilterAndLoad()
        {
            var dbData = DatabaseManager.GetAllMangaData();
            bool showUnreadOnly = UnreadOnlyCheckbox.IsChecked == true;

            var filteredFolders = _masterFolderCache.Where(dir =>
            {
                bool matchesTag = true;
                bool matchesReadStatus = true;
                bool matchesSearch = true; // NEW: Assume it matches until proven otherwise

                // NEW: Search Filter Check
                if (!string.IsNullOrWhiteSpace(_currentSearchQuery))
                {
                    string folderName = new DirectoryInfo(dir).Name;
                    // OrdinalIgnoreCase means it will match "naruto" with "Naruto"
                    matchesSearch = folderName.Contains(_currentSearchQuery, StringComparison.OrdinalIgnoreCase);
                }

                // Tag and Read Status Checks
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

                // Ensure it passes ALL active filters to be shown
                return matchesTag && matchesReadStatus && matchesSearch;
            });

            string sortOption = "Recent";
            if (SortDropdown != null && SortDropdown.SelectedItem is ComboBoxItem item)
            {
                sortOption = item.Content.ToString()!;
            }

            var naturalSorter = new NaturalSortComparer();
            var baseSort = filteredFolders.OrderBy(dir => dbData.ContainsKey(dir) ? dbData[dir].IsRead : false);

            if (sortOption == "Name")
            {
                if (_isSortDescending)
                    _allMangaFolders = baseSort.ThenByDescending(dir => new DirectoryInfo(dir).Name, naturalSorter).ToList();
                else
                    _allMangaFolders = baseSort.ThenBy(dir => new DirectoryInfo(dir).Name, naturalSorter).ToList();
            }
            else
            {
                if (_isSortDescending)
                    _allMangaFolders = baseSort.ThenByDescending(dir => dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.MinValue).ToList();
                else
                    _allMangaFolders = baseSort.ThenBy(dir => dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.MinValue).ToList();
            }

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

                        ImageSource? safeCoverImage = null;
                        try
                        {
                            using (var magickImage = new MagickImage(coverPath))
                            {
                                var bmp = magickImage.ToBitmapSource();
                                bmp.Freeze();
                                safeCoverImage = bmp;
                            }
                        }
                        catch { }

                        temp.Add(new MangaSeries
                        {
                            Title = directoryInfo.Name,
                            FolderPath = dir,
                            CoverPath = coverPath,
                            CoverImage = safeCoverImage,
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

            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).OrderBy(f => f);
            var cover = files.FirstOrDefault(f => IsImageFile(f));

            _coverCache[dir] = cover;
            return cover;
        }

        private async Task PreloadNextPagesAsync(int startPage)
        {
            await Task.Run(() =>
            {
                int startIndex = startPage * _itemsPerPage;
                var foldersToPreload = _allMangaFolders.Skip(startIndex).Take(_itemsPerPage * 3).ToList();

                foreach (var dir in foldersToPreload)
                {
                    GetOrFindCover(dir);
                }
            });
        }

        private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
        {
            _isSortDescending = !_isSortDescending;
            SortDirectionButton.Content = _isSortDescending ? "▼ Desc" : "▲ Asc";
            if (_masterFolderCache.Count > 0) ApplyFilterAndLoad();
        }

        private void LibraryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LibraryListBox.SelectedItem is MangaSeries selectedManga && selectedManga.FolderPath != null)
            {
                // Fire the event to tell MainWindow to switch views!
                OnMangaSelected?.Invoke(selectedManga.FolderPath);
            }
        }

        private async void ItemsPerPageDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ItemsPerPageDropdown != null && ItemsPerPageDropdown.SelectedItem is ComboBoxItem selectedItem)
            {
                if (int.TryParse(selectedItem.Content.ToString(), out int newValue))
                {
                    _itemsPerPage = newValue;
                    if (_allMangaFolders.Count > 0)
                    {
                        _currentLibraryPage = 0;
                        await LoadLibraryPageAsync(_currentLibraryPage);
                    }
                }
            }
        }

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
    }
}