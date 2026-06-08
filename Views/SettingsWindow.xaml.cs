using System.Windows;
using Microsoft.Win32; // Changed from System.Windows.Forms

namespace MangaReader.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            PathTextBox.Text = DatabaseManager.GetSetting("RootPath", "");
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Manga Root Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                PathTextBox.Text = dialog.FolderName;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            DatabaseManager.SaveSetting("RootPath", PathTextBox.Text);
            this.DialogResult = true;
        }
    }
}