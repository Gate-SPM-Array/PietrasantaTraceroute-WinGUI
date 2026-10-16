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
            // Lingua: scelta salvata nelle preferenze, altrimenti quella di Windows (italiano o inglese)
            L.Init(Settings.Load().Language);
            Application.Run(new MainForm());
        }

        static void ShowError(Exception ex)
        {
            MessageBox.Show(L.T("Errore imprevisto:") + "\n\n" + (ex?.Message ?? "?") + "\n\n" + ex?.StackTrace,
                "Pietrasanta Traceroute GUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
