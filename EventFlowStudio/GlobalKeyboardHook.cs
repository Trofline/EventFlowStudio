using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EventFlowStudio.Services
{
    public class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private readonly Action<uint, uint> _onKeyPressed;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;
        private readonly HashSet<int> _keysDown = new();
        private bool _disposed;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public GlobalKeyboardHook(Action<uint, uint> onKeyPressed)
        {
            _onKeyPressed = onKeyPressed;
            // Der Hook läuft auf dem Thread, der ihn installiert (UI-Thread). Die Auswertung wird dorthin gepostet.
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _proc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))
            {
                _keysDown.Remove(Marshal.ReadInt32(lParam));
            }
            else if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Gedrückt gehaltene Tasten senden Auto-Repeat. Ohne diese Sperre lief ein Profil
                // dutzende Male, solange der Hotkey gehalten wurde.
                if (!_keysDown.Add(vkCode)) return CallNextHookEx(_hookID, nCode, wParam, lParam);

                // Modifiers checken
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

                uint modifiers = 0;
                if (alt) modifiers |= 0x0001;
                if (ctrl) modifiers |= 0x0002;
                if (shift) modifiers |= 0x0004;
                if (win) modifiers |= 0x0008;

                // Nicht direkt im Hook ausführen: Windows entfernt Low-Level-Hooks stillschweigend,
                // wenn der Callback zu lange braucht (z.B. Process.Start oder ein UAC-Dialog).
                uint vk = (uint)vkCode;
                _dispatcher.BeginInvoke(() => _onKeyPressed(modifiers, vk));
            }

            // WICHTIG: IMMER CallNextHookEx aufrufen, damit der Input für Spiele NICHT blockiert wird!
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hookID != IntPtr.Zero) UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }
    }
}