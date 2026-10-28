using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SPlayer;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { SetCurrentProcessExplicitAppUserModelID(FileAssociations.AppId); }
        catch (Exception ex) { Log.Write(ex.Message); }
        if (args.Any(arg => arg == "--register"))
        {
            try
            {
                FileAssociations.Register(Environment.ProcessPath ?? "");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write(ex.ToString());
                return 1;
            }
        }
        if (args.Any(arg => arg == "--unregister"))
        {
            try
            {
                FileAssociations.Unregister();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write(ex.ToString());
                return 1;
            }
        }
        var request = LaunchRequest.Parse(args.Prepend(Environment.ProcessPath ?? "SPlayer.exe").ToArray());
        if (request.Smoke || request.Audit)
        {
            var folder = Path.Combine(Path.GetTempPath(), "SPlayer", request.Smoke ? "smoke" : "audit");
            Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable("SPLAYER_DATA", folder);
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write(e.ExceptionObject?.ToString() ?? "未知错误");
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var queue = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
                new App();
            });
            return App.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Write(ex.ToString());
            MessageDialog.Show(ex.Message);
            return 1;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
