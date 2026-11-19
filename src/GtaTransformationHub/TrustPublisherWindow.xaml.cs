using System.Windows;

namespace GtaTransformationHub;

public partial class TrustPublisherWindow : Window
{
    public string PublisherName => PublisherNameText.Text.Trim();
    public string Fingerprint => FingerprintText.Text.Trim();
    public string PublicKeyPem => PublicKeyText.Text.Trim();

    public TrustPublisherWindow()
    {
        InitializeComponent();
    }

    private void TrustButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PublisherName) ||
            string.IsNullOrWhiteSpace(Fingerprint) ||
            string.IsNullOrWhiteSpace(PublicKeyPem))
        {
            MessageBox.Show(
                "Enter a publisher name, independently verified fingerprint and public key PEM.",
                "Missing publisher information",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
