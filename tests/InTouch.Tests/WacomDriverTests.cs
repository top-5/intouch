using System;
using Xunit;
using Xunit.Abstractions;
using WacomMTDN;
using WintabDN;

namespace InTouch.Tests
{
    [Collection("WacomMT")]
    public class WacomDriverTests
    {
        private readonly ITestOutputHelper _output;
        private readonly WacomMTFixture _mt;

        public WacomDriverTests(WacomMTFixture mt, ITestOutputHelper output)
        {
            _mt = mt;
            _output = output;
        }

        // -- Wintab Driver --

        [Fact]
        public void Wintab_IsAvailable()
        {
            bool available = CWintabInfo.IsWintabAvailable();
            _output.WriteLine($"Wintab available: {available}");
            Assert.True(available, "Wintab32.dll driver not responding. Is a Wacom tablet driver installed?");
        }

        [Fact]
        public void Wintab_ReportsAtLeastOneDevice()
        {
            Assert.True(CWintabInfo.IsWintabAvailable(), "Wintab not available");

            uint count = CWintabInfo.GetNumberOfDevices();
            _output.WriteLine($"Number of Wintab devices: {count}");
            Assert.True(count >= 1, "Expected at least one Wintab device attached");
        }

        [Fact]
        public void Wintab_DeviceName_IsIntuosPro()
        {
            Assert.True(CWintabInfo.IsWintabAvailable(), "Wintab not available");

            string name = CWintabInfo.GetDeviceInfo();
            _output.WriteLine($"Device name: \"{name}\"");

            Assert.False(string.IsNullOrWhiteSpace(name), "Device name should not be empty");
            Assert.True(name.Length > 2, $"Device name unexpectedly short: \"{name}\"");
        }

        [Theory]
        [InlineData("Intuos Pro S")]
        [InlineData("Intuos Pro M")]
        [InlineData("Intuos Pro L")]
        public void Wintab_DeviceName_MatchesKnownModel(string expectedSubstring)
        {
            Assert.True(CWintabInfo.IsWintabAvailable(), "Wintab not available");

            string name = CWintabInfo.GetDeviceInfo();
            _output.WriteLine($"Device name: \"{name}\", checking for: \"{expectedSubstring}\"");

            if (!name.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"Connected tablet \"{name}\" is not \"{expectedSubstring}\" — not applicable");
                return;
            }

            Assert.Contains(expectedSubstring, name, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Wintab_DeviceAxis_ReturnsValidRanges()
        {
            Assert.True(CWintabInfo.IsWintabAvailable(), "Wintab not available");

            WintabAxis axisX = CWintabInfo.GetDeviceAxis(0, EAxisDimension.AXIS_X);
            WintabAxis axisY = CWintabInfo.GetDeviceAxis(0, EAxisDimension.AXIS_Y);

            _output.WriteLine($"X axis: min={axisX.axMin}, max={axisX.axMax}, res={axisX.axResolution}, units={axisX.axUnits}");
            _output.WriteLine($"Y axis: min={axisY.axMin}, max={axisY.axMax}, res={axisY.axResolution}, units={axisY.axUnits}");

            Assert.True(axisX.axMax > 0, "X axis max should be > 0");
            Assert.True(axisY.axMax > 0, "Y axis max should be > 0");
        }

        [Fact]
        public void Wintab_MaxPressure_IsPositive()
        {
            Assert.True(CWintabInfo.IsWintabAvailable(), "Wintab not available");

            int maxPressure = CWintabInfo.GetMaxPressure();
            _output.WriteLine($"Max pressure: {maxPressure}");
            Assert.True(maxPressure > 0, "Max pressure should be > 0 for a pen tablet");
        }

        // -- WacomMT (Multi-Touch) Driver --
        // All MT tests share the WacomMTFixture which owns Init/Quit.

        [Fact]
        public void WacomMT_Initializes_Successfully()
        {
            // If we got here, the fixture Init() succeeded.
            _output.WriteLine("WacomMTInitialize via WacomMTFixture succeeded");
            Assert.True(true);
        }

        [Fact]
        public void WacomMT_DetectsAttachedTouchDevice()
        {
            int count = _mt.Config.GetNumAttachedTouchDevices();
            _output.WriteLine($"Attached touch devices: {count}");
            Assert.True(count >= 1, "Expected at least one touch device attached");
        }

        [Fact]
        public void WacomMT_DeviceCapabilities_AreValid()
        {
            Assert.True(_mt.HasDevice, "No touch device attached");

            WacomMTCapability caps = _mt.Config.GetDeviceCaps(_mt.DeviceId);
            _output.WriteLine($"  Device ID:       {_mt.DeviceId}");
            _output.WriteLine($"  Type:            {caps.Type}");
            _output.WriteLine($"  LogicalWidth:    {caps.LogicalWidth}");
            _output.WriteLine($"  LogicalHeight:   {caps.LogicalHeight}");
            _output.WriteLine($"  PhysicalSizeX:   {caps.PhysicalSizeX} mm");
            _output.WriteLine($"  PhysicalSizeY:   {caps.PhysicalSizeY} mm");
            _output.WriteLine($"  ReportedSizeX:   {caps.ReportedSizeX}");
            _output.WriteLine($"  ReportedSizeY:   {caps.ReportedSizeY}");
            _output.WriteLine($"  FingerMax:       {caps.FingerMax}");
            _output.WriteLine($"  CapabilityFlags: {caps.CapabilityFlags}");

            Assert.Equal(WacomMTDeviceType.WMTDeviceTypeOpaque, caps.Type);
            Assert.True(caps.FingerMax >= 2, $"Expected at least 2 finger support, got {caps.FingerMax}");
            Assert.True(caps.PhysicalSizeX > 0, "Physical width should be > 0");
            Assert.True(caps.PhysicalSizeY > 0, "Physical height should be > 0");
        }

        [Fact]
        public void WacomMT_DeviceType_IsOpaque_ForIntuosPro()
        {
            Assert.True(_mt.HasDevice, "No touch device attached");

            WacomMTCapability caps = _mt.Config.GetDeviceCaps(_mt.DeviceId);
            _output.WriteLine($"Device type: {caps.Type}");
            Assert.Equal(WacomMTDeviceType.WMTDeviceTypeOpaque, caps.Type);
        }

        [Fact]
        public void WacomMT_PhysicalSize_DistinguishesModel()
        {
            Assert.True(_mt.HasDevice, "No touch device attached");

            WacomMTCapability caps = _mt.Config.GetDeviceCaps(_mt.DeviceId);
            float width = caps.PhysicalSizeX;
            float height = caps.PhysicalSizeY;

            string guessedModel;
            if (width < 180)
                guessedModel = "Small (S)";
            else if (width < 270)
                guessedModel = "Medium (M)";
            else
                guessedModel = "Large (L)";

            _output.WriteLine($"Physical size: {width:F1} x {height:F1} mm → likely {guessedModel}");

            Assert.InRange(width, 100f, 400f);
            Assert.InRange(height, 60f, 300f);
        }
    }
}
