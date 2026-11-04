using System.IO;
using System.Windows;
using Workspace.Runtime;
namespace Workspace.Shell;
public partial class App:Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var registry=new ModuleRegistry();
        var report=ModuleLoader.Load(Path.Combine(AppContext.BaseDirectory,"modules.json"),Path.Combine(AppContext.BaseDirectory,"Modules"),registry);
        if(e.Args.Length==2&&e.Args[0]=="--smoke") {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await ShellSmokeChecks.RunAsync(registry,report,e.Args[1]));return;
        }
        var window=new MainWindow(registry,report);MainWindow=window;
        if(e.Args.Contains("--preview")){window.Model.OpenPreview.Execute(null);window.WindowState=WindowState.Maximized;}
        window.Show();
    }
}
