using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class StoryTestEvidenceWindow : Window
{
    private readonly ImportedPackage _package;
    private readonly GameInstallation _installation;
    private readonly string _gameExecutableSha256;
    private readonly StoryTestEvidenceService _evidenceService;

    public ObservableCollection<StoryTestScenarioResult> ScenarioResults { get; } = new();
    public IReadOnlyList<StoryTestOutcome> AvailableOutcomes { get; } = Enum.GetValues<StoryTestOutcome>();
    public StoryTestEvidenceRecord? SavedRecord { get; private set; }

    public StoryTestEvidenceWindow(
        ImportedPackage package,
        GameInstallation installation,
        string gameExecutableSha256,
        StoryTestEvidenceService? evidenceService = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(installation);
        _package = package;
        _installation = installation;
        _gameExecutableSha256 = gameExecutableSha256;
        _evidenceService = evidenceService ?? new StoryTestEvidenceService();

        InitializeComponent();
        DataContext = this;
        PackageSummaryText.Text = package.Manifest.Name + " · " + package.Manifest.Id +
                                  " · version " + package.Manifest.Version +
                                  " · archive SHA-256 " + package.ArchiveSha256;
        GameFingerprintText.Text = "GTA V Legacy build " + installation.BuildVersion +
                                   " · GTA5.exe SHA-256 " + _gameExecutableSha256;
        TestEnvironmentTextBox.Text =
            "Windows: " + Environment.OSVersion + Environment.NewLine +
            "Architecture: " + RuntimeInformation.OSArchitecture + Environment.NewLine +
            "Logical processors: " + Environment.ProcessorCount + Environment.NewLine +
            "GPU / VRAM: enter model, driver and VRAM" + Environment.NewLine +
            "RAM: enter installed RAM" + Environment.NewLine +
            "Launcher: enter Steam or Rockstar Games Launcher" + Environment.NewLine +
            "Display / settings: enter resolution, refresh rate and graphics settings";

        foreach (var scenario in StoryTestEvidenceService.CreateScenarioResults())
            ScenarioResults.Add(scenario);
        ScenarioGrid.ItemsSource = ScenarioResults;
    }

    private async void SaveEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        ScenarioGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        ScenarioGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        var record = new StoryTestEvidenceRecord(
            Guid.NewGuid().ToString("N"),
            _package.Manifest.Id,
            _package.Manifest.Version,
            _package.ArchiveSha256,
            _installation.BuildVersion,
            _gameExecutableSha256,
            DateTimeOffset.UtcNow,
            TestEnvironmentTextBox.Text.Trim(),
            SummaryTextBox.Text.Trim(),
            ScenarioResults.Select(item => new StoryTestScenarioResult
            {
                ScenarioId = item.ScenarioId,
                Name = item.Name,
                Outcome = item.Outcome,
                Notes = item.Notes?.Trim() ?? string.Empty
            }).ToArray());

        var errors = StoryTestEvidenceService.Validate(record);
        if (errors.Count > 0)
        {
            EvidenceStatusText.Text = string.Join(Environment.NewLine, errors);
            MessageBox.Show(
                string.Join(Environment.NewLine, errors),
                "Story Mode evidence is incomplete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _evidenceService.SaveAsync(record);
            SavedRecord = record;
            var summary = StoryTestEvidenceService.Summarize(record);
            EvidenceStatusText.Text = summary.IsCompletePass
                ? "All required scenarios are recorded as Passed. This is tester evidence only; it does not automatically approve the package."
                : summary.Passed + "/" + summary.Total + " passed; " + summary.Failed + " failed; " +
                  summary.Blocked + " blocked; " + summary.NotRun + " not run. The record remains incomplete.";
            DialogResult = true;
        }
        catch (Exception exception)
        {
            EvidenceStatusText.Text = "Evidence could not be saved: " + exception.Message;
            MessageBox.Show(
                exception.Message,
                "Evidence save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
