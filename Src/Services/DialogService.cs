using PlaylistEditorX.ViewModels;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PlaylistEditorX.Services;

public sealed class DialogService : IDialogService
{    
    public DialogService()
    {
        //
    }

    public bool IsDialogOpen { get; private set; }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        if (IsDialogOpen) return false;
        
        IsDialogOpen = true;

        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = title,
            IsPrimaryButtonEnabled = true,
            PrimaryButtonText = "OK",//PlaylistEditorX.Properties.Resources.Dialog_Ok,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = "Cancel",//PlaylistEditorX.Properties.Resources.Dialog_CancelClose,
            Content = message
        };

        var result = await dialog.ShowAsync();

        IsDialogOpen = false;

        if (result == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    // Returns null if the dialog is already open or if the user cancels the dialog.
    public async Task<string?> ShowEditDialog(string initialValue)
    {
        if (IsDialogOpen) return null;

        IsDialogOpen = true;

        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = "Edit Dialog",
            IsPrimaryButtonEnabled = true,
            PrimaryButtonText = "OK",//PlaylistEditorX.Properties.Resources.Dialog_Ok,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = "Cancel",//PlaylistEditorX.Properties.Resources.Dialog_CancelClose,
            Content = new Views.EditDialog()
            {
                //DataContext = new DialogViewModel()
            }
        };

        if (dialog.Content is not Views.EditDialog dlg)
        {
            IsDialogOpen = false;
            return null;
        }

        dlg.PathOrUriTextBox.Text = initialValue;

        var result = await dialog.ShowAsync();

        if (result != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary)
        {
            IsDialogOpen = false;
            return null;
        }

        var ret = dlg.PathOrUriTextBox.Text;
        if (ret is not null)
        {
            IsDialogOpen = false;
            return ret;
        }

        IsDialogOpen = false;
        return null;
    }

    // Returns null if the dialog is already open or if the user cancels the dialog.
    public async Task<(string?, string?)> ShowFindAndReplaceDialog()
    {
        if (IsDialogOpen) return (null, null);

        IsDialogOpen = true;

        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = "Find and Replace Dialog",
            IsPrimaryButtonEnabled = true,
            PrimaryButtonText = "OK",//PlaylistEditorX.Properties.Resources.Dialog_Ok,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = "Cancel",//PlaylistEditorX.Properties.Resources.Dialog_CancelClose,
            Content = new Views.FindAndReplaceDialog()
            {
                //DataContext = new DialogViewModel()
            }
        };

        if (dialog.Content is not Views.FindAndReplaceDialog dlg)
        {
            IsDialogOpen = false;
            return (null, null);
        }

        var result = await dialog.ShowAsync();

        if (result != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary)
        {
            IsDialogOpen = false;
            return (null, null);
        }

        var findText = dlg.FindTextBox.Text;
        var replaceText = dlg.ReplaceTextBox.Text;
        if ((findText is not null) && (replaceText is not null))
        {
            IsDialogOpen = false;
            return (findText, replaceText);
        }

        IsDialogOpen = false;
        return (null, null);
    }

    public async Task ShowAboutDialog(string title, MainViewModel viewModel)
    {
        if (IsDialogOpen) return;
        IsDialogOpen = true;
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = title,
            IsPrimaryButtonEnabled = true,
            PrimaryButtonText = "OK",//PlaylistEditorX.Properties.Resources.Dialog_Ok,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
            Content = new Views.AboutDialog()
            {
                DataContext = viewModel
            }
        };
        await dialog.ShowAsync();
        IsDialogOpen = false;
    }
}
