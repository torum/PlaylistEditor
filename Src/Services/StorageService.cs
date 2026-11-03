using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using PlaylistEditor.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlaylistEditor.Services;

public class StorageService : IStorageService
{
    private Window? _mainWindow;
    private bool _isInitialized;
    private static readonly string[] _defaultAllTypesFilter = new[] { "*.*" };

    public StorageService()
    {
    }

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
        _isInitialized = true;
    }

    public async Task<IReadOnlyList<string>> SelectOpenFilesAsync(string title, string[] extensions, bool allowMultiple)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("StorageService is not initialized.");
        }

        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel == null) return System.Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple, // Enabled multi-select as requested
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Allowed Files") { Patterns = extensions }
            }
        };

        // Open picker dialog
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        // Extract the absolute file paths using the TryGetLocalPath extension method
        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path != null) // Filters out empty/unsupported sandboxed paths
            .ToList()!;
    }

    public async Task<string?> SelectSingleOpenFileAsync(string title, string[] extensions)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("StorageService is not initialized.");
        }

        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Playlist Files") { Patterns = extensions },
                new FilePickerFileType("All Files") { Patterns = _defaultAllTypesFilter }
            }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        if (files.Any())
        {
            var file = files[0];
            return (file.Path.LocalPath);
        }

        return null;
    }

    public async Task<IStorageFile?> SelectSingleSaveFileAsync(string title, string defaultFileName, string[] extensions)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("StorageService is not initialized.");
        }
        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel == null) return null;
        var options = new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = extensions.FirstOrDefault(),
            SuggestedFileName = defaultFileName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new FilePickerFileType("Playlist Files") { Patterns = extensions },
                new FilePickerFileType("All Files") { Patterns = _defaultAllTypesFilter }
            }
        };
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        if (file != null)
        {
            return file;//.Path.LocalPath;
        }
        return null;
    }

    public async Task<(Stream Stream, string Name)?> OpenFileAsync(string title, string[] extensions)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("StorageService is not initialized.");
        }

        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Playlist Files") { Patterns = extensions },
                new FilePickerFileType("All Files") { Patterns = _defaultAllTypesFilter }
            }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        if (files.Any())
        {
            var file = files[0];
            var stream = await file.OpenReadAsync();
            return (stream, file.Name);
        }

        return null;
    }

    public async Task CopyTextToClipboardAsync(string text)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("StorageService is not initialized.");
        }

        var topLevel = TopLevel.GetTopLevel(_mainWindow);
        if (topLevel?.Clipboard is not null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
        }
    }
}
