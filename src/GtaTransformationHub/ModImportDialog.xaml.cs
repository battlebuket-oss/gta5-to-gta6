using System.Windows;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class ModImportDialog : Window
{
    public ModArchiveMetadata? Metadata { get; private set; }

    public ModImportDialog()
    {
        InitializeComponent();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(SourceUrlBox.Text.Trim(), UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(
                "Enter the mod author's official HTTPS source URL.",
                "Source URL required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Metadata = new ModArchiveMetadata(
            PackageIdBox.Text.Trim(),
            NameBox.Text.Trim(),
            AuthorBox.Text.Trim(),
            sourceUri,
            LicenceBox.Text.Trim(),
            VersionBox.Text.Trim());

        try
        {
            ModArchiveImportService.ValidateMetadata(Metadata);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Invalid package details",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
