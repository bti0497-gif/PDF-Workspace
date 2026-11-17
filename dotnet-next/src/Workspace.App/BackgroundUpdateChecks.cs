using Workspace.Contracts;
using Workspace.Shell.ModuleDistribution;
namespace Workspace.Shell;
internal static class BackgroundUpdateChecks
{
    internal static async Task<IReadOnlyList<string>> RunAsync()
    {
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);}
        var candidate=new DistributionCandidate("next",new("PDF-Workspace-WPF","1.0.2",1,[]),"next");
        var update=new PreparedModuleUpdate(candidate,null,_=>Task.CompletedTask,_=>Task.CompletedTask);
        var download=new TaskCompletionSource<PreparedModuleUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dirty=false;var busy=false;var modal=false;var prepares=0;var applied=0;var foregroundEdits=0;
        using var coordinator=new BackgroundModuleUpdates((_,_)=>{prepares++;return download.Task;},()=>!dirty&&!busy&&!modal,(_,_)=>{applied++;return Task.FromResult(new CommandResult(true,"applied"));});
        var pending=coordinator.CheckAsync(new InlineProgress<DistributionProgress>(_=>{}));
        Check(!pending.IsCompleted&&coordinator.IsPreparing,"network preparation is asynchronous and does not synchronously block caller");
        foregroundEdits++;dirty=true;
        Check(foregroundEdits==1&&applied==0,"foreground edit executes while preparation is still pending");
        download.SetResult(update);await pending;
        Check(prepares==1&&applied==0&&coordinator.HasPending,"document becoming dirty during download retains inert candidate without swapping");
        dirty=false;busy=true;await coordinator.ApplyWhenIdleAsync();
        Check(applied==0&&coordinator.HasPending,"busy foreground operation defers prepared update");
        busy=false;modal=true;await coordinator.ApplyWhenIdleAsync();
        Check(applied==0&&coordinator.HasPending,"active modal defers prepared update");
        modal=false;await coordinator.ApplyWhenIdleAsync();
        Check(applied==1&&prepares==1&&!coordinator.HasPending,"clean idle later applies retained candidate once without redownload");
        var cancelledDownload=new TaskCompletionSource<PreparedModuleUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);var afterDisposeApplies=0;
        var stopped=new BackgroundModuleUpdates((_,_)=>cancelledDownload.Task,()=>true,(_,_)=>{afterDisposeApplies++;return Task.FromResult(new CommandResult(true,""));});
        var work=stopped.CheckAsync(new InlineProgress<DistributionProgress>(_=>{}));stopped.Dispose();cancelledDownload.SetResult(update);
        var cancelled=false;try{await work;}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled&&afterDisposeApplies==0&&!stopped.HasPending,"shutdown cancels preparation and cannot apply a late result");
        var resource=new ModuleResource("engine","pdfcraft-cli.exe","https://example.invalid/v1/engine",100,"ABC");
        var original=new ModuleAsset("pdfengine","1.0.1","https://example.invalid/v1/module",100,"DEF","Workspace.PdfEngine","engine","Workspace.PdfEngine.PdfEngineModule",[resource]);
        var relocated=original with {Url="https://example.invalid/v2/module",Files=[resource with {Url="https://example.invalid/v2/engine"}]};
        Check(MainWindow.SamePayload(original,relocated),"same engine bytes under a newer release URL are unchanged payload, not a false restart requirement");
        Check(!MainWindow.SamePayload(original,relocated with {Files=[resource with {Sha256="DIFFERENT"}]}),"changed native resource hash is a real payload change");
        return checks;
    }
}
