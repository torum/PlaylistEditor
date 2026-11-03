using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistEditor.Models;

public class Configuration
{
    public class WindowSettings
    {
        public double Width { get; set; } = 780;
        public double Height { get; set; } = 600;
        public int Left { get; set; } = 100;
        public int Top { get; set; } = 100;
        public WindowState WindowState { get; set; } = WindowState.Normal;

        public double RestoreWidth { get; set; } = 780;
        public double RestoreHeight { get; set; } = 600;
        public int RestoreLeft { get; set; } = 100;
        public int RestoreTop { get; set; } = 100;
    }

    public WindowSettings MainWindow { get; set; } = new WindowSettings();
    public bool IsSaveLog { get; set; }
    public string LastOpenedDirectory { get; set; } = string.Empty;

}
