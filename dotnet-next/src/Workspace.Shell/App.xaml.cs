using System.IO;
using System.Windows;
using Workspace.Runtime;
namespace Workspace.Shell;
public partial class App:Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Explicit developer-only verification mode. Never fall through to normal windows, even on bad arguments.
        if (e.Args.Length > 0 && e.Args[0] == "--pdfops-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (e.Args.Length != 4)
            {
                Console.Error.WriteLine("Usage: --pdfops-smoke <pinned-cli-path> <three-page-fixture.pdf> <existing-output-directory>");
                Shutdown(2);
                return;
            }
            try
            {
                if (e.Args.Skip(1).Any(path => !Path.IsPathFullyQualified(path)) || !Directory.Exists(e.Args[3]))
                    throw new ArgumentException("Smoke paths must be absolute and the output directory must already exist.");
                var checks = await PdfOperations.PdfOperationsIntegrationChecks.RunAsync(e.Args[1], e.Args[2], e.Args[3]);
                var summary = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Passed = true, Count = checks.Count, Checks = checks,
                    Assembly = typeof(App).Assembly.Location,
                    Scope = "Published assembly backend + pinned native CLI; not WPF button workflows or installation acceptance"
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var summaryPath = Path.Combine(e.Args[3], "pdfops-smoke-" + Guid.NewGuid().ToString("N") + ".json");
                await using (var file = new FileStream(summaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await using (var writer = new StreamWriter(file)) await writer.WriteAsync(summary);
                Console.WriteLine(summary);
                Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() }));
                Shutdown(1);
            }
            return;
        }
        var registry=new ModuleRegistry();
        var report=ModuleLoader.Load(Path.Combine(AppContext.BaseDirectory,"modules.json"),Path.Combine(AppContext.BaseDirectory,"Modules"),registry);
        if(e.Args.Length==2&&e.Args[0]=="--smoke") {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await ShellSmokeChecks.RunAsync(registry,report,e.Args[1]));return;
        }
        var window=new MainWindow(registry,report);MainWindow=window;
        if(e.Args.Contains("--preview")){window.Model.OpenPreview.Execute(null);window.WindowState=WindowState.Maximized;}
        window.Show();
        // File-association launch: one already-unquoted path argument. Never change associations here.
        if (e.Args.Length == 1 && e.Args[0] != "--preview") window.OpenReadOnlyPdf(e.Args[0]);
        else if (e.Args.Length > 0 && !(e.Args.Length == 1 && e.Args[0] == "--preview")) MessageBox.Show(window, "한 번에 PDF 경로 하나만 지정하십시오.", "PDF 읽기 전용 TEST");
    }
}
