using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditor.Models;

//time(sec),display name
//#EXTINF:254,Artist - SongTitle
// -1 (streaming), some tags, display name
//#EXTINF:-1 tvg-id="CNN.us" tvg-name="CNN International" tvg-logo="https://example.com" group-title="Live News" channel-id="101",CNN International

public class EntryM3u : Entry
{
    public EntryM3u(string? filePath) : base(filePath)
    {

    }
    
    public string ExtGroup { get; set; } = string.Empty;
    public string ExtAlbum { get; set; } = string.Empty;
    public string ExtArtist { get; set; } = string.Empty;
    public string ExtGenre { get; set; } = string.Empty;
    public string ExtAlbumArtUrl { get; set; } = string.Empty;

    public List<string> ExtLines { get; set; } = new List<string>();
}
