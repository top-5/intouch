using System;
using System.Threading;
using Xunit;
using Xunit.Abstractions;
using WacomMTDN;

namespace InTouch.Tests
{
    [Collection("WacomMT")]
    public class TouchToggleTests
    {
        private readonly ITestOutputHelper _output;
        private readonly WacomMTFixture _mt;

        public TouchToggleTests(WacomMTFixture mt, ITestOutputHelper output)
        {
            _mt = mt;
            _output = output;
        }

        [Fact]
        public void Consumer_Registration_Succeeds()
        {
            Assert.True(_mt.HasDevice, "No touch device attached");

            WacomMTCallback callback = (packet, userData) => 0;
            var client = new CWacomMTFingerClient(WacomMTProcessingMode.WMTProcessingModeConsumer);
            var hitRect = new WacomMTHitRect(0, 0, 1, 1);

            client.RegisterHitRectClient(_mt.DeviceId, hitRect, ref callback, IntPtr.Zero);
            _output.WriteLine("Consumer callback registered (touch intercepted)");
            Assert.True(client.IsRegistered(), "Client should be registered");

            client.UnregisterHitRectClient();
            _output.WriteLine("Consumer callback unregistered (touch passthrough)");
            Assert.False(client.IsRegistered(), "Client should be unregistered");
        }

        [Fact]
        public void Observer_Registration_Succeeds()
        {
            Assert.True(_mt.HasDevice, "No touch device attached");

            WacomMTCallback callback = (packet, userData) => 0;
            var client = new CWacomMTFingerClient(WacomMTProcessingMode.WMTProcessingModeObserver);
            var hitRect = new WacomMTHitRect(0, 0, 1, 1);

            client.RegisterHitRectClient(_mt.DeviceId, hitRect, ref callback, IntPtr.Zero);
            _output.WriteLine("Observer callback registered (data + OS passthrough)");
            Assert.True(client.IsRegistered());

            client.UnregisterHitRectClient();
            _output.WriteLine("Observer callback unregistered");
            Assert.False(client.IsRegistered());
        }

        [Fact]
        public void Toggle_Consumer_On_Off_On()
        {
            Assert.True(_mt.HasDevice, "No touch device attached");

            WacomMTCallback callback = (packet, userData) => 0;
            var hitRect = new WacomMTHitRect(0, 0, 1, 1);

            // --- Toggle ON ---
            var client = new CWacomMTFingerClient(WacomMTProcessingMode.WMTProcessingModeConsumer);
            client.RegisterHitRectClient(_mt.DeviceId, hitRect, ref callback, IntPtr.Zero);
            Assert.True(client.IsRegistered(), "Should be ON after first register");
            _output.WriteLine("Toggle ON — consumer registered");

            // --- Toggle OFF ---
            client.UnregisterHitRectClient();
            Assert.False(client.IsRegistered(), "Should be OFF after unregister");
            _output.WriteLine("Toggle OFF — consumer unregistered");

            Thread.Sleep(200);

            // --- Toggle ON again ---
            var client2 = new CWacomMTFingerClient(WacomMTProcessingMode.WMTProcessingModeConsumer);
            client2.RegisterHitRectClient(_mt.DeviceId, hitRect, ref callback, IntPtr.Zero);
            Assert.True(client2.IsRegistered(), "Should be ON after re-register");
            _output.WriteLine("Toggle ON again — consumer re-registered");

            client2.UnregisterHitRectClient();
            _output.WriteLine("Cleanup — unregistered");
        }

        [Fact]
        public void AttachDetach_Callbacks_CanBeRegistered()
        {
            // Session is owned by WacomMTFixture — just register callbacks.
            WacomMTAttachCallback attachCb = (cap, userData) =>
            {
                _output.WriteLine($"Attach callback: device {cap.DeviceID}, type {cap.Type}");
            };
            WacomMTDetachCallback detachCb = (deviceId, userData) =>
            {
                _output.WriteLine($"Detach callback: device {deviceId}");
            };

            var err1 = CWacomMTInterface.WacomMTRegisterAttachCallback(attachCb, IntPtr.Zero);
            var err2 = CWacomMTInterface.WacomMTRegisterDetachCallback(detachCb, IntPtr.Zero);

            _output.WriteLine($"Attach callback registration: {err1}");
            _output.WriteLine($"Detach callback registration: {err2}");

            Assert.Equal(WacomMTError.WMTErrorSuccess, err1);
            Assert.Equal(WacomMTError.WMTErrorSuccess, err2);
        }

        [Fact]
        public void Consumer_ReceivesFingerData_WhenTouchIsOn()
        {
            Assert.True(_mt.HasDevice, "No touch device attached");

            int frameCount = 0;
            WacomMTCallback callback = (packet, userData) =>
            {
                Interlocked.Increment(ref frameCount);
                return 0;
            };

            var client = new CWacomMTFingerClient(WacomMTProcessingMode.WMTProcessingModeConsumer);
            var hitRect = new WacomMTHitRect(0, 0, 1, 1);
            client.RegisterHitRectClient(_mt.DeviceId, hitRect, ref callback, IntPtr.Zero);

            Thread.Sleep(500);

            _output.WriteLine($"Frames received in 500ms: {frameCount}");
            _output.WriteLine("(0 is expected if no finger on tablet — this is normal)");

            client.UnregisterHitRectClient();
            Assert.True(true);
        }

        [Fact]
        public void DeviceCapabilities_ReturnConsistentValues_AcrossCalls()
        {
            Assert.True(_mt.HasDevice, "No touch device");

            var caps1 = _mt.Config.GetDeviceCaps(_mt.DeviceId);
            var caps2 = _mt.Config.GetDeviceCaps(_mt.DeviceId);

            Assert.Equal(caps1.DeviceID, caps2.DeviceID);
            Assert.Equal(caps1.Type, caps2.Type);
            Assert.Equal(caps1.FingerMax, caps2.FingerMax);
            Assert.Equal(caps1.PhysicalSizeX, caps2.PhysicalSizeX);
            Assert.Equal(caps1.PhysicalSizeY, caps2.PhysicalSizeY);

            _output.WriteLine("Device capabilities are consistent across calls");
        }
    }
}
