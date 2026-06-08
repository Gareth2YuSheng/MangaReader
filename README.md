# Manga Reader - Local Library Manager

Welcome to the **Manga Reader**, a lightweight, performant C# WPF desktop application designed to manage and read your local, offline manga collections.

This document is split into two parts:

1. **User Guide:** How to use the app and automate your workflow.
2. **Developer Documentation:** Codebase structure and design explanations for future reference.

---

## Part 1: User Guide

### 1. Initial Setup

When you first launch the application, your library will be empty.

1. Click the **Settings (⚙)** button (or follow the automatic prompt on a fresh install).
2. Set your **Manga Root Path**. This is the master folder where all your manga folders are stored.
3. Click **Save & Close**. The app will automatically scan the folder and generate your library.

### 2. Organizing and Viewing Your Library

The Library View is designed to handle thousands of entries smoothly using pagination (default 24 items per page).

- **Live Search:** Type in the search bar to instantly filter the current view.
- **Filtering:** Use the dropdowns to filter by **Status** (All / Read / Unread) or **Tags**.
- **Favorites:** Check the "My Favorites" box to only see manga you've marked with a heart.
- **Sorting:** Sort alphabetically (Name) or by the date they were added to the database (Recent), in Ascending or Descending order.

**Quick Actions (Hotkeys & Menus):**

- **Double Click:** Open the manga in the Reader view.
- **Right-Click:** Opens the context menu to easily add/remove existing tags, toggle Read status, or toggle Favorite status without opening the manga.
- **Hotkeys (Library):** Select a manga card and press **`R`** to toggle Read/Unread, or **`F`** to toggle Favorite/Unfavorite.

### 3. The Reader View

Once a manga is open, use the bottom navigation bar or your keyboard to read:

- **`Left Arrow` / `Right Arrow`**: Previous / Next page.
- **`Home` / `End`**: Jump instantly to the First or Last page.
- **`Escape`**: Close the reader and return to the library.

### 4. Automated Preprocessing (The `0_new` Folder)

If you frequently download zipped manga hauls, the app can automate the extraction and cleanup process.

**Supported Image Formats:**
Inside your folders or zips, the app looks specifically for **`.jpg`, `.jpeg`, `.png`, and `.webp`** files.

**How to use the Preprocessor:**

1.  Open **Settings** and check the box labeled _"Auto-extract & clean .zip files from '0_new' folder"_.
2.  Inside your Master Manga Root folder, the app will automatically create a subfolder called `0_new` (e.g., `C:\...\MangaRoot\0_new`).
3.  Drop your downloaded `.zip` files directly into this `0_new` folder.
4.  Launch the Manga Reader app.

**What the app does automatically:**

- A dark loading overlay will appear, showing you the extraction progress.
- It creates a new folder for each `.zip` file using the zip's name.
- It extracts all images (e.g., webp, jpg) into that new folder.
- **Cleanup Phase:** It specifically looks for and permanently deletes junk files commonly found in aggregators (specifically `final.jpg` and `ReadMe.txt`).
- It permanently deletes the original `.zip` file to save hard drive space.
- A text file named `processing_log.txt` is generated inside `0_new` so you can verify exactly what was extracted, cleaned, or skipped.

---

## Part 2: Developer Documentation

This section is designed to help you refresh your memory on how the application's architecture is built.

### 1. Technology Stack

- **Framework:** .NET 8.0 WPF (Windows Presentation Foundation).
- **Database:** SQLite (using the `Dapper` micro-ORM for fast query mapping).
- **Image Processing:** `Magick.NET` (Handles modern formats like `.webp` efficiently and safely freezes bitmaps for cross-thread UI loading).

### 2. Project Structure

The application follows a standard MV-ish architecture to separate UI from data logic:

- **`MangaReader/` (Root)**
  - `MainWindow.xaml` / `.cs`: The main window shell. Handles routing between the Library View and Reader View and catches global hotkeys.
  - `DatabaseManager.cs`: The SQLite wrapper. Handles schema initialization (`MangaProgress`, `Settings`), syncing new folders, and fetching library states.
  - `MangaProcessor.cs`: The background worker handling the `0_new` zip extraction and cleanup logic. Uses `IProgress<T>` to talk to the UI.
- **`Models/`**
  - `MangaSeries.cs`: The core data object. Inherits `INotifyPropertyChanged`. Contains `Title`, `FolderPath`, `Tags`, `IsRead`, `IsFavorite`.
- **`Views/`**
  - `LibraryView.xaml` / `.cs`: The complex library grid, search logic, filtering, and pagination system.
  - `ReaderView.xaml` / `.cs`: The image viewer and page navigation logic.
  - `SettingsWindow.xaml` / `.cs`: The modal for configuring DB-stored settings. Uses WPF's native `OpenFolderDialog`.

## 3. Key Architectural Concepts & Optimizations

### A. Reactive UI & `INotifyPropertyChanged`

Instead of forcing a full screen refresh when a manga is marked as favorite or read, the `MangaSeries` model broadcasts property changes to the UI.

```csharp
private bool _isFavorite;

public bool IsFavorite
{
    get => _isFavorite;
    set
    {
        _isFavorite = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(FavoriteBadgeVisibility)); // Instantly updates the UI Heart icon
    }
}
```

**Benefits:**

- Immediate UI updates without reloading the entire library.
- Better performance and responsiveness.
- Clean separation between data state and presentation.

---

### B. Database "Ghost" Management & Self-Healing

The SQLite database acts as an overlay on top of the physical file system.

#### Primary Source of Truth

The actual manga folders on disk are always considered authoritative:

```csharp
Directory.GetDirectories()
```

If a folder does not exist on the hard drive, it should not appear in the application.

#### New Folder Synchronization

`DatabaseManager.SyncNewFolders()` ensures newly downloaded manga are quickly added to the database using an efficient transaction:

```sql
INSERT OR IGNORE
```

#### Orphan Record Cleanup

`DatabaseManager.CleanOrphanedRecords()` automatically removes database entries for folders that have been manually deleted from disk.

**Benefits:**

- Prevents database bloat.
- Keeps the UI synchronized with the filesystem.
- Eliminates stale library entries automatically.

---

### C. Preventing Async Race Conditions (`CancellationTokenSource`)

The Live Search feature triggers a new database query and thumbnail-loading operation every time the user types.

Without protection, slower searches could finish after newer searches and overwrite current results.

#### Solution

`LibraryView` maintains a `CancellationTokenSource`.

Whenever a new search begins:

1. The previous token is cancelled.
2. A new token is created.
3. Background tasks periodically check:

```csharp
token.IsCancellationRequested
```

4. Cancelled tasks abort before performing UI updates.

**Benefits:**

- Prevents duplicate or stale search results.
- Keeps UI state consistent.
- Improves perceived responsiveness during rapid typing.

---

### D. Background Preprocessor

`MangaProcessor.ProcessNewZipsAsync()` performs archive extraction on a background thread using:

```csharp
Task.Run(...)
```

This prevents the UI thread from blocking during large extraction operations.

#### Progress Reporting

The processor communicates status updates using:

```csharp
IProgress<string>
```

Messages are safely marshalled back to the UI thread and displayed in the Library View's loading overlay.

Example workflow:

1. Detect ZIP files in `0_new`.
2. Extract archives.
3. Move content into the library structure.
4. Report progress messages.
5. Clean up processed archives.

**Benefits:**

- No UI freezing during I/O-heavy operations.
- Clear user feedback during processing.
- Safe cross-thread communication.

---

### E. Image Virtualization & Memory Management

Large manga libraries can contain thousands of cover images. Several optimizations are used to keep memory usage under control.

#### WPF Virtualization

The library grid uses WPF's built-in virtualization through `ListBox`.

Only visible items are rendered.

**Benefits:**

- Reduced memory usage.
- Faster scrolling performance.
- Lower UI rendering overhead.

#### Pagination

The library limits rendering to a fixed number of entries per page.

Default:

```text
24 items per page
```

Only the current page generates thumbnail bitmaps.

#### Thread-Safe Bitmap Loading

Images are decoded with `Magick.NET` on background threads.

Before being passed to the UI thread, bitmaps are frozen:

```csharp
bmp.Freeze();
```

This makes them immutable and safe for cross-thread usage.

**Benefits:**

- Eliminates cross-thread exceptions.
- Reduces memory pressure.
- Enables smooth asynchronous image loading.

---

### Summary of Performance Strategies

| Feature                   | Purpose                                   |
| ------------------------- | ----------------------------------------- |
| `INotifyPropertyChanged`  | Instant UI updates without full refreshes |
| Database Sync & Cleanup   | Keeps DB aligned with filesystem          |
| `CancellationTokenSource` | Prevents stale async results              |
| Background ZIP Processing | Avoids UI freezes during extraction       |
| WPF Virtualization        | Renders only visible items                |
| Pagination                | Limits image generation workload          |
| `bmp.Freeze()`            | Safe cross-thread image handling          |
