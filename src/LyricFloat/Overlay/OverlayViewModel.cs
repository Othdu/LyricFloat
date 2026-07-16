using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricFloat.Settings;

namespace LyricFloat.Overlay;

public sealed class LineVm
{
    public required string Text { get; init; }
    public required double FontSize { get; init; }
    public required double Opacity { get; init; }
    public required Brush Foreground { get; init; }
    public required FontWeight FontWeight { get; init; }
    public required FlowDirection FlowDirection { get; init; }
}

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private IReadOnlyList<LyricLine> _allLines = Array.Empty<LyricLine>();
    private SyncType _syncType = SyncType.NotFound;
    private int _activeIndex = -1;
    private TrackInfo? _track;
    private Brush _accent;
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xCF));

    public ObservableCollection<LineVm> Lines { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayViewModel(AppSettings settings)
    {
        _settings = settings;
        _accent = BuildAccent();
        ShowMessage("\u266A  LyricFloat \u2014 play something on Spotify");
    }

    // ----- bindable panel properties -------------------------------------

    public double PanelWidth => _settings.PanelWidth;
    public double PanelOpacity => _settings.PanelOpacity;

    private ImageSource? _albumArt;
    public ImageSource? AlbumArt
    {
        get => _albumArt;
        private set { _albumArt = value; Raise(nameof(AlbumArt)); Raise(nameof(ArtVisibility)); }
    }

    public Visibility ArtVisibility =>
        _settings.ShowAlbumArt && _albumArt is not null ? Visibility.Visible : Visibility.Collapsed;

    private Visibility _panelVisibility = Visibility.Visible;
    public Visibility PanelVisibility
    {
        get => _panelVisibility;
        set { _panelVisibility = value; Raise(nameof(PanelVisibility)); }
    }

    // ----- state transitions ----------------------------------------------

    public void SetTrackLoading(TrackInfo track)
    {
        _track = track;
        _allLines = Array.Empty<LyricLine>();
        _syncType = SyncType.NotFound;
        _activeIndex = -1;
        PanelVisibility = Visibility.Visible;
        ShowMessage($"\u266A  {track.Artist} \u2014 {track.Title}");
    }

    public void SetLyricSet(LyricSet set)
    {
        _syncType = set.Type;
        _allLines = set.Lines;
        _activeIndex = -1;

        switch (set.Type)
        {
            case SyncType.Synced:
                RebuildWindow();
                break;
            case SyncType.Plain:
                ShowMessage("\u266A  Lyrics found (not synced for this track)");
                break;
            default:
                ShowMessage("\u266A  No lyrics found");
                break;
        }
    }

    public void SetActiveIndex(int index)
    {
        if (_syncType != SyncType.Synced) return;
        _activeIndex = index;
        RebuildWindow();
    }

    public void SetAlbumArt(string? url)
    {
        if (url is null) { AlbumArt = null; return; }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            AlbumArt = bmp;
        }
        catch { AlbumArt = null; }
    }

    public void HideForAd() => PanelVisibility = Visibility.Collapsed;

    public void RefreshFromSettings()
    {
        _accent = BuildAccent();
        Raise(nameof(PanelWidth));
        Raise(nameof(PanelOpacity));
        Raise(nameof(ArtVisibility));
        if (_syncType == SyncType.Synced) RebuildWindow();
    }

    // ----- internals -------------------------------------------------------

    private void RebuildWindow()
    {
        Lines.Clear();
        if (_allLines.Count == 0) return;

        var count = Math.Max(1, _settings.LineCount);
        var half = count / 2;
        var center = Math.Clamp(_activeIndex < 0 ? 0 : _activeIndex, 0, _allLines.Count - 1);
        var start = Math.Max(0, Math.Min(center - half, _allLines.Count - count));
        var end = Math.Min(_allLines.Count - 1, start + count - 1);

        for (var i = start; i <= end; i++)
        {
            var isActive = i == _activeIndex;
            var text = string.IsNullOrWhiteSpace(_allLines[i].Text) ? "\u266A" : _allLines[i].Text;
            Lines.Add(new LineVm
            {
                Text = text,
                FontSize = isActive ? _settings.FontSize * 1.25 : _settings.FontSize,
                Opacity = isActive ? 1.0 : 0.45,
                Foreground = isActive ? _accent : Dim,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                FlowDirection = IsRtl(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            });
        }
    }

    private void ShowMessage(string message)
    {
        Lines.Clear();
        Lines.Add(new LineVm
        {
            Text = message,
            FontSize = _settings.FontSize,
            Opacity = 0.8,
            Foreground = Dim,
            FontWeight = FontWeights.Normal,
            FlowDirection = IsRtl(message) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        });
    }

    private Brush BuildAccent()
    {
        try
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(_settings.AccentColor));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0xE7, 0xC8, 0x7A));
        }
    }

    /// <summary>Per-line RTL detection (Arabic + Hebrew ranges) for mixed-language songs.</summary>
    private static bool IsRtl(string text) => text.Any(c =>
        (c >= 0x0590 && c <= 0x05FF) ||   // Hebrew
        (c >= 0x0600 && c <= 0x06FF) ||   // Arabic
        (c >= 0x0750 && c <= 0x077F) ||   // Arabic Supplement
        (c >= 0x08A0 && c <= 0x08FF) ||   // Arabic Extended-A
        (c >= 0xFB50 && c <= 0xFDFF) ||   // Arabic Presentation Forms-A
        (c >= 0xFE70 && c <= 0xFEFF));    // Arabic Presentation Forms-B

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
