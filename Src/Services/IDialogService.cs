using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using PlaylistEditor.ViewModels;
using PlaylistEditor.Services;
using static PlaylistEditor.Services.DialogService;

namespace PlaylistEditor.Services;

public interface IDialogService
{
    Task<bool> ShowConfirmAsync(string title, string message);
    Task<string?> ShowEditDialog(string initialValue);
    Task<(string?, string?)> ShowFindAndReplaceDialog();
    Task ShowAboutDialog(string title, MainViewModel viewModel);

    bool IsDialogOpen { get; }
}
