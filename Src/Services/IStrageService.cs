
using Avalonia.Controls;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace PlaylistEditorX.Services;

public interface IStorageService
{
    void Initialize(Window mainWindow);

    // Returns a stream and the file name, or null if cancelled
    Task<(Stream Stream, string Name)?> OpenFileAsync(string title, string[] extensions);

    // Returns a list of absolute file paths, or an empty list if cancelled
    Task<IReadOnlyList<string>> SelectOpenFilesAsync(string title, string[] extensions, bool allowMultiple);

    Task<string?> SelectSingleOpenFileAsync(string title, string[] extensions);

    Task<IStorageFile?> SelectSingleSaveFileAsync(string title, string defaultFileName, string[] extensions);

    Task CopyTextToClipboardAsync(string text);
}
