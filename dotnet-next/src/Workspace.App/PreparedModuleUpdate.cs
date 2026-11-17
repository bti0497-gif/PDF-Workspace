using Workspace.Shell.ModuleDistribution;
namespace Workspace.Shell;

/// <summary>Verified inert candidate, with explicitly owned activation/rollback callbacks.
/// Candidate must not become active until its runtime views have been created successfully.</summary>
internal sealed class PreparedModuleUpdate(
    DistributionCandidate candidate,
    DistributionCandidate? previousActive,
    Func<CancellationToken,Task> activate,
    Func<CancellationToken,Task> restorePrevious)
{
    internal DistributionCandidate Candidate { get; } = candidate;
    internal DistributionCandidate? PreviousActive { get; } = previousActive;
    internal Task CommitAsync(CancellationToken ct) => activate(ct);
    internal Task RollbackAsync() => restorePrevious(CancellationToken.None);

    internal async Task AttachAndCommitAsync(Func<Task> attach,Func<Task> detach,CancellationToken ct=default)
    {
        var commitStarted=false;
        try
        {
            ct.ThrowIfCancellationRequested();
            await attach();
            ct.ThrowIfCancellationRequested();
            commitStarted=true;
            await CommitAsync(ct);
        }
        catch(Exception failure)
        {
            var failures=new List<Exception>{failure};
            try { await detach(); } catch(Exception cleanup) { failures.Add(new InvalidOperationException("새 기능을 정리하지 못했습니다.",cleanup)); }
            // A commit may have completed its atomic rename before throwing/cancellation.
            // Restore is a compare-and-restore operation, not a blind rollback toggle.
            if(commitStarted)
                try { await RollbackAsync(); } catch(Exception rollback) { failures.Add(new InvalidOperationException("이전 활성 기능 복구에 실패했습니다. 재시작 전 설치 상태를 확인해야 합니다.",rollback)); }
            if(failures.Count>1)throw new AggregateException("기능 연결 또는 활성화 실패",failures);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }
}
