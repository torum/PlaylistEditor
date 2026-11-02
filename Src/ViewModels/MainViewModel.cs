using Avalonia.Controls.Shapes;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
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
    //private static readonly string[] _separator = new[] { "\r\n", "\n" };
    private static readonly string[] _separator = new[] { "\r\n", "\r", "\n" };
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
    public partial string FindText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReplaceText { get; set; } = string.Empty;

    #region == Status Bar ==

    [ObservableProperty]
    public partial string StatusTextLeft { get; set; } = "New";

    [ObservableProperty]
    public partial string StatusTextRight { get; set; } = "";

    [ObservableProperty]
    public partial bool IsStatusBarVisible { get; set; }

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

    #endregion

    #region == Command Availability ==

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

    #region == Error ==

    public FAInfoBarSeverity InfoBarSeverity
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
        }
    } = FAInfoBarSeverity.Warning;

    public string InfoBarMessage
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    public string InfoBarTitle
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    public bool IsInfoBarOpen
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
        }
    }

    #endregion

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
        // Not supported. iTunes TXT has a specific format and cannot be simply appended with arbitrary files.

        InfoBarTitle = "Not supported";
        InfoBarSeverity = FAInfoBarSeverity.Error;
        InfoBarMessage = $"Please convert to other format first. iTunes txt has a specific format and cannot be simply appended with arbitrary files.";
        IsInfoBarOpen = true;
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

        StatusTextRight = $"";
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

        StatusTextRight = $"";
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

        // Re-index
        int rindex = 0;

        foreach (var item in Items)
        {
            item.Index = rindex;
            rindex++;
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

        StatusTextRight = $"{index} files added.";
    }

    #endregion

    #region == Remove ==

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedExecute))]
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

    private bool CanRemoveSelectedExecute()
    {
        if (IsBusy)
        {
            return false;
        }
        if (SelectedItem == null)
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
        return true;
    }

    #endregion

    #region == Find and Replace ==

    [RelayCommand(CanExecute = nameof(CanReplaceAllExecute))]
    public void ReplaceAll()
    {
        if (string.IsNullOrWhiteSpace(FindText)) return;
        if (string.IsNullOrWhiteSpace(ReplaceText)) return;

        ReplaceAll(FindText, ReplaceText);
    }

    private bool CanReplaceAllExecute()
    {
        if (IsBusy)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(FindText))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(ReplaceText))
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
        return true;
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

    [RelayCommand(CanExecute = nameof(CanEditSelectedPathExecute))]
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

    private bool CanEditSelectedPathExecute()
    {
        if (IsBusy)
        {
            return false;
        }
        if (SelectedItem == null)
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
        return true;
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


    [RelayCommand(CanExecute = nameof(CanCopySelectedPathExecute))]
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

    private bool CanCopySelectedPathExecute()
    {
        if (IsBusy)
        {
            return false;
        }
        if (SelectedItem == null)
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
        return true;
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
            //var utf8 = Encoding.UTF8.GetString(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, Encoding.Unicode.GetBytes(text)));

            var lines = text.Split(_separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
                    //StatusTextRight = $"Error: Wrong column count. Invalid format! {cols.Length}:{line}";

                    InfoBarTitle = "Parse Error";
                    InfoBarSeverity = FAInfoBarSeverity.Error;
                    InfoBarMessage = $"Wrong column count. Invalid format! {cols.Length}:{line}";
                    IsInfoBarOpen = true;

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
            Debug.WriteLine($"Error opening iTunes TXT file: {ex}");
            //StatusTextRight = $"Error opening iTunes TXT file: {ex.Message}";
            _currentPlaylistType = PlaylistType.Error;

            InfoBarTitle = "Unexpected Error";
            InfoBarSeverity = FAInfoBarSeverity.Error;
            InfoBarMessage = $"Error opening iTunes TXT file: {ex.Message}";
            IsInfoBarOpen = true;

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

    private static bool IsPureAscii(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            if (b > 127) return false;
        }
        return true;
    }

    private static bool IsValidStrictUtf8(byte[] bytes)
    {
        try
        {
            Debug.WriteLine("IsValidStrictUtf8: Checking bytes for valid strict UTF-8 encoding.");
            // Second parameter 'true' forces exception on illegal UTF8 byte groupings
            var strictUtf8 = new UTF8Encoding(false, true);
            strictUtf8.GetString(bytes);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Encoding? HasBom(byte[] bytes)
    {
        Encoding? encoding = null;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            encoding = Encoding.UTF8;
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            encoding = Encoding.Unicode; // UTF-16 LE
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            encoding = Encoding.BigEndianUnicode; // UTF-16 BE
        else if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            encoding = Encoding.UTF32;

        return encoding;
    }

    private static (Encoding, bool) DetermineEncodingFromBytes(byte[] bytes)
    {
        // Check 1: Check for explicit Byte Order Marks (BOM)
        var bomEncoding = HasBom(bytes);
        if (bomEncoding != null) return (bomEncoding, true);

        // Check 2: Check for Pure ASCII (everything is under 128)
        if (IsPureAscii(bytes)) return (Encoding.ASCII, false);

        // Check 3: Test for valid UTF-8 sequences (without a BOM)
        if (IsValidStrictUtf8(bytes)) return (new UTF8Encoding(false), false);

        // Check 4: Complete Fallback (System Default ANSI page)
        return (Encoding.GetEncoding(0), false);
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
            //var lines = File.ReadAllLines(filename);

            // Read the file as bytes to handle encoding detection
            byte[] fileBytes = File.ReadAllBytes(filename);
            // Process the in-memory bytes to find the encoding
            var (detectedEncoding, hasBom) = DetermineEncodingFromBytes(fileBytes);
            Debug.WriteLine($"OpenM3u: Detected encoding: {detectedEncoding.EncodingName}");

            ReadOnlySpan<byte> byteSpan = fileBytes;

            // Check if the file starts with the Unicode preamble like UTF-8's BOM(0xEF, 0xBB, 0xBF)
            if (hasBom && byteSpan.StartsWith(detectedEncoding.Preamble))
            {
                // Slice past the 3-byte BOM
                byteSpan = byteSpan[detectedEncoding.Preamble.Length..];
            }

            // Decode the text using the already-loaded bytes
            string text = detectedEncoding.GetString(byteSpan);
            //string text = detectedEncoding.GetString(fileBytes);

            var lines = text.Split(_separator, StringSplitOptions.None);

            if (lines.Length <= 0)
            {
                Debug.WriteLine("OpenM3u: No lines found in the file.");
                IsBusy = false;

                InfoBarTitle = "File Open Error";
                InfoBarSeverity = FAInfoBarSeverity.Error;
                InfoBarMessage = $"Error opening M3U file: No text found.";
                IsInfoBarOpen = true;
                
                return;
            }

            (_currentPlaylist as PlaylistM3u)?.DetectedEncoding = detectedEncoding;
            (_currentPlaylist as PlaylistM3u)?.HasBom = hasBom;

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
            Debug.WriteLine($"Error opening M3U file: {ex}");
            //StatusTextRight = $"Error opening M3U file: {ex.Message}";
            _currentPlaylistType = PlaylistType.Error;
            IsBusy = false;

            InfoBarTitle = "Unexpected Error";
            InfoBarSeverity = FAInfoBarSeverity.Error;
            InfoBarMessage = $"Error opening M3U file: {ex.Message}";
            IsInfoBarOpen = true;

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
            //StatusTextRight = $"Error: {ex.Message}";
            Debug.WriteLine($"Error opening XSPF file: {ex}");

            InfoBarTitle = "Unexpected Error";
            InfoBarSeverity = FAInfoBarSeverity.Error;
            InfoBarMessage = $"Error opening XSPF file: {ex.Message}";
            IsInfoBarOpen = true;

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

    [RelayCommand(CanExecute = nameof(CanConvertToXspfExecute))]
    private void ConvertToXspf()
    {
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

    private bool CanConvertToXspfExecute()
    {
        if (IsBusy)
        {
            return false;
        }
        if (_currentPlaylistType == PlaylistType.Xspf)
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
        return true;
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


    [RelayCommand(CanExecute = nameof(CanConvertToM3uExecute))]
    private void ConvertToM3u()
    {
        switch (_currentPlaylistType)
        {
            case PlaylistType.ITunesTsv:
                ConvertItunesToM3u();
                break;
            case PlaylistType.Xspf:
                ConvertXspfToM3u();
                break;
            case PlaylistType.New:
                ConvertNewToM3u();
                break;
            case PlaylistType.M3u:
                // nothing to do, already M3U
                break;
        }
    }

    private bool CanConvertToM3uExecute()
    {
        if (IsBusy)
        {
            return false;
        }
        if (_currentPlaylistType == PlaylistType.M3u)
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
        return true;
    }

    private void ConvertItunesToM3u()
    {
        //
    }

    private void ConvertXspfToM3u()
    {
        //
    }

    private void ConvertNewToM3u()
    {
        //
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
        if (IsBusy || Items.Count == 0)
            return;

        if (_currentPlaylistType != PlaylistType.ITunesTsv || _currentPlaylist is not PlaylistItunes)
        {
            Debug.WriteLine("Error: This shouldn't be happening. Current playlist is not ITunesTsv. Cannot save as ITunesTsv.");
            return;
        }

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

            //File.WriteAllBytes(fileSavePath, bytes);
            var temporaryPath = fileSavePath + ".tmp";
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, fileSavePath, overwrite: true);

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
        if (IsBusy || Items.Count == 0 || _currentPlaylistType != PlaylistType.M3u|| _currentPlaylist is not PlaylistM3u currentPlaylist)
            return;

        string extension = System.IO.Path.GetExtension(_currentFilePath);

        // Save default as M3U UTF-8 without BOM.
        // Or.. the convention states it should be M3U ANSI if the original file was .m3u,
        // but real world usage is that .m3u is also UTF-8 and .m3u is not necessarily ANSI, so let's just save as UTF-8 without BOM.

        var detectedEncoding = currentPlaylist.DetectedEncoding;
        var hasBom = currentPlaylist.HasBom;

        if (detectedEncoding is null)
        {
            Debug.WriteLine("SaveM3uAsync: Detected encoding is null. Defaulting to UTF-8 without BOM.");
            detectedEncoding = new UTF8Encoding(false);
            hasBom = false;
        }

        await SaveM3uCore(detectedEncoding, hasBom, extension, _currentFilePath);
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

    [RelayCommand(CanExecute = nameof(CanSaveM3uAnsi))]
    private Task SaveAsM3uAnsiAsync() => SaveAsM3uAsync(encoding: Encoding.GetEncoding(0), hasBom: false, extension: ".m3u");

    [RelayCommand(CanExecute = nameof(CanSaveM3uUtf8))]
    private Task SaveAsM3uUtf8Async() => SaveAsM3uAsync(encoding: Encoding.UTF8, hasBom: false, extension: ".m3u");

    [RelayCommand(CanExecute = nameof(CanSaveM3uUtf8))]
    private Task SaveAsM3u8Utf8Async() => SaveAsM3uAsync(encoding: Encoding.UTF8, hasBom: false, extension: ".m3u8");

    [RelayCommand(CanExecute = nameof(CanSaveM3uBom))]
    private Task SaveAsM3uUtf8BomAsync() => SaveAsM3uAsync(encoding: Encoding.UTF8, hasBom: true, extension: ".m3u8");

    private async Task SaveAsM3uAsync(Encoding encoding, bool hasBom, string extension)
    {
        if (IsBusy || Items.Count == 0)
            return;

        if (_currentPlaylistType != PlaylistType.M3u || _currentPlaylist is not PlaylistM3u currentPlaylist)
        {
            Debug.WriteLine("TODO: Current playlist is not M3U. Cannot save as M3U.");
            return;
        }

        string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);
        fileNameOnly ??= "playlist";

        string[] allowedExtensions = new[] { "*.m3u", "*.m3u8" };
        var defaultFileName = fileNameOnly + extension;

        if ((encoding == Encoding.GetEncoding(0)) || (encoding == Encoding.ASCII))
        {
            allowedExtensions = new[] { "*.m3u" };
        }

        var storageFile = await _storageService.SelectSingleSaveFileAsync("Save a playlist file", defaultFileName, allowedExtensions);

        if (storageFile is null)
        {
            return;
        }

        await SaveM3uCore(encoding, hasBom, extension, storageFile.Path.LocalPath);
    }

    private async Task SaveM3uCore(Encoding encoding, bool hasBom, string extension, string fileSavePath)
    {
        if (IsBusy || Items.Count == 0)
            return;

        if (_currentPlaylistType != PlaylistType.M3u || _currentPlaylist is not PlaylistM3u currentPlaylist)
        {
            Debug.WriteLine("TODO: Current playlist is not M3U. Cannot save as M3U.");
            return;
        }

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

            // Passing 0 explicitly tells .NET to fetch the OS active ANSI code page.
            Encoding SystemDefaultEncoding = Encoding.GetEncoding(0);

            // Write the final text to the file with the specified encoding
            if (encoding == SystemDefaultEncoding)
            {
                //var bytes = Encoding.GetEncoding("shift_jis").GetBytes(text);

                /*
                // Override with detected encoding if it is ANSI.
                if (currentPlaylist.DetectedEncoding is not null && currentPlaylist.DetectedEncoding.CodePage != SystemDefaultEncoding.CodePage)
                {
                    SystemDefaultEncoding = currentPlaylist.DetectedEncoding;
                }
                */
                Debug.WriteLine($"Saving M3U as ANSI using code page: {SystemDefaultEncoding.WebName}");

                var bytes = SystemDefaultEncoding.GetBytes(finalText);
                //File.WriteAllBytes(fileSavePath, bytes);
                var temporaryPath = fileSavePath + ".tmp";
                File.WriteAllBytes(temporaryPath, bytes);
                File.Move(temporaryPath, fileSavePath, overwrite: true);
            }
            else if (encoding == Encoding.UTF8 && hasBom)
            {
                var bytes = Encoding.UTF8.GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes(finalText))
                    .ToArray();

                //File.WriteAllBytes(fileSavePath, bytes);
                var temporaryPath = fileSavePath + ".tmp";
                File.WriteAllBytes(temporaryPath, bytes);
                File.Move(temporaryPath, fileSavePath, overwrite: true);
            }
            else if (hasBom)
            {
                var bytes = encoding.GetPreamble()
                    .Concat(encoding.GetBytes(finalText))
                    .ToArray();

                //File.WriteAllBytes(fileSavePath, bytes);
                var temporaryPath = fileSavePath + ".tmp";
                File.WriteAllBytes(temporaryPath, bytes);
                File.Move(temporaryPath, fileSavePath, overwrite: true);
            }
            else
            {
                //File.WriteAllText(fileSavePath, finalText, new UTF8Encoding(false));
                var temporaryPath = fileSavePath + ".tmp";
                
                File.WriteAllText(temporaryPath, finalText, new UTF8Encoding(false));
                File.Move(temporaryPath, fileSavePath, overwrite: true);
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
        if (IsBusy || Items.Count == 0)
            return;

        if (_currentPlaylistType != PlaylistType.Xspf || _currentPlaylist is not PlaylistXspf currentPlaylist)
        {
            Debug.WriteLine("TODO: Current playlist is not XSPF. Cannot save as XSPF.");
            return;
        }

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
        if (IsBusy || Items.Count == 0)
            return;

        if (_currentPlaylistType != PlaylistType.Xspf || _currentPlaylist is not PlaylistXspf currentPlaylist)
        {
            Debug.WriteLine("TODO: Current playlist is not XSPF. Cannot save as XSPF.");
            return;
        }

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

            //doc.Save(fileSavePath);
            var temporaryPath = fileSavePath + ".tmp";
            doc.Save(temporaryPath);
            File.Move(temporaryPath, fileSavePath, overwrite: true);

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
