using System.Windows;

namespace Zap.App;

public partial class App : Application
{
    // Paths passed on the command line (e.g. from "Send to") pre-fill the copy list.
    protected override void OnStartup(StartupEventArgs e) => new MainWindow(e.Args).Show();
}
