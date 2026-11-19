using System;
using System.Linq;
using System.Windows;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class BenchmarkImportDialog : Window
{
    public PerformanceBenchmarkMetadata? Metadata { get; private set; }

    public BenchmarkImportDialog()
    {
        InitializeComponent();
    }

    private void AnalyseButton_Click(object sender, RoutedEventArgs e)
    {
        var gameBuild = GameBuildText.Text.Trim();
        var resolution = ResolutionText.Text.Trim();
        var gpu = GpuText.Text.Trim();
        var driver = DriverText.Text.Trim();
        var profile = ProfileText.Text.Trim();
        var scenario = ScenarioText.Text.Trim();

        if (!Version.TryParse(gameBuild, out _))
        {
            MessageBox.Show(
                "Enter the numeric GTA5.exe file version, for example 1.0.3889.0. Do not leave the build as Unknown.",
                "Game build required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dimensions = resolution.Split('x', 'X');
        if (dimensions.Length != 2 ||
            !int.TryParse(dimensions[0], out var width) ||
            !int.TryParse(dimensions[1], out var height) ||
            width < 640 || height < 480 || width > 16384 || height > 16384)
        {
            MessageBox.Show(
                "Enter a valid resolution such as 1920x1080.",
                "Resolution required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(gpu) ||
            string.IsNullOrWhiteSpace(driver) ||
            string.IsNullOrWhiteSpace(profile) ||
            string.IsNullOrWhiteSpace(scenario))
        {
            MessageBox.Show(
                "GPU model, driver version, profile name and benchmark scenario are required for a reproducible report.",
                "Benchmark metadata required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Metadata = new PerformanceBenchmarkMetadata(
            gameBuild,
            width + "x" + height,
            gpu,
            driver,
            profile,
            scenario,
            NotesText.Text.Trim());

        DialogResult = true;
    }
}
