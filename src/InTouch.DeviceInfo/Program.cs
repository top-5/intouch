using System;
using System.Management;
using System.Runtime.InteropServices;
using WacomMTDN;
using WintabDN;

namespace InTouch.DeviceInfo
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            bool json = args.Length > 0 && args[0] == "--json";

            Console.WriteLine("InTouch Device Info");
            Console.WriteLine(new string('=', 50));

            // -- HID / USB device name (the real marketing name) --
            Console.WriteLine();
            Console.WriteLine("[USB/HID Devices]");
            try
            {
                PrintUsbDevices(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error querying USB devices: {ex.Message}");
            }

            // -- Wintab --
            Console.WriteLine();
            Console.WriteLine("[Wintab Driver]");
            bool wintabOk = false;
            try
            {
                wintabOk = CWintabInfo.IsWintabAvailable();
                Console.WriteLine($"  Available:      {wintabOk}");

                if (wintabOk)
                {
                    Console.WriteLine($"  Device name:    {CWintabInfo.GetDeviceInfo()}");
                    Console.WriteLine($"  Device count:   {CWintabInfo.GetNumberOfDevices()}");

                    var axX = CWintabInfo.GetDeviceAxis(0, EAxisDimension.AXIS_X);
                    var axY = CWintabInfo.GetDeviceAxis(0, EAxisDimension.AXIS_Y);
                    Console.WriteLine($"  X axis:         0–{axX.axMax} (res {axX.axResolution}, units {axX.axUnits})");
                    Console.WriteLine($"  Y axis:         0–{axY.axMax} (res {axY.axResolution}, units {axY.axUnits})");
                    Console.WriteLine($"  Max pressure:   {CWintabInfo.GetMaxPressure()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }

            // -- WacomMT (Multi-Touch) --
            Console.WriteLine();
            Console.WriteLine("[WacomMT (Multi-Touch)]");
            CWacomMTConfig? config = null;
            try
            {
                config = new CWacomMTConfig();
                config.Init();

                int count = config.GetNumAttachedTouchDevices();
                Console.WriteLine($"  Touch devices:  {count}");

                for (int i = 0; i < count; i++)
                {
                    int id = config.GetAttachedDeviceID(i);
                    if (id < 0) continue;

                    var caps = config.GetDeviceCaps(id);
                    string model = ClassifyModel(caps.PhysicalSizeX);

                    Console.WriteLine();
                    Console.WriteLine($"  --- Touch Device {i} (ID {id}) ---");
                    Console.WriteLine($"  Type:           {caps.Type}");
                    Console.WriteLine($"  Model (est.):   {model}");
                    Console.WriteLine($"  Physical size:  {caps.PhysicalSizeX:F1} × {caps.PhysicalSizeY:F1} mm");
                    Console.WriteLine($"  Logical size:   {caps.LogicalWidth} × {caps.LogicalHeight}");
                    Console.WriteLine($"  Reported size:  {caps.ReportedSizeX} × {caps.ReportedSizeY}");
                    Console.WriteLine($"  Scan size:      {caps.ScanSizeX} × {caps.ScanSizeY}");
                    Console.WriteLine($"  Max fingers:    {caps.FingerMax}");
                    Console.WriteLine($"  Max blobs:      {caps.BlobMax}");

                    var flags = caps.CapabilityFlags;
                    bool raw = (flags & WacomMTCapabilityFlags.WMTCapabilityFlagsRawAvailable) != 0;
                    bool blob = (flags & WacomMTCapabilityFlags.WMTCapabilityFlagsBlobAvailable) != 0;
                    bool sens = (flags & WacomMTCapabilityFlags.WMTCapabilityFlagsSensitivityAvailable) != 0;
                    Console.WriteLine($"  Capabilities:   Raw={raw}, Blob={blob}, Sensitivity={sens}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
            finally
            {
                try { config?.Quit(); } catch { }
            }

            // -- System info --
            Console.WriteLine();
            Console.WriteLine("[System]");
            Console.WriteLine($"  OS:             {RuntimeInformation.OSDescription}");
            Console.WriteLine($"  Architecture:   {RuntimeInformation.OSArchitecture}");
            Console.WriteLine($"  Process arch:   {RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"  .NET:           {RuntimeInformation.FrameworkDescription}");

            return 0;
        }

        static string ClassifyModel(float physicalWidthMm)
        {
            if (physicalWidthMm < 180) return "Small (S)";
            if (physicalWidthMm < 270) return "Medium (M)";
            return "Large (L)";
        }

        static void PrintUsbDevices(bool json)
        {
            // Query WMI for Wacom USB/HID devices — this gives the real product name
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE Name LIKE '%Wacom%' OR Name LIKE '%Intuos%'");

            var results = searcher.Get();
            int found = 0;

            foreach (ManagementObject obj in results)
            {
                string? name = obj["Name"]?.ToString();
                string? deviceId = obj["DeviceID"]?.ToString();
                string? status = obj["Status"]?.ToString();

                if (name == null) continue;
                found++;
                Console.WriteLine($"  [{found}] {name}");
                Console.WriteLine($"       ID:     {deviceId}");
                Console.WriteLine($"       Status: {status}");
            }

            if (found == 0)
                Console.WriteLine("  No Wacom USB/HID devices found");
        }
    }
}
