using System.Collections.ObjectModel;
using System.Windows;
using GtaTransformationHub.Services;

namespace GtaTransformationHub;

public partial class ManagePublishersWindow : Window
{
    private readonly TrustedPublisherStore _store;
    private readonly ObservableCollection<TrustedPublisher> _publishers = new();

    public ManagePublishersWindow(TrustedPublisherStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        InitializeComponent();
        PublisherList.ItemsSource = _publishers;
        PublisherList.SelectionChanged += (_, _) =>
            RemovePublisherButton.IsEnabled = PublisherList.SelectedItem is TrustedPublisher;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var publisher in await _store.ListAsync())
                _publishers.Add(publisher);

            StatusText.Text = _publishers.Count == 0
                ? "No publisher keys are currently trusted."
                : _publishers.Count + " trusted publisher key(s).";
        }
        catch (Exception exception)
        {
            StatusText.Text = "The trust store could not be read: " + exception.Message;
            RemovePublisherButton.IsEnabled = false;
        }
    }

    private async void RemovePublisherButton_Click(object sender, RoutedEventArgs e)
    {
        if (PublisherList.SelectedItem is not TrustedPublisher publisher)
            return;

        var confirmation = MessageBox.Show(
            "Revoke trust for '" + publisher.Name + "'?" + Environment.NewLine + Environment.NewLine +
            "Packages signed by this key will be treated as untrusted during the next preflight. Existing game files will not be changed.",
            "Revoke publisher key",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        RemovePublisherButton.IsEnabled = false;
        try
        {
            var removed = await _store.RemoveAsync(publisher.Fingerprint);
            if (removed)
            {
                _publishers.Remove(publisher);
                StatusText.Text = "Publisher key revoked.";
            }
            else
            {
                StatusText.Text = "The publisher key was not found in the trust store.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "The publisher key could not be revoked: " + exception.Message;
        }

        RemovePublisherButton.IsEnabled = PublisherList.SelectedItem is TrustedPublisher;
    }
}
