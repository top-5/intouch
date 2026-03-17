using System;
using System.Windows.Forms;

namespace InTouch
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Log.Info($"InTouch starting (PID {Environment.ProcessId})");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            try
            {
                Application.Run(new TrayApp());
            }
            finally
            {
                Log.Info("InTouch exiting");
                Log.Close();
            }
        }
    }
}
