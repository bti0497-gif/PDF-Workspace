using System.IO;
using System.Windows;
namespace Workspace.Shell;
public partial class App:Application
{
    private InstalledCoreLease? installedCoreLease;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        if(e.Args.Length==3&&e.Args[0]=="--local-ui-review")
        {
            try
            {
                if(!Path.IsPathFullyQualified(e.Args[1])||!Path.IsPathFullyQualified(e.Args[2])||Path.Exists(e.Args[2]))throw new ArgumentException("Use an absolute fixture path and a new, nonexistent isolated review directory.");
                Directory.CreateDirectory(e.Args[2]);
                var review=new MainWindow(new Startup.JsonRecentFilesStore(Path.Combine(e.Args[2],"recent-files.json")));MainWindow=review;review.Show();
                await review.StartModularAsync(FormalChecks.CreateLocalUiReviewPreparation(e.Args[1],e.Args[2]));
            }
            catch(Exception error){Console.Error.WriteLine(error);Shutdown(1);}
            return;
        }
#endif
        if(e.Args.Length>0 && e.Args[0].StartsWith("--",StringComparison.Ordinal))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try
            {
                if(e.Args.Length==2 && e.Args[0]=="--formal-smoke") await FormalChecks.RunSmokeAsync(e.Args[1]);
                else if(e.Args.Length==5 && e.Args[0]=="--release-rehearsal") await FormalChecks.RunReleaseAsync(e.Args[1],e.Args[2],e.Args[3],e.Args[4]);
                else if(e.Args.Length==4 && e.Args[0]=="--trusted-release-rehearsal") await FormalChecks.RunReleaseTrustedAsync(e.Args[1],e.Args[2],e.Args[3]);
                else throw new ArgumentException("Unsupported verification arguments. No normal startup or network fallback was attempted.");
                Shutdown(0);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine(ex);
                var output=e.Args[0]=="--formal-smoke" && e.Args.Length==2?e.Args[1]:(e.Args[0]=="--release-rehearsal"&&e.Args.Length==5)||(e.Args[0]=="--trusted-release-rehearsal"&&e.Args.Length==4)?e.Args[3]:null;
                if(output!=null && Path.IsPathFullyQualified(output)) { Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output,"failure.txt"),ex.ToString()); }
                Shutdown(1);
            }
            return;
        }
        try { installedCoreLease=InstalledCoreLease.AcquireProduction(AppContext.BaseDirectory); }
        catch(CoreLaunchBlockedException error)
        {
            MessageBox.Show(error.Message,ProductBrand.DialogTitle("시작"),MessageBoxButton.OK,MessageBoxImage.Information);
            Shutdown(2);return;
        }
        var window=new MainWindow();MainWindow=window;window.Show();
        await window.StartModularAsync(ModulePreparation.CreateDefault(),e.Args.Length==1?e.Args[0]:null);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        try { installedCoreLease?.Dispose();installedCoreLease=null; }
        finally { base.OnExit(e); }
    }
}
