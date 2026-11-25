using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EventFlowStudio.Models;

namespace EventFlowStudio.Services
{
    // Momentaufnahme des Systemzustands, die der Monitor regelmäßig liefert
    public readonly record struct SystemStatus(TimeSpan IdleTime, bool OnAcPower, bool HasBattery, int BatteryPercent);

    /// <summary>
    /// Reine Logik für zeit- und zustandsbasierte Trigger (Uhrzeit, Intervall, Leerlauf, Akku).
    /// Ohne Windows-Abhängigkeiten, damit sie testbar bleibt.
    /// </summary>
    public static class TriggerEvaluator
    {
        public const int DefaultIdleMinutes = 5;
        public const int DefaultBatteryPercent = 20;
        public const int DefaultIntervalMinutes = 30;

        // Wie lange nach der geplanten Uhrzeit ein Uhrzeit-Trigger noch auslösen darf
        // (deckt kurze Hänger ab, verhindert aber Auslösen beim App-Start für längst vergangene Zeiten)
        public static readonly TimeSpan TimeOfDayWindow = TimeSpan.FromMinutes(2);

        /// <summary>"07:30, 18:00" -> Liste von Uhrzeiten. Ungültige Einträge werden ignoriert.</summary>
        public static List<TimeSpan> ParseTimes(string? text)
        {
            var result = new List<TimeSpan>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TimeSpan.TryParseExact(part.Trim(), new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var t)
                    && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                    result.Add(t);
            }
            return result;
        }

        /// <summary>Liest eine positive Zahl aus dem Eingabefeld, sonst den Standardwert.</summary>
        public static double ParseNumber(string? text, double fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string cleaned = new string(text.Trim().TakeWhile(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
        }

        public static bool ShouldFire(TriggerCondition trigger, DateTime now, SystemStatus status)
        {
            switch (trigger.Type)
            {
                case TriggerType.TimeOfDay:
                    foreach (var time in ParseTimes(trigger.TargetName))
                    {
                        var scheduled = now.Date + time;
                        // Kurz nach Mitternacht auch die Zeit von gestern prüfen (z.B. 23:59 -> 00:00:30)
                        if (scheduled > now) scheduled = scheduled.AddDays(-1);
                        if (now - scheduled < TimeOfDayWindow && trigger.LastFired < scheduled)
                        {
                            trigger.LastFired = now;
                            return true;
                        }
                    }
                    return false;

                case TriggerType.Interval:
                    {
                        var interval = TimeSpan.FromMinutes(ParseNumber(trigger.TargetName, DefaultIntervalMinutes));
                        if (trigger.LastFired == default) { trigger.LastFired = now; return false; } // Erst nach dem ersten Intervall auslösen
                        if (now - trigger.LastFired < interval) return false;
                        trigger.LastFired = now;
                        return true;
                    }

                case TriggerType.UserIdle:
                    {
                        var threshold = TimeSpan.FromMinutes(ParseNumber(trigger.TargetName, DefaultIdleMinutes));
                        if (status.IdleTime < threshold) { trigger.StateLatched = false; return false; }
                        if (trigger.StateLatched) return false;
                        trigger.StateLatched = true;
                        return true;
                    }

                case TriggerType.UserReturned:
                    {
                        var threshold = TimeSpan.FromMinutes(ParseNumber(trigger.TargetName, DefaultIdleMinutes));
                        if (status.IdleTime >= threshold) { trigger.StateLatched = true; return false; }
                        if (!trigger.StateLatched) return false;
                        trigger.StateLatched = false;
                        return true;
                    }

                case TriggerType.BatteryLow:
                    {
                        if (!status.HasBattery || status.BatteryPercent < 0) return false;
                        double threshold = ParseNumber(trigger.TargetName, DefaultBatteryPercent);
                        if (status.OnAcPower || status.BatteryPercent > threshold) { trigger.StateLatched = false; return false; }
                        if (trigger.StateLatched) return false;
                        trigger.StateLatched = true;
                        return true;
                    }

                default:
                    return false;
            }
        }

        /// <summary>Trigger, die regelmäßig über <see cref="ShouldFire"/> ausgewertet werden.</summary>
        public static bool IsPolled(TriggerType type) =>
            type is TriggerType.TimeOfDay or TriggerType.Interval or TriggerType.UserIdle or TriggerType.UserReturned or TriggerType.BatteryLow;

        /// <summary>
        /// Netzwerk-Trigger: leeres Feld = jede Verbindung (Ereignis "*"), sonst Teil des WLAN-Namens.
        /// </summary>
        public static bool IsNetworkMatch(string triggerTarget, string actual)
        {
            if (string.IsNullOrWhiteSpace(triggerTarget)) return actual == SystemMonitorService.AnyNetwork;
            return actual != SystemMonitorService.AnyNetwork && ExecutionEngine.IsNameMatch(triggerTarget, actual);
        }
    }
}
