using ImageMagick;
using MangaReader.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;

namespace MangaReader.Views
{
    public partial class LibraryView : UserControl
    {
        // Define the event so MainWindow knows a manga was clicked
        public delegate void MangaSelectedHandler(string folderPath);
        public event MangaSelectedHandler? OnMangaSelected;

        private string RootMangaPath => DatabaseManager.GetSetting("RootPath", "");
        private List<string> _allMangaFolders = new List<string>();
        private int _currentLibraryPage = 0;
        private int _itemsPerPage = 24;
        private Dictionary<string, string?> _coverCache = new Dictionary<string, string?>();
        private string _currentTagFilter = "All";
        private List<string> _masterFolderCache = new List<string>();
        private bool _isSortDescending = true;
        private readonly string[] _validExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private string _currentSearchQuery = "";
        private CancellationTokenSource? _loadCts;

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
            string currentPath = RootMangaPath;

            // 1. The Gatekeeper: If no path is set, prompt the user
            if (string.IsNullOrWhiteSpace(currentPath) || !Directory.Exists(currentPath))
            {
                MangaLibrary.Clear();
                _masterFolderCache.Clear();

                MessageBox.Show("Welcome! Please set your Manga Root Folder to load your library.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Information);

                var settings = new SettingsWindow { Owner = Window.GetWindow(this) };
                if (settings.ShowDialog() == true)
                {
                    currentPath = RootMangaPath;
                }

                if (string.IsNullOrWhiteSpace(currentPath) || !Directory.Exists(currentPath)) return;
            }

            // === THE PREPROCESSOR ===
            bool autoProcess = DatabaseManager.GetSetting("AutoProcessZips", "False") == "True";
            if (autoProcess)
            {
                // 1. Show the overlay
                LoadingOverlay.Visibility = Visibility.Visible;

                // 2. Create the Progress object. This safely updates the TextBlock on the UI thread.
                var progressReporter = new Progress<string>(statusMessage =>
                {
                    LoadingStatusText.Text = statusMessage;
                });

                // 3. Run the processor and pass in our reporter
                await MangaProcessor.ProcessNewZipsAsync(currentPath, progressReporter);

                // 4. Hide the overlay when completely finished
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            // =============================

            // 2. Proceed with loading using the newly confirmed/processed path
            _masterFolderCache = Directory.GetDirectories(currentPath)
                .Where(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Any(f => IsImageFile(f)))
                .ToList();

            DatabaseManager.SyncNewFolders(_masterFolderCache);
            DatabaseManager.CleanOrphanedRecords(_masterFolderCache);

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
            if (TagFilterDropdown == null || SortDropdown == null || StatusDropdown == null || FavoritesOnlyCheckbox == null) return;

            if (TagFilterDropdown.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                _currentTagFilter = item.Content.ToString()!;
                if (_masterFolderCache.Count > 0) ApplyFilterAndLoad();
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

            // Grab the current text from the Status dropdown
            string statusFilter = "All";
            if (StatusDropdown.SelectedItem is ComboBoxItem statusItem)
            {
                statusFilter = statusItem.Content.ToString()!;
            }

            bool showFavoritesOnly = FavoritesOnlyCheckbox.IsChecked == true;

            var filteredFolders = _masterFolderCache.Where(dir =>
            {
                bool matchesTag = true;
                bool matchesReadStatus = true;
                bool matchesSearch = true;
                bool matchesFavorite = true;

                if (!string.IsNullOrWhiteSpace(_currentSearchQuery))
                {
                    string folderName = new DirectoryInfo(dir).Name;
                    matchesSearch = folderName.Contains(_currentSearchQuery, StringComparison.OrdinalIgnoreCase);
                }

                if (dbData.TryGetValue(dir, out var data))
                {
                    if (_currentTagFilter != "All")
                    {
                        var tags = data.Tags.Split(',').Select(t => t.Trim());
                        matchesTag = tags.Contains(_currentTagFilter, StringComparer.OrdinalIgnoreCase);
                    }

                    // Apply the specific Status rules
                    if (statusFilter == "Unread") matchesReadStatus = !data.IsRead;
                    else if (statusFilter == "Read") matchesReadStatus = data.IsRead;
                    if (showFavoritesOnly) matchesFavorite = data.IsFavorite;
                }
                else
                {
                    if (statusFilter == "Read") matchesReadStatus = false;
                    if (showFavoritesOnly) matchesFavorite = false;
                }

                return matchesTag && matchesReadStatus && matchesSearch && matchesFavorite;
            });

            string sortOption = "Recent";
            if (SortDropdown != null && SortDropdown.SelectedItem is ComboBoxItem item)
            {
                sortOption = item.Content.ToString()!;
            }

            var naturalSorter = new NaturalSortComparer();

            if (sortOption == "Name")
            {
                if (_isSortDescending)
                    _allMangaFolders = filteredFolders.OrderByDescending(dir => new DirectoryInfo(dir).Name, naturalSorter).ToList();
                else
                    _allMangaFolders = filteredFolders.OrderBy(dir => new DirectoryInfo(dir).Name, naturalSorter).ToList();
            }
            else
            {
                if (_isSortDescending)
                    _allMangaFolders = filteredFolders.OrderByDescending(dir => dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.MinValue).ToList();
                else
                    _allMangaFolders = filteredFolders.OrderBy(dir => dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.MinValue).ToList();
            }

            _currentLibraryPage = 0;
            _ = LoadLibraryPageAsync(0);
        }

        private async Task LoadLibraryPageAsync(int pageIndex)
        {
            // 1. If a previous load is still running, cancel it!
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

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
                    // 2. Early exit: If the user typed another letter while we were working, stop decoding images to save CPU!
                    if (token.IsCancellationRequested) return temp;

                    var directoryInfo = new DirectoryInfo(dir);
                    string? coverPath = GetOrFindCover(dir);

                    if (coverPath != null)
                    {
                        bool isRead = dbData.ContainsKey(dir) ? dbData[dir].IsRead : false;
                        string tags = dbData.ContainsKey(dir) ? dbData[dir].Tags : "";
                        DateTime dateAdded = dbData.ContainsKey(dir) ? dbData[dir].DateAdded : DateTime.Now;
                        bool isFavorite = dbData.ContainsKey(dir) ? dbData[dir].IsFavorite : false;

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
                            IsFavorite = isFavorite,
                            Tags = tags,
                            DateAdded = dateAdded
                        });
                    }
                }
                return temp;
            }, token);

            // 3. THE MAGIC SHIELD: If this task was cancelled while it was awaiting the background thread, do NOT update the UI.
            if (token.IsCancellationRequested) return;

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

        // --- HOTKEY LOGIC ---
        private void LibraryListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (LibraryListBox.SelectedItem is MangaSeries selectedManga)
            {
                if (e.Key == Key.R)
                {
                    ToggleReadStatus(selectedManga);
                    e.Handled = true;
                }
                else if (e.Key == Key.F) // NEW: The 'F' Hotkey
                {
                    ToggleFavoriteStatus(selectedManga);
                    e.Handled = true;
                }
            }
        }

        private void ToggleReadStatus(MangaSeries manga)
        {
            if (manga == null || string.IsNullOrEmpty(manga.FolderPath)) return;

            // Flip the status
            manga.IsRead = !manga.IsRead;

            // Save to database
            DatabaseManager.UpdateReadStatus(manga.FolderPath, manga.IsRead);
        }

        private void ToggleFavoriteStatus(MangaSeries manga)
        {
            if (manga == null || string.IsNullOrEmpty(manga.FolderPath)) return;
            manga.IsFavorite = !manga.IsFavorite;
            DatabaseManager.UpdateFavoriteStatus(manga.FolderPath, manga.IsFavorite);
        }

        // --- RIGHT CLICK MENU LOGIC ---
        private void MangaCard_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is MangaSeries manga)
            {
                var contextMenu = new ContextMenu();

                var favMenuItem = new MenuItem
                {
                    Header = manga.IsFavorite ? "Remove from Favorites" : "❤ Add to Favorites",
                    Foreground = manga.IsFavorite ? Brushes.White : (Brush)new BrushConverter().ConvertFrom("#FF4B4B")
                };
                favMenuItem.Click += (s, args) => ToggleFavoriteStatus(manga);
                contextMenu.Items.Add(favMenuItem);

                var readMenuItem = new MenuItem
                {
                    Header = manga.IsRead ? "Mark as Unread" : "Mark as Read",
                    FontWeight = FontWeights.Bold
                };
                readMenuItem.Click += (s, args) => ToggleReadStatus(manga);
                contextMenu.Items.Add(readMenuItem);

                contextMenu.Items.Add(new Separator());

                var existingTags = DatabaseManager.GetAllUniqueTags();
                var tagMenuItem = new MenuItem { Header = "Quick Tag" };

                if (existingTags.Count == 0)
                    tagMenuItem.Items.Add(new MenuItem { Header = "No tags created yet", IsEnabled = false });
                else
                {
                    foreach (var tag in existingTags)
                    {
                        var tItem = new MenuItem { Header = tag };
                        if (manga.TagList.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        {
                            tItem.IsEnabled = false;
                            tItem.Header = $"{tag} (Added)";
                        }
                        else tItem.Click += (s, args) => AddTagFromLibrary(manga, tag);

                        tagMenuItem.Items.Add(tItem);
                    }
                }
                contextMenu.Items.Add(tagMenuItem);

                border.ContextMenu = contextMenu;

                // FORCES THE MENU TO OPEN INSTANTLY ON THE FIRST CLICK
                contextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        // The new Library First/Last Page buttons
        private async void LibFirstButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLibraryPage > 0)
            {
                _currentLibraryPage = 0;
                await LoadLibraryPageAsync(_currentLibraryPage);
            }
        }

        private async void LibLastButton_Click(object sender, RoutedEventArgs e)
        {
            int maxPages = (int)Math.Ceiling((double)_allMangaFolders.Count / _itemsPerPage);
            if (_currentLibraryPage < maxPages - 1)
            {
                _currentLibraryPage = maxPages - 1;
                await LoadLibraryPageAsync(_currentLibraryPage);
            }
        }

        private void AddTagFromLibrary(MangaSeries manga, string newTag)
        {
            if (manga == null || string.IsNullOrEmpty(manga.FolderPath)) return;

            var tags = manga.TagList;
            if (!tags.Contains(newTag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(newTag);
                string finalTags = string.Join(", ", tags);

                // Update DB
                DatabaseManager.SaveTags(manga.FolderPath, finalTags);

                // Update UI (Because of our property setter, the pills draw instantly)
                manga.Tags = finalTags;

                // Ensure the top filter dropdown updates if it's a brand new tag
                PopulateTagDropdown();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow { Owner = Window.GetWindow(this) };

            // Only reload IF they actually clicked the Save button
            if (settings.ShowDialog() == true)
            {
                _ = PopulateLibraryViewAsync();
            }
        }
    }
}