using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EventFlowStudio.Models;

namespace EventFlowStudio.Services
{
    /// <summary>Windows-Funktionen für Lautstärke, Medientasten, Energie-Befehle und Systemzustand.</summary>
    public static class SystemActions
    {
        // ---------- Lautstärke (Core Audio, Standard-Ausgabegerät) ----------

        /// <summary>Setzt die Lautstärke (0-100). Bei mehr als 0 wird die Stummschaltung aufgehoben.</summary>
        public static void SetVolume(int percent) => WithEndpointVolume(v =>
        {
            var ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx));
            if (percent > 0) Marshal.ThrowExceptionForHR(v.SetMute(false, ref ctx));
        });

        public static void SetMute(bool mute) => WithEndpointVolume(v =>
        {
            var ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(v.SetMute(mute, ref ctx));
        });

        private static void WithEndpointVolume(Action<IAudioEndpointVolume> work)
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            IAudioEndpointVolume? volume = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device));
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o));
                volume = (IAudioEndpointVolume)o;
                work(volume);
            }
            finally
            {
                if (volume != null) Marshal.ReleaseComObject(volume);
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        // ---------- Medientasten ----------

        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
        private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

        public static void PressMediaKey(MediaKey key)
        {
            byte vk = key switch
            {
                MediaKey.NextTrack => 0xB0,
                MediaKey.PreviousTrack => 0xB1,
                MediaKey.Stop => 0xB2,
                MediaKey.VolumeDown => 0xAE,
                MediaKey.VolumeUp => 0xAF,
                _ => 0xB3, // Play/Pause
            };
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ---------- Energie / Sitzung ----------

        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ExitWindowsEx(uint flags, uint reason);
        [DllImport("powrprof.dll", SetLastError = true)] private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        // Zeit, in der man ein Herunterfahren/Neustarten noch mit "shutdown /a" abbrechen kann
        public const int ShutdownGraceSeconds = 15;

        public static string RunPowerCommand(SystemPowerCommand command)
        {
            switch (command)
            {
                case SystemPowerCommand.MonitorOff:
                    PostMessage((IntPtr)0xFFFF /* HWND_BROADCAST */, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF170 /* SC_MONITORPOWER */, (IntPtr)2);
                    return "Bildschirm ausgeschaltet";
                case SystemPowerCommand.Sleep:
                    if (!SetSuspendState(false, false, false)) throw new System.ComponentModel.Win32Exception();
                    return "Energiesparmodus";
                case SystemPowerCommand.Hibernate:
                    if (!SetSuspendState(true, false, false)) throw new System.ComponentModel.Win32Exception();
                    return "Ruhezustand";
                case SystemPowerCommand.SignOut:
                    if (!ExitWindowsEx(0 /* EWX_LOGOFF */, 0)) throw new System.ComponentModel.Win32Exception();
                    return "Abmelden";
                case SystemPowerCommand.Shutdown:
                case SystemPowerCommand.Restart:
                    string mode = command == SystemPowerCommand.Shutdown ? "/s" : "/r";
                    using (Process.Start(new ProcessStartInfo("shutdown", $"{mode} /t {ShutdownGraceSeconds} /c \"EventFlow Studio\"") { UseShellExecute = false, CreateNoWindow = true })) { }
                    return $"{(command == SystemPowerCommand.Shutdown ? "Herunterfahren" : "Neustart")} in {ShutdownGraceSeconds} Sek. (abbrechen mit \"shutdown /a\")";
                default:
                    return "Unbekannter Befehl";
            }
        }

        // ---------- Systemzustand ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }
        [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        /// <summary>Netzteil/Akku-Zustand. BatteryPercent = -1, wenn unbekannt oder kein Akku vorhanden.</summary>
        public static (bool OnAcPower, bool HasBattery, int BatteryPercent) GetPowerStatus()
        {
            if (!GetSystemPowerStatus(out var ps)) return (true, false, -1);
            bool onAc = ps.ACLineStatus != 0;                               // 0 = Akku, 1 = Netz, 255 = unbekannt
            bool hasBattery = ps.BatteryFlag != 128 && ps.BatteryFlag != 255; // 128 = kein Akku
            int percent = hasBattery && ps.BatteryLifePercent != 255 ? ps.BatteryLifePercent : -1;
            return (onAc, hasBattery, percent);
        }
    }
}
