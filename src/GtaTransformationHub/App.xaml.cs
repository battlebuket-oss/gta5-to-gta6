using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _instanceMutex;
    private bool _ownsMutex;

    public static LocalAuditLogService Diagnostics { get; } = new();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _instanceMutex = new System.Threading.Mutex(
            initiallyOwned: true,
            name: @"Local\GtaTransformationHub.Singleton",
            createdNew: out _ownsMutex);

        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show(
                "GTA V Transformation Hub is already running. Use the existing window to avoid concurrent game-file operations.",
                "Transformation Hub already open",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            Diagnostics.Write(
                AuditLogLevel.Critical,
                "AppDomainUnhandledException",
                "An unhandled application-domain exception occurred. IsTerminating=" + args.IsTerminating,
                exception);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            Diagnostics.Write(
                AuditLogLevel.Critical,
                "DispatcherUnhandledException",
                "An unhandled UI-dispatcher exception occurred.",
                args.Exception);
            // Preserve WPF's default fail-fast behaviour for unhandled exceptions.
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Diagnostics.Write(
                AuditLogLevel.Error,
                "UnobservedTaskException",
                "An unobserved background-task exception was raised.",
                args.Exception);
        };

        Diagnostics.Write(AuditLogLevel.Information, "ApplicationStarted", "Transformation Hub started.");
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Diagnostics.Write(AuditLogLevel.Information, "ApplicationExit", "Transformation Hub is shutting down.");
        if (_ownsMutex)
            _instanceMutex?.ReleaseMutex();

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
