using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace InTouch
{
    /// <summary>
    /// Known Wacom devices with touch support, keyed by USB Product ID (VID is always 056A).
    /// PIDs sourced from the Linux wacom kernel driver (input-wacom wiki) and devicehunt.com.
    /// </summary>
    internal static class WacomDeviceDatabase
    {
        internal enum DeviceType { Tablet, Display }
        internal enum SizeClass { S, M, L, XL }
        internal enum ConnectionBus { Usb, Bluetooth }

        internal sealed record WacomDeviceInfo(
            int Pid,
            string ModelNumber,
            string ProductName,
            string Series,
            int Generation,
            DeviceType Type,
            SizeClass Size,
            float ActiveWidthMm,
            float ActiveHeightMm,
            int MaxTouchPoints,
            bool HasBluetooth,
            bool IsTouchInterface,
            string? Notes = null);

        // Map: USB PID → device info.  For pen displays the touch sensor often
        // has a *different* PID than the pen/pad sensor — both are listed.
        private static readonly Dictionary<int, WacomDeviceInfo> s_byPid = new()
        {
            // ── Intuos Pro 1st generation (PTH-x51) ──────────────────────
            [0x0314] = new(0x0314, "PTH-451", "Intuos Pro S", "Intuos Pro", 1,
                DeviceType.Tablet, SizeClass.S, 157f, 98f, 10, true, false),
            [0x0315] = new(0x0315, "PTH-651", "Intuos Pro M", "Intuos Pro", 1,
                DeviceType.Tablet, SizeClass.M, 224f, 148f, 10, true, false),
            [0x0317] = new(0x0317, "PTH-851", "Intuos Pro L", "Intuos Pro", 1,
                DeviceType.Tablet, SizeClass.L, 325f, 203f, 10, true, false),

            // ── Intuos Pro 2nd generation (PTH-x60) ──────────────────────
            [0x0357] = new(0x0357, "PTH-660", "Intuos Pro M", "Intuos Pro", 2,
                DeviceType.Tablet, SizeClass.M, 224f, 148f, 10, true, false),
            [0x0358] = new(0x0358, "PTH-860", "Intuos Pro L", "Intuos Pro", 2,
                DeviceType.Tablet, SizeClass.L, 311f, 216f, 10, true, false),

            // ── Cintiq Pro (1st wave: 13/16/24/32, pen+pad sensor) ───────
            [0x034F] = new(0x034F, "DTH-1320", "Cintiq Pro 13", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.S, 294f, 166f, 10, false, false),
            [0x0350] = new(0x0350, "DTH-1620", "Cintiq Pro 16", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.M, 344f, 194f, 10, false, false),
            [0x0351] = new(0x0351, "DTH-2420", "Cintiq Pro 24", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.L, 520f, 294f, 10, false, false),
            [0x0352] = new(0x0352, "DTH-3220", "Cintiq Pro 32", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.XL, 696f, 392f, 10, false, false),

            // ── Cintiq Pro (1st wave: touch sensor PIDs) ─────────────────
            [0x0353] = new(0x0353, "DTH-1320", "Cintiq Pro 13", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.S, 294f, 166f, 10, false, true,
                "Touch sensor interface"),
            [0x0354] = new(0x0354, "DTH-1620", "Cintiq Pro 16", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.M, 344f, 194f, 10, false, true,
                "Touch sensor interface"),
            [0x0355] = new(0x0355, "DTH-2420", "Cintiq Pro 24", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.L, 520f, 294f, 10, false, true,
                "Touch sensor interface"),
            [0x0356] = new(0x0356, "DTH-3220", "Cintiq Pro 32", "Cintiq Pro", 1,
                DeviceType.Display, SizeClass.XL, 696f, 392f, 10, false, true,
                "Touch sensor interface"),

            // ── Cintiq 13HD Touch ────────────────────────────────────────
            [0x0333] = new(0x0333, "DTH-1300", "Cintiq 13HD Touch", "Cintiq", 1,
                DeviceType.Display, SizeClass.S, 299f, 171f, 10, false, false),
            [0x0335] = new(0x0335, "DTH-1300", "Cintiq 13HD Touch", "Cintiq", 1,
                DeviceType.Display, SizeClass.S, 299f, 171f, 10, false, true,
                "Touch sensor interface"),

            // ── DTH-1152 ─────────────────────────────────────────────────
            [0x035A] = new(0x035A, "DTH-1152", "DTH-1152", "Cintiq", 1,
                DeviceType.Display, SizeClass.S, 235f, 132f, 10, false, false),
            [0x0368] = new(0x0368, "DTH-1152", "DTH-1152", "Cintiq", 1,
                DeviceType.Display, SizeClass.S, 235f, 132f, 10, false, true,
                "Touch sensor interface"),

            // ── Wacom One 13 Touch (DTH-134) ─────────────────────────────
            //    PID from devicehunt.com; exact value may vary by HW revision
            [0x03A6] = new(0x03A6, "DTH-134", "Wacom One 13 Touch", "Wacom One", 1,
                DeviceType.Display, SizeClass.S, 294f, 165f, 10, false, false,
                "PID approximate — verify with actual hardware"),

            // ── Wacom Movink (DTH-135K0) ─────────────────────────────────
            [0x03F0] = new(0x03F0, "DTH-135", "Wacom Movink", "Movink", 1,
                DeviceType.Display, SizeClass.S, 294f, 165f, 10, false, false,
                "OLED pen display"),

            // ── Cintiq Pro 2nd wave (2024): DTH-167, DTH-227, DTH-272 ───
            //    PIDs tentative — these shipped after most PID databases were
            //    updated.  WacomMT will still report capabilities even if the
            //    PID is unknown to this table.
            [0x03D0] = new(0x03D0, "DTH-167", "Cintiq Pro 16 (2024)", "Cintiq Pro", 2,
                DeviceType.Display, SizeClass.M, 344f, 194f, 10, false, false,
                "PID from devicehunt; verify on real hardware"),
        };

        // Bluetooth PIDs — Wacom tablets present a different PID over BT HID.
        // The Linux driver uses 0x509F for PTH-660 BT, but this appears to be
        // a generic "Pen and multitouch sensor" PID shared across BT models.
        // For now we store the few confirmed ones.
        private static readonly Dictionary<int, int> s_bluetoothToUsbPid = new()
        {
            // BT HID PID → USB PID (canonical)
        };

        public static bool TryGetByPid(int pid, [NotNullWhen(true)] out WacomDeviceInfo? info)
            => s_byPid.TryGetValue(pid, out info);

        public static IReadOnlyDictionary<int, WacomDeviceInfo> All => s_byPid;

        /// <summary>
        /// Best-effort identification from WacomMT capabilities when no USB PID
        /// is available.  Matches on physical dimensions + device type.
        /// </summary>
        public static WacomDeviceInfo? GuessFromCapabilities(
            float physicalWidthMm, float physicalHeightMm, bool isOpaque)
        {
            WacomDeviceInfo? best = null;
            float bestDist = float.MaxValue;

            foreach (var entry in s_byPid.Values)
            {
                // Filter: opaque tablets vs displays
                bool entryIsOpaque = entry.Type == DeviceType.Tablet;
                if (entryIsOpaque != isOpaque) continue;

                // Skip duplicate touch-sensor interface entries
                if (entry.IsTouchInterface) continue;

                float dx = entry.ActiveWidthMm - physicalWidthMm;
                float dy = entry.ActiveHeightMm - physicalHeightMm;
                float dist = dx * dx + dy * dy;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = entry;
                }
            }

            return best;
        }

        /// <summary>
        /// Classifies by physical width alone when no other data is available.
        /// </summary>
        public static SizeClass ClassifySize(float physicalWidthMm)
        {
            if (physicalWidthMm < 180) return SizeClass.S;
            if (physicalWidthMm < 300) return SizeClass.M;
            if (physicalWidthMm < 550) return SizeClass.L;
            return SizeClass.XL;
        }
    }
}
