using Microsoft.UI.Xaml;
using SPlayer.Ui;

namespace SPlayer;

public partial class App : Application
{
    private ShellWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Write(e.Exception.ToString());
            UnexpectedError?.Invoke("操作未完成，请重试。若问题持续，请重新打开应用。");
            e.Handled = UnexpectedError is not null;
        };
    }

    internal static event Action<string>? UnexpectedError;
    internal static int ExitCode { get; set; }

    private static bool Automated() =>
        Environment.GetCommandLineArgs().Any(arg => arg is "--smoke" or "--audit-fullscreen");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new ShellWindow(LaunchRequest.Parse(Environment.GetCommandLineArgs()));
            _window.Activate();
        }
        catch (Exception ex)
        {
            Log.Write(ex.ToString());
            ExitCode = 1;
            if (!Automated()) MessageDialog.Show(ex.Message);
            Exit();
        }
    }
}
