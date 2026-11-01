using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditorX.Models;

public class PlaylistItunes : Playlist
{
    public PlaylistItunes(string? filePath) : base()
    {
        FilePath = filePath;
    }

    public string HeaderLine { get; set; } = string.Empty;
}
