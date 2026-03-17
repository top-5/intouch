using System;
using System.IO;

namespace InTouch
{
    /// <summary>
    /// Simple file logger that writes to %APPDATA%\InTouch\intouch.log.
    /// Rotates when the file exceeds ~1 MB.
    /// </summary>
    internal static class Log
    {
        private const long MaxBytes = 1_048_576; // 1 MB
        private static readonly object s_lock = new();
        private static readonly string s_dir;
        private static readonly string s_path;
        private static StreamWriter? s_writer;

        static Log()
        {
            s_dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "InTouch");
            s_path = Path.Combine(s_dir, "intouch.log");
        }

        public static string LogFilePath => s_path;

        public static void Info(string message) => Write("INF", message);
        public static void Warn(string message) => Write("WRN", message);
        public static void Error(string message) => Write("ERR", message);

        public static void Error(string message, Exception ex)
            => Write("ERR", $"{message}: {ex.Message}");

        private static void Write(string level, string message)
        {
            lock (s_lock)
            {
                try
                {
                    EnsureWriter();
                    s_writer!.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
                    s_writer.Flush();
                }
                catch
                {
                    // Never throw from the logger
                }
            }
        }

        private static void EnsureWriter()
        {
            if (s_writer != null)
            {
                // Rotate if needed
                try
                {
                    if (new FileInfo(s_path).Length > MaxBytes)
                    {
                        s_writer.Dispose();
                        s_writer = null;
                        var old = s_path + ".old";
                        File.Delete(old);
                        File.Move(s_path, old);
                    }
                }
                catch { }
            }

            if (s_writer == null)
            {
                Directory.CreateDirectory(s_dir);
                s_writer = new StreamWriter(s_path, append: true) { AutoFlush = false };
            }
        }

        public static void Close()
        {
            lock (s_lock)
            {
                s_writer?.Dispose();
                s_writer = null;
            }
        }
    }
}
