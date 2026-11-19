using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class ModCatalogueWindow : Window
{
    private readonly string? _installedGameBuild;
    private readonly string? _gameExecutablePath;
    private readonly string _packageLibraryRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GtaTransformationHub", "Packages");
    private readonly PackageAcquisitionService _packageAcquisitionService = new();
    private readonly OpenRageToolchainService _openRageToolchainService;
    private readonly GtaViInspiredHudProfileAdapter _hudProfileAdapter;
    private readonly ImportedModLibraryService _libraryService = new();
    private readonly ModProfileStore _profileStore = new();
    private readonly ModProfileSelectionService _selectionService = new();
    private ImportedModLibraryEntry? _hudPackage;
    private bool? _hudInstalled;
    private bool _operationBusy;
    private CatalogueDocument? _catalogue;
    private HashSet<string> _selectedPackageIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _profileStorageAvailable = true;
    private bool _savingProfile;
    private bool _loaded;

    public ModCatalogueWindow(string? installedGameBuild, string? gameExecutablePath)
    {
        InitializeComponent();
        _installedGameBuild = installedGameBuild;
        _gameExecutablePath = gameExecutablePath;
        _openRageToolchainService = new OpenRageToolchainService(_packageAcquisitionService);
        _hudProfileAdapter = new GtaViInspiredHudProfileAdapter(_openRageToolchainService);
        Closed += (_, _) => _packageAcquisitionService.Dispose();
        GameBuildText.Text = string.IsNullOrWhiteSpace(installedGameBuild)
            ? "LOCATE GTA V TO CHECK BUILD"
            : "GTA V LEGACY · " + installedGameBuild;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var cataloguePath = Path.Combine(AppContext.BaseDirectory, "Catalogue", "catalogue.json");
            _catalogue = new CatalogueService().Load(cataloguePath);

            try
            {
                var saved = await _profileStore.LoadEnabledPackageIdsAsync();
                var knownIds = new HashSet<string>(
                    _catalogue.Packages.Select(package => package.Id),
                    StringComparer.OrdinalIgnoreCase);
                var unknownIds = saved.Where(id => !knownIds.Contains(id)).ToArray();
                _selectedPackageIds = saved.Where(knownIds.Contains)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                StatusText.Text = unknownIds.Length > 0
                    ? unknownIds.Length + " saved package ID(s) are no longer in the catalogue and were ignored."
                    : "Saved profile loaded. No GTA V files have been changed.";
            }
            catch (Exception exception)
            {
                _profileStorageAvailable = false;
                ProfileStorageText.Text = "PROFILE READ-ONLY";
                StatusText.Text = "Saved profile could not be read. Toggles are locked to avoid overwriting it: " +
                                  exception.Message;
            }

            _loaded = true;
            RenderCatalogue();
            await RefreshInstalledStateAsync();
        }
        catch (Exception exception)
        {
            CatalogueList.Children.Clear();
            CatalogueList.Children.Add(new TextBlock
            {
                Text = "The mod catalogue could not be loaded. No changes were made.\n\n" + exception.Message,
                Foreground = Brushes.IndianRed,
                TextWrapping = TextWrapping.Wrap
            });
            ProfileStorageText.Text = "CATALOGUE ERROR";
        }
    }

    private void RenderCatalogue()
    {
        if (_catalogue is null)
            return;

        CatalogueList.Children.Clear();
        foreach (var category in _catalogue.Categories)
        {
            var packages = _catalogue.Packages
                .Where(package => string.Equals(package.Category, category.Id,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (packages.Length == 0)
                continue;

            CatalogueList.Children.Add(new TextBlock
            {
                Text = category.Name.ToUpperInvariant(),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 70, 85)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, 14, 2, 8)
            });

            foreach (var package in packages)
                CatalogueList.Children.Add(CreatePackageCard(package));
        }

        var selectedCount = _selectedPackageIds.Count;
        var researchingCount = _catalogue.Packages.Count(package =>
            _selectedPackageIds.Contains(package.Id) &&
            package.Status == CatalogueStatus.Researching);
        SelectionSummaryText.Text =
            selectedCount + " selected in profile · " +
            researchingCount + " research candidate(s) selected";
        ApplyProfileButton.IsEnabled = _profileStorageAvailable &&
                                       !_operationBusy &&
                                       !string.IsNullOrWhiteSpace(_gameExecutablePath);
        RefreshInstalledStateButton.IsEnabled = !_operationBusy &&
                                                !string.IsNullOrWhiteSpace(_gameExecutablePath);
    }

    private Border CreatePackageCard(ModPackage package)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 29, 36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 0, 0, 7)
        };

        var content = new StackPanel();
        var top = new DockPanel();

        var toggle = new CheckBox
        {
            Content = _selectedPackageIds.Contains(package.Id) ? "ON" : "OFF",
            IsChecked = _selectedPackageIds.Contains(package.Id),
            IsEnabled = _profileStorageAvailable &&
                        package.Status is not (CatalogueStatus.Blocked or CatalogueStatus.Deprecated),
            Tag = package.Id,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 62,
            ToolTip = "Include or exclude this package from the saved profile. This does not install it."
        };
        toggle.Checked += PackageToggle_Changed;
        toggle.Unchecked += PackageToggle_Changed;
        DockPanel.SetDock(toggle, Dock.Right);
        top.Children.Add(toggle);

        top.Children.Add(new TextBlock
        {
            Text = package.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0)
        });
        content.Children.Add(top);

        content.Children.Add(new TextBlock
        {
            Text = package.Status.ToString().ToUpperInvariant() + "  ·  " +
                   package.Version + "  ·  " + package.PerformanceTier,
            Foreground = package.Status == CatalogueStatus.Tested
                ? Brushes.LightGreen
                : package.Status is CatalogueStatus.Blocked or CatalogueStatus.Deprecated
                    ? Brushes.IndianRed
                    : new SolidColorBrush(Color.FromRgb(255, 190, 105)),
            FontSize = 10,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = package.Notes,
            Foreground = new SolidColorBrush(Color.FromRgb(173, 180, 192)),
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var requirements = package.ExternalRequirements is { Count: > 0 }
            ? "Requires: " + string.Join(", ", package.ExternalRequirements)
            : "No external tool requirement recorded.";
        content.Children.Add(new TextBlock
        {
            Text = requirements,
            Foreground = new SolidColorBrush(Color.FromRgb(141, 150, 165)),
            FontSize = 10,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var footer = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
        var sourceButton = new Button
        {
            Content = "OPEN AUTHOR SOURCE",
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(52, 58, 69)),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        sourceButton.Click += (_, _) => OpenSource(package.SourceUri);
        footer.Children.Add(sourceButton);

        footer.Children.Add(new TextBlock
        {
            Text = package.InstallAdapter,
            Foreground = new SolidColorBrush(Color.FromRgb(141, 150, 165)),
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        });
        content.Children.Add(footer);

        card.Child = content;
        return card;
    }

    private async void PackageToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _savingProfile || _catalogue is null ||
            sender is not CheckBox toggle || toggle.Tag is not string packageId)
            return;

        var wasEnabled = _selectedPackageIds.Contains(packageId);
        var requestedEnabled = toggle.IsChecked == true;
        if (wasEnabled == requestedEnabled)
            return;

        var result = _selectionService.SetEnabled(
            _catalogue, _selectedPackageIds, packageId, requestedEnabled);
        if (!result.Success)
        {
            MessageBox.Show(result.Error ?? "The selection could not be changed.",
                "Mod selection blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            RenderCatalogue();
            return;
        }

        var previous = _selectedPackageIds;
        _selectedPackageIds = result.SelectedPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _savingProfile = true;
        CatalogueList.IsEnabled = false;
        ProfileStorageText.Text = "SAVING PROFILE…";
        try
        {
            await _profileStore.SaveEnabledPackageIdsAsync(_selectedPackageIds);
            var changes = result.AddedDependencies.Count > 1
                ? " Dependencies were selected automatically: " +
                  string.Join(", ", result.AddedDependencies.Where(id => id != packageId)) + "."
                : string.Empty;
            if (result.RemovedDependents.Count > 0)
                changes += " Dependent packages were also switched off: " +
                           string.Join(", ", result.RemovedDependents) + ".";

            StatusText.Text = (requestedEnabled ? "Added " : "Removed ") +
                              packageId + " in the saved profile." + changes +
                              " Installation state is unchanged until APPLY SELECTED PROFILE is used.";
            ProfileStorageText.Text = "PROFILE SAVED";
        }
        catch (Exception exception)
        {
            _selectedPackageIds = previous;
            StatusText.Text = "Profile could not be saved. Previous selection was restored: " +
                              exception.Message;
            ProfileStorageText.Text = "SAVE FAILED";
            MessageBox.Show(StatusText.Text, "Profile save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _savingProfile = false;
            CatalogueList.IsEnabled = true;
            RenderCatalogue();
        }
    }

    private async Task RefreshInstalledStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_gameExecutablePath))
        {
            _hudInstalled = null;
            AppliedStateText.Text = "GAME STATE: locate GTA V Legacy in the main window before applying a profile.";
            return;
        }

        if (_operationBusy)
            return;

        _operationBusy = true;
        RefreshSessionButtons();
        try
        {
            var entries = await _libraryService.LoadAsync(_packageLibraryRoot);
            _hudPackage = entries
                .Where(entry =>
                    string.Equals(entry.PackageId, GtaViInspiredHudPackageService.PackageId,
                        StringComparison.OrdinalIgnoreCase) &&
                    entry.ManifestValid &&
                    string.Equals(entry.OriginalArchiveSha256,
                        GtaViInspiredHudPackageService.PackageSha256,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.ImportedAt)
                .FirstOrDefault();

            if (_hudPackage is null)
            {
                _hudInstalled = null;
                AppliedStateText.Text = "HUD STATE: unknown — import the pinned official Legacy HUD OIV from the main window first.";
                return;
            }

            var installation = new GameInstallationService()
                .ValidateExecutable(_gameExecutablePath);
            var groupName = GtaViInspiredHudProfileAdapter.GetPackageGroupName(
                _hudPackage.OivInspection!);
            var installed = await _hudProfileAdapter.GetInstalledStateAsync(
                installation, groupName);

            _hudInstalled = installed is not null;
            AppliedStateText.Text = installed is null
                ? "GTA VI HUD: NOT INSTALLED · OpenRAGE journal confirms the package group is absent."
                : "GTA VI HUD: INSTALLED · " + installed.RecordedChanges +
                  " journalled changes · group: " + installed.Name;
            StatusText.Text = "Installed state refreshed from the OpenRAGE package journal.";
        }
        catch (Exception exception)
        {
            _hudInstalled = null;
            AppliedStateText.Text = "HUD STATE: UNKNOWN — " + exception.Message;
            StatusText.Text = "Installed state could not be verified. Apply is blocked until the journal can be read.";
        }
        finally
        {
            _operationBusy = false;
            RefreshSessionButtons();
        }
    }

    private void RefreshSessionButtons()
    {
        var canUseGame = !string.IsNullOrWhiteSpace(_gameExecutablePath);
        ApplyProfileButton.IsEnabled = _profileStorageAvailable && !_operationBusy && canUseGame;
        RefreshInstalledStateButton.IsEnabled = !_operationBusy && canUseGame;
        CatalogueList.IsEnabled = !_operationBusy && _profileStorageAvailable;
    }

    private async void RefreshInstalledStateButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshInstalledStateAsync();
    }

    private async void ApplyProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_catalogue is null || string.IsNullOrWhiteSpace(_gameExecutablePath) ||
            !_profileStorageAvailable || _operationBusy)
            return;

        var unsupportedSelected = _selectedPackageIds
            .Where(id => !string.Equals(id, GtaViInspiredHudPackageService.PackageId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (unsupportedSelected.Length > 0)
        {
            ReadinessSummaryText.Text =
                "Profile apply blocked: " + unsupportedSelected.Length +
                " selected package(s) do not yet have verified ON/OFF adapters.";
            ReadinessDetailsText.Text =
                string.Join(Environment.NewLine, unsupportedSelected.Select(id =>
                    "• " + id + " — desired selection is saved, but the installer adapter is not implemented/approved."));
            StatusText.Text = "No game changes were made. Disable unsupported selections or wait for their adapter integrations.";
            return;
        }

        _operationBusy = true;
        RefreshSessionButtons();
        try
        {
            var entries = await _libraryService.LoadAsync(_packageLibraryRoot);
            _hudPackage = entries
                .Where(entry =>
                    string.Equals(entry.PackageId, GtaViInspiredHudPackageService.PackageId,
                        StringComparison.OrdinalIgnoreCase) &&
                    entry.ManifestValid &&
                    string.Equals(entry.OriginalArchiveSha256,
                        GtaViInspiredHudPackageService.PackageSha256,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.ImportedAt)
                .FirstOrDefault();

            if (_hudPackage is null)
            {
                throw new InvalidOperationException(
                    "The pinned HUD OIV is not imported. Use GET OFFICIAL LEGACY HUD OIV in the main window, then retry.");
            }

            var installation = new GameInstallationService()
                .ValidateExecutable(_gameExecutablePath);
            var shouldEnable = _selectedPackageIds.Contains(
                GtaViInspiredHudPackageService.PackageId);
            var groupName = GtaViInspiredHudProfileAdapter.GetPackageGroupName(
                _hudPackage.OivInspection!);
            var packageState = await _hudProfileAdapter.GetInstalledStateAsync(
                installation, groupName);

            if (shouldEnable && packageState is null)
            {
                var prerequisites = new GtaViInspiredHudPrerequisiteService().Inspect(installation);
                if (!prerequisites.AllRequiredFilesPresent)
                {
                    throw new InvalidOperationException(
                        "HUD loader prerequisites are missing: " +
                        string.Join(", ", prerequisites.MissingItems.Select(item => item.RelativePath)));
                }

                var preview = await _openRageToolchainService.InspectOivAsync(
                    installation, _hudPackage.OriginalArchivePath!);
                if (!preview.IsInstallable)
                {
                    throw new InvalidDataException(
                        "OpenRAGE blocked this package during dry-run: " +
                        string.Join("; ", preview.Blockers));
                }

                var runtimeConsent = MessageBox.Show(
                    "This is a research-stage community HUD, not a Hub-approved/tested package. Its author requires GTA V English, Script Hook V .NET 3.7.0 Nightly 193 or newer, and a Script Hook V release compatible with the exact game patch. The Hub verified the required loader files but cannot prove their runtime versions.\\n\\nConfirm that you have independently verified those requirements and that you accept testing this package in Story Mode?",
                    "Confirm HUD runtime and compatibility",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (runtimeConsent != MessageBoxResult.Yes)
                    return;

                var rootFileConsent = !preview.RequiresExecutableConsent ||
                    MessageBox.Show(
                        "OpenRAGE's dry-run includes loose game-root plugin files alongside mods-folder changes. These can affect GTA5.exe startup. OpenRAGE will journal the changes and handle its own uninstall group. Continue?",
                        "Confirm game-root plugin operations",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                if (!rootFileConsent)
                    return;

                var finalConfirm = MessageBox.Show(
                    "Apply the saved profile by installing this exact pinned HUD OIV through OpenRAGE?\\n\\nPackage group: " +
                    groupName + "\\nArchive SHA-256: " + _hudPackage.OriginalArchiveSha256 +
                    "\\n\\nThe OIV dry-run will be repeated immediately before installation, and the Hub will verify the installed group afterwards.",
                    "Apply selected GTA V profile",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (finalConfirm != MessageBoxResult.Yes)
                    return;

                var result = await _hudProfileAdapter.ApplyAsync(
                    installation, _packageLibraryRoot, _hudPackage,
                    shouldBeEnabled: true,
                    userConfirmed: true,
                    allowGameFolderFiles: preview.RequiresExecutableConsent,
                    confirmedHudRuntimeAndLanguage: true);

                ReadinessSummaryText.Text = result.Message;
                ReadinessDetailsText.Text = string.Join(Environment.NewLine, result.Details);
                StatusText.Text = "HUD enabled through OpenRAGE and its exact journal group was verified. Gameplay compatibility is not yet certified.";
            }
            else if (!shouldEnable && packageState is not null)
            {
                var disableConfirm = MessageBox.Show(
                    "The saved profile has the HUD OFF. OpenRAGE will uninstall only this exact journal group and verify that it is absent afterwards. Continue?\\n\\n" +
                    groupName,
                    "Disable GTA VI-Inspired HUD",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (disableConfirm != MessageBoxResult.Yes)
                    return;

                var result = await _hudProfileAdapter.ApplyAsync(
                    installation, _packageLibraryRoot, _hudPackage,
                    shouldBeEnabled: false,
                    userConfirmed: true,
                    allowGameFolderFiles: false,
                    confirmedHudRuntimeAndLanguage: false);

                ReadinessSummaryText.Text = result.Message;
                ReadinessDetailsText.Text = string.Join(Environment.NewLine, result.Details);
                StatusText.Text = "HUD disable operation completed and OpenRAGE journal confirms the group is absent.";
            }
            else
            {
                ReadinessSummaryText.Text = shouldEnable
                    ? "The saved HUD selection is ON and the OpenRAGE journal already confirms it is installed."
                    : "The saved HUD selection is OFF and the OpenRAGE journal confirms it is already uninstalled.";
                ReadinessDetailsText.Text = "No game files were changed.";
                StatusText.Text = "Profile and installed HUD state already match.";
            }

            await RefreshInstalledStateAfterApplyAsync();
        }
        catch (Exception exception)
        {
            ReadinessSummaryText.Text = "Profile apply failed or was blocked.";
            ReadinessDetailsText.Text = exception.ToString();
            StatusText.Text = "The Hub did not mark the profile as applied. Inspect OpenRAGE's journal/history before retrying.";
            MessageBox.Show(exception.Message, "Profile apply failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _operationBusy = false;
            RefreshSessionButtons();
            await RefreshInstalledStateAsync();
            return;
        }

        _operationBusy = false;
        RefreshSessionButtons();
    }

    private async Task RefreshInstalledStateAfterApplyAsync()
    {
        _operationBusy = false;
        RefreshSessionButtons();
        await RefreshInstalledStateAsync();
    }

    private void CheckReadinessButton_Click(object sender, RoutedEventArgs e)
    {
        if (_catalogue is null)
            return;

        if (_selectedPackageIds.Count == 0)
        {
            ReadinessSummaryText.Text = "No packages are selected. Choose mods to create a loadout.";
            ReadinessDetailsText.Text = "No installation has been attempted.";
            return;
        }

        var build = string.IsNullOrWhiteSpace(_installedGameBuild)
            ? "unknown"
            : _installedGameBuild;
        var result = new CatalogueService().ValidateSelection(
            _catalogue, _selectedPackageIds, build);

        ReadinessSummaryText.Text = result.IsValid
            ? "Catalogue checks passed. A separate deployment preflight and in-game validation are still required."
            : result.Errors.Count + " blocker(s) prevent installation. " +
              result.Warnings.Count + " external requirement warning(s).";

        ReadinessDetailsText.Text = string.Join(
            Environment.NewLine,
            result.Errors.Take(18).Select(error => "• " + error)
                .Concat(result.Warnings.Take(8).Select(warning => "• " + warning)));

        StatusText.Text = result.IsValid
            ? "Selection passed catalogue validation; no game files have been changed."
            : "Selection saved, but installation is blocked until every readiness issue is resolved.";
    }

    private static void OpenSource(Uri sourceUri)
    {
        try
        {
            if (sourceUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Only HTTPS author-source links are allowed.");

            Process.Start(new ProcessStartInfo
            {
                FileName = sourceUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not open author source",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
