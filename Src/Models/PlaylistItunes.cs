using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditor.Models;

public class PlaylistItunes : Playlist
{
    public PlaylistItunes(string? filePath) : base()
    {
        FilePath = filePath;
    }

    public string HeaderLine { get; set; } = string.Empty;
}
