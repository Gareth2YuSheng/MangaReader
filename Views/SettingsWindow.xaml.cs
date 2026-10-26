using System.Windows;
using Microsoft.Win32;

namespace MangaReader.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            PathTextBox.Text = DatabaseManager.GetSetting("RootPath", "");

            // Load the setting
            AutoProcessCheckBox.IsChecked = DatabaseManager.GetSetting("AutoProcessZips", "False") == "True";
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Manga Root Folder" };
            if (dialog.ShowDialog() == true) PathTextBox.Text = dialog.FolderName;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // If the library moved (e.g. C: -> D:), carry the read/favorite/tag data over to the new paths
            string oldRootPath = DatabaseManager.GetSetting("RootPath", "");
            DatabaseManager.CopyProgressToNewRoot(oldRootPath, PathTextBox.Text);

            DatabaseManager.SaveSetting("RootPath", PathTextBox.Text);

            // Save the toggle state
            DatabaseManager.SaveSetting("AutoProcessZips", AutoProcessCheckBox.IsChecked == true ? "True" : "False");

            this.DialogResult = true;
            this.Close();
        }
    }
}