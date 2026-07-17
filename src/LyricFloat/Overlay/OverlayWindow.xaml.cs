using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LyricFloat.Settings;

namespace LyricFloat.Overlay;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;   // hidden from Alt+Tab
    private const int WsExNoActivate = 0x08000000;   // never steals game focus

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoMoveNoSizeNoActivate = 0x0001 | 0x0002 | 0x0010;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    private const uint GwOwner = 4;

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _topmostWatchdog;
    private IntPtr _handle;
    private bool _editMode;

    public OverlayWindow(OverlayViewModel vm, AppSettings settings, SettingsStore store)
    {
        InitializeComponent();
        DataContext = vm;
        _settings = settings;
        _store = store;

        // Slide-up transition when the active lyric line advances: the fresh
        // content starts 14px low and eases into place, reading as a smooth
        // scroll step (SyncEngine raises this on the UI thread already).
        vm.ActiveLineAdvanced += () =>
        {
            if (!_settings.AnimateTransitions) return;
            var transform = (TranslateTransform)LinesControl.RenderTransform;
            var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.YProperty, slide);

            var fade = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LinesControl.BeginAnimation(OpacityProperty, fade);
        };

        RestorePosition();

        // Valorant borderless is ITSELF an always-on-top window and re-asserts
        // its z-order aggressively. A plain SetWindowPos(HWND_TOPMOST) on a
        // window that already has WS_EX_TOPMOST can be a no-op within the
        // topmost band - so we force a re-insertion at the very top of the
        // band by dropping topmost and immediately re-claiming it.
        _topmostWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _topmostWatchdog.Tick += (_, _) => ForceTopmost();
        _topmostWatchdog.Start();
    }

    public bool EditMode
    {
        get => _editMode;
        set
        {
            _editMode = value;
            ApplyClickThrough(!value);
            RootPanel.BorderBrush = value
                ? new SolidColorBrush(Color.FromRgb(0xE7, 0xC8, 0x7A))
                : null;
            RootPanel.BorderThickness = new Thickness(value ? 1.5 : 0);
            if (!value) SavePosition();
        }
    }

    // WinEvent hook: fires the instant ANY window becomes foreground
    // (game launching, alt-tab) so we re-claim topmost immediately instead
    // of waiting for the next watchdog tick.
    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutofcontext = 0x0000;
    private WinEventDelegate? _foregroundHook;   // keep a reference: GC must not collect it

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        _foregroundHook = (_, _, _, _, _, _, _) => Dispatcher.BeginInvoke(ForceTopmost);
        SetWinEventHook(EventSystemForeground, EventSystemForeground,
            IntPtr.Zero, _foregroundHook, 0, 0, WineventOutofcontext);

        var style = GetWindowLong(_handle, GwlExStyle);
        SetWindowLong(_handle, GwlExStyle,
            style | WsExLayered | WsExToolWindow | WsExNoActivate);

        ApplyClickThrough(true);
    }

    /// <summary>
    /// Re-insert this window at the very top of the always-on-top band.
    /// The NOTOPMOST->TOPMOST cycle is required to climb above other topmost
    /// windows (games); both calls happen back-to-back without a repaint, so
    /// there is no visible flicker.
    /// </summary>
    public void ForceTopmost()
    {
        if (_handle == IntPtr.Zero || Visibility != Visibility.Visible) return;

        // KNOWN WPF BUG (confirmed by Microsoft): with ShowInTaskbar=False,
        // WPF parents this window to a HIDDEN owner window that never gets
        // WS_EX_TOPMOST - and Windows z-orders owned windows with their
        // owner. Aggressive games (Valorant) exploit exactly that handicap.
        // Fix: force the hidden owner into the topmost band as well.
        var owner = GetWindow(_handle, GwOwner);
        if (owner != IntPtr.Zero)
        {
            SetWindowPos(owner, HwndNoTopmost, 0, 0, 0, 0, SwpNoMoveNoSizeNoActivate);
            SetWindowPos(owner, HwndTopmost, 0, 0, 0, 0, SwpNoMoveNoSizeNoActivate);
        }

        SetWindowPos(_handle, HwndNoTopmost, 0, 0, 0, 0, SwpNoMoveNoSizeNoActivate);
        SetWindowPos(_handle, HwndTopmost, 0, 0, 0, 0, SwpNoMoveNoSizeNoActivate);
    }

    private void ApplyClickThrough(bool enabled)
    {
        if (_handle == IntPtr.Zero) return;
        var style = GetWindowLong(_handle, GwlExStyle);
        SetWindowLong(_handle, GwlExStyle,
            enabled ? style | WsExTransparent : style & ~WsExTransparent);
    }

    private void OnPanelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editMode) DragMove();
    }

    private void OnPanelMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_editMode) return;
        // Scroll to resize the panel width in edit mode.
        _settings.PanelWidth = Math.Clamp(_settings.PanelWidth + (e.Delta > 0 ? 20 : -20), 220, 900);
        (DataContext as OverlayViewModel)?.RefreshFromSettings();
    }

    private void RestorePosition()
    {
        if (!double.IsNaN(_settings.X) && !double.IsNaN(_settings.Y))
        {
            Left = _settings.X;
            Top = _settings.Y;
        }
        else
        {
            // Default: top-center of the primary screen.
            Left = (SystemParameters.PrimaryScreenWidth - _settings.PanelWidth) / 2;
            Top = 40;
        }
    }

    public void SavePosition()
    {
        _settings.X = Left;
        _settings.Y = Top;
        _store.Save(_settings);
    }
}
