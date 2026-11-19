using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GtaTransformationHub.Services;
using Microsoft.Win32;

namespace GtaTransformationHub;

public partial class EcologyInventoryCurationWindow : Window
{
    private readonly GameInstallation _installation;
    private readonly EcologyInventoryCurationService _curationService;
    private EcologyInventoryCurationWorkspace _workspace;

    public ObservableCollection<EcologyCurationCandidateRow> CandidateRows { get; }
    public ObservableCollection<EcologySpeciesCurationRow> SpeciesRows { get; }
    public ObservableCollection<EcologyHabitatCurationRow> HabitatRows { get; }
    public ObservableCollection<EcologyPopulationCurationRow> PopulationRows { get; }
    public ObservableCollection<EcologyWaterBodyCurationRow> WaterRows { get; }

    public EcologyInventoryCurationWindow(
        GameInstallation installation,
        EcologyInventoryCurationWorkspace workspace,
        EcologyInventoryCurationService curationService)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(curationService);

        _installation = installation;
        _workspace = workspace;
        _curationService = curationService;

        CandidateRows = new ObservableCollection<EcologyCurationCandidateRow>(
            workspace.Candidates ?? new List<EcologyCurationCandidateRow>());
        SpeciesRows = new ObservableCollection<EcologySpeciesCurationRow>(
            workspace.Species ?? new List<EcologySpeciesCurationRow>());
        HabitatRows = new ObservableCollection<EcologyHabitatCurationRow>(
            workspace.Habitats ?? new List<EcologyHabitatCurationRow>());
        PopulationRows = new ObservableCollection<EcologyPopulationCurationRow>(
            workspace.PopulationEntries ?? new List<EcologyPopulationCurationRow>());
        WaterRows = new ObservableCollection<EcologyWaterBodyCurationRow>(
            workspace.WaterBodies ?? new List<EcologyWaterBodyCurationRow>());

        InitializeComponent();
        DataContext = this;

        HumanReviewedCheckBox.IsChecked = workspace.HumanReviewed;
        ReviewNotesTextBox.Text = workspace.ReviewNotes ?? string.Empty;
        CurationStatusText.Text =
            "Game build " + workspace.GameBuild +
            " · GTA5.exe SHA-256 " + workspace.GameExecutableSha256 +
            " · " + CandidateRows.Count + " heuristic candidate rows loaded.";
    }

    private async void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitPendingGridEdits();
            SynchronizeWorkspace();
            var draftPath = await _curationService.SaveDraftAsync(_workspace);
            CurationStatusText.Text =
                "Curation draft saved and serialized. Exact-build revalidation is performed when it is reopened or exported." +
                Environment.NewLine + draftPath;
        }
        catch (Exception exception)
        {
            CurationStatusText.Text = "Draft save failed: " + exception.Message;
            MessageBox.Show(exception.Message, "Ecology curation draft",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportSnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitPendingGridEdits();
            SynchronizeWorkspace();

            var validation = _curationService.BuildNormalizedSnapshot(_workspace);
            if (validation.Errors.Count > 0 || validation.Snapshot is null)
            {
                var errorText = string.Join(Environment.NewLine, validation.Errors);
                CurationStatusText.Text = "Snapshot validation failed. Correct the listed issues before exporting.";
                MessageBox.Show(errorText, "Normalized ecology snapshot validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Title = "Export normalized ecology inventory snapshot",
                Filter = "Ecology inventory JSON (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = "gta-v-ecology-" + _workspace.GameBuild + "-candidate.json",
                OverwritePrompt = true
            };
            if (saveDialog.ShowDialog(this) != true)
                return;

            // Persist, then reload through the provenance verifier before export.
            var draftPath = await _curationService.SaveDraftAsync(_workspace);
            _workspace = await _curationService.LoadDraftAsync(_installation, draftPath);

            var verified = _curationService.BuildNormalizedSnapshot(_workspace);
            if (verified.Errors.Count > 0 || verified.Snapshot is null)
            {
                var errorText = string.Join(Environment.NewLine, verified.Errors);
                CurationStatusText.Text = "Revalidated draft failed snapshot validation.";
                MessageBox.Show(errorText, "Normalized ecology snapshot validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var exported = await _curationService.ExportNormalizedSnapshotAsync(
                _workspace, saveDialog.FileName);

            var warnings = validation.Warnings.Count == 0
                ? string.Empty
                : Environment.NewLine + "Warnings: " + string.Join(" | ", validation.Warnings.Take(4));

            CurationStatusText.Text =
                "Normalized snapshot exported. The snapshot and evidence sidecar are saved." +
                Environment.NewLine + "Snapshot: " + exported.SnapshotPath +
                Environment.NewLine + "SHA-256: " + exported.SnapshotSha256 +
                Environment.NewLine + "Evidence: " + exported.EvidencePath +
                Environment.NewLine + "CompletenessVerified: false · PackageEligibilityUnlocked: false" +
                warnings;
            MessageBox.Show(
                "Normalized snapshot exported successfully. This is a human-curated partial inventory; it is not an exhaustive GTA V baseline and cannot unlock ecology-sensitive packages.",
                "Ecology snapshot exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            CurationStatusText.Text = "Snapshot export failed: " + exception.Message;
            MessageBox.Show(exception.Message, "Normalized ecology snapshot export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CommitPendingGridEdits()
    {
        CommitGrid(CandidateGrid);
        CommitGrid(SpeciesGrid);
        CommitGrid(HabitatGrid);
        CommitGrid(PopulationGrid);
        CommitGrid(WaterGrid);
    }

    private static void CommitGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
    }

    private void SynchronizeWorkspace()
    {
        _workspace.HumanReviewed = HumanReviewedCheckBox.IsChecked == true;
        _workspace.ReviewNotes = ReviewNotesTextBox.Text ?? string.Empty;
        _workspace.Candidates = CandidateRows.ToList();
        _workspace.Species = SpeciesRows.ToList();
        _workspace.Habitats = HabitatRows.ToList();
        _workspace.PopulationEntries = PopulationRows.ToList();
        _workspace.WaterBodies = WaterRows.ToList();
    }
}
