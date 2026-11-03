using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PlaylistEditor.Models;
using PlaylistEditor.Services;
using PlaylistEditor.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PlaylistEditor.Views;

public partial class MainWindow : Window
{
    private readonly Services.IConfigurationService _configurationService;
    private readonly Services.IDialogService _dialogService;
    private readonly DataFormat<List<Entry>> _nodeFormat = DataFormat.CreateInProcessFormat<List<Entry>>("PlaylistItemFormat");
    private InsertionLineAdorner? _insertionLine;
    private AdornerLayer? _adornerLayer;
    private Point _pointerPressedPoint;
    private bool _isPressed;
    private bool _isPressedAndMoved;
    private bool _isDropAbove;
    private bool _isClosing;

    private int _winRestoreWidth = 1024;
    private int _winRestoreHeight = 768;
    private int _winRestoreTop = 100;
    private int _winRestoreLeft = 100;

    // Parameterless constructor for XAML previewer and designer.
    public MainWindow()
    {
        _configurationService = new Services.ConfigurationService();
        _dialogService = new Services.DialogService();
        InitializeComponent();
    }
    public MainWindow(Services.IConfigurationService configurationService, Services.IDialogService dialogService) : this()
    {
        _configurationService = configurationService;
        _dialogService = dialogService;

        //InitializeComponent();

        Bitmap bitmap = new(AssetLoader.Open(new Uri("avares://PlaylistEditor/Assets/PlaylistEditor.ico")));
        this.ImageAppIcon.Source = bitmap;

        var mw = _configurationService.CurrentConfiguration.MainWindow;

        // TODO: multi-monitor support. If the saved position is outside of the current monitor, move it to the monitor.
        if ((mw.Left >= 0) && (mw.Top >= 0))
        {
            this.Position = new PixelPoint(mw.Left, mw.Top);
        }
        else
        {
            this.Position = new PixelPoint(0, 0);
        }

        if (mw.Height >= 120)
        {
            this.Height = mw.Height;
        }

        if (mw.Width >= 480)
        {
            this.Width = mw.Width;
        }

        _winRestoreWidth = (int)mw.RestoreWidth;
        _winRestoreHeight = (int)mw.RestoreHeight;
        _winRestoreLeft = mw.RestoreLeft;
        _winRestoreTop = mw.RestoreTop;

        if (mw.WindowState == WindowState.Maximized)
        {
            this.WindowState = WindowState.Maximized;
        }
        else
        {
            this.WindowState = WindowState.Normal;
        }

        this.PlaylistListBox.AddHandler(InputElement.PointerPressedEvent, OnItemPointerPressed, RoutingStrategies.Tunnel);

        this.Closing += Window_Closing;
        this.SizeChanged += Window_SizeChanged;
        this.Activated += Window_Activated;
        this.Deactivated += Window_Deactivated;
        this.Resized += Window_Resized;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is not MainViewModel _mainViewModel)
        {
            return;
        }
        _mainViewModel.WorkingStateChanged += OnWorkingStateChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _adornerLayer = AdornerLayer.GetAdornerLayer(PlaylistListBox);
    }

    protected override void OnClosed(EventArgs e)
    {
        //this.Closing -= Window_Closing;
        this.SizeChanged -= Window_SizeChanged;
        this.Activated -= Window_Activated;
        this.Deactivated -= Window_Deactivated;
        this.Resized -= Window_Resized;

        base.OnClosed(e);

        if (DataContext is not MainViewModel _mainViewModel)
        {
            return;
        }
        _mainViewModel.WorkingStateChanged -= OnWorkingStateChanged;
    }

    private void OnWorkingStateChanged(object? sender, bool e)
    {
        if (e)
        {
            this.Cursor = new Cursor(StandardCursorType.AppStarting);
        }
        else
        {
            this.Cursor = new Cursor(StandardCursorType.Arrow);
        }
    }

    private async void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel)
            return;

        if (e.Source is not Control control || control.DataContext is not Entry item)
        {
            //Debug.WriteLine($"Not DataContext is Entry: {e.Source}");
            return;
        }

        //Debug.WriteLine($"PointerPressed on {item.Text}");
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var selectedItems = PlaylistListBox.SelectedItems;
        if (selectedItems == null || selectedItems.Count == 0)
            return;

        var isSelected = selectedItems.Contains(item);
        var payload = isSelected
            ? selectedItems.OfType<Entry>().ToList()
            : new List<Entry> { item };

        // Keep the ListBox from replacing the multi-selection on pointer-down.
        if (isSelected) e.Handled = true;

        _pointerPressedPoint = e.GetPosition(this);
        _isPressed = true;
        _isPressedAndMoved = false;

        while (_isPressed)
        {
            await Task.Delay(100);
            if (_isPressedAndMoved)
                break;
        }

        if (!_isPressedAndMoved)
        {
            //Debug.WriteLine("Pointer did not move");
            e.Handled = false;
            return;
        }

        var transferItem = DataTransferItem.Create(_nodeFormat, payload);

        // 2. Prepare the v12 DataTransfer container payload
        var dragData = new DataTransfer();

        dragData.Add(transferItem);

        // Initiate the asynchronous native drag frame
        // This will block synchronously until the user releases the cursor
        var result = await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Move);

        // Logic to remove node from its old parent if necessary

        if (result == DragDropEffects.Move)
        {
            e.Handled = true;
        }
        else
        {
            e.Handled = false;
        }

        Debug.WriteLine(result == DragDropEffects.Move
            ? "DoDragDropAsync result = Succuses (Move)"
            : "DoDragDropAsync result = Fail (None)");

        _isPressedAndMoved = false;
        _isPressed = false;

        RemoveInsertionLine();
    }

    private async void ListBox_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        var draggedNode = e.DataTransfer.TryGetValue(_nodeFormat);

        if (draggedNode is List<Entry> draggedNodes)
        {
            var pos = e.GetPosition(PlaylistListBox);
            AutoScroll(PlaylistListBox, pos);

            var currentPosition = e.GetPosition(PlaylistListBox);
            var visualTarget = PlaylistListBox.GetVisualAt(currentPosition);
            var targetListBoxItem = visualTarget?.FindAncestorOfType<ListBoxItem>();

            if (targetListBoxItem != null)
            {
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;

                var positionInItem = e.GetPosition(targetListBoxItem);
                var itemHeight = targetListBoxItem.Bounds.Height;
                _isDropAbove = positionInItem.Y < itemHeight / 2;

                var itemEdge = new Point(0, _isDropAbove ? 0 : itemHeight);
                var edgeInListBox = targetListBoxItem.TranslatePoint(itemEdge, PlaylistListBox);

                if (edgeInListBox is null)
                {
                    RemoveInsertionLine();
                    return;
                }

                ShowInsertionLine(edgeInListBox.Value.Y);

                return;
            }
        }

        // Fallback: If they hover over empty space or an invalid format, reject the drop
        e.DragEffects = DragDropEffects.None;

        RemoveInsertionLine();
    }

    private async void ListBox_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.DataTransfer.Formats.Contains(_nodeFormat))
        {
            var draggedNode = e.DataTransfer.TryGetValue(_nodeFormat);

            if (draggedNode is not null)
            {
                if ((e.Source as Control)?.DataContext is not Entry targetNode)
                {
                    e.DragEffects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                // Success.

                //Debug.WriteLine($"DraggedNode = {draggedNode.Text}, TargetNode = {targetNode?.Text}");

                vm.ReorderItems(draggedNode, targetNode, _isDropAbove);

                // Report success.
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;

                RemoveInsertionLine();

                PlaylistListBox.SelectedItems?.Clear();
            }

            e.Handled = true;
            return;
        }

        // Handle file drops from outside of the application.
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();

            if (files != null)
            {
                // Convert IStorageItem to local paths (for Desktop use cases)
                var localPaths = files
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => path != null)
                    ;//.ToArray();

                await vm.FilesDroped(localPaths);
            }

            e.Handled = true;
            return;
        }
    }

    private void ShowInsertionLine(double y)
    {
        if (_adornerLayer == null)
            return;

        if (_insertionLine == null)
        {
            _insertionLine = new InsertionLineAdorner();
            AdornerLayer.SetAdornedElement(_insertionLine, this.PlaylistListBox);
            _adornerLayer.Children.Add(_insertionLine);
        }

        _insertionLine.Y = y;
        _insertionLine.InvalidateVisual();
    }

    private void RemoveInsertionLine()
    {
        if (_adornerLayer == null)
            return;

        if (_insertionLine != null)
        {
            _adornerLayer.Children.Remove(_insertionLine);
            _insertionLine = null;
        }
    }

    private async void ListBox_FileDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();

            if (files != null)
            {
                var localPaths = files
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => path != null)
                    .ToArray();

                if (localPaths?.Length > 0)
                {
                    Debug.WriteLine($"ListBox_FileDrop: Files dropped: {localPaths.Length}");

                    //vm.AddFilesToPlaylist(localPaths!);
                    await vm.FilesDroped(localPaths);
                }
            }

            e.Handled = true;
        }
    }

    private static void AutoScroll(ListBox listBox, Avalonia.Point pointerPos)
    {
        const double scrollMargin = 30;

        if (listBox.Scroll != null)
        {
            if (pointerPos.Y < scrollMargin)
                listBox.Scroll.Offset = new Avalonia.Vector(listBox.Scroll.Offset.X, listBox.Scroll.Offset.Y - 10);

            else if (pointerPos.Y > listBox.Bounds.Height - scrollMargin)
                listBox.Scroll.Offset = new Avalonia.Vector(listBox.Scroll.Offset.X, listBox.Scroll.Offset.Y + 10);
        }
    }

    private void PlaylistListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (vm.SelectedItem == null)
            return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (!ctrl)
            return;

        if (e.Key == Key.Up)
        {
            vm.MoveItemUp(vm.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            vm.MoveItemDown(vm.SelectedItem);
            e.Handled = true;
        }
    }

    private void PlaylistListBox_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);

        // Calculate the distance moved
        var deltaX = Math.Abs(currentPoint.X - _pointerPressedPoint.X);
        var deltaY = Math.Abs(currentPoint.Y - _pointerPressedPoint.Y);

        if (deltaX > 4 || deltaY > 4)
        {
            // The pointer HAS moved beyond a tiny jitter threshold
            //Debug.WriteLine("Pointer is moved");
            _isPressedAndMoved = true;
            _isPressed = false;
        }
    }

    private void PlaylistListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        //_isPressedAndMoved = false;
        //_isPressed = false;
    }

    private void PlaylistListBox_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPressedAndMoved = false;
        _isPressed = false;
    }

    private async void Window_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel _mainViewModel)
        {
            return;
        }

        Debug.WriteLine($"Window_Closing: IsDirty = {_mainViewModel.IsDirty}");

        if (_dialogService.IsDialogOpen)
        {
            Debug.WriteLine("Window_Closing: Dialog is open, canceling close.");
            e.Cancel = true;
            return;
        }

        if (_mainViewModel.IsDirty)
        {
            e.Cancel = true;
            var confirm = await _dialogService.ShowConfirmAsync(
                "Playlist has been changed",
                "Do you wish to discard changes and close?"
            );
            if (!confirm)
            {
                return;
            }
        }

        this.Closing -= Window_Closing;

        if (_isClosing) return;
        _isClosing = true;

        e.Cancel = true; // Cancel the default close behavior to allow for async operations

        try
        {
            await SaveSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in Window_Closing: {ex.Message}");
        }
        
        this.Close();
    }

    private async Task SaveSettings()
    {
        if (DataContext is not MainViewModel _mainViewModel)
        {
            return;
        }

        _mainViewModel.UpdateSettings();

        var config = _configurationService?.CurrentConfiguration;

        if (config == null)
        {
            return;
        }

        // Window Position must be saved before the window is closed, otherwise the position will be lost.
        #region == Save Configuration ==

        config.MainWindow.Width = this.Width;
        config.MainWindow.Height = this.Height;
        config.MainWindow.Left = this.Position.X;
        config.MainWindow.Top = this.Position.Y;
        config.MainWindow.WindowState = this.WindowState;
        config.MainWindow.RestoreWidth = _winRestoreWidth;
        config.MainWindow.RestoreHeight = _winRestoreHeight;
        config.MainWindow.RestoreLeft = _winRestoreLeft;
        config.MainWindow.RestoreTop = _winRestoreTop;

        if (_configurationService is null)
        {
            Debug.WriteLine("ConfigurationService is null");
            return;
        }
        await _configurationService.SaveConfigurationAsync();

        #endregion

    }

    private void Window_Resized(object? sender, Avalonia.Controls.WindowResizedEventArgs e)
    {
        if (this.WindowState == WindowState.Normal)
        {
            _winRestoreHeight = (int)this.Height;
            _winRestoreWidth = (int)this.Width;
            _winRestoreTop = this.Position.Y;
            _winRestoreLeft = this.Position.X;
        }
    }

    private void Window_Activated(object? sender, System.EventArgs e)
    {
        this.AppTitleBarMenu.Opacity = 1;

        if (this.WindowState == WindowState.Normal)
        {
            _winRestoreHeight = (int)this.Height;
            _winRestoreWidth = (int)this.Width;
            _winRestoreTop = this.Position.Y;
            _winRestoreLeft = this.Position.X;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        this.AppTitleBarMenu.Opacity = 0.5;
    }

    private void Window_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (this.WindowState == WindowState.Normal)
        {
            _winRestoreHeight = (int)this.Height;
            _winRestoreWidth = (int)this.Width;
            _winRestoreTop = this.Position.Y;
            _winRestoreLeft = this.Position.X;
        }
    }
}