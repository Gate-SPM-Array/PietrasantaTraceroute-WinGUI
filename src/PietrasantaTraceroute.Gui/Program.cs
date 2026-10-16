using System;
using System.Threading;
using System.Windows.Forms;

namespace PstrGui
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }

        static void ShowError(Exception ex)
        {
            MessageBox.Show("Errore imprevisto:\n\n" + (ex?.Message ?? "sconosciuto") + "\n\n" + ex?.StackTrace,
                "Pietrasanta Traceroute GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
