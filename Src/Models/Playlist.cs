using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace PlaylistEditorX.Models;

public abstract class Playlist
{
    public string? FilePath { get; set; }

    //public ObservableCollection<Entry> Entries { get; set; } = new ObservableCollection<Entry>();
}
