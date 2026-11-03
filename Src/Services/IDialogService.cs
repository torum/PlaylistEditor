using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using PlaylistEditorX.ViewModels;
using PlaylistEditorX.Services;
using static PlaylistEditorX.Services.DialogService;

namespace PlaylistEditorX.Services;

public interface IDialogService
{
    Task<bool> ShowConfirmAsync(string title, string message);
    Task<string?> ShowEditDialog(string initialValue);
    Task<(string?, string?)> ShowFindAndReplaceDialog();
    Task ShowAboutDialog(string title, MainViewModel viewModel);

    bool IsDialogOpen { get; }
}
