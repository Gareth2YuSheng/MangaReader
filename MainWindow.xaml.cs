using System.Windows;
using System.Windows.Input;

namespace MangaReader
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DatabaseManager.InitializeDatabase();
        }

        // --- THE ROUTING LOGIC ---

        // 1. The Library fires this when you double-click a card
        private void LibraryComponent_OnMangaSelected(string folderPath)
        {
            LibraryComponent.Visibility = Visibility.Collapsed;

            // Tell the reader to load the images for this specific folder
            ReaderComponent.LoadManga(folderPath);
            ReaderComponent.Visibility = Visibility.Visible;

            this.Focus(); // Keep keyboard shortcuts working
        }

        // 2. The Reader fires this when you click the 'Back' button
        private void ReaderComponent_OnBackRequested()
        {
            ReaderComponent.Visibility = Visibility.Collapsed;

            // Tell the library to refresh in case you marked something as read
            LibraryComponent.RefreshLibrary();
            LibraryComponent.Visibility = Visibility.Visible;

            this.Focus();
        }

        // --- GLOBAL KEYBOARD SHORTCUTS ---
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ReaderComponent.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Left)
                {
                    ReaderComponent.GoToPreviousPage();
                    e.Handled = true;
                }
                else if (e.Key == Key.Right)
                {
                    ReaderComponent.GoToNextPage();
                    e.Handled = true;
                }
                // NEW: Jump to Start
                else if (e.Key == Key.Home)
                {
                    ReaderComponent.GoToFirstPage();
                    e.Handled = true;
                }
                // NEW: Jump to End
                else if (e.Key == Key.End)
                {
                    ReaderComponent.GoToLastPage();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    ReaderComponent_OnBackRequested();
                    e.Handled = true;
                }
            }
        }
    }
}