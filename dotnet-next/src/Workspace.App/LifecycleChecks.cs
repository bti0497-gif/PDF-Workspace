using Workspace.Shell.ModuleDistribution;
namespace Workspace.Shell;

internal static class LifecycleChecks
{
    internal static async Task<IReadOnlyList<string>> RunAsync()
    {
        var checks=new List<string>();
        var manifest=new DistributionManifest("PDF-Workspace-WPF","1.0.1",1,[]);
        var old=new DistributionCandidate("old",manifest,"old");
        var next=new DistributionCandidate("next",manifest,"next");
        void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
        async Task<Exception> Reject(Func<Task> action){try{await action();}catch(Exception e){return e;}throw new InvalidOperationException("Expected failure.");}
        string? active="old";var attached=false;var commits=0;var restores=0;
        PreparedModuleUpdate Update(Func<CancellationToken,Task>? commit=null,Func<CancellationToken,Task>? rollback=null)=>new(next,old,commit??(_=>{commits++;active="next";return Task.CompletedTask;}),rollback??(_=>{restores++;active="old";return Task.CompletedTask;}));
        await Update().AttachAndCommitAsync(()=>{Check(active=="old","activation follows successful attachment");attached=true;return Task.CompletedTask;},()=>{attached=false;return Task.CompletedTask;});
        Check(active=="next"&&attached&&commits==1&&restores==0,"successful runtime attachment commits exactly once");
        active="old";attached=false;commits=restores=0;
        await Reject(()=>Update().AttachAndCommitAsync(()=>{attached=true;throw new InvalidOperationException("view creation failed");},()=>{attached=false;return Task.CompletedTask;}));
        Check(active=="old"&&!attached&&commits==0&&restores==0,"partial view failure disposes candidate and never changes active pointer");
        await Reject(()=>Update(_=>{commits++;active="next";throw new InvalidOperationException("post-rename failure");}).AttachAndCommitAsync(()=>{attached=true;return Task.CompletedTask;},()=>{attached=false;return Task.CompletedTask;}));
        Check(active=="old"&&!attached&&commits==1&&restores==1,"post-commit exception restores exact previous active pointer");
        active=null;attached=false;
        var firstInstall=new PreparedModuleUpdate(next,null,_=>{active="next";throw new InvalidOperationException("first install verify failed");},_=>{active=null;return Task.CompletedTask;});
        await Reject(()=>firstInstall.AttachAndCommitAsync(()=>{attached=true;return Task.CompletedTask;},()=>{attached=false;return Task.CompletedTask;}));
        Check(active==null&&!attached,"first-install activation failure restores null active state");
        using var cancelled=new CancellationTokenSource();commits=restores=0;active="old";
        await Reject(()=>Update().AttachAndCommitAsync(()=>{attached=true;cancelled.Cancel();return Task.CompletedTask;},()=>{attached=false;return Task.CompletedTask;},cancelled.Token));
        Check(active=="old"&&!attached&&commits==0&&restores==0,"cancellation after attachment leaves previous active unchanged");
        var recoveryTokenWasSafe=false;
        using var duringCommit=new CancellationTokenSource();
        var cancelledCommit=new PreparedModuleUpdate(next,old,_=>{active="next";duringCommit.Cancel();throw new OperationCanceledException();},ct=>{recoveryTokenWasSafe=!ct.IsCancellationRequested;active="old";return Task.CompletedTask;});
        await Reject(()=>cancelledCommit.AttachAndCommitAsync(()=>Task.CompletedTask,()=>Task.CompletedTask,duringCommit.Token));
        Check(active=="old"&&recoveryTokenWasSafe,"rollback uses independent non-cancelled token");
        restores=0;
        var cleanupFailure=await Reject(()=>Update(_=>{active="next";throw new InvalidOperationException("commit failed");}).AttachAndCommitAsync(()=>Task.CompletedTask,()=>throw new InvalidOperationException("dispose failed")));
        Check(cleanupFailure is AggregateException&&restores==1&&active=="old","cleanup failure does not skip disk rollback and is reported");
        var rollbackFailure=await Reject(()=>Update(_=>throw new InvalidOperationException("commit failed"),_=>throw new InvalidOperationException("restore failed")).AttachAndCommitAsync(()=>Task.CompletedTask,()=>Task.CompletedTask));
        Check(rollbackFailure is AggregateException a&&a.InnerExceptions.Count==2,"rollback failure is explicit, never reported as restored");
        return checks;
    }
}
