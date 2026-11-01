using PlaylistEditorX.Models;
using PlaylistEditorX.Services;
//using PlaylistEditorX.Views.Dialogs;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

namespace PlaylistEditorX.Services;

internal sealed class DialogService : IDialogService
{
    public sealed record AddToDialogResult(string PlaylistName, bool AsNew);

    public DialogService()
    {
        //
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = title,
            IsPrimaryButtonEnabled = true,
            PrimaryButtonText = "OK",//MPDCtrlX.Properties.Resources.Dialog_Ok,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = "Cancel",//MPDCtrlX.Properties.Resources.Dialog_CancelClose,
            Content = message
            /*
            Content = new Views.Dialogs.Keybindings()
            {
                //DataContext = new DialogViewModel()
            }
            */
        };

        var result = await dialog.ShowAsync();

        if (result == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary)
        {
            return true;

        }
        else
        {
            return false;
        }
    }

    public async Task<AddToDialogResult?> ShowAddToDialog(ViewModels.MainViewModel vm)
    {
        if (vm is null)
        {
            return null;
        }
        return await Task.FromResult<AddToDialogResult?>(null);

        /*
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = MPDCtrlX.Properties.Resources.Dialog_Title_SelectPlaylist,
            IsPrimaryButtonEnabled = true,
            PrimaryButtonText = MPDCtrlX.Properties.Resources.Dialog_Ok,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = MPDCtrlX.Properties.Resources.Dialog_CancelClose,
            Content = new Views.Dialogs.SaveToDialog()
            {
                //DataContext = new DialogViewModel()
            }
        };

        if (dialog.Content is not SaveToDialog dialogContent)
        {
            return null;
        }

        // Sort
        CultureInfo ci = CultureInfo.CurrentCulture;
        StringComparer comp = StringComparer.Create(ci, true);

        //asdf.PlaylistListBox.ItemsSource = vm?.Playlists;
        //asdf.PlaylistListBox.ItemsSource = new ObservableCollection<Playlist>(vm.Playlists.OrderBy(x => x.Name, comp));
        dialogContent.PlaylistComboBox.ItemsSource = new ObservableCollection<Playlist>(vm.Playlists.OrderBy(x => x.Name, comp));

        var result = await dialog.ShowAsync();

        if (result == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary && dialog.Content is Views.Dialogs.SaveToDialog dlg)
        {

            if (dlg.CreateNewCheckBox.IsChecked is true)
            {
                var str = dlg.TextBoxPlaylistName.Text ?? string.Empty;

                if (!string.IsNullOrEmpty(str.Trim()))
                {
                    //vm?.QueueSaveToDialog_Execute(str.Trim());
                    return new AddToDialogResult(str.Trim(), true);
                }
            }
            else
            {
                var plselitem = dlg.PlaylistComboBox.SelectedItem;

                if (plselitem is Models.Playlist pl)
                {
                    if (!string.IsNullOrWhiteSpace(pl.Name))
                    {
                        //vm?.QueueSaveToDialog_Execute(pl.Name.Trim());
                        return new AddToDialogResult(pl.Name, false);
                    }

                }
            }
        }

        return null;
        */
    }
}
