using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditorX.Models;

/*
#EXTM3U
#PLAYLIST:Global Media Network
#EXTGRP:Classic Rock
#EXTINF:254,Pink Floyd - Time
C:\Music\Rock\time.mp3
#EXTGRP:Live News
#EXTINF:-1 tvg-id="CNN" tvg-logo="https://example.com",CNN International
https://example.com
*/

public class PlaylistM3u : Playlist
{
    public PlaylistM3u(string? filePath) : base()
    {
        FilePath = filePath;
    }

    public Encoding DetectedEncoding { get; set; } = Encoding.UTF8;
    public bool HasBom { get; set; }

    public bool IsEXTM3U { get; set; }
    public string PlaylistName { get; set; } = string.Empty;

}

