using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class CompatibilityTestEvidenceWindow : Window
{
    private readonly string _gameBuild;
    private readonly string _gameExecutableSha256;
    private readonly string _profileName;
    private readonly IReadOnlyList<CompatibilityTestPackage> _packages;
    private readonly CompatibilityTestEvidenceService _evidenceService;

    public IReadOnlyList<CompatibilityTestOutcome> AvailableOutcomes { get; } =
        Enum.GetValues<CompatibilityTestOutcome>();

    public List<CompatibilityTestScenarioResult> ScenarioResults { get; } = new();
    public CompatibilityTestEvidenceRecord? SavedRecord { get; private set; }

    public CompatibilityTestEvidenceWindow(
        string gameBuild,
        string gameExecutableSha256,
        string profileName,
        IReadOnlyList<CompatibilityTestPackage> packages,
        CompatibilityTestEvidenceService? evidenceService = null,
        string? initialEnvironment = null)
    {
        _gameBuild = gameBuild;
        _gameExecutableSha256 = gameExecutableSha256;
        _profileName = profileName;
        _packages = packages?.ToArray() ?? throw new ArgumentNullException(nameof(packages));
        _evidenceService = evidenceService ?? new CompatibilityTestEvidenceService();

        InitializeComponent();
        DataContext = this;

        GameSummaryText.Text = "GTA V Legacy build " + _gameBuild +
                               " · GTA5.exe SHA-256 " + _gameExecutableSha256;
        PackageStackText.Text = _packages.Count == 0
            ? "Package stack: VANILLA BASELINE (no Hub-managed mod packages)"
            : "Package stack:" + Environment.NewLine +
              string.Join(Environment.NewLine, _packages.Select(package =>
                  "• " + package.PackageName + " v" + package.PackageVersion +
                  " · " + package.PackageId + " · archive SHA-256 " + package.ArchiveSha256 +
                  " · signature " + package.SignatureStatus));

        TestEnvironmentTextBox.Text = string.IsNullOrWhiteSpace(initialEnvironment)
            ? PerformanceProfileService.FormatHardwareEvidence(PerformanceProfileService.CaptureHardware())
            : initialEnvironment;


        foreach (var scenario in CompatibilityTestEvidenceService.CreateScenarioResults())
            ScenarioResults.Add(scenario);
        ScenarioGrid.ItemsSource = ScenarioResults;
    }

    private async void SaveEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        ScenarioGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        ScenarioGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        if (!TryReadInt(BenchmarkRepeatsTextBox.Text, "Benchmark repeats", out var repeats) ||
            !TryReadInt(SoakDurationTextBox.Text, "Soak duration", out var soakMinutes) ||
            !TryReadOptionalDouble(AverageFpsTextBox.Text, "Average FPS", out var averageFps) ||
            !TryReadOptionalDouble(OnePercentLowFpsTextBox.Text, "1% low FPS", out var onePercentLow) ||
            !TryReadOptionalDouble(FrameTimeP95TextBox.Text, "P95 frame time", out var p95) ||
            !TryReadOptionalDouble(FrameTimeP99TextBox.Text, "P99 frame time", out var p99) ||
            !TryReadOptionalLong(PeakSystemRamTextBox.Text, "Peak system RAM", out var peakRam) ||
            !TryReadOptionalLong(PeakVramTextBox.Text, "Peak VRAM", out var peakVram) ||
            !TryReadOptionalDouble(PeakGpuTemperatureTextBox.Text, "Peak GPU temperature", out var gpuTemp) ||
            !TryReadOptionalDouble(PeakCpuTemperatureTextBox.Text, "Peak CPU temperature", out var cpuTemp))
            return;

        var metrics = new CompatibilityPerformanceMetrics(
            WarmupCompletedCheckBox.IsChecked == true,
            repeats,
            averageFps,
            onePercentLow,
            p95,
            p99,
            peakRam,
            peakVram,
            gpuTemp,
            cpuTemp,
            soakMinutes);

        var record = new CompatibilityTestEvidenceRecord(
            Guid.NewGuid().ToString("N"),
            _gameBuild,
            _gameExecutableSha256,
            _profileName,
            DateTimeOffset.UtcNow,
            TestEnvironmentTextBox.Text.Trim(),
            SummaryTextBox.Text.Trim(),
            _packages,
            metrics,
            ScenarioResults.Select(item => new CompatibilityTestScenarioResult
            {
                ScenarioId = item.ScenarioId,
                Name = item.Name,
                Outcome = item.Outcome,
                Notes = item.Notes?.Trim() ?? string.Empty
            }).ToArray());

        var errors = CompatibilityTestEvidenceService.Validate(record);
        if (errors.Count > 0)
        {
            EvidenceStatusText.Text = string.Join(Environment.NewLine, errors);
            MessageBox.Show(
                string.Join(Environment.NewLine, errors),
                "Compatibility test evidence is incomplete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _evidenceService.SaveAsync(record);
            SavedRecord = record;
            var summary = CompatibilityTestEvidenceService.Summarize(record);
            EvidenceStatusText.Text = summary.IsCompletePass
                ? "All " + summary.Total + " required scenarios are Passed, with the required benchmark and soak evidence. This is tester evidence only; it does not automatically approve the profile or packages."
                : summary.Passed + "/" + summary.Total + " passed; " + summary.Failed + " failed; " +
                  summary.Blocked + " blocked; " + summary.NotRun + " not run. The record is incomplete.";
            DialogResult = true;
        }
        catch (Exception exception)
        {
            EvidenceStatusText.Text = "Evidence could not be saved: " + exception.Message;
            MessageBox.Show(exception.Message, "Compatibility test evidence",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryReadInt(string? text, string label, out int value)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        EvidenceStatusText.Text = label + " must be an integer.";
        return false;
    }

    private bool TryReadOptionalLong(string? text, string label, out long? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        EvidenceStatusText.Text = label + " must be a whole number or blank.";
        value = null;
        return false;
    }

    private bool TryReadOptionalDouble(string? text, string label, out double? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed))
        {
            value = parsed;
            return true;
        }

        EvidenceStatusText.Text = label + " must be a finite number or blank (use a dot for decimals).";
        value = null;
        return false;
    }
}
