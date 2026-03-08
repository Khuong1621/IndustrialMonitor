using System;
using System.Windows.Forms;
using IndustrialMonitor.UI;

namespace IndustrialMonitor
{
    /// <summary>
    /// Entry point của ứng dụng WinForms
    /// </summary>
    static class Program
    {
        [STAThread]   // Required for WinForms (Single-Threaded Apartment)
        static void Main()
        {
            // Xử lý unhandled exception toàn ứng dụng
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Instance.Log($"FATAL: {ex?.Message}", LogLevel.Error);
                MessageBox.Show($"Unhandled error:\n{ex?.Message}", "Fatal Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.ThreadException += (s, e) =>
            {
                Logger.Instance.Log($"UI Thread Exception: {e.Exception.Message}", LogLevel.Error);
                MessageBox.Show($"UI error:\n{e.Exception.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Logger.Instance.Log("Application starting...", LogLevel.Info);
            Application.Run(new MainForm());
            Logger.Instance.Log("Application closed.", LogLevel.Info);
        }
    }
}
