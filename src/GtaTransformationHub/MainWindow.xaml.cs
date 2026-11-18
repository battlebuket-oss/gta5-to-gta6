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
    private readonly PackageLibraryService _packageLibrary = new();
    private readonly PackageDeploymentCoordinator _deploymentCoordinator;
    private readonly ProfileStoreService _profileStore = new();
    private readonly ObservableCollection<ImportedPackage> _packages = new();
    private readonly ObservableCollection<ModProfile> _profiles = new();
    private readonly DispatcherTimer _processTimer;

    private GameInstallation? _installation;
    private bool _recoveryCompleted;
    private bool _deploymentReady;
    private bool _loading;
    private bool _operationInProgress;
    private string? _lastDeploymentTransactionId;

    public MainWindow()
    {
        InitializeComponent();

        _gameInstallationService = new GameInstallationService(_processMonitor);
        _deploymentCoordinator = new PackageDeploymentCoordinator(_deploymentService);

        PackageList.ItemsSource = _packages;
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
            _deploymentReady = false;
            _lastDeploymentTransactionId = null;
            GamePathText.Text = _installation.RootPath;
            DetectionText.Text = "GTA V Legacy build " + _installation.BuildVersion + " detected.";
            StatusText.Text = "Installation validated. Import packages, review the source and run preflight.";
        }
        catch (Exception exception)
        {
            _installation = null;
            _deploymentReady = false;
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
            _deploymentReady = false;
            DetectionText.Text = exception.Message;
            StatusText.Text = "Game installation validation failed.";
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
            Filter = "GTA Hub package (*.zip)|*.zip",
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
            var imported = await _archiveService.ImportAsync(dialog.FileName);
            var duplicate = _packages.FirstOrDefault(item =>
                item.Manifest.Id.Equals(imported.Manifest.Id, StringComparison.OrdinalIgnoreCase) &&
                item.Manifest.Version.Equals(imported.Manifest.Version, StringComparison.OrdinalIgnoreCase) &&
                item.PackageRoot.Equals(imported.PackageRoot, StringComparison.OrdinalIgnoreCase));

            if (duplicate is null)
                _packages.Add(imported);

            _deploymentReady = false;
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

        if (TrustPackagesCheckBox.IsChecked != true)
        {
            StatusText.Text = "Acknowledge package source and execution risk before applying.";
            return;
        }

        var selected = GetSelectedPackages();
        var confirmation = MessageBox.Show(
            "You are about to copy third-party mod files into this GTA V installation." +
            Environment.NewLine + Environment.NewLine +
            "Some mod files, including ASI or DLL plugins, may execute when GTA V starts. The Hub does not sandbox those files or verify the publisher's identity. Continue only if you obtained and trust these packages." +
            Environment.NewLine + Environment.NewLine +
            "A transaction journal and backups will be created. Continue with final preflight and deployment?",
            "Confirm third-party package installation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            StatusText.Text = "Running final preflight checks...";
            var report = await _deploymentCoordinator.ValidateAsync(_installation, selected);
            if (!report.IsValid)
            {
                StatusText.Text = "Preflight rejected the selected packages.";
                MessageBox.Show(string.Join(Environment.NewLine, report.Errors),
                    "Preflight failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "Applying verified files through the transactional deployment engine...";
            var result = await _deploymentCoordinator.ApplyAsync(_installation, selected);
            _lastDeploymentTransactionId = result.TransactionId;
            _deploymentReady = true;
            StatusText.Text = "Applied " + result.FilesApplied + " file(s). Transaction: " + result.TransactionId;
        }
        catch (Exception exception)
        {
            _deploymentReady = false;
            StatusText.Text = "Deployment failed. Inspect the transaction journal before retrying.";
            MessageBox.Show(
                exception.Message,
                "Deployment failed",
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
            _deploymentReady = false;
            TrustPackagesCheckBox.IsChecked = false;

            if (result.FilesApplied == 0)
            {
                StatusText.Text = "No matching Hub-managed files were found to remove.";
                _lastDeploymentTransactionId = null;
            }
            else
            {
                _lastDeploymentTransactionId = result.TransactionId;
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
        if (!CanChangeConfiguration() || string.IsNullOrWhiteSpace(_lastDeploymentTransactionId))
            return;

        var confirmation = MessageBox.Show(
            "Restore files from the latest deployment or removal transaction? Files changed after the operation will be protected and may cause rollback to stop.",
            "Confirm rollback",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _operationInProgress = true;
        RefreshSessionState();
        try
        {
            await _deploymentService.RollbackLatestAsync(_lastDeploymentTransactionId);
            StatusText.Text = "The latest transaction was rolled back.";
            _lastDeploymentTransactionId = null;
            _deploymentReady = false;
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

        var selectedIds = GetSelectedPackages()
            .Select(package => package.Manifest.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = CreateProfileId(name);
        if (!PackageIdValidator.IsValid(id))
        {
            StatusText.Text = "The profile name must produce a valid profile ID.";
            return;
        }

        var profile = new ModProfile(id, name, selectedIds, DateTimeOffset.UtcNow);
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
        _deploymentReady = false;
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
            var availableIds = new HashSet<string>(
                _packages.Select(package => package.Manifest.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var package in _packages)
            {
                if (profile.EnabledPackageIds.Contains(package.Manifest.Id))
                    PackageList.SelectedItems.Add(package);
            }

            var missing = profile.EnabledPackageIds.Where(id => !availableIds.Contains(id)).ToArray();
            ProfileWarningText.Text = missing.Length == 0
                ? "This profile references packages available in the local library."
                : "Missing imported packages: " + string.Join(", ", missing);
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

        _deploymentReady = false;
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
        if (!CanChangeConfiguration() || !_deploymentReady || _installation is null)
        {
            StatusText.Text = "Apply a validated package selection before launching from the Hub.";
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
            StatusText.Text = "GTA V launch requested. Configuration will lock when GTA5.exe starts.";
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
        PackageList.IsEnabled = canConfigure;
        ProfileSelector.IsEnabled = canConfigure;
        ProfileNameText.IsEnabled = canConfigure;
        SaveProfileButton.IsEnabled = canConfigure;
        ValidateButton.IsEnabled = canConfigure && _installation is not null && PackageList.SelectedItems.Count > 0;
        TrustPackagesCheckBox.IsEnabled = canConfigure && _installation is not null && PackageList.SelectedItems.Count > 0;
        ApplyButton.IsEnabled = canConfigure && _installation is not null &&
                                PackageList.SelectedItems.Count > 0 &&
                                TrustPackagesCheckBox.IsChecked == true;
        RemoveSelectedButton.IsEnabled = canConfigure && _installation is not null &&
                                         PackageList.SelectedItems.Count > 0;
        RollbackButton.IsEnabled = canConfigure && !string.IsNullOrWhiteSpace(_lastDeploymentTransactionId);
        LaunchButton.IsEnabled = canConfigure && _installation is not null && _deploymentReady;

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
        _processTimer.Stop();
        base.OnClosed(e);
    }
}
