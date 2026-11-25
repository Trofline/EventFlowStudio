using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EventFlowStudio.Models;

namespace EventFlowStudio.Services
{
    public class SystemMonitorService
    {
        /// <summary>Name, der bei Netzwerk-Ereignissen für "irgendeine Verbindung" gemeldet wird.</summary>
        public const string AnyNetwork = "*";

        public event Action<string>? ProcessStarted;
        public event Action<string>? ProcessStopped;
        public event Action<string>? BluetoothConnected;
        public event Action<string>? BluetoothDisconnected;
        public event Action<string>? UsbConnected;
        public event Action<string>? UsbDisconnected;
        public event Action<string>? ForegroundAppChanged;
        public event Action<string>? NetworkConnected;
        public event Action<string>? NetworkDisconnected;
        public event Action<bool>? PowerSourceChanged; // true = Netzteil angeschlossen
        public event Action<SystemStatus>? StatusUpdated;

        /// <summary>
        /// Welche Trigger-Typen gerade in aktiven Profilen vorkommen. Teure Abfragen (WMI, netsh) laufen nur, wenn sie gebraucht werden.
        /// Wird vom UI-Thread neu gesetzt und hier nur gelesen.
        /// </summary>
        public volatile IReadOnlySet<TriggerType> UsedTriggerTypes = new HashSet<TriggerType>();

        private const int LoopMilliseconds = 1500;
        private const int NetworkEveryNthLoop = 5; // ~7,5 Sek.

        private readonly HashSet<string> _runningProcesses = new(StringComparer.OrdinalIgnoreCase);
        private readonly DeviceSnapshot _bluetooth = new();
        private readonly DeviceSnapshot _usb = new();
        private readonly DeviceSnapshot _networks = new();
        private string? _foregroundApp;
        private bool? _onAcPower;
        private int _loopCount;
        private CancellationTokenSource? _cts;

        public void StartMonitoring()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => MonitorLoop(_cts.Token));
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
        }

        private bool Uses(params TriggerType[] types) => types.Any(UsedTriggerTypes.Contains);

        private async Task MonitorLoop(CancellationToken token)
        {
            UpdateProcessSnapshot(firstRun: true);

            while (!token.IsCancellationRequested)
            {
                // Jeder Schritt einzeln abgesichert: ein Fehler (auch in einem Event-Handler) darf die anderen nicht blockieren
                Safe(() => UpdateProcessSnapshot(firstRun: false));

                Safe(() => UpdateDevices(_bluetooth, Uses(TriggerType.BluetoothConnected, TriggerType.BluetoothDisconnected),
                    () => QueryPnpNames("SELECT Name FROM Win32_PNPEntity WHERE PNPClass = 'Bluetooth' AND Status = 'OK'"),
                    BluetoothConnected, BluetoothDisconnected));

                Safe(() => UpdateDevices(_usb, Uses(TriggerType.UsbDeviceConnected, TriggerType.UsbDeviceDisconnected),
                    () => QueryPnpNames("SELECT Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%' AND Status = 'OK'"),
                    UsbConnected, UsbDisconnected));

                if (_loopCount % NetworkEveryNthLoop == 0)
                    Safe(() => UpdateDevices(_networks, Uses(TriggerType.NetworkConnected, TriggerType.NetworkDisconnected),
                        QueryNetworks, NetworkConnected, NetworkDisconnected));

                Safe(UpdateForegroundApp);
                Safe(UpdatePowerAndStatus);

                _loopCount++;
                try { await Task.Delay(LoopMilliseconds, token); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static void Safe(Action step)
        {
            try { step(); }
            catch (Exception ex) { Debug.WriteLine($"SystemMonitorService: {ex}"); }
        }

        private void UpdateProcessSnapshot(bool firstRun)
        {
            var currentProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p) currentProcesses.Add(p.ProcessName);
            }

            var started = firstRun ? new List<string>() : currentProcesses.Except(_runningProcesses).ToList();
            var stopped = firstRun ? new List<string>() : _runningProcesses.Except(currentProcesses).ToList();

            // Erst den Stand übernehmen, dann melden: sonst würde ein Fehler im Handler die Meldung jede Runde wiederholen
            _runningProcesses.Clear();
            foreach (var p in currentProcesses) _runningProcesses.Add(p);

            foreach (var proc in started) ProcessStarted?.Invoke(proc);
            foreach (var proc in stopped) ProcessStopped?.Invoke(proc);
        }

        /// <summary>Vergleicht eine Geräteliste mit dem letzten Stand. Ist der Trigger nicht in Gebrauch, wird nichts abgefragt.</summary>
        private static void UpdateDevices(DeviceSnapshot snapshot, bool inUse, Func<HashSet<string>> query, Action<string>? added, Action<string>? removed)
        {
            if (!inUse) { snapshot.Reset(); return; }

            var current = query();
            if (!snapshot.Initialized)
            {
                // Erste Abfrage nach dem Aktivieren nur merken, nicht melden
                snapshot.Set(current);
                return;
            }

            var newItems = current.Except(snapshot.Items).ToList();
            var goneItems = snapshot.Items.Except(current).ToList();
            snapshot.Set(current);

            foreach (var d in newItems) added?.Invoke(d);
            foreach (var d in goneItems) removed?.Invoke(d);
        }

        private static HashSet<string> QueryPnpNames(string wql)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var searcher = new ManagementObjectSearcher(wql);
            using var collection = searcher.Get();
            foreach (ManagementObject device in collection.Cast<ManagementObject>())
            {
                using (device)
                {
                    string? name = device["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                }
            }
            return result;
        }

        // ---------- Netzwerk ----------

        private static HashSet<string> QueryNetworks()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (HasUsableNetwork()) result.Add(AnyNetwork);
            foreach (var ssid in QueryWifiNames()) result.Add(ssid);
            return result;
        }

        private static bool HasUsableNetwork()
        {
            // GetIsNetworkAvailable() ist auch bei virtuellen Adaptern (Hyper-V, VPN) true. Wir verlangen ein Gateway.
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (nic.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)))
                    return true;
            }
            return false;
        }

        private static IEnumerable<string> QueryWifiNames()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // netsh schreibt in der OEM-Codepage (z.B. 850), sonst werden Umlaute im WLAN-Namen zerstört
                    StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
                };
                using var proc = Process.Start(psi);
                if (proc == null) return Array.Empty<string>();
                string output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } }
                return ParseWifiNames(output);
            }
            catch
            {
                return Array.Empty<string>(); // Kein WLAN-Dienst (z.B. Desktop ohne WLAN)
            }
        }

        /// <summary>Liest die SSIDs aus der Ausgabe von "netsh wlan show interfaces". BSSID-Zeilen werden ignoriert.</summary>
        public static List<string> ParseWifiNames(string netshOutput)
        {
            var result = new List<string>();
            foreach (Match m in Regex.Matches(netshOutput ?? string.Empty, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline))
            {
                string name = m.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
            }
            return result;
        }

        // ---------- Vordergrund-App ----------

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private void UpdateForegroundApp()
        {
            if (!Uses(TriggerType.ForegroundApp)) { _foregroundApp = null; return; }

            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string name;
            try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
            catch { return; }

            if (string.Equals(name, _foregroundApp, StringComparison.OrdinalIgnoreCase)) return;
            bool firstRun = _foregroundApp == null;
            _foregroundApp = name;
            if (!firstRun) ForegroundAppChanged?.Invoke(name);
        }

        // ---------- Strom, Akku, Leerlauf ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }
        [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        private void UpdatePowerAndStatus()
        {
            bool onAc = true, hasBattery = false;
            int percent = -1;
            if (GetSystemPowerStatus(out var ps))
            {
                onAc = ps.ACLineStatus != 0; // 0 = Akku, 1 = Netz, 255 = unbekannt
                hasBattery = ps.BatteryFlag != 128 && ps.BatteryFlag != 255; // 128 = kein Akku
                percent = ps.BatteryLifePercent == 255 ? -1 : ps.BatteryLifePercent;
            }

            if (_onAcPower.HasValue && _onAcPower.Value != onAc) PowerSourceChanged?.Invoke(onAc);
            _onAcPower = onAc;

            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            var idle = TimeSpan.Zero;
            if (GetLastInputInfo(ref info)) idle = TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));

            StatusUpdated?.Invoke(new SystemStatus(idle, onAc, hasBattery, percent));
        }

        private sealed class DeviceSnapshot
        {
            public HashSet<string> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
            public bool Initialized { get; private set; }
            public void Set(IEnumerable<string> items) { Items.Clear(); foreach (var i in items) Items.Add(i); Initialized = true; }
            public void Reset() { Items.Clear(); Initialized = false; }
        }
    }
}
