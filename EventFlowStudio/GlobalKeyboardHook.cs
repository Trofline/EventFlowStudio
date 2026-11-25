using System;
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

        // Gedrückt gehaltene Tasten senden laufend KEYDOWN. Wir melden nur das erste, sonst startet ein Hotkey-Profil dutzendfach.
        // Falls ein KEYUP verloren geht (z.B. beim Sperren), gilt eine Taste nach 1,2 Sek. ohne Wiederholung wieder als losgelassen.
        private readonly System.Collections.Generic.Dictionary<int, long> _keysDown = new();
        private const long RepeatTimeoutMs = 1200;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private readonly Action<uint, uint> _onKeyPressed;

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
            _proc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                long now = Environment.TickCount64;
                bool isRepeat = _keysDown.TryGetValue(vkCode, out long lastSeen) && now - lastSeen < RepeatTimeoutMs;
                _keysDown[vkCode] = now;
                if (isRepeat) return CallNextHookEx(_hookID, nCode, wParam, lParam);

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

                _onKeyPressed?.Invoke(modifiers, (uint)vkCode);
            }

            else if (nCode >= 0 && (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))
            {
                _keysDown.Remove(Marshal.ReadInt32(lParam));
            }

            // WICHTIG: IMMER CallNextHookEx aufrufen, damit der Input für Spiele NICHT blockiert wird!
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            UnhookWindowsHookEx(_hookID);
        }
    }
}