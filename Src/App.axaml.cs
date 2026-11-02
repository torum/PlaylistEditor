using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PlaylistEditorX.ViewModels;
using PlaylistEditorX.Views;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlaylistEditorX;

public partial class App : Application
{
    public const string AppName = "PlaylistEditorX";
    private const string AppDeveloper = "torum";

    // Error log file path.
    private static readonly StringBuilder _errortxt = new();
    private static readonly Lock _logLock = new();
    private static readonly string _logFilePath = System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + System.IO.Path.DirectorySeparatorChar + AppName + "_errors.txt";

    // Data folder and Config file path.
    private static readonly string _envDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string AppDataFolder { get; } = System.IO.Path.Combine(System.IO.Path.Combine(_envDataFolder, AppDeveloper), AppName);
    public static string AppConfigFilePath { get; } = System.IO.Path.Combine(AppDataFolder, AppName + ".config");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public async override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RegisterGlobalExceptionHandlers();

            // Register code page provider for ANSI/System Default code page encoding support.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Services.ConfigurationService configurationService = new();
            configurationService.Initialize(AppConfigFilePath);
            await configurationService.LoadConfigurationAsync();
            Services.StorageService storageService = new();
            Services.DialogService dialogService = new();

            desktop.MainWindow = new MainWindow(configurationService)
            {
                DataContext = new MainViewModel(storageService, dialogService, configurationService)
            };
            storageService.Initialize(desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // UI thread.
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;

        // Any thread, process is about to terminate.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // Faulted tasks nobody awaited.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Cancellation is normal (e.g. shutdown), not an error.
        if (e.Exception is OperationCanceledException)
        {
            e.Handled = true;
            return;
        }

        System.Diagnostics.Debug.WriteLine($"An unhandled exception occurred: {e.Exception}");
        AppendErrorLog("DispatcherUnhandledException", e.Exception.ToString());
        SaveErrorLog();

        // Keep the app alive for recoverable errors, but never for out-of-memory.
        e.Handled = e.Exception is not OutOfMemoryException;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppendErrorLog("AppDomain.UnhandledException", e.ExceptionObject?.ToString() ?? "(null)");
        SaveErrorLog(); // Last chance: the process terminates after this.
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (e.Exception.InnerExceptions.All(ex => ex is OperationCanceledException))
        {
            e.SetObserved();
            return;
        }

        AppendErrorLog("TaskScheduler.UnobservedTaskException", e.Exception.ToString());
        SaveErrorLog();
        e.SetObserved();
    }

    public static void AppendErrorLog(string source, string details)
    {
        var nowString = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

        lock (_logLock)
        {
            _errortxt.AppendLine(nowString + " - " + source + " - " + details);
        }
    }

    public static void SaveErrorLog()
    {
        try
        {
            lock (_logLock)
            {
                if (_errortxt.Length == 0)
                {
                    return;
                }

                Directory.CreateDirectory(AppDataFolder);
                File.WriteAllText(_logFilePath, _errortxt.ToString());
            }
        }
        catch (Exception ex)
        {
            // Logging must never take the app down.
            System.Diagnostics.Debug.WriteLine($"Could not save error log: {ex.Message}");
        }
    }
}