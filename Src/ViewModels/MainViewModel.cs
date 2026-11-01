using Avalonia.Controls.Shapes;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaylistEditorX.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

#pragma warning disable CA1822 // Mark members as static

namespace PlaylistEditorX.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static string ConfigFilePath => "app.config";
    private readonly DispatcherTimer _hideStatusTimer;
    private readonly Services.IStorageService _storageService;
    private readonly Services.IDialogService _dialogService;
    private readonly Services.IConfigurationService _configurationService;
    public static readonly HashSet<string> PlaylistExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".xspf", ".m3u", ".m3u8" };
    private static readonly string[] _separator = new[] { "\r\n", "\n" };
    private static readonly string[] _allExtensions = new[] { "*.*" };

    private string _currentFilePath = string.Empty;
    private Playlist? _currentPlaylist;
    private PlaylistType _currentPlaylistType = PlaylistType.New;
    private string _lastOpenedDirectory = string.Empty;

    public event EventHandler<bool>? WorkingStateChanged;

    internal MainViewModel(Services.IStorageService storageService, Services.IDialogService dialogService, Services.IConfigurationService configurationService)
    {
        _storageService = storageService;
        _dialogService = dialogService;
        _configurationService = configurationService;

        _lastOpenedDirectory = _configurationService.CurrentConfiguration.LastOpenedDirectory;

        // Status bar initially hidden
        IsStatusBarVisible = false;

        // Timer equivalent of TimerHideStatusbar
        _hideStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        _hideStatusTimer.Tick += (_, _) =>
        {
            IsStatusBarVisible = false;
            _hideStatusTimer.Stop();
        };

        ClearPlaylist();

        // TODO: wrap this with async safe method.
        _ = TryOpenStartupFile();

    }

    #region == Properties ==

    public ObservableCollection<Entry> Items { get; } = new ObservableCollection<Entry>();

    [ObservableProperty]
    public partial Entry? SelectedItem { get; set; }

    public bool IsBusy
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;

            Dispatcher.UIThread.Post(() =>
            {
                WorkingStateChanged?.Invoke(this, value);
            }, DispatcherPriority.Input);
        }
    }

    [ObservableProperty]
    public partial int ProgressValue
    {
        get;
        set;
    }

    [ObservableProperty]
    public partial int ProgressMaximum
    {
        get;
        set;
    }

    [ObservableProperty]
    public partial int ProgressMinimum
    {
        get;
        set;
    }

    [ObservableProperty]
    public partial bool IsRearrangedOrder { get; set; }

    [ObservableProperty]
    public partial bool IsLocationEdited { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsCloseRequested { get; set; }

    [ObservableProperty]
    public partial string AppVersion { get; set; } = "1.0.0";

    [ObservableProperty]
    public partial string StatusTextLeft { get; set; } = "New";

    [ObservableProperty]
    public partial string StatusTextRight { get; set; } = "";

    [ObservableProperty]
    public partial bool IsStatusBarVisible { get; set; }

    [ObservableProperty]
    public partial string FindText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReplaceText { get; set; } = string.Empty;

    // These boolean properties control command availability
    [ObservableProperty]
    public partial bool CanSaveItunesTxt { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3u { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3uAnsi { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3uBom { get; set; }

    [ObservableProperty]
    public partial bool CanSaveM3uUtf8 { get; set; }

    [ObservableProperty]
    public partial bool CanSaveXspf { get; set; }

    [ObservableProperty]
    public partial bool CanConvertToM3u { get; set; }

    [ObservableProperty]
    public partial bool CanConvertToXspf { get; set; }

    [ObservableProperty]
    public partial bool CanCopySelectedPath { get; set; }

    [ObservableProperty]
    public partial bool CanOpenContainingFolder { get; set; }

    [ObservableProperty]
    public partial bool CanRemoveSelected { get; set; }

    [ObservableProperty]
    public partial bool CanAddFiles { get; set; }

    #endregion

    #region == Public Methods ==

    public void UpdateSettings()
    {
        var config = _configurationService.CurrentConfiguration;

        config.LastOpenedDirectory = _lastOpenedDirectory;
    }

    public async Task FilesDroped(IEnumerable<string?> files)
    {
        if (IsBusy) return;
        if (files is null) return;
        if (!files.Any()) return;

        if (files.Count() == 1)
        {
            var extension = System.IO.Path.GetExtension(files.First());
            if (!string.IsNullOrEmpty(extension) && files.First() != null)
            {
                if (PlaylistExtensions.Contains(extension))
                {
                    if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (await CheckIsDirtyClear())
                        {
                            ClearPlaylist();
                            await OpenItunesTxt(files.First()!);
                        }
                    }
                    else if (extension.Equals(".xspf", StringComparison.OrdinalIgnoreCase))
                    {
                        if (await CheckIsDirtyClear())
                        {
                            ClearPlaylist();
                            await OpenXspf(files.First()!);
                        }
                    }
                    else if (extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase) || extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
                    {
                        if (await CheckIsDirtyClear())
                        {
                            ClearPlaylist();
                            await OpenM3u(files.First()!);
                        }
                    }
                    else
                    {
                        // Shouldn't be happening.
                    }

                    var dir = System.IO.Path.GetDirectoryName(files.First());
                    if (dir is not null) _lastOpenedDirectory = dir;
                }
                else
                {
                    // Non-playlist files, add to the current playlist.
                    AddFilesToPlaylist(files.Where(f => f != null).Select(f => f!).ToArray());
                }
            }
            else
            {
                Debug.WriteLine($"FilesDroped: Files.First() is null.");
            }
        }
        else
        {
            // multiple files, add to the current playlist.
            AddFilesToPlaylist(files.Where(f => f != null).Select(f => f!).ToArray());
        }
    }

    private async Task<bool> CheckIsDirtyClear()
    {
        if (IsDirty)
        {
            var confirm = await _dialogService.ShowConfirmAsync(
                "Playlist has been changed",
                "Do you wish to discard changes and proceed?"
            );

            if (!confirm)
                return false;
        }
        return true;
    }

    public void ReorderItems(List<Entry> dragged, Entry? target, bool isDropAbove)
    {
        //Debug.WriteLine($"ReorderItems: dragged={dragged.Text}, target={(target != null ? target.Text : "null")}");

        if (target == null || dragged == null || dragged.Count == 0)
            return;

        var moving = dragged
        .Where(item => Items.Contains(item))
        .Distinct()
        .OrderBy(item => Items.IndexOf(item))
        .ToList();

        if (moving.Count == 0 || moving.Contains(target))
            return;

        int targetIndex = Items.IndexOf(target);
        if (targetIndex < 0)
            return;

        var originalItems = Items.ToList();
        //var originalRawLines = RawLines.ToList();
        var rawLineByItem = new Dictionary<Entry, string>();

        //for (int i = 0; i < originalItems.Count; i++)
        //    rawLineByItem.Add(originalItems[i], originalRawLines[i]);

        int insertionIndex = targetIndex + (isDropAbove ? 0 : 1);
        insertionIndex -= moving.Count(item => Items.IndexOf(item) < insertionIndex);

        foreach (var item in moving)
            Items.Remove(item);

        for (int i = 0; i < moving.Count; i++)
            Items.Insert(insertionIndex + i, moving[i]);

        if (originalItems.SequenceEqual(Items))
            return;

        for (int i = 0; i < Items.Count; i++)
            Items[i].Index = i;

        //RawLines.Clear();
        //foreach (var item in Items)
        //    RawLines.Add(rawLineByItem[item]);

        IsRearrangedOrder = true;
        IsDirty = true;
        /*
        IsRearrangedOrder = true;
        IsDirty = true;

        int oldIndex = Items.IndexOf(dragged);
        int targetIndex = Items.IndexOf(target);
        
        if (oldIndex == -1 || targetIndex == -1)
            return;

        int newIndex = isDropAbove ? targetIndex : targetIndex + 1;

        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        if (newIndex < 0 || newIndex >= Items.Count || oldIndex == newIndex)
            return;

        Items.RemoveAt(oldIndex);
        Items.Insert(newIndex, dragged);

        // Reindex Items
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
        }

        // Rebuild RawLines to match new order
        var newRaw = Items.Select(x => RawLines[x.Index - 1]).ToList();

        RawLines.Clear();
        foreach (var line in newRaw)
            RawLines.Add(line);
        */
    }

    #endregion

    #region == Private Methods ==

    #region == Init Utilities ==

    private void DisableAllCommands()
    {
        CanSaveItunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = false;

        CanConvertToM3u = false;
        CanConvertToXspf = false;

        CanCopySelectedPath = false;
        CanOpenContainingFolder = false;
        CanRemoveSelected = false;
        CanAddFiles = false;
    }

    public void ClearPlaylist()
    {
        //RawLines.Clear();
        Items.Clear();
        IsDirty = false;
        IsRearrangedOrder = false;
        IsLocationEdited = false;
        _currentPlaylistType = PlaylistType.New;
        _currentPlaylist = new PlaylistNew();
        _currentFilePath = string.Empty;
        StatusTextLeft = "New";
        StatusTextRight = "";
        ProgressMinimum = 0;
        ProgressMaximum = 100;
        ProgressValue = 0;

        // Disable commands
        DisableAllCommands();
    }

    #endregion

    #region == Path Conversion ==

    private static string UrlEncode(string s)
    {
        var allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.:/~";
        var sb = new StringBuilder();

        foreach (var ch in s)
        {
            if (allowed.Contains(ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('%');
                sb.Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static string UrlDecode(string s)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length)
            {
                var hex = s.Substring(i + 1, 2);
                if (byte.TryParse(hex, NumberStyles.HexNumber, null, out var value))
                {
                    sb.Append((char)value);
                    i += 2;
                    continue;
                }
            }

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    private static string ConvertUriToLocalPath(string strPath)
    {
        string s = strPath;

        if (s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Replace("file:///", "");

            if (s.Contains(":/"))
            {
                // Windows drive letter
                s = s.Replace("/", "\\");
            }
            else
            {
                // Linux absolute path
                s = "/" + s;
            }
        }
        else if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            // Windows UNC
            s = s.Replace("file://", "\\\\");
            s = s.Replace("/", "\\");
        }

        return UrlDecode(s);
    }

    private static string ConvertLocalPathToUri(string strUri)
    {
        // return new Uri(path, UriKind.Absolute).ToString();

        if (string.IsNullOrEmpty(strUri) || strUri.Length < 2)
            return strUri;

        string s = strUri;

        // Windows UNC: \\server\share
        if (s.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Replace("\\", "/");
            s = UrlEncode(s);
            return "file:" + s;
        }

        // Windows relative path: \folder\file
        if (s.StartsWith("\\", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Replace("\\", "/");
            return UrlEncode(s);
        }

        // macOS iTunes: Macintosh HD:Users:Toru:Music:...
        if (s.StartsWith("Macintosh HD:", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Replace("Macintosh HD:", "");
            s = s.Replace(":", "/");
            s = UrlEncode(s);
            return "file:///" + s;
        }

        // Windows drive letter: C:\path
        if (s.Length >= 3 && s[1] == ':' && s.Contains(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Replace("\\", "/");
            s = UrlEncode(s);
            return "file:///" + s;
        }

        // Windows relative path with backslashes
        if (s.Contains(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Replace("\\", "/");
            return UrlEncode(s);
        }

        // Already a URI
        if (s.Contains("://") || s.Contains(":/") || s.Contains("//") || s.Contains('#', StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }

        // Unix absolute path: /home/user/file
        if (s.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            s = "file://" + s;
            return UrlEncode(s);
        }

        // Unix relative path or relative URI
        return UrlEncode(s);
    }

    private static string ConvertMacPathToUnixPath(string strUri)
    {
        string s = strUri;

        if (s.StartsWith("Macintosh HD:", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Replace("Macintosh HD:", "");
            s = s.Replace(":", "/");
            s = "/" + s;
        }

        return s;
    }

    #endregion

    private async Task TryOpenStartupFile()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length <= 1)
            return;

        var file = args[1];
        var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();

        if (ext == ".m3u" || ext == ".m3u8")
        {
            await OpenM3u(file);
        }
        else if (ext == ".xspf")
        {
            await OpenXspf(file);
        }
        else if (ext == ".txt")
        {
            await OpenItunesTxt(file);
        }
    }

    #endregion

    #region == Commands ==

    #region == Add Files ==

    [RelayCommand(CanExecute = nameof(CanAddFilesExecute))]
    public async Task AddFilesAsync()
    {
        await _storageService.SelectOpenFilesAsync("Select files to add to new playlist", _allExtensions, true).ContinueWith(task =>
        {
            if (task.Result != null && task.Result.Count > 0)
            {
                var files = task.Result.ToArray();
                if (files == null || files.Length == 0)
                    return;

                switch (_currentPlaylistType)
                {
                    case PlaylistType.ITunesTsv:
                        AddFilesITunes(files);
                        break;

                    case PlaylistType.M3u:
                        AddFilesM3u(files);
                        break;

                    case PlaylistType.Xspf:
                        AddFilesXspf(files);
                        break;

                    case PlaylistType.New:
                        AddFilesNew(files);
                        break;

                    case PlaylistType.Error:
                        break;
                }
            }
        });
    }

    private bool CanAddFilesExecute()
    {
        if (IsBusy)
        {
            return false;
        }

        if (_currentPlaylistType == PlaylistType.Error)
        {
            return false;
        }

        return true;
    }

    private void AddFilesToPlaylist(string[] files)
    {
        if (files == null || files.Length == 0)
            return;

        switch (_currentPlaylistType)
        {
            case PlaylistType.ITunesTsv:
                AddFilesITunes(files);
                break;

            case PlaylistType.M3u:
                AddFilesM3u(files);
                break;

            case PlaylistType.Xspf:
                AddFilesXspf(files);
                break;

            case PlaylistType.New:
                AddFilesNew(files);
                break;

            case PlaylistType.Error:
                break;
        }
    }

    private void AddFilesITunes(string[] files)
    {
        /*
        // Convert TSV to NEW format
        var newLines = new List<string>();

        foreach (var item in Items)
        {
            var row = RawLines[item.OriginalIndex].Split('\t');
            if (row.Length >= 31)
            {
                var unix = ConvertMacPathToUnixPath(row[30]);
                newLines.Add(unix);
            }
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        //_currentPlaylistType = PlaylistType.New;

        // Append new files
        AddFilesNew(files);
        */

        IsDirty = true;

    }

    private void AddFilesM3u(string[] files)
    {
        /*
        // Rebuild RawLines in current order
        var ordered = Items.Select(x => RawLines[x.OriginalIndex - 1]).ToList();

        RawLines.Clear();
        foreach (var l in ordered)
            RawLines.Add(l);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        // Append new files
        AddFilesNew(files);
        */


        IsDirty = true;

    }

    private void AddFilesXspf(string[] files)
    {
        /*
        if (_xspfDoc == null)
            return;

        var trackList = _xspfDoc.Descendants("trackList").FirstOrDefault();
        if (trackList == null)
            return;

        int index = Items.Count;

        foreach (var file in files)
        {
            var uri = ConvertLocalPathToUri(file);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);

            RawLines.Add(uri);

            Items.Add(new PlaylistItem
            {
                Index = ++index,
                FilePath = uri,
                OriginalIndex = index
            });
        }
        */

        IsDirty = true;

    }

    private void AddFilesNew(string[] files)
    {
        if (_currentPlaylist is not PlaylistNew currentPlaylist)
        {
            return;
        }

        if (_currentPlaylistType != PlaylistType.New)
        {
            return;
        }

        int index = 0;

        foreach (var file in files)
        {
            Items.Add(new EntryNew(file)
            {
                Index = index,
            });
            index++;
        }

        IsDirty = true;

        // Enable/disable commands
        CanSaveItunesTxt = false;
        CanSaveM3u = true;
        CanSaveM3uAnsi = true;
        CanSaveM3uBom = true;
        CanSaveM3uUtf8 = true;
        CanSaveXspf = true;
        CanConvertToM3u = true;
        CanConvertToXspf = true;
    }

    #endregion

    #region == Remove ==

    [RelayCommand]
    public void RemoveSelected()
    {
        if (SelectedItem == null)
            return;

        //RawLines.RemoveAt(SelectedItem.OriginalIndex - 1);
        Items.Remove(SelectedItem);

        // Reindex
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
            //Items[i].OriginalIndex = i + 1;
        }

        IsDirty = true;
        IsRearrangedOrder = true;
    }

    #endregion

    #region == Find and Replace ==

    [RelayCommand]
    public void ReplaceAll()
    {
        if (string.IsNullOrWhiteSpace(FindText)) return;
        if (string.IsNullOrWhiteSpace(ReplaceText)) return;

        ReplaceAll(FindText, ReplaceText);
    }

    public void ReplaceAll(string find, string replace)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(find)) return;
        //if (RawLines.Count == 0) return;

        IsBusy = true;
        IsLocationEdited = true;
        IsDirty = true;

        StatusTextRight = "";
        StatusTextLeft = "Replacing...";
        IsStatusBarVisible = true;

        Items.Clear();

        switch (_currentPlaylistType)
        {
            case PlaylistType.ITunesTsv:
                ReplaceAllITunes(find, replace);
                break;

            case PlaylistType.M3u:
                ReplaceAllM3u(find, replace);
                break;

            case PlaylistType.Xspf:
                ReplaceAllXspf(find, replace);
                break;

            case PlaylistType.New:
                ReplaceAllNew(find, replace);
                break;

            case PlaylistType.Error:
                // do nothing
                break;
        }

        StatusTextRight = "";
        IsBusy = false;
    }

    private void ReplaceAllITunes(string find, string replace)
    {
        /*
        var newLines = new List<string>();

        for (int line = 0; line < RawLines.Count; line++)
        {
            var row = RawLines[line].Split('\t');

            if (row.Length >= 31)
            {
                row[30] = row[30].Replace(find, replace, StringComparison.Ordinal);

                // Rebuild TSV line
                var rebuilt = string.Join("\t", row);
                newLines.Add(rebuilt);

                Items.Add(new PlaylistItem
                {
                    Index = line + 1,
                    FilePath = row[30],
                    OriginalIndex = line + 1
                });
            }
            else
            {
                Items.Add(new PlaylistItem
                {
                    Index = line + 1,
                    FilePath = "Error",
                    OriginalIndex = line + 1
                });

                newLines.Add(RawLines[line]);
            }

            StatusTextRight = $"Processing... {line + 1}";
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
        */
    }

    private void ReplaceAllM3u(string find, string replace)
    {
        /*
        var newLines = new List<string>();
        int index = 1;

        for (int i = 0; i < RawLines.Count; i++)
        {
            var line = RawLines[i];

            if (!line.StartsWith("#", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(line))
            {
                var replaced = line.Replace(find, replace, StringComparison.Ordinal);
                newLines.Add(replaced);

                Items.Add(new PlaylistItem
                {
                    Index = index,
                    FilePath = replaced,
                    OriginalIndex = index
                });

                index++;
            }
            else
            {
                newLines.Add(line);
            }

            StatusTextRight = $"Processing... {index}";
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
        */
    }

    private void ReplaceAllXspf(string find, string replace)
    {
        /*
        if (_xspfDoc == null)
            return;

        var newLines = new List<string>();
        int index = 1;

        var tracks = _xspfDoc.Descendants("track");

        foreach (var track in tracks)
        {
            var loc = track.Element("location");
            if (loc == null) continue;

            var newValue = loc.Value.Replace(find, replace, StringComparison.Ordinal);
            loc.Value = newValue;

            newLines.Add(newValue);

            Items.Add(new PlaylistItem
            {
                Index = index,
                FilePath = newValue,
                OriginalIndex = index
            });

            StatusTextRight = $"Processing... [{index}]";
            index++;
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
        */
    }

    private void ReplaceAllNew(string find, string replace)
    {
        /*
        var newLines = new List<string>();

        for (int i = 0; i < RawLines.Count; i++)
        {
            var replaced = RawLines[i].Replace(find, replace, StringComparison.Ordinal);
            newLines.Add(replaced);

            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = replaced,
                OriginalIndex = i + 1
            });

            StatusTextRight = $"Processing... {i + 1}";
        }

        RawLines.Clear();
        foreach (var l in newLines)
            RawLines.Add(l);
        */
    }

    #endregion

    #region == Edit Path ==

    [RelayCommand]
    public void EditSelectedPath(string newValue)
    {
        /*
        if (SelectedItem == null)
            return;

        int i = SelectedItem.OriginalIndex; // Pascal Column2

        IsDirty = true;
        IsLocationEdited = true;

        switch (_currentPlaylistType)
        {
            case PlaylistType.ITunesTsv:
                EditITunesPath(i, newValue);
                break;

            case PlaylistType.M3u:
                EditM3uPath(i, newValue);
                break;

            case PlaylistType.Xspf:
                EditXspfPath(i, newValue);
                break;

            case PlaylistType.New:
                EditNewPath(i, newValue);
                break;

            case PlaylistType.Error:
                return;
        }

        // Update UI list
        SelectedItem.FilePath = newValue;
        */
    }

    private void EditITunesPath(int index, string newValue)
    {
        /*
        var row = RawLines[index].Split('\t');

        if (row.Length >= 31)
        {
            row[30] = newValue;
            RawLines[index] = string.Join("\t", row);
        }
        */
    }

    private void EditM3uPath(int index, string newValue)
    {
        //RawLines[index - 1] = newValue;
    }

    private void EditXspfPath(int index, string newValue)
    {
        /*
        if (_xspfDoc == null)
            return;

        var tracks = _xspfDoc.Descendants("track").ToList();
        if (index - 1 >= tracks.Count)
            return;

        var track = tracks[index - 1];
        var loc = track.Element("location");
        loc?.Value = newValue;

        RawLines[index - 1] = newValue;
        */
    }

    private void EditNewPath(int index, string newValue)
    {
        /*
        RawLines[index - 1] = newValue;
        */
    }


    [RelayCommand]
    private async Task CopySelectedPath()
    {
        /*
        if (SelectedItem == null)
            return;
        var path = SelectedItem.FilePath;
        try
        {
            var local = ConvertUriToLocalPath(path);
            await _storageService.CopyTextToClipboardAsync(local);
        }
        catch
        {
            // ignore errors
        }
        */
    }

    #endregion

    #region == New File ==

    [RelayCommand(CanExecute = nameof(CanNewFileExecute))]
    private async Task NewFileAsync()
    {
        if (IsBusy) return;

        if (await CheckIsDirtyClear())
        {
            ClearPlaylist();
        }
    }

    private bool CanNewFileExecute()
    {
        if (IsBusy)
        {
            return false;
        }

        if ((_currentPlaylistType == PlaylistType.New) && !IsDirty)
        {
            return false;
        }

        return true;
    }

    public void CreateNew(string[] files)
    {
        /*
        IsBusy = true;
        StatusTextLeft = "New";
        StatusTextRight = "";
        _currentPlaylistType = PlaylistType.New;

        RawLines.Clear();
        Items.Clear();

        int index = 1;

        foreach (var file in files)
        {
            Debug.WriteLine(file);
            RawLines.Add(file);

            Items.Add(new PlaylistItem
            {
                Index = index,
                FilePath = file,
                OriginalIndex = index
            });

            StatusTextRight = $"Processing... [{index}/{files.Length}]";
            index++;
        }

        StatusTextRight = "";
        IsBusy = false;

        // Enable/disable commands
        CanSaveM3u = true;
        CanConvertToM3u = true;
        CanConvertToXspf = true;

        CanSaveITunesTxt = false;
        CanSaveXspf = false;
        */
    }

    #endregion

    #region == Open file ==

    [RelayCommand]
    private async Task OpenFile()
    {
        if (IsBusy) return;

        if (!await CheckIsDirtyClear())
        {
            return;
        }

        ClearPlaylist();

        string[] allowedExtensions = new[] { "*.m3u", "*.m3u8", "*.xspf", "*.txt" };

        var result = await _storageService.SelectSingleOpenFileAsync("Select a playlist file", allowedExtensions);

        if (string.IsNullOrEmpty(result))
        {
            return;
        }

        _currentFilePath = result;
        await OpenPlaylistFile(result);
    }

    private async Task OpenPlaylistFile(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return;
        
        var extension = System.IO.Path.GetExtension(filename).ToLowerInvariant();

        if (extension == ".txt")
        {
            await OpenItunesTxt(filename);
        }
        else if (extension == ".m3u" || extension == ".m3u8")
        {
            await OpenM3u(filename);
        }
        else if (extension == ".xspf")
        {
            await OpenXspf(filename);
        }
    }

    private async Task OpenItunesTxt(string filename)
    {
        IsBusy = true;
        StatusTextLeft = "iTunes";
        StatusTextRight = "";

        _currentPlaylistType = PlaylistType.ITunesTsv;
        _currentPlaylist = new PlaylistItunes(filename);

        Items.Clear();
        _currentFilePath = filename;

        ProgressMinimum = 0;
        ProgressMaximum = 100;
        ProgressValue = 0;

        try
        {
            // Load UTF-16 → convert to UTF-8
            var text = File.ReadAllText(filename, Encoding.Unicode);
            var utf8 = Encoding.UTF8.GetString(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, Encoding.Unicode.GetBytes(text)));

            var lines = utf8.Split(_separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            int index = 0;
            int songIndex = 0;

            ProgressMaximum = lines.Length;

            foreach (var line in lines)
            {
                if (index == 0)
                {
                    if (_currentPlaylist is not PlaylistItunes currentPlaylist)
                    {
                        return;
                    }

                    currentPlaylist.HeaderLine = line;
                    index++;
                    continue; // skip header line
                }

                var cols = line.Split('\t');

                if (cols.Length <= 1)
                {
                    index++;
                    // end of line?
                    continue;
                }

                if (cols.Length < 31)
                {
                    Debug.WriteLine($"Error: Wrong column count. Invalid format! {cols.Length}:{line}");
                    StatusTextRight = $"Error: Wrong column count. Invalid format! {cols.Length}:{line}";
                    _currentPlaylistType = PlaylistType.Error;
                    IsBusy = false;
                    return;
                }

                var value = cols[30];

                /*
                RawLines.Add(value);

                Items.Add(new PlaylistItem
                {
                    Index = index,
                    FilePath = value,
                    OriginalIndex = index
                });
                */
                Items.Add(new EntryItunes(value)
                {
                    Index = songIndex,
                    OriginalLine = line,
                });

                ProgressValue = index;

                StatusTextRight = $"Processing... [{index}/{ProgressMaximum}]";
                index++;
                songIndex++;
            }

            StatusTextRight = "";
            IsBusy = false;

            // Enable/disable commands
            CanSaveItunesTxt = true;
            CanSaveM3u = true;
            CanSaveM3uAnsi = true;
            CanSaveM3uBom = true;
            CanSaveM3uUtf8 = true;
            CanSaveXspf = true;
            CanConvertToM3u = true;
            CanConvertToXspf = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening iTunes TXT file: {ex.Message}");
            StatusTextRight = $"Error opening iTunes TXT file: {ex.Message}";
            _currentPlaylistType = PlaylistType.Error;
            
        }
        finally
        {
            IsBusy = false;
            ProgressMinimum = 0;
            //ProgressMaximum = 0;
            ProgressValue = 0;
        }
    }

    private async Task OpenM3u(string filename)
    {
        IsBusy = true;
        StatusTextLeft = "M3U";
        StatusTextRight = "";
        _currentPlaylistType = PlaylistType.M3u;
        _currentPlaylist = new PlaylistM3u(filename);
        _currentFilePath = filename;

        //RawLines.Clear();
        Items.Clear();

        ProgressMinimum = 0;
        ProgressMaximum = 100;
        ProgressValue = 0;

        try
        {
            var lines = File.ReadAllLines(filename, Encoding.Default);

            int index = 0;
            int songIndex = 0;
            var extGroup = string.Empty;
            var extAlbum = string.Empty;
            var extArtist = string.Empty;
            var extGenre = string.Empty;
            var extAlbumArtUrl = string.Empty;
            var extLines = new List<string>();

            ProgressMaximum = lines.Length;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    continue;
                }

                var trimedline = line.Trim();

                if (trimedline.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimedline.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                    {
                        // File scope directive, must be the first line in the file.
                        (_currentPlaylist as PlaylistM3u)?.IsEXTM3U = true;
                    }
                    else if (trimedline.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase))
                    {
                        // File scope directive.
                        (_currentPlaylist as PlaylistM3u)?.PlaylistName = trimedline.Replace("#PLAYLIST:", "").Trim();
                    }
                    else if (trimedline.StartsWith("#EXTENC:", StringComparison.OrdinalIgnoreCase))
                    {
                        // File scope directive.
                        //(_currentPlaylist as PlaylistM3u)?.EXTENC = trimedline.Replace("#EXTENC:", "").Trim();
                    }
                    else if (trimedline.StartsWith("#EXTM3A:", StringComparison.OrdinalIgnoreCase))
                    {
                        // File scope directive, must be the first line in the file.
                        // Ignore.
                    }
                    else if (trimedline.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Muliti-line extension directive, applies to the following file path until it changes.
                        extGroup = trimedline.Replace("#EXTGRP:", "").Trim();
                    }
                    else if (trimedline.StartsWith("#EXTALB:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Muliti-line extension directive, applies to the following file path until it changes.
                        extAlbum = trimedline.Replace("#EXTALB:", "").Trim();
                    }
                    else if (trimedline.StartsWith("#EXTART:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Muliti-line extension directive, applies to the following file path until it changes.
                        extArtist = trimedline.Replace("#EXTART:", "").Trim();
                    }
                    else if (trimedline.StartsWith("#EXTGENRE:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Muliti-line extension directive, applies to the following file path until it changes.
                        extGenre = trimedline.Replace("#EXTGENRE:", "").Trim();
                    }
                    else if (trimedline.StartsWith("#EXTALBUMARTURL:", StringComparison.OrdinalIgnoreCase))
                    {
                        // VLC custom ext
                        extAlbumArtUrl = trimedline.Replace("#EXTALBUMARTURL:", "").Trim();
                    }
                    else if (trimedline.StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase))
                    {
                        // HLS EXT-X- directives, ignore for now, it is not a standard M3U directive, but used in HLS playlists.
                        // almost all tags starting with #EXT-X- act as stateful, multiline directives.
                    }
                    else if (trimedline.StartsWith("#EXT", StringComparison.OrdinalIgnoreCase))
                    {
                        // Saves single line extensions like #EXTINF, etc. to be associated with the next file path.
                        extLines.Add(trimedline);
                    }
                    else if (trimedline.StartsWith("#KODIPROP:", StringComparison.OrdinalIgnoreCase))
                    {
                        // KODI custom property
                        extLines.Add(trimedline);
                    }
                    else
                    {
                        // Pure comment.
                        extLines.Add(trimedline);
                    }

                    index++;
                    continue;
                }

                Items.Add(new EntryM3u(trimedline)
                {
                    Index = songIndex,
                    ExtGroup = extGroup,
                    ExtAlbum = extAlbum,
                    ExtArtist = extArtist,
                    ExtGenre = extGenre,
                    ExtAlbumArtUrl = extAlbumArtUrl,
                    ExtLines = extLines,
                });

                // Clear single scope extention directives (not multi-line like extGroup) after processing each file path.
                extLines = new List<string>();

                ProgressValue = index;

                StatusTextRight = $"Processing... [{index}/{lines.Length}]";
                index++;
                songIndex++;
            }

            StatusTextRight = "";
            IsBusy = false;

            // Enable/disable commands
            CanSaveItunesTxt = false;

            CanSaveM3u = true;
            CanSaveM3uAnsi = true;
            CanSaveM3uBom = true;
            CanSaveM3uUtf8 = true;
            CanSaveXspf = true;
            CanConvertToM3u = false;
            CanConvertToXspf = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening M3U file: {ex.Message}");
            StatusTextRight = $"Error opening M3U file: {ex.Message}";
            _currentPlaylistType = PlaylistType.Error;
            IsBusy = false;
            return;
        }
        finally
        {
            IsBusy = false;
            ProgressMinimum = 0;
            //ProgressMaximum = 0;
            ProgressValue = 0;
        }
    }

    private async Task OpenXspf(string filename)
    {
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";

        _currentPlaylistType = PlaylistType.Xspf;
        _currentPlaylist = new PlaylistXspf(filename);
        _currentFilePath = filename;

        //RawLines.Clear();
        Items.Clear();

        ProgressMinimum = 0;
        ProgressMaximum = 100;
        ProgressValue = 0;

        try
        {
            var doc = XDocument.Load(filename);
            if (doc.Root is null)
            {
                IsBusy = false;
                StatusTextLeft = "XSPF";
                StatusTextRight = "XSPF file has no root element.";
                Debug.WriteLine("XSPF file has no root element.");
                return;
            }

            // Store the XDocument in the PlaylistXspf instance for later use
            (_currentPlaylist as PlaylistXspf)?.SetXDocument(doc);

            XNamespace ns = "http://xspf.org/ns/0/";

            var tracks = doc.Root.Descendants(ns + "track");
            //Debug.WriteLine($"Found {tracks.Count()} tracks in XSPF file.");

            ProgressMaximum = tracks.Count() -1; // -1?

            int index = 0;
            int songIndex = 0;

            foreach (var track in tracks)
            {
                var loc = track.Element(ns + "location")?.Value;
                if (loc == null) continue;

                Items.Add(new EntryXspf(loc)
                {
                    Index = songIndex,
                    XElement = track
                });

                ProgressValue = index;

                StatusTextRight = $"Processing... [{index}/{ProgressMaximum}]";
                index++;
                songIndex++;
            }

            StatusTextRight = "";
            IsBusy = false;

            // Enable/disable commands
            CanSaveItunesTxt = false;

            CanSaveM3u = true;
            CanSaveM3uAnsi = true;
            CanSaveM3uBom = true;
            CanSaveM3uUtf8 = true;

            CanSaveXspf = true;

            CanConvertToM3u = true;
            CanConvertToXspf = false;
        }
        catch (Exception ex)
        {
            StatusTextLeft = "XSPF";
            StatusTextRight = $"Error: {ex.Message}";
            Debug.WriteLine($"Error opening XSPF file: {ex.Message}");
            return;
        }
        finally
        {
            IsBusy = false;
            ProgressMinimum = 0;
            //ProgressMaximum = 0;
            ProgressValue = 0;
        }
    }

    #endregion

    #region == Convert Format ==

    [RelayCommand]
    private void ConvertToXspf()
    {
        IsLocationEdited = true;
        IsDirty = true;

        switch (_currentPlaylistType)
        {
            case PlaylistType.ITunesTsv:
                ConvertITunesToXspf();
                break;

            case PlaylistType.M3u:
                ConvertM3uToXspf();
                break;

            case PlaylistType.New:
                ConvertNewToXspf();
                break;

            case PlaylistType.Xspf:
                // nothing to do, already XSPF
                break;
        }
    }

    private void ConvertITunesToXspf()
    {
        /*
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "Converting...";

        // Build XSPF XML
        var doc = new XDocument(
            new XElement("playlist",
                new XAttribute("xmlns", "http://xspf.org/ns/0/"),
                new XElement("trackList")
            )
        );

        var trackList = doc.Root!.Element("trackList")!;

        var newLines = new List<string>();

        // Items are already rearranged; use OriginalIndex to access RawLines
        foreach (var item in Items)
        {
            var line = RawLines[item.OriginalIndex]; // Pascal used Column2 as index into slMain
            var cols = line.Split('\t');

            if (cols.Length < 31)
                continue;

            var path = cols[30];
            var uri = ConvertLocalPathToUri(path);

            newLines.Add(uri);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);
        }

        _xspfDoc = doc;

        // Replace RawLines with URIs
        RawLines.Clear();
        foreach (var uri in newLines)
            RawLines.Add(uri);

        // Rebuild Items
        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        _currentPlaylistType = PlaylistType.Xspf;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";

        // Command flags
        CanSaveITunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = true;

        CanConvertToM3u = true;
        CanConvertToXspf = false;

        IsBusy = false;
        */
    }

    private void ConvertM3uToXspf()
    {
        /*
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "Converting...";

        var doc = new XDocument(
            new XElement("playlist",
                new XAttribute("xmlns", "http://xspf.org/ns/0/"),
                new XElement("trackList")
            )
        );

        var trackList = doc.Root!.Element("trackList")!;
        var newLines = new List<string>();

        foreach (var item in Items)
        {
            var line = RawLines[item.OriginalIndex - 1];

            if (line.StartsWith("#", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(line))
                continue;

            var uri = ConvertLocalPathToUri(line);
            newLines.Add(uri);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);
        }

        _xspfDoc = doc;

        RawLines.Clear();
        foreach (var uri in newLines)
            RawLines.Add(uri);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        _currentPlaylistType = PlaylistType.Xspf;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";

        CanSaveITunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = true;

        CanConvertToM3u = true;
        CanConvertToXspf = false;

        IsBusy = false;
        */
    }

    private void ConvertNewToXspf()
    {
        /*
        IsBusy = true;
        StatusTextLeft = "XSPF";
        StatusTextRight = "Converting...";

        var doc = new XDocument(
            new XElement("playlist",
                new XAttribute("xmlns", "http://xspf.org/ns/0/"),
                new XElement("trackList")
            )
        );

        var trackList = doc.Root!.Element("trackList")!;
        var newLines = new List<string>();

        foreach (var item in Items)
        {
            var line = RawLines[item.OriginalIndex - 1];

            if (line.StartsWith("#", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(line))
                continue;

            var uri = ConvertLocalPathToUri(line);
            newLines.Add(uri);

            var track = new XElement("track",
                new XElement("location", uri)
            );

            trackList.Add(track);
        }

        _xspfDoc = doc;

        RawLines.Clear();
        foreach (var uri in newLines)
            RawLines.Add(uri);

        Items.Clear();
        for (int i = 0; i < RawLines.Count; i++)
        {
            Items.Add(new PlaylistItem
            {
                Index = i + 1,
                FilePath = RawLines[i],
                OriginalIndex = i + 1
            });
        }

        _currentPlaylistType = PlaylistType.Xspf;
        StatusTextLeft = "XSPF";
        StatusTextRight = "";

        CanSaveITunesTxt = false;
        CanSaveM3u = false;
        CanSaveM3uAnsi = false;
        CanSaveM3uBom = false;
        CanSaveM3uUtf8 = false;
        CanSaveXspf = true;

        CanConvertToM3u = true;
        CanConvertToXspf = false;

        IsBusy = false;
        */
    }

    #endregion

    #region == Save File ==


    [RelayCommand(CanExecute = nameof(CanSaveFileExecute))]
    private async Task SaveFileAsync()
    {
        if (IsBusy) return;

        switch (_currentPlaylistType)
        {
            case PlaylistType.ITunesTsv:
                await SaveItunesTxtCoreAsync(_currentFilePath);
                break;

            case PlaylistType.M3u:
                await SaveM3uAsync();
                break;

            case PlaylistType.Xspf:
                await SaveXspfCoreAsync(_currentFilePath);
                break;

            case PlaylistType.New:
                await SaveAsAsync();
                break;

            case PlaylistType.Error:
                break;
        }
    }

    private bool CanSaveFileExecute()
    {
        if (IsBusy)
        {
            return false;
        }
        if (!IsDirty)
        {
            return false;
        }
        if (Items.Count <= 0)
        {
            return false;
        }
        if (_currentPlaylistType == PlaylistType.Error)
        {
            return false;
        }
        if (_currentPlaylistType == PlaylistType.New)
        {
            return false;
        }
        return true;
    }


    private async Task SaveAsAsync()
    {
        // Currently, disabled in CanSaveFileExecute().

        if (_currentPlaylistType != PlaylistType.New)
        {
            return;
        }

        // let's just save as M3U for now?


    }

    [RelayCommand(CanExecute = nameof(CanSaveItunesTxt))]
    private async Task SaveAsItunesTxtAsync()
    {
        if (IsBusy || Items.Count == 0 || _currentPlaylistType != PlaylistType.ITunesTsv || _currentPlaylist is not PlaylistItunes)
            return;

        string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);
        fileNameOnly ??= "playlist";

        string[] allowedExtensions = new[] { "*.txt" };
        var defaultFileName = fileNameOnly + ".txt";

        var storageFile = await _storageService.SelectSingleSaveFileAsync("Save a playlist file", defaultFileName, allowedExtensions);

        if (storageFile is null)
        {
            return;
        }

        await SaveItunesTxtCoreAsync(storageFile.Path.LocalPath);
    }

    private async Task SaveItunesTxtCoreAsync(string fileSavePath)
    {
        if (IsBusy || Items.Count == 0 || _currentPlaylistType != PlaylistType.ITunesTsv || _currentPlaylist is not PlaylistItunes currentPlaylist)
            return;

        if (string.IsNullOrEmpty(fileSavePath))
        {
            return;
        }

        try
        {
            var tsv = currentPlaylist.HeaderLine + "\r\n";

            foreach (var item in Items)
            {
                if (item is EntryItunes entryItunes)
                {
                    var line = entryItunes.OriginalLine;

                    // TODO: update if path is edited.
                    var cols = line.Split('\t');

                    if (cols.Length <= 1)
                    {
                        // end of line?
                        continue;
                    }

                    if (cols.Length < 31)
                    {
                        Debug.WriteLine($"Error: Wrong column count. Invalid format! {cols.Length}:{line}");
                        StatusTextRight = $"Error: Wrong column count. Invalid format! {cols.Length}:{line}";
                        _currentPlaylistType = PlaylistType.Error;
                        IsBusy = false;
                        return;
                    }

                    int columnIndex = 0;
                    var updatedLine = string.Empty;
                    foreach (var col in cols)
                    {
                        if (columnIndex == 30)
                        {
                            // Update the path.
                            updatedLine += item.FilePathOrUri + '\t';
                        }
                        else
                        {
                            updatedLine += col + '\t';
                        }
                        columnIndex++;
                    }

                    tsv += updatedLine + "\r\n";
                }
            }

            // Convert UTF‑8 → UTF‑16 LE with BOM
            var bytes = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes(tsv))
                .ToArray();

            File.WriteAllBytes(fileSavePath, bytes);

            StatusTextRight = $"Saved as txt: {fileSavePath}";
            IsDirty = false;
        }
        catch
        {
            StatusTextRight = $"Error saving txt: {fileSavePath}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveM3uExecute))]
    private async Task SaveM3uAsync()
    {
        string extension = System.IO.Path.GetExtension(_currentFilePath);

        await SaveM3uCore(false, false, extension, _currentFilePath);

        /*
        using (var reader = new StreamReader(_currentFilePath, Encoding.Default, detectEncodingFromByteOrderMarks: true))
        {
            // Force StreamReader to read the preamble/first character
            reader.Peek(); //

            // Get the preamble bytes expected for the detected encoding
            byte[] expectedPreamble = reader.CurrentEncoding.GetPreamble(); //

            // Check the underlying stream position
            long streamPosition = reader.BaseStream.Position;

            // If the stream position is exactly equal to the expected preamble length, 
            // and that preamble length is greater than 0, it means a BOM was found and consumed.
            if (expectedPreamble.Length > 0 && streamPosition == expectedPreamble.Length)
            {
                Console.WriteLine($"File has BOM. Encoding: {reader.CurrentEncoding.WebName}");
            }
            else
            {
                Console.WriteLine("File does NOT have a BOM. (StreamReader falls back to default)");
            }
        }
        */

        /*
        public static string GetBomStatus(string filePath)
        {
            // Create a small buffer to store the header bytes
            byte[] buffer = new byte[4];
        
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // Read up to 4 bytes
                int bytesRead = stream.Read(buffer, 0, 4);
            }

            // Match the byte signatures
            if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return "UTF-8 with BOM"; //
            
            if (buffer[0] == 0xFF && buffer[1] == 0xFE)
                return "UTF-16 Little Endian with BOM"; //
            
            if (buffer[0] == 0xFE && buffer[1] == 0xFF)
                return "UTF-16 Big Endian with BOM";
            
            if (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
                return "UTF-32 Big Endian with BOM"; //
            
            if (buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
                return "UTF-32 Little Endian with BOM";

            return "No BOM detected"; 
        }
        */
    }

    private bool CanSaveM3uExecute()
    {
        if (!CanSaveM3uUtf8)
        {
            return false;
        }

        if (!CanSaveM3uBom)
        {
            return false;
        }

        if (!CanSaveM3uUtf8)
        {
            return false;
        }

        if (!CanSaveM3uAnsi)
        {
            return false;
        }

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanSaveM3uUtf8))]
    private Task SaveAsM3uUtf8Async() => SaveAsM3uAsync(ansi: false, utf8Bom: false, extension: ".m3u");

    [RelayCommand(CanExecute = nameof(CanSaveM3uBom))]
    private Task SaveAsM3uBomAsync() => SaveAsM3uAsync(ansi: false, utf8Bom: true, extension: ".m3u");

    [RelayCommand(CanExecute = nameof(CanSaveM3uUtf8))]
    private Task SaveAsM3u8Async() => SaveAsM3uAsync(ansi: false, utf8Bom: true, extension: ".m3u8");

    [RelayCommand(CanExecute = nameof(CanSaveM3uAnsi))]
    private Task SaveAsM3uAnsiAsync() => SaveAsM3uAsync(ansi: true, utf8Bom: false, extension: ".m3u");

    private async Task SaveAsM3uAsync(bool ansi, bool utf8Bom, string extension)
    {
        if (IsBusy || Items.Count == 0 || _currentPlaylistType != PlaylistType.M3u || _currentPlaylist is not PlaylistM3u currentPlaylist)
            return;

        string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);
        fileNameOnly ??= "playlist";

        string[] allowedExtensions = new[] { "*.m3u", "*.m3u8" };
        var defaultFileName = fileNameOnly + extension;

        if (ansi)
        {
            allowedExtensions = new[] { "*.m3u" };
        }

        var storageFile = await _storageService.SelectSingleSaveFileAsync("Save a playlist file", defaultFileName, allowedExtensions);

        if (storageFile is null)
        {
            return;
        }

        await SaveM3uCore(ansi, utf8Bom, extension, storageFile.Path.LocalPath);
    }

    private async Task SaveM3uCore(bool ansi, bool utf8Bom, string extension, string fileSavePath)
    {
        if (IsBusy || Items.Count == 0 || _currentPlaylistType != PlaylistType.M3u || _currentPlaylist is not PlaylistM3u currentPlaylist)
            return;

        if (string.IsNullOrEmpty(fileSavePath))
        {
            return;
        }

        try
        {
            var text = string.Empty;

            if (currentPlaylist.IsEXTM3U)
            {
                text += "#EXTM3U" + Environment.NewLine;//"\r\n";
            }

            if (!string.IsNullOrWhiteSpace(currentPlaylist.PlaylistName))
            {
                text += "#PLAYLIST:" + currentPlaylist.PlaylistName + Environment.NewLine;//"\r\n";
            }

            foreach (var item in Items)
            {
                if (item is not EntryM3u entryM3u)
                {
                    return;
                }

                text += "#EXTGRP:" + entryM3u.ExtGroup + Environment.NewLine;//"\r\n";
                text += "#EXTALB:" + entryM3u.ExtAlbum + Environment.NewLine;//"\r\n";
                text += "#EXTART:" + entryM3u.ExtArtist + Environment.NewLine;//"\r\n";
                text += "#EXTGENRE:" + entryM3u.ExtGenre + Environment.NewLine;//"\r\n";
                text += "#EXTALBUMARTURL:" + entryM3u.ExtAlbumArtUrl + Environment.NewLine;//"\r\n";

                if (entryM3u.ExtLines.Count > 0)
                {
                    foreach (var extLine in entryM3u.ExtLines)
                    {
                        text += extLine + Environment.NewLine;//"\r\n";
                    }
                }

                text += entryM3u.FilePathOrUri + Environment.NewLine;//"\r\n";
            }

            // Clean up dupe directives.
            var finalText = string.Empty;

            var extGroup = string.Empty;
            var extAlbum = string.Empty;
            var extArtist = string.Empty;
            var extGenre = string.Empty;
            var extAlbumArtUrl = string.Empty;

            using (var reader = new StringReader(text))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Replace("#EXTGRP:", "").Trim();
                        if (value.Equals(extGroup, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // skip duplicate directive
                        }
                        extGroup = value;
                    }
                    else if (line.StartsWith("#EXTALB:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Replace("#EXTALB:", "").Trim();
                        if (value.Equals(extAlbum, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // skip duplicate directive
                        }
                        extAlbum = value;
                    }
                    else if (line.StartsWith("#EXTART:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Replace("#EXTART:", "").Trim();
                        if (value.Equals(extArtist, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // skip duplicate directive
                        }
                        extArtist = value;
                    }
                    else if (line.StartsWith("#EXTGENRE:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Replace("#EXTGENRE:", "").Trim();
                        if (value.Equals(extGenre, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // skip duplicate directive
                        }
                        extGenre = value;
                    }
                    else if (line.StartsWith("#EXTALBUMARTURL:", StringComparison.OrdinalIgnoreCase))
                    {
                        // VLC custom ext
                        var value = line.Replace("#EXTALBUMARTURL:", "").Trim();
                        if (value.Equals(extAlbumArtUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // skip duplicate directive
                        }
                        extAlbumArtUrl = value;
                    }

                    finalText += line + Environment.NewLine;
                }
            }

            // Write the final text to the file with the specified encoding
            if (ansi)
            {
                //var bytes = Encoding.GetEncoding("shift_jis").GetBytes(text);
                var bytes = Encoding.GetEncoding(Encoding.Default.CodePage).GetBytes(finalText);
                File.WriteAllBytes(fileSavePath, bytes);
            }
            else if (utf8Bom)
            {
                var bytes = Encoding.UTF8.GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes(finalText))
                    .ToArray();

                File.WriteAllBytes(fileSavePath, bytes);
            }
            else
            {
                File.WriteAllText(fileSavePath, finalText, new UTF8Encoding(false));
            }

            StatusTextRight = $"Saved as m3u: {fileSavePath}";
            IsDirty = false;
        }
        catch
        {
            StatusTextRight = $"Error saving m3u: {fileSavePath}";
        }

    }

    [RelayCommand(CanExecute = nameof(CanSaveXspf))]
    private async Task SaveAsXspfAsync()
    {
        if (IsBusy || Items.Count == 0 || _currentPlaylistType != PlaylistType.Xspf || _currentPlaylist is not PlaylistXspf currentPlaylist)
            return;

        string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);
        fileNameOnly ??= "playlist";

        string[] allowedExtensions = new[] { "*.xspf" };
        var defaultFileName = fileNameOnly + ".xspf";

        var storageFile = await _storageService.SelectSingleSaveFileAsync("Save a playlist file", defaultFileName, allowedExtensions);

        if (storageFile is null)
        {
            return;
        }

        await SaveXspfCoreAsync(storageFile.Path.LocalPath);
    }

    private async Task SaveXspfCoreAsync(string fileSavePath)
    {
        if (IsBusy || Items.Count == 0 || _currentPlaylistType != PlaylistType.Xspf || _currentPlaylist is not PlaylistXspf currentPlaylist)
            return;

        if (string.IsNullOrEmpty(fileSavePath))
        {
            return;
        }

        var doc = currentPlaylist.XspfDocument;
        if (doc is null)
        {
            StatusTextRight = $"Error: XSPF document is null.";
            return;
        }

        var root = doc.Root;
        if (root is null)
        {
            StatusTextRight = $"Error: XSPF document root is null.";
            return;
        }

        XNamespace ns = "http://xspf.org/ns/0/";

        var trackList = root.Descendants(ns + "trackList").FirstOrDefault();
        if (trackList is null)
        {
            StatusTextRight = $"Error: XSPF document trackList is null.";
            return;
        }

        try
        {
            // Clear existing tracks
            trackList.Elements().Remove();

            foreach (var item in Items)
            {
                if (item is not EntryXspf entryXspf)
                {
                    return;
                }

                if (entryXspf.FilePathOrUri is null)
                {
                    StatusTextRight = $"Error: EntryXspf FilePathOrUri is null.";
                    continue;
                }

                var existingTrack = entryXspf.XElement;
                if (existingTrack is not null)
                {
                    // Update the location element
                    var locationElement = existingTrack.Element("location");
                    locationElement?.Value = entryXspf.FilePathOrUri;
                    trackList.Add(existingTrack);
                }
                else
                {
                    // Should not happen, but if the XElement is null, create a new track element.

                    trackList.Add(
                        new XElement("track",
                            new XElement("location", entryXspf.FilePathOrUri)
                        )
                    );
                }
            }

            doc.Save(fileSavePath);
            StatusTextRight = $"Saved as xspf: {fileSavePath}";
            IsDirty = false;
        }
        catch
        {
            StatusTextRight = $"Error saving xspf: {fileSavePath}";
        }
    }

    #endregion

    #region == Menu Items ==

    [RelayCommand]
    private void MoveItemUp()
    {
        if (SelectedItem is null)
            return;

        MoveItemUp(SelectedItem);
    }

    [RelayCommand]
    private void MoveItemDown()
    {
        if (SelectedItem is null)
            return;

        MoveItemDown(SelectedItem);
    }

    public void MoveItemUp(Entry item)
    {
        int index = Items.IndexOf(item);
        if (index <= 0)
            return;

        Items.RemoveAt(index);
        Items.Insert(index - 1, item);

        IsRearrangedOrder = true;
        IsDirty = true;

        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
            //Items[i].OriginalIndex = i + 1;
        }
    }

    public void MoveItemDown(Entry item)
    {
        int index = Items.IndexOf(item);
        if (index < 0 || index >= Items.Count - 1)
            return;

        Items.RemoveAt(index);
        Items.Insert(index + 1, item);

        IsRearrangedOrder = true;
        IsDirty = true;

        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
            //Items[i].OriginalIndex = i + 1;
        }
    }


    [RelayCommand]
    private void OpenContainingFolder()
    {
        if (SelectedItem == null)
            return;
        /*
        var path = SelectedItem.FilePath;

        try
        {
            var local = ConvertUriToLocalPath(path);
            var folder = System.IO.Path.GetDirectoryName(local);

            if (folder != null && Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // ignore errors
        }
        */
    }


    #endregion

    #endregion

}

