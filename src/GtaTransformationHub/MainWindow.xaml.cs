using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GtaTransformationHub;

public partial class MainWindow : Window
{
    private string? _gameExecutable;
    private Process? _gameProcess;
    private readonly DispatcherTimer _processTimer;

    public MainWindow()
    {
        InitializeComponent();
        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _processTimer.Tick += (_, _) => RefreshSessionState();
        _processTimer.Start();
        RefreshSessionState();
    }

    private void LocateButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Select GTA V Legacy executable",
            Filter = "GTA V executable (GTA5.exe)|GTA5.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) return;
        if (!string.Equals(Path.GetFileName(dialog.FileName), "GTA5.exe", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Select the GTA5.exe file from your GTA V Legacy installation.";
            return;
        }

        _gameExecutable = dialog.FileName;
        GamePathText.Text = Path.GetDirectoryName(dialog.FileName) ?? dialog.FileName;
        DetectionText.Text = "Executable selected. Edition and build validation is not yet implemented.";
        LaunchButton.IsEnabled = true;
        StatusText.Text = "Game path saved for this session. Mod deployment is not yet enabled.";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSessionState();
        StatusText.Text = IsGameRunning()
            ? "GTA V is running. Configuration is locked."
            : "No GTA V process detected. Configuration is unlocked.";
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning())
        {
            RefreshSessionState();
            return;
        }

        if (string.IsNullOrWhiteSpace(_gameExecutable) || !File.Exists(_gameExecutable))
        {
            StatusText.Text = "Locate a valid GTA5.exe before launching.";
            LaunchButton.IsEnabled = false;
            return;
        }

        // Deliberately do not deploy files yet. A verified transactional installer
        // and per-mod manifests must exist before the app can alter the game folder.
        try
        {
            _gameProcess = Process.Start(new ProcessStartInfo
            {
                FileName = _gameExecutable,
                WorkingDirectory = Path.GetDirectoryName(_gameExecutable)!,
                UseShellExecute = true
            });
            StatusText.Text = "GTA V launch requested. Configuration will lock while GTA5.exe is running.";
            RefreshSessionState();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not launch GTA V: {ex.Message}";
        }
    }

    private bool IsGameRunning()
    {
        try
        {
            return Process.GetProcessesByName("GTA5").Any(p =>
            {
                try { return !p.HasExited; }
                catch { return false; }
            });
        }
        catch
        {
            return false;
        }
    }

    private void RefreshSessionState()
    {
        var running = IsGameRunning();
        LocateButton.IsEnabled = !running;
        RefreshButton.IsEnabled = !running;
        LaunchButton.IsEnabled = !running && !string.IsNullOrWhiteSpace(_gameExecutable) && File.Exists(_gameExecutable);

        SessionStateText.Text = running ? "GAME RUNNING · LOCKED" : "CONFIGURATION UNLOCKED";
        LockExplanationText.Text = running
            ? "GTA5.exe is active. Mod changes and deployment are disabled until the process exits."
            : "Game not running — configuration is available.";

        if (running)
        {
            ModList.IsEnabled = false;
            LaunchButton.IsEnabled = false;
        }
        else
        {
            ModList.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _processTimer.Stop();
        base.OnClosed(e);
    }
}
