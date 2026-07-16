using System.Windows;
using LyricFloat.Spotify;

namespace LyricFloat.Settings;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly SpotifyEnrichment _spotify;
    private readonly Action _applyChanges;

    public SettingsWindow(
        AppSettings settings, SettingsStore store, SpotifyEnrichment spotify, Action applyChanges)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        _spotify = spotify;
        _applyChanges = applyChanges;

        OpacitySlider.Value = settings.PanelOpacity;
        FontSlider.Value = settings.FontSize;
        LinesSlider.Value = settings.LineCount;
        OffsetSlider.Value = settings.OffsetMs;
        AutoStartBox.IsChecked = settings.AutoStart;
        AlbumArtBox.IsChecked = settings.ShowAlbumArt;
        ClientIdBox.Text = settings.SpotifyClientId;

        UpdateConnStatus();
        _spotify.ConnectionChanged += _ => Dispatcher.Invoke(UpdateConnStatus);
    }

    private void UpdateConnStatus()
        => ConnStatus.Text = _spotify.IsConnected ? "Connected \u2713" : "Not connected";

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        _settings.SpotifyClientId = ClientIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(_settings.SpotifyClientId))
        {
            MessageBox.Show(this, "Paste your Spotify app Client ID first.", "LyricFloat");
            return;
        }
        _store.Save(_settings);

        ConnectButton.IsEnabled = false;
        ConnStatus.Text = "Waiting for browser\u2026";
        var ok = await _spotify.ConnectAsync();
        ConnectButton.IsEnabled = true;
        ConnStatus.Text = ok ? "Connected \u2713" : "Failed \u2014 check Client ID / redirect URI";
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        _spotify.Disconnect();
        UpdateConnStatus();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.PanelOpacity = OpacitySlider.Value;
        _settings.FontSize = FontSlider.Value;
        _settings.LineCount = (int)LinesSlider.Value;
        _settings.OffsetMs = (int)OffsetSlider.Value;
        _settings.AutoStart = AutoStartBox.IsChecked == true;
        _settings.ShowAlbumArt = AlbumArtBox.IsChecked == true;
        _settings.SpotifyClientId = ClientIdBox.Text.Trim();

        _store.Save(_settings);
        try { AutoStartHelper.Apply(_settings.AutoStart); } catch { }
        _applyChanges();
        Close();
    }
}
