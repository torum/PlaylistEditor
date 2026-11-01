using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using PlaylistEditorX.ViewModels;
using PlaylistEditorX.Services;
using static PlaylistEditorX.Services.DialogService;

namespace PlaylistEditorX.Services;

internal interface IDialogService
{
    Task<bool> ShowConfirmAsync(string title, string message);

    Task<AddToDialogResult?> ShowAddToDialog(MainViewModel vm);


}
