using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;

namespace LyricFloat.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly TaskbarIcon? _icon;
    private readonly MenuItem? _editItem;

    public event Action? ToggleOverlayRequested;
    public event Action<bool>? EditModeChanged;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconController()
    {
        try
        {
            var menu = new ContextMenu();

            var toggle = new MenuItem { Header = "Show / Hide overlay  (Ctrl+Shift+L)" };
            toggle.Click += (_, _) => ToggleOverlayRequested?.Invoke();

            _editItem = new MenuItem { Header = "Edit mode  (Ctrl+Shift+E)", IsCheckable = true };
            _editItem.Click += (_, _) => EditModeChanged?.Invoke(_editItem.IsChecked);

            var settings = new MenuItem { Header = "Settings\u2026  (Ctrl+Shift+S)" };
            settings.Click += (_, _) => SettingsRequested?.Invoke();

            var exit = new MenuItem { Header = "Exit" };
            exit.Click += (_, _) => ExitRequested?.Invoke();

            menu.Items.Add(toggle);
            menu.Items.Add(_editItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(settings);
            menu.Items.Add(new Separator());
            menu.Items.Add(exit);

            _icon = new TaskbarIcon
            {
                ToolTipText = "LyricFloat",
                ContextMenu = menu,
                Visibility = Visibility.Visible,
                IconSource = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Assets/app.ico")),
            };
            _icon.TrayLeftMouseUp += (_, _) => SettingsRequested?.Invoke();

            // H.NotifyIcon requires an explicit create when the icon is built
            // in code (outside a XAML tree) - without this it never appears.
            _icon.ForceCreate(enablesEfficiencyMode: false);

            Log.Write("Tray icon created");
        }
        catch (Exception ex)
        {
            // Never let a tray failure kill the app; hotkeys still work.
            Log.Write($"Tray icon FAILED: {ex.Message}");
            _icon = null;
        }
    }

    public void SetEditMode(bool on)
    {
        if (_editItem is not null) _editItem.IsChecked = on;
    }

    public void Dispose() => _icon?.Dispose();
}
