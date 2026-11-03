using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditor.Models;

public class EntryItunes : Entry
{
    public EntryItunes(string? filePath) : base(filePath)
    {
    }

    public string OriginalLine { get; set; } = string.Empty;
}
