using System.Windows.Controls;
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
    private string? _oivInstallerExecutable;
    private string? _lastImportedOivPath;
    private OivManifestInspectionResult? _lastOivInspection;
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
        await RefreshImportedPackageLibraryAsync();
        RefreshSessionState();
    }

    private string PackageLibraryRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GtaTransformationHub", "Packages");

    private async Task RefreshImportedPackageLibraryAsync()
    {
        try
        {
            var entries = await new ImportedModLibraryService().LoadAsync(PackageLibraryRoot);
            ModList.Children.Clear();

            var cleanInventories = entries.Count(entry => entry.ManifestValid);
            LibrarySummaryText.Text =
                entries.Count + " imported package(s) | " + cleanInventories +
                " per-file SHA-256 checks clear | source archive digest is not re-verified | no packages approved";

            if (entries.Count == 0)
            {
                LibrarySummaryText.Text = "No packages imported. Importing an archive only stores it for inspection.";
                ModList.Children.Add(new TextBlock
                {
                    Text = "No local research packages imported yet. Import a ZIP/OIV archive to inspect it.",
                    Foreground = (System.Windows.Media.Brush)FindResource("SecondaryText"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                });
                return;
            }

            foreach (var entry in entries)
            {
                var card = new Border
                {
                    BorderBrush = (System.Windows.Media.Brush)FindResource("PanelBackground"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var details = new StackPanel();
                details.Children.Add(new TextBlock
                {
                    Text = entry.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                details.Children.Add(new TextBlock
                {
                    Text = entry.Status + "  |  " + entry.FileCount + " files  |  " + entry.Version,
                    Foreground = (System.Windows.Media.Brush)FindResource("SecondaryText"),
                    FontSize = 10,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                details.Children.Add(new TextBlock
                {
                    Text = entry.Author + "  |  " +
                           (entry.ImportedAt == DateTimeOffset.MinValue
                               ? "Import timestamp unavailable"
                               : entry.ImportedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm")),
                    Foreground = (System.Windows.Media.Brush)FindResource("SecondaryText"),
                    FontSize = 10,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                details.Children.Add(new TextBlock
                {
                    Text = entry.ManifestValid
                        ? entry.VerificationNote
                        : string.Join(Environment.NewLine, entry.Warnings.Take(3)),
                    Foreground = (System.Windows.Media.Brush)FindResource("SecondaryText"),
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });

                var openFolderButton = new Button
                {
                    Content = "OPEN PACKAGE FOLDER",
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                openFolderButton.Click += (_, _) =>
                {
                    if (Directory.Exists(entry.PackageDirectory))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = entry.PackageDirectory,
                            UseShellExecute = true
                        });
                    }
                };
                details.Children.Add(openFolderButton);

                var verifyButton = new Button
                {
                    Content = "VERIFY FILE SHA-256",
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                verifyButton.Click += async (_, _) =>
                {
                    verifyButton.IsEnabled = false;
                    StatusText.Text = "Recomputing SHA-256 for " + entry.Name + "...";
                    try
                    {
                        var verification = await new ImportedModLibraryService().VerifyAsync(
                            PackageLibraryRoot, entry.PackageDirectory);
                        if (verification.IsValid)
                        {
                            StatusText.Text = entry.Name + ": file hashes verified. Package remains research-only.";
                            MessageBox.Show(
                                verification.FilesChecked + " file(s) passed SHA-256 verification.\n\n" +
                                "This verifies the imported files against their local manifest only. " +
                                "It does not verify the author's identity, licence, compatibility or safety, and does not install the mod.",
                                "Package integrity check passed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            StatusText.Text = entry.Name + ": integrity check found issues.";
                            MessageBox.Show(
                                string.Join(Environment.NewLine, verification.Errors.Take(12)),
                                "Package integrity check failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    catch (Exception exception)
                    {
                        StatusText.Text = "Package integrity check failed.";
                        MessageBox.Show(exception.Message, "Verification failed",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        verifyButton.IsEnabled = true;
                    }
                };
                details.Children.Add(verifyButton);
                card.Child = details;
                ModList.Children.Add(card);
            }
        }
        catch (Exception exception)
        {
            ModList.Children.Clear();
            LibrarySummaryText.Text = "Local package library could not be read.";
            ModList.Children.Add(new TextBlock
            {
                Text = "The local package library could not be read: " + exception.Message,
                Foreground = (System.Windows.Media.Brush)FindResource("SecondaryText"),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private async void RefreshLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning() || !_recoveryCompleted)
            return;

        RefreshLibraryButton.IsEnabled = false;
        try
        {
            await RefreshImportedPackageLibraryAsync();
            StatusText.Text = "Local package library refreshed. Imported packages remain research-only.";
        }
        finally
        {
            RefreshSessionState();
        }
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
            var result = await new ModArchiveImportService().ImportAsync(
                archiveDialog.FileName, PackageLibraryRoot, metadataDialog.Metadata);

            if (string.Equals(Path.GetExtension(archiveDialog.FileName), ".oiv", StringComparison.OrdinalIgnoreCase))
            {
                _lastImportedOivPath = Path.GetFullPath(archiveDialog.FileName);
                _lastOivInspection = result.OivInspection;
            }
            else
            {
                _lastImportedOivPath = null;
                _lastOivInspection = null;
            }

            var inspectionDetails = result.OivInspection is null
                ? "OIV manifest: not applicable (ZIP inventory only)."
                : "OIV format: " + result.OivInspection.FormatVersion + "\n" +
                  "OIV target: " + result.OivInspection.Target + "\n" +
                  "Game version marker: " + result.OivInspection.GameVersionRequirement + "\n" +
                  "Manifest package: " + result.OivInspection.Name + " (" + result.OivInspection.ModVersion + ")\n" +
                  "Operations: " + result.OivInspection.Operations.Count +
                  " | file adds/replacements: " + result.OivInspection.FileAddCount +
                  " | deletes: " + result.OivInspection.DeleteCount +
                  " | archive edits: " + result.OivInspection.ArchiveEditCount +
                  " | patch commands: " + result.OivInspection.PatchOperationCount + "\n" +
                  "Payload issues: " + result.OivInspection.PayloadSourceIssues.Count +
                  " | unrecognised commands: " + result.OivInspection.UnsupportedCommands.Count + "\n" +
                  "Manifest structural warnings: " + (result.OivInspection.RequiresManualReview ? "Present" : "None detected") + "\n" +
                  "Licence, game-build, compatibility and runtime review is still required before approval.";

            await RefreshImportedPackageLibraryAsync();
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
            await RefreshImportedPackageLibraryAsync();
            RefreshSessionState();
        }
    }

    private void GetOivInstallerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/crxhvrd/CodeWalkerProjects/releases/latest",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open the external OIV installer release page: " + ex.Message;
        }
    }

    private void SelectOivInstallerButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning() || !_recoveryCompleted)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select CodeWalker OIV Package Installer",
            Filter = "CodeWalker OIV installer (CodeWalker.OIVInstaller.exe)|CodeWalker.OIVInstaller.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var installer = new OivInstallerLauncherService().InspectInstallerExecutable(dialog.FileName);
            _oivInstallerExecutable = installer.ExecutablePath;
            OivEngineStatusText.Text =
                "Selected " + Path.GetFileName(installer.ExecutablePath) +
                " | file version " + (string.IsNullOrWhiteSpace(installer.FileVersion) ? "unreported" : installer.FileVersion) +
                " | SHA-256 " + installer.Sha256[..12] +
                " | signature not verified by Hub";
            StatusText.Text = "External OIV engine selected. The Hub will open packages for review; it will not silently install them.";
        }
        catch (Exception ex)
        {
            _oivInstallerExecutable = null;
            OivEngineStatusText.Text = "External OIV engine selection failed: " + ex.Message;
            StatusText.Text = "The selected external installer was rejected.";
        }
        finally
        {
            RefreshSessionState();
        }
    }

    private void OpenImportedOivButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning() || !_recoveryCompleted)
            return;
        if (string.IsNullOrWhiteSpace(_gameExecutable) ||
            string.IsNullOrWhiteSpace(_oivInstallerExecutable) ||
            string.IsNullOrWhiteSpace(_lastImportedOivPath) ||
            _lastOivInspection is null)
        {
            StatusText.Text = "Select and verify GTA V Legacy, select the external OIV engine, and import an OIV package first.";
            return;
        }

        var inspection = _lastOivInspection;
        if (!inspection.IsGtaVTarget)
        {
            MessageBox.Show("The imported package does not target GTA V. It will not be opened for this installation.",
                "OIV target mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (inspection.IsExplicitlyEnhanced)
        {
            MessageBox.Show("This package explicitly targets GTA V Enhanced. The selected game is GTA V Legacy.",
                "OIV edition mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (inspection.UnsupportedCommands.Count > 0 || inspection.PayloadSourceIssues.Count > 0)
        {
            MessageBox.Show(
                "The OIV manifest has unresolved payload references or commands this Hub cannot inspect. " +
                "The external installer handoff is blocked until those issues are resolved.\n\n" +
                "Unrecognised commands: " + string.Join(", ", inspection.UnsupportedCommands.Take(8)) + "\n" +
                "Payload issues: " + string.Join("\n", inspection.PayloadSourceIssues.Take(8)),
                "OIV preflight blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var allowUnspecified = !inspection.IsExplicitlyLegacy && !inspection.IsAnyGameVersion;
        if (allowUnspecified)
        {
            var answer = MessageBox.Show(
                "The OIV package does not explicitly declare GTA V Legacy compatibility. " +
                "Its target is GTA V, but the game-edition marker is missing or unrecognised.\n\n" +
                "Continue only if the package author's instructions confirm it supports your exact GTA V Legacy build.",
                "Confirm OIV Legacy compatibility",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        var finalAnswer = MessageBox.Show(
            "The Hub will now open this OIV in the separate CodeWalker installer.\n\n" +
            "The external installer will show its own package window. Review the author, files, game target and backup plan there, " +
            "then click Install yourself. The Hub is not performing or verifying the installation.\n\n" +
            "Continue?",
            "Open OIV for manual installation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (finalAnswer != MessageBoxResult.Yes)
            return;

        try
        {
            var process = new OivInstallerLauncherService().OpenPackageInInstaller(
                _oivInstallerExecutable,
                _lastImportedOivPath,
                _gameExecutable,
                inspection,
                allowUnspecifiedLegacyTarget: allowUnspecified);
            var processId = process.Id;
            process.Dispose();
            StatusText.Text = "OIV package opened in the external installer. No installation result has been confirmed by the Hub.";
            OivEngineStatusText.Text = "External installer launched (process " + processId + "). Complete/review installation inside that application.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "The external OIV installer could not be started.";
            OivEngineStatusText.Text = "OIV handoff failed: " + ex.Message;
            MessageBox.Show(ex.Message, "External OIV installer error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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

        _gameExecutable = null;
        LaunchButton.IsEnabled = false;
        try
        {
            var installation = new GameInstallationDetector().Inspect(dialog.FileName);
            _gameExecutable = installation.ExecutablePath;
            GamePathText.Text = installation.GameDirectory;
            DetectionText.Text =
                installation.Edition + " detected | executable version " + installation.FileVersion +
                " | SHA-256 " + installation.ExecutableSha256[..12] +
                " | mod compatibility not yet approved";
            StatusText.Text =
                "Legacy installation fingerprint recorded. Launch remains disabled until package compatibility, deployment and rollback are approved.";
        }
        catch (Exception ex)
        {
            GamePathText.Text = "GTA V Legacy not verified";
            DetectionText.Text = "Installation check failed: " + ex.Message;
            StatusText.Text = "No game installation was accepted. Select the actual GTA V Legacy root GTA5.exe.";
        }
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
        RefreshLibraryButton.IsEnabled = !running && _recoveryCompleted;
        SelectOivInstallerButton.IsEnabled = !running && _recoveryCompleted;
        OpenImportedOivButton.IsEnabled =
            !running && _recoveryCompleted &&
            _gameExecutable is not null &&
            _oivInstallerExecutable is not null &&
            _lastImportedOivPath is not null &&
            _lastOivInspection is not null;
        GetOivInstallerButton.IsEnabled = true;
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
