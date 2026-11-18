namespace GtaTransformationHub;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _instanceMutex;
    private bool _ownsMutex;

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

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_ownsMutex)
            _instanceMutex?.ReleaseMutex();

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
