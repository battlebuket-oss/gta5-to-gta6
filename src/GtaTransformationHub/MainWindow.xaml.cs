using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class MainWindow : Window
{
    private readonly GameProcessMonitor _processMonitor = new();
    private readonly TransactionalDeploymentService _deploymentService = new();
    private readonly TransactionalRemovalService _removalService = new();
    private readonly GameInstallationService _gameInstallationService;
    private readonly PackageArchiveService _archiveService = new();
    private readonly OivPackageImportService _oivImportService = new();
    private readonly PackageAcquisitionService _acquisitionService = new();
    private readonly StorySaveBackupService _storySaveBackupService = new();
    private readonly RageCliToolchainService _rageCliToolchainService = new();
    private readonly PackageLibraryService _packageLibrary = new();
    private readonly PackageDeploymentCoordinator _deploymentCoordinator;
    private readonly ProfileActivationService _profileActivationService;
    private readonly ProfileStoreService _profileStore = new();
    private readonly TrustedPublisherStore _trustedPublisherStore = new();
    private readonly ObservableCollection<ImportedPackage> _packages = new();
    private readonly ObservableCollection<ModPackage> _cataloguePackages = new();
    private readonly ObservableCollection<ModProfile> _profiles = new();
    private readonly DispatcherTimer _processTimer;

    private GameInstallation? _installation;
    private bool _recoveryCompleted;    private bool _loading;
    private bool _operationInProgress;
    private string? _verifiedRageCliExecutablePath;
    private CancellationTokenSource? _assetCatalogueCancellation;
    private readonly List<string> _rollbackTransactionIds = new();

    public MainWindow()
    {
        InitializeComponent();

        _gameInstallationService = new GameInstallationService(_processMonitor);
        _deploymentCoordinator = new PackageDeploymentCoordinator(_deploymentService);
        _profileActivationService = new ProfileActivationService(
            _deploymentCoordinator, _removalService, _deploymentService);

        PackageList.ItemsSource = _packages;
        ResearchCatalogueList.ItemsSource = _cataloguePackages;
        ProfileSelector.ItemsSource = _profiles;

        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _processTimer.Tick += (_, _) => RefreshSessionState();
        _processTimer.Start();
        RefreshSessionState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        LocateButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Checking for interrupted deployment transactions...";

        try
        {
            var recovered = await _deploymentService.RecoverInterruptedTransactionsAsync();
            _recoveryCompleted = true;

            try
            {
                foreach (var imported in _packageLibrary.ListImported())
                    _packages.Add(imported);
            }
            catch (Exception exception)
            {
                StatusText.Text = "Recovery passed, but the local package library could not be fully read: " + exception.Message;
            }

            await LoadProfilesAsync();

            try
            {
                RageCliExecutablePathText.Text =
                    await _rageCliToolchainService.LoadConfiguredExecutablePathAsync() ?? string.Empty;
                RageCliStatusText.Text = string.IsNullOrWhiteSpace(RageCliExecutablePathText.Text)
                    ? "No rage-cli executable has been configured. Select the official v0.28.1 Windows x64 asset."
                    : "Saved tool path found. Verify its checksum and version before use.";
            }
            catch (Exception toolchainException)
            {
                RageCliStatusText.Text = "Saved rage-cli settings could not be read: " + toolchainException.Message;
            }

            if (recovered.Count == 0)
                StatusText.Text = "Recovery passed. No interrupted deployments were found.";
            else
                StatusText.Text = "Recovered " + recovered.Count + " interrupted deployment transaction(s).";

            DetectionText.Text = "Locate GTA5.exe to verify the installed game build.";
        }
        catch (Exception exception)
        {
            _recoveryCompleted = false;
            DetectionText.Text = "Recovery could not be verified. Game configuration remains locked.";
            StatusText.Text = "Safety recovery failed: " + exception.Message;
        }
        finally
        {
            _loading = false;
            if (ProfileSelector.SelectedIndex < 0 && _profiles.Count > 0)
                ProfileSelector.SelectedIndex = 0;
            else
                ApplySelectedProfile();
            RefreshSessionState();
        }
    }

    private async Task LoadProfilesAsync()
    {
        var savedProfiles = await _profileStore.LoadAsync();
        foreach (var profile in savedProfiles)
            _profiles.Add(profile);

        var cataloguePath = Path.Combine(AppContext.BaseDirectory, "Catalogue", "catalogue.json");
        if (!File.Exists(cataloguePath))
            return;

        var catalogue = new CatalogueService().Load(cataloguePath);
        foreach (var package in catalogue.Packages)
            _cataloguePackages.Add(package);

        foreach (var template in catalogue.Profiles)
        {
            if (_profiles.Any(profile => profile.Id.Equals(template.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            _profiles.Add(new ModProfile(
                template.Id,
                template.Name,
                new HashSet<string>(template.EnabledPackageIds, StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.UtcNow));
        }
    }

    private void LocateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select GTA V Legacy executable",
            Filter = "GTA V executable (GTA5.exe)|GTA5.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _installation = _gameInstallationService.ValidateExecutable(dialog.FileName);
            _rollbackTransactionIds.Clear();
            GamePathText.Text = _installation.RootPath;
            DetectionText.Text = "GTA V Legacy build " + _installation.BuildVersion + " detected.";
            StatusText.Text = "Installation validated. Import packages, review the source and run preflight.";
        }
        catch (Exception exception)
        {
            _installation = null;
            GamePathText.Text = "No validated installation selected";
            DetectionText.Text = exception.Message;
            StatusText.Text = "Game installation validation failed.";
        }

        RefreshSessionState();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        if (_installation is null)
        {
            StatusText.Text = "Locate GTA5.exe first.";
            return;
        }

        try
        {
            _installation = _gameInstallationService.ValidateExecutable(_installation.ExecutablePath);
            DetectionText.Text = "GTA V Legacy build " + _installation.BuildVersion + " verified.";
            StatusText.Text = "Installation check passed.";
        }
        catch (Exception exception)
        {
            _installation = null;
            DetectionText.Text = exception.Message;
            StatusText.Text = "Game installation validation failed.";
        }

        RefreshSessionState();
    }

    private void ManagePublishersButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        var dialog = new ManagePublishersWindow(_trustedPublisherStore) { Owner = this };
        dialog.ShowDialog();
        StatusText.Text = "Publisher trust changes will be applied during the next package preflight.";
    }

    private async void TrustPublisherButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        var dialog = new TrustPublisherWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            var publisher = await _trustedPublisherStore.AddAsync(
                dialog.PublisherName,
                dialog.PublicKeyPem,
                dialog.Fingerprint);
            StatusText.Text = "Trusted publisher '" + publisher.Name +
                              "' (" + publisher.Fingerprint + "). Package signatures will be rechecked during preflight.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Publisher key was not trusted.";
            MessageBox.Show(
                exception.Message,
                "Publisher trust failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _operationInProgress = false;
        }

        RefreshSessionState();
    }

    private async void ImportPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import a mod package archive",
            Filter = "Supported mod packages (*.zip;*.oiv)|*.zip;*.oiv",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            StatusText.Text = "Inspecting archive and verifying manifest-declared files...";
            var imported = Path.GetExtension(dialog.FileName).Equals(".oiv", StringComparison.OrdinalIgnoreCase)
                ? await _oivImportService.ImportAsync(dialog.FileName)
                : await _archiveService.ImportAsync(dialog.FileName);
            var duplicate = _packages.FirstOrDefault(item =>
                item.Manifest.Id.Equals(imported.Manifest.Id, StringComparison.OrdinalIgnoreCase) &&
                item.Manifest.Version.Equals(imported.Manifest.Version, StringComparison.OrdinalIgnoreCase) &&
                item.PackageRoot.Equals(imported.PackageRoot, StringComparison.OrdinalIgnoreCase));

            if (duplicate is null)
                _packages.Add(imported);
            TrustPackagesCheckBox.IsChecked = false;
            PackageList.SelectedItems.Clear();
            PackageList.SelectedItem = duplicate ?? imported;
            StatusText.Text = "Imported " + imported.Manifest.Name +
                              ". File hashes verify integrity, not publisher identity or package safety.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Package import failed.";
            MessageBox.Show(
                exception.Message,
                "Package import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _operationInProgress = false;
        }

        RefreshSessionState();
    }

    private async void DownloadPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        if (!Uri.TryCreate(PackageDownloadUrlText.Text?.Trim(), UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            StatusText.Text = "Enter a direct HTTPS URL for a ZIP or OIV package.";
            return;
        }

        _operationInProgress = true;
        RefreshSessionState();
        DownloadPackageButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Downloading package from the supplied HTTPS source...";
            var acquired = await _acquisitionService.DownloadAsync(sourceUri);
            var imported = Path.GetExtension(acquired.FilePath).Equals(".oiv", StringComparison.OrdinalIgnoreCase)
                ? await _oivImportService.ImportAsync(acquired.FilePath)
                : await _archiveService.ImportAsync(acquired.FilePath);

            var duplicate = _packages.FirstOrDefault(item =>
                item.Manifest.Id.Equals(imported.Manifest.Id, StringComparison.OrdinalIgnoreCase) &&
                item.Manifest.Version.Equals(imported.Manifest.Version, StringComparison.OrdinalIgnoreCase) &&
                item.PackageRoot.Equals(imported.PackageRoot, StringComparison.OrdinalIgnoreCase));
            if (duplicate is null)
                _packages.Add(imported);

            PackageList.SelectedItems.Clear();
            PackageList.SelectedItem = duplicate ?? imported;
            TrustPackagesCheckBox.IsChecked = false;
            PackageDownloadUrlText.Clear();
            StatusText.Text = "Downloaded and imported " + imported.Manifest.Name +
                ". SHA-256: " + acquired.Sha256 +
                ". Publisher identity and compatibility still require independent verification.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Package download/import failed.";
            MessageBox.Show(exception.Message, "Package acquisition failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _operationInProgress = false;
            DownloadPackageButton.IsEnabled = true;
        }

        RefreshSessionState();
    }

    private void DetectPerformanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        try
        {
            var hardware = PerformanceProfileService.CaptureHardware();
            var recommended = PerformanceProfileService.Recommend(hardware);
            var totalRam = hardware.TotalPhysicalMemoryMb / 1024.0;
            var availableRam = hardware.AvailablePhysicalMemoryMb / 1024.0;
            PerformanceGuidanceText.Text =
                "Recommended: " + recommended.Name + " — " + recommended.Target + Environment.NewLine +
                "CPU threads: " + hardware.LogicalProcessorCount +
                " | RAM: " + totalRam.ToString("0.0") + " GB total, " +
                availableRam.ToString("0.0") + " GB available." + Environment.NewLine +
                recommended.Guidance + Environment.NewLine +
                "Guidance only: no game settings were changed and no FPS result is guaranteed. GPU VRAM must be checked separately.";
        }
        catch (Exception exception)
        {
            PerformanceGuidanceText.Text = "Hardware detection failed: " + exception.Message;
        }
    }

    private async void BackupStorySavesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select any save file inside GTA V's Profiles folder",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        var profilesDirectory = FindProfilesDirectory(Path.GetDirectoryName(dialog.FileName));
        if (profilesDirectory is null)
        {
            MessageBox.Show(
                "The selected file is not inside a directory named Profiles. No backup was created.",
                "Select GTA V save files",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            var backup = await _storySaveBackupService.CreateBackupAsync(profilesDirectory);
            StorySaveBackupText.Text =
                "Backup created: " + backup.FileCount + " file(s), " +
                (backup.UncompressedBytes / (1024.0 * 1024.0)).ToString("0.0") +
                " MB. SHA-256: " + backup.Sha256 + Environment.NewLine +
                backup.ArchivePath;
        }
        catch (Exception exception)
        {
            StorySaveBackupText.Text = "Save backup failed: " + exception.Message;
        }
        finally
        {
            _operationInProgress = false;
        }
        RefreshSessionState();
    }

    private void SelectRageCliButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select the official rage-cli v0.28.1 Windows x64 executable",
            Filter = "rage-cli v0.28.1 Windows x64 (rage-windows-x86_64.exe)|rage-windows-x86_64.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        RageCliExecutablePathText.Text = dialog.FileName;
        _verifiedRageCliExecutablePath = null;
        RageCliStatusText.Text = "Executable selected. Verify its pinned SHA-256 and reported version before running it.";
        RefreshSessionState();
    }

    private async void VerifyRageCliButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        _operationInProgress = true;
        _verifiedRageCliExecutablePath = null;
        RefreshSessionState();
        try
        {
            RageCliStatusText.Text = "Checking the executable digest and invoking its version command...";
            var verified = await _rageCliToolchainService.ValidateExecutableAsync(
                RageCliExecutablePathText.Text.Trim());
            await _rageCliToolchainService.SaveConfiguredExecutablePathAsync(verified.ExecutablePath);

            _verifiedRageCliExecutablePath = verified.ExecutablePath;
            RageCliStatusText.Text =
                "Verified rage " + verified.Version + Environment.NewLine +
                "SHA-256: " + verified.Sha256 + Environment.NewLine +
                "Pinned release: " + RageCliToolchainService.ReleaseUrl;
            StatusText.Text = "The external GTA V asset inspection tool passed version and checksum verification.";
        }
        catch (Exception exception)
        {
            RageCliStatusText.Text = "rage-cli verification failed: " + exception.Message;
            StatusText.Text = "The external asset inspection tool was not trusted or launched.";
        }
        finally
        {
            _operationInProgress = false;
            RefreshSessionState();
        }
    }

    private async void BuildAssetCatalogueButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration() || _installation is null ||
            string.IsNullOrWhiteSpace(_verifiedRageCliExecutablePath))
            return;

        var confirmation = MessageBox.Show(
            "rage-cli will read the selected GTA V installation and build a local catalogue of supported model and texture assets. It will not modify game files or install mods. The scan can take a long time and may use several gigabytes of disk space. A strict scan failure will remain a failure; it will not be treated as a verified ecological baseline. Continue?",
            "Build GTA V asset catalogue",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _assetCatalogueCancellation?.Dispose();
        _assetCatalogueCancellation = new CancellationTokenSource();
        _operationInProgress = true;
        RefreshSessionState();
        RageCliStatusText.Text = "Scanning game archives in load order. The original game files are read only...";
        StatusText.Text = "Building the optional GTA V model and texture catalogue...";

        try
        {
            var result = await _rageCliToolchainService.BuildAssetCatalogueAsync(
                _verifiedRageCliExecutablePath,
                _installation,
                _assetCatalogueCancellation.Token);

            RageCliStatusText.Text =
                "Asset catalogue scan completed." + Environment.NewLine +
                "Database: " + result.DatabasePath + " (" +
                (result.DatabaseBytes / (1024.0 * 1024.0)).ToString("0.0") + " MB)" + Environment.NewLine +
                "Report: " + result.ReportPath + Environment.NewLine +
                "Report SHA-256: " + result.ReportSha256 + Environment.NewLine +
                "Database SHA-256: " + result.DatabaseSha256 + Environment.NewLine +
                "Duration: " + result.Duration.ToString(@"hh\:mm\:ss") + Environment.NewLine +
                result.Summary + Environment.NewLine +
                "This is a model/texture catalogue, not a complete flora/fauna/population/water baseline.";
            StatusText.Text = "GTA V asset catalogue created. Ecology-sensitive packages remain blocked.";
        }
        catch (OperationCanceledException)
        {
            RageCliStatusText.Text =
                "The asset catalogue scan was cancelled. Any partial output must be treated as incomplete.";
            StatusText.Text = "GTA V asset catalogue scan cancelled.";
        }
        catch (Exception exception)
        {
            RageCliStatusText.Text = "Asset catalogue scan failed: " + exception.Message;
            StatusText.Text = "The GTA V asset catalogue was not accepted.";
        }
        finally
        {
            _assetCatalogueCancellation?.Dispose();
            _assetCatalogueCancellation = null;
            _operationInProgress = false;
            RefreshSessionState();
        }
    }

    private void CancelAssetCatalogueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_assetCatalogueCancellation is null)
            return;

        RageCliStatusText.Text = "Cancellation requested. Waiting for the scan process to stop safely...";
        _assetCatalogueCancellation.Cancel();
        CancelAssetCatalogueButton.IsEnabled = false;
    }

    private static string? FindProfilesDirectory(string? startDirectory)
    {
        for (var current = string.IsNullOrWhiteSpace(startDirectory) ? null : new DirectoryInfo(startDirectory);
             current is not null;
             current = current.Parent)
        {
            if (current.Name.Equals("Profiles", StringComparison.OrdinalIgnoreCase))
                return current.FullName;
        }
        return null;
    }

    private void OpenCatalogueSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        if (sender is not Button button || button.Tag is not Uri sourceUri ||
            sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            StatusText.Text = "The catalogue source is not a valid HTTPS URL.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(sourceUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not open the catalogue source page: " + exception.Message;
        }
    }

    private void OpenPackageSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string sourceText ||
            !Uri.TryCreate(sourceText, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            StatusText.Text = "The package source is not a valid HTTPS URL.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(sourceUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not open the package source page: " + exception.Message;
        }
    }

    private async void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration() || _installation is null)
            return;

        var selected = GetSelectedPackages();
        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            StatusText.Text = "Checking build compatibility, dependencies, conflicts, paths and file hashes...";
            var report = await _deploymentCoordinator.ValidateAsync(_installation, selected);
            if (!report.IsValid)
            {
                StatusText.Text = "Preflight rejected the selected packages.";
                MessageBox.Show(
                    string.Join(Environment.NewLine, report.Errors),
                    "Preflight failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var warningText = report.Warnings.Count == 0
                ? "No metadata warnings were found."
                : string.Join(Environment.NewLine, report.Warnings);
            StatusText.Text = "Preflight passed for " + report.FilesToDeploy +
                              " file(s). Review and acknowledge package trust before applying.";
            MessageBox.Show(
                "Preflight passed for " + report.FilesToDeploy + " file(s)." +
                Environment.NewLine + Environment.NewLine + warningText,
                "Package preflight",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            StatusText.Text = "Preflight failed.";
            MessageBox.Show(exception.Message, "Preflight failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationInProgress = false;
        }

        RefreshSessionState();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration() || _installation is null)
            return;

        var selected = GetSelectedPackages();
        if (selected.Count > 0 && TrustPackagesCheckBox.IsChecked != true)
        {
            StatusText.Text = "Acknowledge package source and execution risk before applying.";
            return;
        }

        PackagePreflightReport? preflightReport = null;
        if (selected.Count > 0)
        {
            try
            {
                StatusText.Text = "Checking signatures, licences, build compatibility and package files...";
                preflightReport = await _deploymentCoordinator.ValidateAsync(_installation, selected);
                if (!preflightReport.IsValid)
                {
                    StatusText.Text = "Profile preflight rejected the selected packages.";
                    MessageBox.Show(
                        string.Join(Environment.NewLine, preflightReport.Errors),
                        "Profile preflight failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception exception)
            {
                StatusText.Text = "Profile preflight could not be completed.";
                MessageBox.Show(
                    exception.Message,
                    "Profile preflight failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        var confirmationText = selected.Count == 0
            ? "The selected profile contains no packages. The Hub will attempt to remove every currently managed imported package, restoring original files where its journals and hashes permit it."
            : "The Hub will reconcile the selected profile: remove no-longer-selected managed packages, preserve intact packages that are already active, and install missing desired packages." +
              Environment.NewLine + Environment.NewLine +
              "Some mod files, including ASI or DLL plugins, may execute when GTA V starts. The Hub does not sandbox those files or verify the publisher's identity. Continue only if you obtained and trust these packages.";

        var warningText = preflightReport is null || preflightReport.Warnings.Count == 0
            ? "No package metadata warnings were reported."
            : "Review these package warnings:" + Environment.NewLine +
              string.Join(Environment.NewLine, preflightReport.Warnings);

        var confirmation = MessageBox.Show(
            confirmationText + Environment.NewLine + Environment.NewLine +
            warningText + Environment.NewLine + Environment.NewLine +
            "A transaction journal and backups will be created for file changes. Continue?",
            "Confirm profile activation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            StatusText.Text = "Reconciling the selected profile against active deployment records...";
            var result = await _profileActivationService.ActivateAsync(
                _installation, _packages.ToArray(), selected);

            foreach (var transactionId in result.TransactionIds)
                _rollbackTransactionIds.Add(transactionId);
            if (result.TransactionIds.Count == 0)
            {
                StatusText.Text = "The selected profile is already active. No game files needed changing.";
            }
            else
            {
                StatusText.Text = "Profile reconciled. Removed " + result.FilesRemoved +
                                  " file(s), installed/repaired " + result.FilesApplied +
                                  " file(s), kept " + result.PackagesAlreadyActive +
                                  " intact package(s).";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Profile activation failed. The previous package state was preserved where rollback was possible.";
            MessageBox.Show(
                exception.Message,
                "Profile activation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _operationInProgress = false;
        }

        RefreshSessionState();
    }

    private async void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration() || _installation is null)
            return;

        var selected = GetSelectedPackages();
        if (selected.Count == 0)
            return;

        var confirmation = MessageBox.Show(
            "Remove the selected packages only where the Hub can verify a prior deployment record and the installed file still matches the package hash?",
            "Confirm managed package removal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            var result = await _removalService.RemovePackagesAsync(_installation, selected);
            TrustPackagesCheckBox.IsChecked = false;

            if (result.FilesApplied == 0)
            {
                StatusText.Text = "No matching Hub-managed files were found to remove.";
                // No transaction was created because no managed files were present.
            }
            else
            {
                _rollbackTransactionIds.Add(result.TransactionId);
                StatusText.Text = "Removed " + result.FilesApplied +
                                  " managed file(s). Rollback is available for this removal transaction.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Package removal stopped. User-modified or unmanaged files were preserved.";
            MessageBox.Show(
                exception.Message,
                "Package removal stopped",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _operationInProgress = false;
        }

        RefreshSessionState();
    }

    private async void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration() || _rollbackTransactionIds.Count == 0)
            return;

        var transactionId = _rollbackTransactionIds[^1];
        var confirmation = MessageBox.Show(
            "Restore files from the latest transaction? Files changed after the operation will be protected and may cause rollback to stop.",
            "Confirm rollback",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            await _deploymentService.RollbackLatestAsync(transactionId);
            _rollbackTransactionIds.RemoveAt(_rollbackTransactionIds.Count - 1);
            StatusText.Text = "The latest transaction was rolled back. Roll back again to reverse the preceding step of a profile change.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Rollback did not complete. Preserve the transaction journal for recovery.";
            MessageBox.Show(exception.Message, "Rollback stopped", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationInProgress = false;
        }

        RefreshSessionState();
    }

    private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration())
            return;

        var name = ProfileNameText.Text.Trim();
        if (name.Length == 0)
        {
            StatusText.Text = "Enter a name for the profile.";
            return;
        }

        var selectedPackages = GetSelectedPackages();
        if (selectedPackages.Select(package => package.Manifest.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedPackages.Count)
        {
            StatusText.Text = "Select only one imported version of each package ID before saving a profile.";
            return;
        }

        var selectedIds = selectedPackages
            .Select(package => package.Manifest.Id + "@" + package.Manifest.Version)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = CreateProfileId(name);
        if (!PackageIdValidator.IsValid(id))
        {
            StatusText.Text = "The profile name must produce a valid profile ID.";
            return;
        }

        var selectedVersions = selectedPackages.ToDictionary(
            package => package.Manifest.Id,
            package => package.Manifest.Version,
            StringComparer.OrdinalIgnoreCase);
        var profile = new ModProfile(
            id, name, selectedIds, DateTimeOffset.UtcNow, selectedVersions);
        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            await _profileStore.SaveAsync(profile);

            var existing = _profiles.FirstOrDefault(item =>
                item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                _profiles.Remove(existing);
            _profiles.Add(profile);
            ProfileSelector.SelectedItem = profile;
            StatusText.Text = "Saved profile '" + name + "' with " + selectedIds.Count + " package(s).";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Profile could not be saved.";
            MessageBox.Show(exception.Message, "Profile save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationInProgress = false;
        }

        RefreshSessionState();
    }

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _operationInProgress)
            return;

        ApplySelectedProfile();
        TrustPackagesCheckBox.IsChecked = false;
        RefreshSessionState();
    }

    private void ApplySelectedProfile()
    {
        if (ProfileSelector.SelectedItem is not ModProfile profile)
            return;

        var wasLoading = _loading;
        _loading = true;
        try
        {
            PackageList.SelectedItems.Clear();
            var missing = new List<string>();

            foreach (var packageReference in profile.EnabledPackageIds)
            {
                var separator = packageReference.LastIndexOf('@');
                var packageId = separator > 0 ? packageReference[..separator] : packageReference;
                var pinnedVersion = separator > 0 ? packageReference[(separator + 1)..] : null;

                var matches = _packages.Where(item =>
                    item.Manifest.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

                var package = pinnedVersion is null
                    ? matches.OrderByDescending(item => item.ImportedAt).FirstOrDefault()
                    : matches.FirstOrDefault(item =>
                        item.Manifest.Version.Equals(pinnedVersion, StringComparison.OrdinalIgnoreCase));

                if (package is null)
                    missing.Add(packageReference);
                else
                    PackageList.SelectedItems.Add(package);
            }

            ProfileWarningText.Text = missing.Count == 0
                ? "Saved profiles pin the package versions selected when they were saved. Catalogue profiles without a version pin use the most recently imported version."
                : "Missing imported package versions: " + string.Join(", ", missing);
            ProfileNameText.Text = profile.Name;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void PackageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _operationInProgress)
            return;
        TrustPackagesCheckBox.IsChecked = false;
        RefreshSessionState();
    }

    private void TrustAcknowledgement_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && !_operationInProgress)
            RefreshSessionState();
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeConfiguration() || _installation is null)
        {
            StatusText.Text = "Locate and validate GTA5.exe before launching.";
            return;
        }

        try
        {
            _processMonitor.EnsureConfigurationUnlocked();
            Process.Start(new ProcessStartInfo
            {
                FileName = _installation.ExecutablePath,
                WorkingDirectory = _installation.RootPath,
                UseShellExecute = true
            });
            StatusText.Text = "GTA V launch requested. Configuration will lock when a protected GTA V process starts.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "The game launcher could not start GTA V.";
            MessageBox.Show(
                "Direct executable launch may require Steam or Rockstar Games Launcher." +
                Environment.NewLine + exception.Message,
                "Launch failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        RefreshSessionState();
    }

    private IReadOnlyList<ImportedPackage> GetSelectedPackages() =>
        PackageList.SelectedItems.Cast<ImportedPackage>().ToArray();

    private bool CanChangeConfiguration() =>
        _recoveryCompleted &&
        !_operationInProgress &&
        !_processMonitor.IsStoryModeProcessRunning();

    private void RefreshSessionState()
    {
        var running = _processMonitor.IsStoryModeProcessRunning();
        var canConfigure = _recoveryCompleted && !_operationInProgress && !running;
        LocateButton.IsEnabled = canConfigure;
        RefreshButton.IsEnabled = canConfigure;
        ImportPackageButton.IsEnabled = canConfigure;
        DownloadPackageButton.IsEnabled = canConfigure;
        DetectPerformanceButton.IsEnabled = canConfigure;
        BackupStorySavesButton.IsEnabled = canConfigure;
        TrustPublisherButton.IsEnabled = canConfigure;
        PackageList.IsEnabled = canConfigure;
        ResearchCatalogueList.IsEnabled = canConfigure;
        ResearchCatalogueExpander.IsEnabled = canConfigure;
        ProfileSelector.IsEnabled = canConfigure;
        ProfileNameText.IsEnabled = canConfigure;
        SaveProfileButton.IsEnabled = canConfigure;
        ValidateButton.IsEnabled = canConfigure && _installation is not null && PackageList.SelectedItems.Count > 0;
        TrustPackagesCheckBox.IsEnabled = canConfigure && _installation is not null && PackageList.SelectedItems.Count > 0;
        ApplyButton.IsEnabled = canConfigure && _installation is not null && _packages.Count > 0 &&
                                (PackageList.SelectedItems.Count == 0 ||
                                 TrustPackagesCheckBox.IsChecked == true);
        RemoveSelectedButton.IsEnabled = canConfigure && _installation is not null &&
                                         PackageList.SelectedItems.Count > 0;
        RollbackButton.IsEnabled = canConfigure && _rollbackTransactionIds.Count > 0;
        LaunchButton.IsEnabled = canConfigure && _installation is not null;
        SelectRageCliButton.IsEnabled = canConfigure;
        VerifyRageCliButton.IsEnabled = canConfigure &&
                                        !string.IsNullOrWhiteSpace(RageCliExecutablePathText.Text);
        BuildAssetCatalogueButton.IsEnabled = canConfigure &&
                                              _installation is not null &&
                                              !string.IsNullOrWhiteSpace(_verifiedRageCliExecutablePath);
        CancelAssetCatalogueButton.IsEnabled = _operationInProgress &&
                                               _assetCatalogueCancellation is not null;

        SessionStateText.Text = running ? "GAME RUNNING · LOCKED" :
            _operationInProgress ? "OPERATION IN PROGRESS" :
            !_recoveryCompleted ? "RECOVERY REQUIRED · LOCKED" : "CONFIGURATION READY";
        LockExplanationText.Text = running
            ? "GTA5.exe is active or its state cannot be verified. All configuration and deployment controls are disabled."
            : _operationInProgress
                ? "A package or profile operation is running. Other configuration controls are temporarily locked."
                : !_recoveryCompleted
                    ? "Startup recovery has not completed successfully. Restart after resolving the transaction journal."
                    : "The game is stopped. Package actions require a validated installation and explicit package trust acknowledgement.";
    }

    private static string CreateProfileId(string name)
    {
        var characters = name.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var id = new string(characters);
        while (id.Contains("--", StringComparison.Ordinal))
            id = id.Replace("--", "-", StringComparison.Ordinal);
        id = id.Trim('-');

        if (id.Length < 3)
            id = "profile-" + id;
        if (id.Length > 80)
            id = id[..80].TrimEnd('-');

        return id;
    }

    protected override void OnClosed(EventArgs e)
    {
        _assetCatalogueCancellation?.Cancel();
        _assetCatalogueCancellation?.Dispose();
        _processTimer.Stop();
        _acquisitionService.Dispose();
        base.OnClosed(e);
    }
}
