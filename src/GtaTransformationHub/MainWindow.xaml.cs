using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class MainWindow : Window
{
    private string? _gameExecutable;
    private string? _oivInstallerExecutable;
    private string? _oivInstallerSha256;
    private string? _lastImportedOivPath;
    private string? _lastOivArchiveSha256;
    private string? _lastImportedOivPackageDirectory;
    private string? _lastImportedOivPackageId;
    private OivManifestInspectionResult? _lastOivInspection;
    private bool _openRageHudPrerequisitesReady = true;
    private bool _recoveryCompleted;
    private readonly DispatcherTimer _processTimer;
    private readonly PackageAcquisitionService _packageAcquisitionService;
    private readonly OpenRageToolchainService _openRageToolchainService;
    private OpenRageOivInspection? _reviewedOpenRageInspection;
    private bool _openRageReady;
    private bool _openRageBusy;

    public MainWindow()
    {
        InitializeComponent();
        _packageAcquisitionService = new PackageAcquisitionService();
        _openRageToolchainService = new OpenRageToolchainService(_packageAcquisitionService);
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
        await RefreshOpenRageToolStatusAsync();
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

            // Restore the newest fully verified OIV after a Hub restart. The library
            // service re-hashes the retained source archive as part of each package's
            // inventory check; only entries with a complete OIV inspection can be
            // offered to the external installer.
            var latestVerifiedOiv = entries.FirstOrDefault(entry =>
                entry.ManifestValid &&
                entry.OriginalArchivePath is not null &&
                string.Equals(Path.GetExtension(entry.OriginalArchivePath), ".oiv", StringComparison.OrdinalIgnoreCase) &&
                entry.OivInspection is not null &&
                entry.OriginalArchiveSha256 is { Length: 64 } digest &&
                digest.All(Uri.IsHexDigit));

            _lastImportedOivPath = latestVerifiedOiv?.OriginalArchivePath;
            _lastOivArchiveSha256 = latestVerifiedOiv?.OriginalArchiveSha256;
            _lastImportedOivPackageDirectory = latestVerifiedOiv?.PackageDirectory;
            _lastImportedOivPackageId = latestVerifiedOiv?.PackageId;
            _lastOivInspection = latestVerifiedOiv?.OivInspection;
            _openRageHudPrerequisitesReady = true;

            var cleanInventories = entries.Count(entry => entry.ManifestValid);
            LibrarySummaryText.Text =
                entries.Count + " imported package(s) | " + cleanInventories +
                " source archive + extracted file SHA-256 checks clear | no packages approved";

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

                if (entry.OivInspection is not null &&
                    entry.OriginalArchivePath is not null &&
                    string.Equals(Path.GetExtension(entry.OriginalArchivePath), ".oiv", StringComparison.OrdinalIgnoreCase))
                {
                    var selectOpenRageButton = new Button
                    {
                        Content = "SELECT FOR OPENRAGE PREVIEW",
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 6, 8, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = entry.ManifestValid &&
                                    entry.OriginalArchiveSha256 is { Length: 64 } &&
                                    !IsGameRunning() &&
                                    _recoveryCompleted
                    };
                    selectOpenRageButton.ToolTip =
                        "Select this exact retained OIV for checksum verification and an OpenRAGE dry-run preview. Selection does not install it.";
                    selectOpenRageButton.Click += (_, _) =>
                    {
                        if (!entry.ManifestValid ||
                            entry.OriginalArchivePath is null ||
                            entry.OriginalArchiveSha256 is not { Length: 64 } ||
                            entry.OivInspection is null)
                        {
                            MessageBox.Show(
                                "This package's retained OIV or integrity metadata is unavailable. Re-import the original package before selecting it.",
                                "OIV package unavailable",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        _lastImportedOivPath = entry.OriginalArchivePath;
                        _lastOivArchiveSha256 = entry.OriginalArchiveSha256;
                        _lastImportedOivPackageDirectory = entry.PackageDirectory;
                        _lastImportedOivPackageId = entry.PackageId;
                        _lastOivInspection = entry.OivInspection;
                        _reviewedOpenRageInspection = null;
                        _openRageHudPrerequisitesReady = true;
                        OpenRageOutputText.Text =
                            "Selected OIV: " + entry.Name + Environment.NewLine +
                            "Version: " + entry.Version + Environment.NewLine +
                            "SHA-256: " + entry.OriginalArchiveSha256 + Environment.NewLine +
                            "Next step: run PREVIEW IMPORTED OIV. No files have been installed.";
                        StatusText.Text = "Selected " + entry.Name + " for OpenRAGE preview. The selection is not an installation approval.";
                        RefreshSessionState();
                    };
                    details.Children.Add(selectOpenRageButton);

                    var openOivButton = new Button
                    {
                        Content = "OPEN RETAINED OIV FOR REVIEW",
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 6, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = entry.ManifestValid &&
                                    entry.OriginalArchiveSha256 is { Length: 64 } &&
                                    !IsGameRunning() &&
                                    _recoveryCompleted &&
                                    _gameExecutable is not null &&
                                    _oivInstallerExecutable is not null &&
                                    _oivInstallerSha256 is not null
                    };
                    openOivButton.ToolTip =
                        "Reopen this previously imported OIV. The Hub will re-verify all local file hashes and the original archive digest before opening the external installer.";
                    openOivButton.Click += (_, _) =>
                    {
                        if (!entry.ManifestValid ||
                            entry.OriginalArchivePath is null ||
                            entry.OriginalArchiveSha256 is not { Length: 64 } ||
                            entry.OivInspection is null)
                        {
                            MessageBox.Show(
                                "This package's retained OIV or integrity metadata is unavailable. Re-import the original package before opening it.",
                                "OIV package unavailable",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        _lastImportedOivPath = entry.OriginalArchivePath;
                        _lastOivArchiveSha256 = entry.OriginalArchiveSha256;
                        _lastImportedOivPackageDirectory = entry.PackageDirectory;
                        _lastImportedOivPackageId = entry.PackageId;
                        _lastOivInspection = entry.OivInspection;
                        _openRageHudPrerequisitesReady = true;
                        RefreshSessionState();
                        OpenImportedOivButton_Click(this, new RoutedEventArgs());
                    };
                    details.Children.Add(openOivButton);
                }

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

    private string BenchmarkReportRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GtaTransformationHub", "Benchmarks");

    private async void ImportBenchmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var csvDialog = new OpenFileDialog
        {
            Title = "Select a PresentMon GTA V frame capture",
            Filter = "PresentMon CSV (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };
        if (csvDialog.ShowDialog() != true)
            return;

        var metadataDialog = new BenchmarkImportDialog { Owner = this };
        if (!string.IsNullOrWhiteSpace(_gameExecutable) && File.Exists(_gameExecutable))
        {
            try
            {
                metadataDialog.SetGameBuild(new GameInstallationDetector().Inspect(_gameExecutable).FileVersion);
            }
            catch
            {
                // The user must still provide the exact build manually in the metadata form.
            }
        }

        if (metadataDialog.ShowDialog() != true || metadataDialog.Metadata is null)
            return;

        ImportBenchmarkButton.IsEnabled = false;
        StatusText.Text = "Analysing GTA5.exe frame timing from PresentMon CSV...";
        try
        {
            var analyzer = new PresentMonCsvAnalyzer();
            var report = await analyzer.AnalyzeAsync(csvDialog.FileName, metadataDialog.Metadata);
            var reportPath = await analyzer.SaveReportAsync(report, BenchmarkReportRoot);

            BenchmarkSummaryText.Text =
                report.AverageFps.ToString("F1") + " average FPS | " +
                report.EstimatedOnePercentLowFps.ToString("F1") + " estimated 1% low | " +
                report.P95FrameTimeMs.ToString("F2") + " ms P95 | " +
                report.P99FrameTimeMs.ToString("F2") + " ms P99 | " +
                report.DroppedFrames + " dropped frames. Report: " + reportPath;

            StatusText.Text = "Benchmark report saved. Compare runs only when route, resolution, game build and settings match.";
            MessageBox.Show(
                "PresentMon analysis completed.\n\n" +
                "Frames analysed: " + report.FramesAnalyzed + "\n" +
                "Average FPS: " + report.AverageFps.ToString("F1") + "\n" +
                "Estimated 1% low: " + report.EstimatedOnePercentLowFps.ToString("F1") + "\n" +
                "Mean frame time: " + report.MeanFrameTimeMs.ToString("F2") + " ms\n" +
                "P95 / P99 frame time: " + report.P95FrameTimeMs.ToString("F2") + " / " +
                report.P99FrameTimeMs.ToString("F2") + " ms\n" +
                "Dropped frames: " + report.DroppedFrames + "\n" +
                "Average CPU/GPU busy: " +
                (report.AverageCpuBusyMs?.ToString("F2") ?? "n/a") + " / " +
                (report.AverageGpuBusyMs?.ToString("F2") ?? "n/a") + " ms\n\n" +
                "Saved report: " + reportPath + "\n\n" +
                "PresentMon does not supply a complete RTX 4050 profile. VRAM, temperature, utilisation, clocks and power must be measured separately.",
                "Performance analysis completed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            StatusText.Text = "Benchmark analysis failed. No GTA V files were changed.";
            MessageBox.Show(
                "The CSV was not accepted or could not be analysed.\n\n" + exception.Message,
                "PresentMon analysis failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            ImportBenchmarkButton.IsEnabled = true;
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
                if (result.OriginalArchivePath is null || result.OivInspection is null)
                    throw new InvalidDataException("The imported OIV was not retained with its inspection record.");

                _lastImportedOivPath = result.OriginalArchivePath;
                _lastOivArchiveSha256 = result.ArchiveSha256;
                _lastImportedOivPackageDirectory = result.PackageDirectory;
                _lastImportedOivPackageId = metadataDialog.Metadata.PackageId;
                _lastOivInspection = result.OivInspection;
                _openRageHudPrerequisitesReady = true;
            }
            else
            {
                _lastImportedOivPath = null;
                _lastOivArchiveSha256 = null;
                _lastImportedOivPackageDirectory = null;
                _lastImportedOivPackageId = null;
                _lastOivInspection = null;
                _openRageHudPrerequisitesReady = true;
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
                  " | unrecognised commands: " + result.OivInspection.UnsupportedCommands.Count +
                  " | manifest warnings: " + (result.OivInspection.ManifestWarnings?.Count ?? 0) + "\n" +
                  "Manifest structural review: " + (result.OivInspection.RequiresManualReview ? "Required" : "No structural warnings detected") + "\n" +
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

    private async void RefreshOivInstallHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning() || !_recoveryCompleted || string.IsNullOrWhiteSpace(_gameExecutable))
            return;

        RefreshOivInstallHistoryButton.IsEnabled = false;
        OivInstallHistoryText.Text = "Reading CodeWalker backup/install records...";
        try
        {
            var installation = new GameInstallationDetector().Inspect(_gameExecutable);
            var report = await new CodeWalkerInstallHistoryService().ReadAsync(installation.GameDirectory);

            if (!report.BackupDirectoryExists)
            {
                OivInstallHistoryText.Text =
                    "No CodeWalker OIV backup directory exists for this game installation. " +
                    "Packages installed without CodeWalker's backup feature may not appear here.";
                StatusText.Text = "No CodeWalker install-history directory was found.";
                return;
            }

            if (report.Packages.Count == 0)
            {
                OivInstallHistoryText.Text =
                    "CodeWalker backup directory exists, but no readable install.log records were found.";
            }
            else
            {
                var lines = report.Packages.Take(6).Select(entry =>
                {
                    var installed = entry.InstalledAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "date unknown";
                    var version = string.IsNullOrWhiteSpace(entry.Version) ? "version unknown" : entry.Version;
                    return "• " + entry.PackageName + " | " + version + " | " + entry.GameEdition +
                           " | " + installed + " | " + entry.FileOperationCount + " recorded file operations";
                });

                var summary = "CodeWalker installer records — not a runtime verification:\n" +
                              string.Join("\n", lines);
                if (report.Packages.Count > 6)
                    summary += "\n…and " + (report.Packages.Count - 6) + " more record(s).";
                OivInstallHistoryText.Text = summary;
            }

            if (report.Warnings.Count > 0)
            {
                OivInstallHistoryText.Text += "\nWarnings: " +
                    string.Join(" | ", report.Warnings.Take(3));
            }

            StatusText.Text =
                "Read " + report.Packages.Count +
                " CodeWalker install record(s). This does not prove the packages are currently active or working in-game.";
        }
        catch (Exception ex)
        {
            OivInstallHistoryText.Text = "Could not read CodeWalker install records: " + ex.Message;
            StatusText.Text = "CodeWalker install-history read failed. No game files were changed.";
        }
        finally
        {
            RefreshSessionState();
        }
    }

    private void GetOivInstallerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.gta5-mods.com/tools/oiv-package-installer",
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
            _oivInstallerSha256 = installer.Sha256;
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
            _oivInstallerSha256 = null;
            OivEngineStatusText.Text = "External OIV engine selection failed: " + ex.Message;
            StatusText.Text = "The selected external installer was rejected.";
        }
        finally
        {
            RefreshSessionState();
        }
    }

    private async void OpenImportedOivButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning() || !_recoveryCompleted)
            return;
        if (string.IsNullOrWhiteSpace(_gameExecutable) ||
            string.IsNullOrWhiteSpace(_oivInstallerExecutable) ||
            string.IsNullOrWhiteSpace(_oivInstallerSha256) ||
            string.IsNullOrWhiteSpace(_lastImportedOivPath) ||
            string.IsNullOrWhiteSpace(_lastOivArchiveSha256) ||
            string.IsNullOrWhiteSpace(_lastImportedOivPackageDirectory) ||
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

        if (inspection.ManifestWarnings is { Count: > 0 })
        {
            var warningAnswer = MessageBox.Show(
                "The OIV manifest contains structural warnings:\n\n" +
                string.Join("\n", inspection.ManifestWarnings.Take(8)) +
                "\n\nThe Hub can inventory these operations but cannot prove how the external installer will handle them. " +
                "Review the author's instructions before continuing.",
                "OIV manifest warnings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (warningAnswer != MessageBoxResult.Yes)
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
            "The Hub will configure CodeWalker to the verified GTA V Legacy folder, then open the OIV in its review window.\n\n" +
            "The package will be preloaded for review, but it will not be installed automatically. Check the package identity, author, file operations, game target and backup plan in CodeWalker. " +
            "Click Install yourself only after reviewing. The Hub does not perform the install or confirm the result.\n\n" +
            "Continue?",
            "Open OIV for manual installation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (finalAnswer != MessageBoxResult.Yes)
            return;

        try
        {
            var verification = await new ImportedModLibraryService().VerifyAsync(
                PackageLibraryRoot, _lastImportedOivPackageDirectory);
            if (!verification.IsValid)
            {
                MessageBox.Show(
                    "The retained OIV archive or its extracted inventory failed SHA-256 verification. " +
                    "The package will not be opened in the external installer.\n\n" +
                    string.Join(Environment.NewLine, verification.Errors.Take(12)),
                    "OIV integrity verification failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusText.Text = "OIV integrity verification failed. External installer handoff blocked.";
                return;
            }

            OpenImportedOivButton.IsEnabled = false;
            StatusText.Text = "Verifying the external installer and configuring its target as the selected GTA V Legacy folder...";
            OivEngineStatusText.Text = "Configuring CodeWalker for the verified Legacy installation. The Hub is not installing a mod.";

            var process = await new OivInstallerLauncherService().OpenPackageInInstallerAsync(
                _oivInstallerExecutable,
                _oivInstallerSha256,
                _lastImportedOivPath,
                _lastOivArchiveSha256,
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

    private async Task RefreshOpenRageToolStatusAsync()
    {
        try
        {
            var tool = await _openRageToolchainService.ValidateInstalledAsync();
            _openRageReady = true;
            OpenRageToolStatusText.Text =
                "Verified OpenRAGE v" + tool.Version + " | archive SHA-256 " +
                tool.ArchiveSha256[..12] + " | CLI SHA-256 " + tool.ExecutableSha256[..12];
        }
        catch (FileNotFoundException)
        {
            _openRageReady = false;
            OpenRageToolStatusText.Text =
                "OpenRAGE v" + OpenRageToolchainService.PinnedVersion +
                " is not installed. The Hub downloads the official upstream archive and verifies its pinned SHA-256.";
        }
        catch (Exception exception)
        {
            _openRageReady = false;
            OpenRageToolStatusText.Text =
                "OpenRAGE verification failed. The tool will not be run: " + exception.Message;
        }
    }

    private bool CanRunOpenRage()
    {
        if (IsGameRunning() || !_recoveryCompleted || _openRageBusy)
        {
            StatusText.Text = "OpenRAGE actions are locked while GTA V is running, recovery is incomplete, or another operation is active.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_gameExecutable))
        {
            StatusText.Text = "Locate and verify GTA V Legacy before using the OIV installer.";
            return false;
        }

        return true;
    }

    private GameInstallation GetOpenRageGameInstallation()
    {
        if (string.IsNullOrWhiteSpace(_gameExecutable))
            throw new InvalidOperationException("Locate GTA V Legacy first.");

        return new GameInstallationService().ValidateExecutable(_gameExecutable);
    }

    private async void DownloadOpenRageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunOpenRage())
            return;

        _openRageBusy = true;
        RefreshSessionState();
        StatusText.Text = "Downloading and verifying the official OpenRAGE v0.4.0 Windows x64 release...";
        try
        {
            var tool = await _openRageToolchainService.DownloadAndInstallAsync();
            _openRageReady = true;
            OpenRageToolStatusText.Text =
                "Installed and verified OpenRAGE v" + tool.Version +
                " | archive SHA-256 " + tool.ArchiveSha256[..12] +
                " | CLI SHA-256 " + tool.ExecutableSha256[..12];
            StatusText.Text = "OpenRAGE is installed and verified. No GTA V mod has been installed.";
        }
        catch (Exception exception)
        {
            _openRageReady = false;
            OpenRageToolStatusText.Text = "OpenRAGE installation failed: " + exception.Message;
            StatusText.Text = "OpenRAGE installation failed. GTA V files were not modified.";
            MessageBox.Show(exception.Message, "OpenRAGE installation failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _openRageBusy = false;
            RefreshSessionState();
        }
    }

    private async void VerifyOpenRageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunOpenRage())
            return;

        _openRageBusy = true;
        RefreshSessionState();
        try
        {
            await RefreshOpenRageToolStatusAsync();
            StatusText.Text = _openRageReady
                ? "OpenRAGE checksum, executable and version verification passed."
                : "OpenRAGE is not ready. Review the tool status before proceeding.";
        }
        finally
        {
            _openRageBusy = false;
            RefreshSessionState();
        }
    }

    private async void PreviewOpenRageOivButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunOpenRage())
            return;

        if (!_openRageReady)
        {
            await RefreshOpenRageToolStatusAsync();
            if (!_openRageReady)
                return;
        }

        if (_lastImportedOivPath is null || _lastOivArchiveSha256 is null ||
            _lastImportedOivPackageDirectory is null || _lastOivInspection is null)
        {
            StatusText.Text = "Import an OIV package for review before requesting an OpenRAGE preview.";
            return;
        }

        if (!_lastOivInspection.IsGtaVTarget || _lastOivInspection.IsExplicitlyEnhanced ||
            _lastOivInspection.UnsupportedCommands.Count > 0 ||
            _lastOivInspection.PayloadSourceIssues.Count > 0)
        {
            MessageBox.Show(
                "The OIV manifest is not safe for this GTA V Legacy preview. It must target GTA V, must not explicitly target Enhanced, and must not contain unrecognised commands or unresolved payload sources.",
                "OIV manifest preflight blocked",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_lastOivInspection.IsExplicitlyLegacy && !_lastOivInspection.IsAnyGameVersion)
        {
            var targetAnswer = MessageBox.Show(
                "This OIV does not explicitly identify GTA V Legacy compatibility. Confirm that the author supports your exact Legacy game build before continuing.",
                "Confirm OIV Legacy compatibility",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (targetAnswer != MessageBoxResult.Yes)
                return;
        }

        _openRageBusy = true;
        _reviewedOpenRageInspection = null;
        RefreshSessionState();
        OpenRageOutputText.Text = "Rechecking imported archive hashes and asking OpenRAGE to inspect the OIV...";
        StatusText.Text = "Running OpenRAGE OIV dry-run preview. No files will be installed.";
        try
        {
            var inventory = await new ImportedModLibraryService().VerifyAsync(
                PackageLibraryRoot, _lastImportedOivPackageDirectory);
            if (!inventory.IsValid)
                throw new InvalidDataException(
                    "The retained OIV or extracted file inventory failed SHA-256 verification:" +
                    Environment.NewLine + string.Join(Environment.NewLine, inventory.Errors.Take(12)));

            var actualOivHash = await ComputeFileSha256Async(_lastImportedOivPath);
            if (!string.Equals(actualOivHash, _lastOivArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The retained OIV SHA-256 no longer matches the archive reviewed in the Hub.");

            var installation = GetOpenRageGameInstallation();
            var preview = await _openRageToolchainService.InspectOivAsync(
                installation, _lastImportedOivPath);

            if (!string.Equals(preview.PackageSha256, _lastOivArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("OpenRAGE preview inspected a different OIV archive than the one retained in the Hub.");

            _reviewedOpenRageInspection = preview;
            var sections = new List<string>
            {
                "OIV SHA-256: " + preview.PackageSha256,
                "Install blockers: " + preview.Blockers.Count,
                "Warnings: " + preview.Warnings.Count,
                "Game-folder executable/file changes: " + preview.GameFolderChanges.Count,
                "",
                "OPENRAGE PREVIEW OUTPUT:",
                preview.Output
            };
            if (preview.Blockers.Count > 0)
                sections.Add("BLOCKERS:" + Environment.NewLine + string.Join(Environment.NewLine, preview.Blockers));
            if (preview.Warnings.Count > 0)
                sections.Add("WARNINGS:" + Environment.NewLine + string.Join(Environment.NewLine, preview.Warnings));
            if (preview.GameFolderChanges.Count > 0)
                sections.Add("GAME-FOLDER CHANGES:" + Environment.NewLine + string.Join(Environment.NewLine, preview.GameFolderChanges));

            _openRageHudPrerequisitesReady = true;
            if (string.Equals(_lastImportedOivPackageId, "gta-vi-inspired-hud-single-player",
                    StringComparison.OrdinalIgnoreCase))
            {
                var hudPrerequisites = new GtaViInspiredHudPrerequisiteService().Inspect(installation);
                _openRageHudPrerequisitesReady = hudPrerequisites.AllRequiredFilesPresent;
                sections.Add("DOCUMENTED GTA VI-INSPIRED HUD PREREQUISITES:");
                sections.AddRange(hudPrerequisites.Items.Select(item =>
                    (item.IsPresent ? "[FOUND] " : "[MISSING] ") +
                    item.RelativePath +
                    (string.IsNullOrWhiteSpace(item.DetectedFileVersion)
                        ? string.Empty
                        : " | file version " + item.DetectedFileVersion)));
                sections.Add("This is a file-presence check only. It cannot prove the installed Script Hook V supports this exact game build or that Script Hook V .NET is v3.7.0 Nightly 193 or newer.");
                sections.Add("The HUD author documents English-only support; the character switcher is reported to crash with other game languages.");
            }

            OpenRageOutputText.Text = string.Join(Environment.NewLine, sections);
            InstallReviewedOpenRageOivButton.IsEnabled =
                preview.IsInstallable && _openRageHudPrerequisitesReady;
            StatusText.Text = !preview.IsInstallable
                ? "OpenRAGE blocked this OIV. Resolve the blockers before installation."
                : !_openRageHudPrerequisitesReady
                    ? "OpenRAGE preview completed, but required HUD loader files are missing. Installation remains blocked."
                    : "OpenRAGE dry-run completed. Review every proposed operation before installing.";
        }
        catch (Exception exception)
        {
            _reviewedOpenRageInspection = null;
            OpenRageOutputText.Text = "OIV preview failed:" + Environment.NewLine + exception;
            StatusText.Text = "OpenRAGE preview failed. No installation was attempted.";
            MessageBox.Show(exception.Message, "OpenRAGE OIV preview failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _openRageBusy = false;
            RefreshSessionState();
        }
    }

    private async void InstallReviewedOpenRageOivButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunOpenRage())
            return;

        var reviewed = _reviewedOpenRageInspection;
        if (!_openRageReady || reviewed is null || !reviewed.IsInstallable ||
            !_openRageHudPrerequisitesReady ||
            _lastImportedOivPath is null || _lastOivArchiveSha256 is null ||
            !string.Equals(reviewed.PackagePath, _lastImportedOivPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reviewed.PackageSha256, _lastOivArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "No current, installable OpenRAGE preview is available. Preview the retained OIV again.";
            return;
        }

        if (string.Equals(_lastImportedOivPackageId, "gta-vi-inspired-hud-single-player",
                StringComparison.OrdinalIgnoreCase))
        {
            HudPrerequisiteInspection hudPrerequisites;
            try
            {
                hudPrerequisites = new GtaViInspiredHudPrerequisiteService()
                    .Inspect(GetOpenRageGameInstallation());
            }
            catch (Exception exception)
            {
                StatusText.Text = "The GTA VI-inspired HUD prerequisite check failed. Installation is blocked.";
                MessageBox.Show(exception.Message, "HUD prerequisite check failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _openRageHudPrerequisitesReady = hudPrerequisites.AllRequiredFilesPresent;
            if (!_openRageHudPrerequisitesReady)
            {
                MessageBox.Show(
                    "This HUD requires the documented Legacy loader files and OpenIV mods folder. Missing items:\n\n" +
                    string.Join("\n", hudPrerequisites.MissingItems.Select(item => item.RelativePath)) +
                    "\n\nInstall and verify the prerequisites first, then preview the OIV again.",
                    "HUD prerequisites missing",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var runtimeAndLanguageConfirmed = MessageBox.Show(
                "The HUD author documents GTA V set to English and requires Script Hook V .NET 3.7.0 Nightly 193 or newer. The Hub can check that prerequisite files exist, but it cannot prove the installed nightly build or that Script Hook V supports your exact GTA V patch.\n\nConfirm both: (1) GTA V is configured to English, and (2) you have checked the author-linked Script Hook V release for this exact game build and have SHVDN 3.7.0 Nightly 193 or newer.",
                "Confirm HUD runtime and language compatibility",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (runtimeAndLanguageConfirmed != MessageBoxResult.Yes)
                return;
        }

        var confirm = MessageBox.Show(
            "OpenRAGE found no blocking OIV operations. The selected prebuilt OIV will now be installed into the selected GTA V Legacy mods folder. The Hub re-runs the dry-run and verifies the exact archive hash immediately before installation. Continue?",
            "Install reviewed OIV",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var allowGameFolderFiles = false;
        if (reviewed.RequiresExecutableConsent)
        {
            var executableConfirm = MessageBox.Show(
                "This OIV proposes files outside the OpenIV mods folder, including game-folder executable/plugin files. These files can affect the base game and require additional review. Explicitly allow these changes?",
                "Additional game-folder file consent",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (executableConfirm != MessageBoxResult.Yes)
                return;
            allowGameFolderFiles = true;
        }

        _openRageBusy = true;
        RefreshSessionState();
        try
        {
            var inventory = await new ImportedModLibraryService().VerifyAsync(
                PackageLibraryRoot, _lastImportedOivPackageDirectory!);
            if (!inventory.IsValid)
                throw new InvalidDataException("The retained OIV inventory is no longer valid. No installation was attempted.");

            var actualOivHash = await ComputeFileSha256Async(_lastImportedOivPath!);
            if (!string.Equals(actualOivHash, reviewed.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(actualOivHash, _lastOivArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The OIV changed after preview. Re-import and preview it before installation.");

            var result = await _openRageToolchainService.InstallOivAsync(
                GetOpenRageGameInstallation(),
                reviewed,
                userConfirmed: true,
                allowGameFolderFiles: allowGameFolderFiles);

            OpenRageOutputText.Text =
                "OpenRAGE install command completed (exit code " + result.ExitCode + ")." +
                Environment.NewLine + "Duration: " + result.Duration +
                Environment.NewLine + result.StandardOutput +
                (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty :
                    Environment.NewLine + result.StandardError) +
                Environment.NewLine + Environment.NewLine +
                "This confirms the external installer command completed. It is not proof of gameplay compatibility; test the mod in Story Mode and verify rollback.";
            _reviewedOpenRageInspection = null;
            InstallReviewedOpenRageOivButton.IsEnabled = false;
            StatusText.Text = "OpenRAGE reported a successful OIV install operation. Verify gameplay and installed-file state before marking the mod tested.";
        }
        catch (Exception exception)
        {
            OpenRageOutputText.Text = "OpenRAGE OIV installation failed or was blocked:" +
                                      Environment.NewLine + exception;
            StatusText.Text = "OpenRAGE OIV installation did not complete. Inspect its output and rollback records.";
            MessageBox.Show(exception.Message, "OpenRAGE OIV installation failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _openRageBusy = false;
            RefreshSessionState();
        }
    }

    private async void ListOpenRageOivButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunOpenRage())
            return;
        if (!_openRageReady)
        {
            await RefreshOpenRageToolStatusAsync();
            if (!_openRageReady) return;
        }

        _openRageBusy = true;
        RefreshSessionState();
        try
        {
            var result = await _openRageToolchainService.ListInstalledOivPackagesAsync(
                GetOpenRageGameInstallation());
            OpenRageOutputText.Text = result.StandardOutput +
                (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty :
                    Environment.NewLine + result.StandardError);
            StatusText.Text = "OpenRAGE installed-package list refreshed. This is installer metadata; verify file integrity separately.";
        }
        catch (Exception exception)
        {
            OpenRageOutputText.Text = exception.ToString();
            StatusText.Text = "OpenRAGE could not list installed OIV packages.";
        }
        finally
        {
            _openRageBusy = false;
            RefreshSessionState();
        }
    }

    private async void UninstallOpenRageOivButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunOpenRage())
            return;
        if (!_openRageReady)
        {
            await RefreshOpenRageToolStatusAsync();
            if (!_openRageReady) return;
        }

        var groupName = OpenRageUninstallGroupBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(groupName) ||
            groupName.Equals("Enter exact OpenRAGE package group", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Enter the exact package group name shown by LIST INSTALLED OIVS.";
            return;
        }

        var answer = MessageBox.Show(
            "OpenRAGE will uninstall the package group '" + groupName +
            "' using its own recorded uninstall data. Continue?",
            "Uninstall OIV package",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _openRageBusy = true;
        RefreshSessionState();
        try
        {
            var result = await _openRageToolchainService.UninstallOivPackageAsync(
                GetOpenRageGameInstallation(), groupName, userConfirmed: true);
            OpenRageOutputText.Text = result.StandardOutput +
                (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty :
                    Environment.NewLine + result.StandardError);
            StatusText.Text = "OpenRAGE uninstall command completed. Launch GTA V and verify the restored state.";
            _reviewedOpenRageInspection = null;
        }
        catch (Exception exception)
        {
            OpenRageOutputText.Text = exception.ToString();
            StatusText.Text = "OpenRAGE could not uninstall the selected package group.";
            MessageBox.Show(exception.Message, "OpenRAGE uninstall failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _openRageBusy = false;
            RefreshSessionState();
        }
    }

    private async void UndoOpenRageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunOpenRage())
            return;
        if (!_openRageReady)
        {
            await RefreshOpenRageToolStatusAsync();
            if (!_openRageReady) return;
        }

        var answer = MessageBox.Show(
            "OpenRAGE will undo its latest recorded change for this GTA V installation. This may reverse a mod operation. Continue only after backing up your game and save data.",
            "Undo latest OpenRAGE change",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _openRageBusy = true;
        RefreshSessionState();
        try
        {
            var result = await _openRageToolchainService.UndoLatestOpenRageChangeAsync(
                GetOpenRageGameInstallation(), userConfirmed: true);
            OpenRageOutputText.Text = result.StandardOutput +
                (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty :
                    Environment.NewLine + result.StandardError);
            StatusText.Text = "OpenRAGE undo command completed. Verify the game and its mod state before continuing.";
            _reviewedOpenRageInspection = null;
        }
        catch (Exception exception)
        {
            OpenRageOutputText.Text = exception.ToString();
            StatusText.Text = "OpenRAGE could not undo the latest recorded change.";
            MessageBox.Show(exception.Message, "OpenRAGE undo failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _openRageBusy = false;
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

    private void ManageModsButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGameRunning() || !_recoveryCompleted)
            return;

        string? installedBuild = null;
        if (!string.IsNullOrWhiteSpace(_gameExecutable))
        {
            try
            {
                installedBuild = new GameInstallationDetector().Inspect(_gameExecutable).FileVersion;
            }
            catch
            {
                // The catalogue window remains usable for profile preparation, but readiness
                // checks will correctly fail until the exact Legacy build is detected.
            }
        }

        var window = new ModCatalogueWindow(installedBuild) { Owner = this };
        window.ShowDialog();
        StatusText.Text = "Mod profile window closed. Saved selections do not install files; use readiness checks before deployment.";
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
        var operationBusy = _openRageBusy;
        LocateButton.IsEnabled = !running && !operationBusy && _recoveryCompleted;
        ManageModsButton.IsEnabled = !running && !operationBusy && _recoveryCompleted;
        ImportArchiveButton.IsEnabled = !running && !operationBusy && _recoveryCompleted;
        ImportBenchmarkButton.IsEnabled = !operationBusy;
        RefreshButton.IsEnabled = !running && !operationBusy && _recoveryCompleted;
        RefreshLibraryButton.IsEnabled = !running && !operationBusy && _recoveryCompleted;
        SelectOivInstallerButton.IsEnabled = !running && !operationBusy && _recoveryCompleted;
        OpenImportedOivButton.IsEnabled =
            !running && !operationBusy && _recoveryCompleted &&
            _gameExecutable is not null &&
            _oivInstallerExecutable is not null &&
            _oivInstallerSha256 is not null &&
            _lastImportedOivPath is not null &&
            _lastOivArchiveSha256 is not null &&
            _lastOivInspection is not null;
        GetOivInstallerButton.IsEnabled = true;
        RefreshOivInstallHistoryButton.IsEnabled =
            !running && !operationBusy && _recoveryCompleted && _gameExecutable is not null;
        DownloadOpenRageButton.IsEnabled = !running && _recoveryCompleted && !_openRageBusy;
        VerifyOpenRageButton.IsEnabled = !running && _recoveryCompleted && !_openRageBusy;
        PreviewOpenRageOivButton.IsEnabled =
            !running && _recoveryCompleted && !_openRageBusy && _openRageReady &&
            _gameExecutable is not null && _lastImportedOivPath is not null &&
            _lastOivArchiveSha256 is not null && _lastImportedOivPackageDirectory is not null;
        InstallReviewedOpenRageOivButton.IsEnabled =
            !running && _recoveryCompleted && !_openRageBusy && _openRageReady &&
            _reviewedOpenRageInspection is { IsInstallable: true };
        ListOpenRageOivButton.IsEnabled = !running && _recoveryCompleted && !_openRageBusy && _openRageReady;
        UninstallOpenRageOivButton.IsEnabled = !running && _recoveryCompleted && !_openRageBusy && _openRageReady;
        UndoOpenRageButton.IsEnabled = !running && _recoveryCompleted && !_openRageBusy && _openRageReady;
        // Never launch from the dashboard until profile validation and transactional
        // deployment are wired into this exact path.
        LaunchButton.IsEnabled = false;

        SessionStateText.Text = running ? "GAME RUNNING · LOCKED" : "CONFIGURATION UNLOCKED";
        LockExplanationText.Text = running
            ? "GTA5.exe is active or its state cannot be verified. Mod changes and deployment are disabled."
            : "Game not running — configuration is available.";

        ModList.IsEnabled = !running && !operationBusy;
    }

    private static async Task<string> ComputeFileSha256Async(string path)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    protected override void OnClosed(EventArgs e)
    {
        _processTimer.Stop();
        _packageAcquisitionService.Dispose();
        base.OnClosed(e);
    }
}
