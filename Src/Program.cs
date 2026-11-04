using Avalonia;
using System;

namespace PlaylistEditor
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Suppress the experimental warning required for ForceDrawnDecorations
#pragma warning disable AVALONIA_WAYLAND_FORCE_CSD
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseWaylandWithFallback()
                .With(new WaylandPlatformOptions()
                {
                    AppId = "PlaylistEditor",  // Matches .desktop launcher file
                    ForceDrawnDecorations = true    // Enables custom window decorations especially for KDE environments where they prefer server-side decorations.
                })
                .WithInterFont();
    }
#pragma warning restore AVALONIA_WAYLAND_FORCE_CSD
}
