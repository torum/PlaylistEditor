using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditorX.Models;

public abstract partial class Entry : ObservableObject
{
    public Entry(string? filePathOrUri)
    {
        FilePathOrUri = filePathOrUri;
    }

    [ObservableProperty]
    public partial string? FilePathOrUri { get; set; }

    [NotifyPropertyChangedFor(nameof(IndexPlusOne))]
    [ObservableProperty]
    public partial int Index { get; set; }

    public int IndexPlusOne => Index + 1;

}
