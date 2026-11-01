using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditorX.Models;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class EntryNew : Entry
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    public EntryNew(string? filePath) : base(filePath)
    {
        //
    }
}
