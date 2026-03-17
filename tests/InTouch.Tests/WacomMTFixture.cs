using System;
using Xunit;
using WacomMTDN;

namespace InTouch.Tests
{
    /// <summary>
    /// Shared fixture that manages a single WacomMT session for all tests.
    /// WacomMT is a process-global singleton — calling WacomMTQuit() kills
    /// it for everything, so we must Init once and Quit once.
    /// </summary>
    public sealed class WacomMTFixture : IDisposable
    {
        public CWacomMTConfig Config { get; }
        public bool HasDevice { get; }
        public int DeviceId { get; }

        public WacomMTFixture()
        {
            Config = new CWacomMTConfig();
            Config.Init();

            HasDevice = Config.GetNumAttachedTouchDevices() > 0;
            DeviceId = HasDevice ? Config.GetAttachedDeviceID(0) : -1;
        }

        public void Dispose()
        {
            Config.Quit();
        }
    }

    /// <summary>
    /// xUnit collection that ensures all WacomMT test classes share
    /// the same fixture (and therefore the same Init/Quit lifecycle).
    /// </summary>
    [CollectionDefinition("WacomMT")]
    public class WacomMTCollection : ICollectionFixture<WacomMTFixture> { }
}
