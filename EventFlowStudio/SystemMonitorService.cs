using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace EventFlowStudio.Services
{
    public class SystemMonitorService
    {
        public event Action<string>? ProcessStarted;
        public event Action<string>? ProcessStopped;
        public event Action<string>? BluetoothConnected;
        public event Action<string>? BluetoothDisconnected;
        public event Action<string>? UsbConnected;

        private readonly HashSet<string> _runningProcesses = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _connectedBtDevices = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _connectedUsbDevices = new(StringComparer.OrdinalIgnoreCase);
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

        private async Task MonitorLoop(CancellationToken token)
        {
            UpdateProcessSnapshot(firstRun: true);
            UpdateBluetoothSnapshot(firstRun: true);
            UpdateUsbSnapshot(firstRun: true);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    UpdateProcessSnapshot(firstRun: false);
                    UpdateBluetoothSnapshot(firstRun: false);
                    UpdateUsbSnapshot(firstRun: false);
                }
                catch { }

                await Task.Delay(1500, token);
            }
        }

        private void UpdateProcessSnapshot(bool firstRun)
        {
            var currentProcesses = Process.GetProcesses()
                                          .Select(p => p.ProcessName)
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!firstRun)
            {
                foreach (var proc in currentProcesses.Except(_runningProcesses))
                {
                    ProcessStarted?.Invoke(proc);
                }

                foreach (var proc in _runningProcesses.Except(currentProcesses))
                {
                    ProcessStopped?.Invoke(proc);
                }
            }

            _runningProcesses.Clear();
            foreach (var p in currentProcesses) _runningProcesses.Add(p);
        }

        private void UpdateBluetoothSnapshot(bool firstRun)
        {
            var currentDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PNPEntity WHERE PNPClass = 'Bluetooth' AND Status = 'OK'");
                foreach (ManagementObject device in searcher.Get().Cast<ManagementObject>())
                {
                    string? name = device["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name)) currentDevices.Add(name);
                }
            }
            catch { }

            if (!firstRun)
            {
                foreach (var dev in currentDevices.Except(_connectedBtDevices)) BluetoothConnected?.Invoke(dev);
                foreach (var dev in _connectedBtDevices.Except(currentDevices)) BluetoothDisconnected?.Invoke(dev);
            }

            _connectedBtDevices.Clear();
            foreach (var d in currentDevices) _connectedBtDevices.Add(d);
        }

        private void UpdateUsbSnapshot(bool firstRun)
        {
            var currentDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%' AND Status = 'OK'");
                foreach (ManagementObject device in searcher.Get().Cast<ManagementObject>())
                {
                    string? name = device["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name)) currentDevices.Add(name);
                }
            }
            catch { }

            if (!firstRun)
            {
                foreach (var dev in currentDevices.Except(_connectedUsbDevices)) UsbConnected?.Invoke(dev);
            }

            _connectedUsbDevices.Clear();
            foreach (var d in currentDevices) _connectedUsbDevices.Add(d);
        }
    }
}