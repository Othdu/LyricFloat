using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LyricFloat.Settings;

public enum Hotkey
{
    ToggleOverlay = 1, // Ctrl+Shift+L
    ToggleEditMode = 2, // Ctrl+Shift+E
    OffsetPlus = 3, // Ctrl+Shift+Up    (lyrics later)
    OffsetMinus = 4, // Ctrl+Shift+Down  (lyrics earlier)
    OpenSettings = 5, // Ctrl+Shift+S
}

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    public event Action<Hotkey>? Pressed;

    public HotkeyManager(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)!;
        _source.AddHook(WndProc);

        RegisterHotKey(handle, (int)Hotkey.ToggleOverlay, ModControl | ModShift, 0x4C); // L
        RegisterHotKey(handle, (int)Hotkey.ToggleEditMode, ModControl | ModShift, 0x45); // E
        RegisterHotKey(handle, (int)Hotkey.OffsetPlus, ModControl | ModShift, 0x26); // Up
        RegisterHotKey(handle, (int)Hotkey.OffsetMinus, ModControl | ModShift, 0x28); // Down
        RegisterHotKey(handle, (int)Hotkey.OpenSettings, ModControl | ModShift, 0x53); // S
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            Pressed?.Invoke((Hotkey)wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        var handle = _source.Handle;
        foreach (Hotkey hk in Enum.GetValues<Hotkey>())
            UnregisterHotKey(handle, (int)hk);
        _source.RemoveHook(WndProc);
    }
}
