using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EventFlowStudio.Services
{
    public class NativeHotkeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;

        private HwndSource? _source;
        private IntPtr _windowHandle;
        private readonly Action<int> _onHotkeyTriggered;

        public NativeHotkeyService(Action<int> onHotkeyTriggered)
        {
            _onHotkeyTriggered = onHotkeyTriggered;
        }

        public void Initialize(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);
        }

        public bool Register(int id, uint modifiers, uint vk)
        {
            if (_windowHandle == IntPtr.Zero) return false;
            Unregister(id);
            return RegisterHotKey(_windowHandle, id, modifiers, vk);
        }

        public void Unregister(int id)
        {
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, id);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                _onHotkeyTriggered?.Invoke(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _source?.RemoveHook(HwndHook);
        }
    }
}