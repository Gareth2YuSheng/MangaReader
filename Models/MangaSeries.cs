using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace MangaReader.Models
{
    // Blueprint for individual manga entities in the grid
    public class MangaSeries : INotifyPropertyChanged
    {
        public string? Title { get; set; }
        public string? FolderPath { get; set; }
        public string? CoverPath { get; set; }
        public ImageSource? CoverImage { get; set; }
        private bool _isRead;
        public bool IsRead
        {
            get => _isRead;
            set
            {
                _isRead = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReadBadgeVisibility)); // Tells the green badge to appear/disappear instantly!
            }
        }
        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteBadgeVisibility));
            }
        }
        public Visibility FavoriteBadgeVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReadBadgeVisibility => IsRead ? Visibility.Visible : Visibility.Collapsed;

        // SQLite TEXT maps beautifully to DateTime in most ORMs (like EF Core), 
        // or can be parsed manually if using Microsoft.Data.Sqlite
        public DateTime DateAdded { get; set; } = DateTime.Now;

        // This is the reactive part!
        private string? _tags;
        public string? Tags
        {
            get => _tags;
            set
            {
                _tags = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TagList)); // Tells the UI to redraw the pills
            }
        }

        public List<string> TagList => string.IsNullOrWhiteSpace(Tags)
            ? new List<string>()
            : Tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();

        // The required plumbing for the radio broadcaster
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class NaturalSortComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? x, string? y)
        {
            if (x == null || y == null) return 0;
            return StrCmpLogicalW(x, y);
        }
    }
}
