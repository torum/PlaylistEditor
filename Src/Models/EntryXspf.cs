using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace PlaylistEditor.Models;

public class EntryXspf : Entry
{
    private XElement? _xElement;

    public EntryXspf(string? filePath) : base(filePath)
    {

    }

    public XElement? XElement
    {
        get { return _xElement; }
        set { _xElement = value; }
    }

    public void SetXElement(XElement xElement)
    {
        _xElement = xElement;
    }

}
