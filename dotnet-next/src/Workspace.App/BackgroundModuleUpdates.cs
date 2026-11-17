using Workspace.Contracts;
using Workspace.Shell.ModuleDistribution;
namespace Workspace.Shell;

/// <summary>Downloads inert bytes without owning the document input lock.
/// The host rechecks quiescence at apply time; pending updates survive dirty/modal states.</summary>
internal sealed class BackgroundModuleUpdates(
    Func<IProgress<DistributionProgress>,CancellationToken,Task<PreparedModuleUpdate>> prepare,
    Func<bool> canApply,
    Func<PreparedModuleUpdate,CancellationToken,Task<CommandResult>> apply):IDisposable
{
    private readonly CancellationTokenSource lifetime=new();
    private PreparedModuleUpdate? pending;
    private bool working,disposed;
    internal bool HasPending=>pending!=null;
    internal bool IsWorking=>working;
    internal bool IsPreparing {get;private set;}
    internal async Task<CommandResult> CheckAsync(IProgress<DistributionProgress> progress,CancellationToken ct=default)
    {
        if(disposed)return new(false,"업데이트 확인이 종료되었습니다.");
        if(working)return new(false,"업데이트 확인이 진행 중입니다.");
        working=true;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token);
        try
        {
            if(pending==null)
            {
                IsPreparing=true;
                var update=await prepare(progress,linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                pending=update;
            }
            IsPreparing=false;
            return await ApplyPendingCoreAsync(linked.Token);
        }
        finally {IsPreparing=false;working=false;}
    }
    internal async Task<CommandResult> ApplyWhenIdleAsync(CancellationToken ct=default)
    {
        if(disposed||working||pending==null)return new(false,"");
        working=true;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token);
        try {return await ApplyPendingCoreAsync(linked.Token);}
        finally {working=false;}
    }
    private async Task<CommandResult> ApplyPendingCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(disposed||pending==null)return new(false,"");
        if(!canApply())return new(true,"업데이트 파일을 준비했습니다. 문서 작업을 마치면 적용합니다.");
        var result=await apply(pending,ct);
        if(result.Success)pending=null;
        return result;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;pending=null;lifetime.Cancel();lifetime.Dispose();
    }
}
