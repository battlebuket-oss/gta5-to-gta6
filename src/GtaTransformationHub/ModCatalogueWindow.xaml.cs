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
    private readonly ModProfileStore _profileStore = new();
    private readonly ModProfileSelectionService _selectionService = new();
    private CatalogueDocument? _catalogue;
    private HashSet<string> _selectedPackageIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _profileStorageAvailable = true;
    private bool _loaded;

    public ModCatalogueWindow(string? installedGameBuild)
    {
        InitializeComponent();
        _installedGameBuild = installedGameBuild;
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
            researchingCount + " still require verification · no mods installed by this screen";
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
        if (!_loaded || _catalogue is null || sender is not CheckBox toggle ||
            toggle.Tag is not string packageId)
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
                              " Nothing has been installed.";
            ProfileStorageText.Text = "PROFILE SAVED";
            RenderCatalogue();
        }
        catch (Exception exception)
        {
            _selectedPackageIds = previous;
            RenderCatalogue();
            StatusText.Text = "Profile could not be saved. Previous selection was restored: " +
                              exception.Message;
            MessageBox.Show(StatusText.Text, "Profile save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
