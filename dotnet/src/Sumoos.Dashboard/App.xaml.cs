using System.Windows;
using System.Windows.Threading;
namespace Sumoos.Dashboard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--smoke-test")) return;
        // Load and lay out the real XAML. This catches runtime binding/startup failures.
        DispatcherUnhandledException += (_, error) => { Console.Error.WriteLine(error.Exception); error.Handled = true; Shutdown(1); };
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
            if (MainWindow is null) Shutdown(1);
            else MainWindow.Close();
        }));
    }
}
