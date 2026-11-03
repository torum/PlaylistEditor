using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace PlaylistEditor.Models;

public class PlaylistXspf : Playlist
{
    private XDocument? _xspfDoc;

    public PlaylistXspf(string? filePath) : base()
    {
        FilePath = filePath;
    }

    public void SetXDocument(XDocument xspfDoc)
    {
        _xspfDoc = xspfDoc;
    }

    public XDocument? XspfDocument
    {
        get => _xspfDoc;
    }
}
