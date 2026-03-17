using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using static InTouch.WacomDeviceDatabase;

namespace InTouch
{
    /// <summary>
    /// Identifies the attached Wacom device by reading USB VID/PID from WMI.
    /// All queries use Win32_PnPEntity which is read-only and works under
    /// standard (unprivileged) user accounts — no admin elevation required.
    /// </summary>
    internal sealed class DeviceIdentifier
    {
        private static readonly Regex s_pidRegex = new(
            @"VID_056A&PID_([0-9A-Fa-f]{4})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public sealed record IdentifiedDevice(
            string RawName,
            string DeviceInstanceId,
            int UsbPid,
            WacomDeviceInfo? KnownDevice);

        /// <summary>
        /// Enumerates all Wacom PnP entities and returns identified devices.
        /// Merges USB PID lookup with the static database.
        /// </summary>
        public static List<IdentifiedDevice> Detect()
        {
            var results = new List<IdentifiedDevice>();
            var seen = new HashSet<int>();

            try
            {
                // Win32_PnPEntity is accessible by all users (no admin needed).
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DeviceID FROM Win32_PnPEntity " +
                    "WHERE DeviceID LIKE '%VID_056A%' OR DeviceID LIKE '%VID&0002056A%'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string? deviceId = obj["DeviceID"]?.ToString();
                    string? name = obj["Name"]?.ToString();
                    if (deviceId == null || name == null) continue;

                    var match = s_pidRegex.Match(deviceId);
                    if (!match.Success) continue;

                    int pid = Convert.ToInt32(match.Groups[1].Value, 16);
                    if (!seen.Add(pid)) continue; // deduplicate

                    TryGetByPid(pid, out var known);
                    results.Add(new IdentifiedDevice(name, deviceId, pid, known));
                }
            }
            catch
            {
                // WMI may not be available in some sandboxed environments
            }

            return results;
        }

        /// <summary>
        /// Returns a single best-match summary for display in the tray tooltip.
        /// Prefers USB PID match; falls back to WacomMT capability guess.
        /// </summary>
        public static string GetDeviceSummary(float wmtWidthMm = 0, float wmtHeightMm = 0, bool isOpaque = true)
        {
            var devices = Detect();

            // Prefer the first device with a known PID match
            foreach (var d in devices)
            {
                if (d.KnownDevice != null)
                    return $"{d.KnownDevice.ProductName} ({d.KnownDevice.ModelNumber})";
            }

            // Fall back to raw WMI name
            foreach (var d in devices)
                return d.RawName;

            // Fall back to WacomMT dimension guess
            if (wmtWidthMm > 0)
            {
                var guess = GuessFromCapabilities(wmtWidthMm, wmtHeightMm, isOpaque);
                if (guess != null)
                    return $"{guess.ProductName} (estimated)";
            }

            return "Unknown Wacom device";
        }
    }
}
