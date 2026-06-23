using Microsoft.UI.Xaml;
using HduVodApp.Services;

namespace HduVodApp;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        AppLog.Init();
        AppLog.Info("App", "ctor");
        this.UnhandledException += (_, e) =>
        {
            AppLog.Error("App", "UnhandledException", e.Exception);
            e.Handled = false;
        };
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Info("App", "OnLaunched");
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
