using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class MainWindow : Window
{
    private string? _gameExecutable;
    private bool _recoveryCompleted;
    private readonly DispatcherTimer _processTimer;

    public MainWindow()
    {
        InitializeComponent();
        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _processTimer.Tick += (_, _) => RefreshSessionState();
        _processTimer.Start();
        RefreshSessionState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LocateButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Checking for interrupted deployment transactions...";
        try
        {
            var recovered = await new TransactionalDeploymentService().RecoverInterruptedTransactionsAsync();
            _recoveryCompleted = true;
            StatusText.Text = recovered.Count == 0
                ? "Recovery check passed. No interrupted deployments were found."
                : $"Recovered {recovered.Count} interrupted deployment transaction(s).";
        }
        catch (Exception)
        {
            _recoveryCompleted = false;
            DetectionText.Text = "Recovery could not be verified. Configuration remains locked; restart the Hub after reviewing the transaction journal.";
            StatusText.Text = "Safety recovery failed. No installation or launch actions are available.";
        }
        RefreshSessionState();
    }
    private async void ImportArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning() || !_recoveryCompleted)
            return;

        var archiveDialog = new OpenFileDialog
        {
            Title = "Select a GTA V mod archive",
            Filter = "Mod archives (*.zip;*.oiv)|*.zip;*.oiv",
            CheckFileExists = true,
            Multiselect = false
        };
        if (archiveDialog.ShowDialog() != true)
            return;

        var metadataDialog = new ModImportDialog { Owner = this };
        if (metadataDialog.ShowDialog() != true || metadataDialog.Metadata is null)
            return;

        ImportArchiveButton.IsEnabled = false;
        StatusText.Text = "Inspecting archive and extracting into the local research library...";
        try
        {
            var importRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GtaTransformationHub", "Packages");
            var result = await new ModArchiveImportService().ImportAsync(
                archiveDialog.FileName, importRoot, metadataDialog.Metadata);

            var inspectionDetails = result.OivInspection is null
                ? "OIV manifest: not applicable (ZIP inventory only)."
                : "OIV format: " + result.OivInspection.FormatVersion + "\n" +
                  "OIV target: " + result.OivInspection.Target + "\n" +
                  "Manifest package: " + result.OivInspection.Name + " (" + result.OivInspection.ModVersion + ")\n" +
                  "Operations: " + result.OivInspection.Operations.Count +
                  " | file adds/replacements: " + result.OivInspection.FileAddCount +
                  " | deletes: " + result.OivInspection.DeleteCount +
                  " | archive edits: " + result.OivInspection.ArchiveEditCount + "\n" +
                  "Payload issues: " + result.OivInspection.PayloadSourceIssues.Count +
                  " | unrecognised commands: " + result.OivInspection.UnsupportedCommands.Count + "\n" +
                  "Manual review required: " + (result.OivInspection.RequiresManualReview ? "Yes" : "No");

            StatusText.Text = "Imported " + metadataDialog.Metadata.Name +
                              " for review. No GTA V files were changed; package status remains Researching.";
            DetectionText.Text = "Research package stored at: " + result.PackageDirectory;
            MessageBox.Show(
                "Archive imported and hashed successfully.\n\n" +
                "Files: " + result.Files.Count + "\n" +
                "SHA-256: " + result.ArchiveSha256 + "\n\n" +
                inspectionDetails + "\n\n" +
                "This import is quarantined for inspection only. It has NOT been approved or installed.",
                "Mod archive imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Archive import failed. No game files were changed.";
            MessageBox.Show(
                "The archive was not imported.\n\n" + ex.Message,
                "Mod archive import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            RefreshSessionState();
        }
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
        LaunchButton.IsEnabled = false;
        StatusText.Text = "Game path saved. Launch is disabled until verified package deployment is connected.";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSessionState();
        StatusText.Text = IsGameRunning()
            ? "GTA V is running or process state is uncertain. Configuration is locked."
            : "No GTA V process detected. Configuration is unlocked.";
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Launch is intentionally disabled until verified mod deployment and profile validation are connected.";
    }

    private bool IsGameRunning()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("GTA5"))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited) return true;
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited during inspection; continue checking.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Fail closed when process state cannot be inspected.
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            // Fail closed: uncertain process state must never permit deployment.
            return true;
        }
    }

    private void RefreshSessionState()
    {
        var running = IsGameRunning();
        LocateButton.IsEnabled = !running && _recoveryCompleted;
        ImportArchiveButton.IsEnabled = !running && _recoveryCompleted;
        RefreshButton.IsEnabled = !running && _recoveryCompleted;
        // Never launch from the dashboard until profile validation and transactional
        // deployment are wired into this exact path.
        LaunchButton.IsEnabled = false;

        SessionStateText.Text = running ? "GAME RUNNING · LOCKED" : "CONFIGURATION UNLOCKED";
        LockExplanationText.Text = running
            ? "GTA5.exe is active or its state cannot be verified. Mod changes and deployment are disabled."
            : "Game not running — configuration is available.";

        ModList.IsEnabled = !running;
    }

    protected override void OnClosed(EventArgs e)
    {
        _processTimer.Stop();
        base.OnClosed(e);
    }
}
