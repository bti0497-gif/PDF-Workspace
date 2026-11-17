using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
using Workspace.Runtime;
using Workspace.Shell.ModuleDistribution;
namespace Workspace.Shell;

public partial class MainWindow
{
    private WpfAreaSwapCoordinator<FrameworkElement>? areaCoordinator;
    private readonly Dictionary<string,FrameworkElement> areaViews=new(StringComparer.Ordinal);
    private DistributionCandidate? runningCandidate;
    private Func<IProgress<DistributionProgress>,CancellationToken,Task<PreparedModuleUpdate>>? prepareModules;
    private int quiescenceDepth;
    private bool updateChecking, updateScheduled, modularCommandsBound;
    private BackgroundModuleUpdates? backgroundUpdates;
    private System.Windows.Threading.DispatcherTimer? periodicUpdateTimer,idleApplyTimer;
    private volatile bool backgroundStopped;
    private CommandResult ScheduleModuleUpdate()
    {
        if(updateScheduled||updateChecking)return new(false,"업데이트 확인이 진행 중입니다.");
        updateScheduled=true;
        // Return from the requesting module first so its proxy lease/pending command is released.
        _=Dispatcher.BeginInvoke(new Action(async()=>{updateScheduled=false;await CheckModuleUpdatesAsync();}));
        return new(true,"업데이트 확인을 시작합니다.");
    }
    internal DistributionCandidate? RunningCandidate=>runningCandidate;
    internal string DisplayedProductVersion=>ProductVersionText.Text;
    private void CommitRunningCandidate(DistributionCandidate candidate){runningCandidate=candidate;ProductVersionText.Text=candidate.Manifest.Version;}
    internal IReadOnlyList<RetiredAreaGeneration> RetiredAreas=>areaCoordinator?.RetiredGenerations??[];
    internal async Task<object> WaitForRenderedAreasForChecksAsync()
    {
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                foreach(var node in Descendants(VisualTreeHelper.GetChild(root,i)))yield return node;
        }
        int Count(string region)=>areaViews.TryGetValue(region,out var view)?Descendants(view).OfType<Image>().Count(i=>i.Source is System.Windows.Media.Imaging.BitmapSource):0;
        var deadline=DateTime.UtcNow.AddSeconds(45);
        while(DateTime.UtcNow<deadline)
        {
            var root=(FrameworkElement)Content;root.Measure(new Size(1280,780));root.Arrange(new Rect(0,0,1280,780));root.UpdateLayout();
            await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if(Count(WorkspaceRegions.Sidebar)>0&&Count(WorkspaceRegions.Workspace)>0)
                return new {SidebarImages=Count(WorkspaceRegions.Sidebar),ViewerImages=Count(WorkspaceRegions.Workspace)};
            await Task.Delay(100);
        }
        var detail=string.Join(" | ",areaViews.Values.SelectMany(Descendants).OfType<TextBlock>().Select(t=>t.Text).Where(t=>!string.IsNullOrWhiteSpace(t)));
        throw new InvalidOperationException($"Offscreen rendering did not become ready: sidebar={Count(WorkspaceRegions.Sidebar)}, viewer={Count(WorkspaceRegions.Workspace)}. {detail}");
    }
    private static IsolatedModuleManifest Manifest(ModuleAsset asset,int api)=>new(asset.Id,asset.Version,api,ModuleDistributionService.ModuleFileName(asset),asset.Type??throw new InvalidOperationException("모듈 진입점이 없습니다."));
    private void RefreshDocumentStatus()
    {
        var doc=sharedDocument?.Document;
        StatusText.Text=doc?.PageCount is >0?$"{doc.CurrentPage} / {doc.PageCount} 쪽":"";
    }
    private static string OperationCaption(string command)=>command switch
    {
        "document.open"=>"PDF 여는 중…", "document.save" or "document.saveAs"=>"PDF 저장 중…",
        "document.merge"=>"PDF 병합 중…", "pages.extract"=>"선택한 페이지 추출 중…",
        "pages.rotate"=>"페이지 회전 중…", "pages.delete"=>"페이지 삭제 중…",
        "pages.reorder"=>"페이지 순서 변경 중…", "document.undo"=>"실행 취소 중…",
        "document.redo"=>"다시 실행 중…", _=>"작업 중…"
    };
    private bool CanApplyBackgroundUpdate()=>!backgroundStopped&&runningCandidate!=null&&sharedDocument!=null&&!modularBusy&&!confirmingExit&&quiescenceDepth==0&&!sharedDocument.Document.IsDirty&&!OwnedWindows.Cast<Window>().Any(w=>w.IsVisible);
    private void StartBackgroundUpdates()
    {
        if(prepareModules==null||backgroundUpdates!=null)return;
        backgroundStopped=false;
        backgroundUpdates=new(prepareModules,CanApplyBackgroundUpdate,ApplyPreparedAreaUpdateAsync);
        periodicUpdateTimer=new(System.Windows.Threading.DispatcherPriority.ApplicationIdle,Dispatcher){Interval=TimeSpan.FromHours(6)};
        periodicUpdateTimer.Tick+=async (_,_)=>await CheckModuleUpdatesAsync(silent:true);
        idleApplyTimer=new(System.Windows.Threading.DispatcherPriority.ApplicationIdle,Dispatcher){Interval=TimeSpan.FromSeconds(1)};
        idleApplyTimer.Tick+=async (_,_)=>
        {
            idleApplyTimer.Stop();
            if(backgroundUpdates==null||!CanApplyBackgroundUpdate()||backgroundUpdates.IsWorking)return;
            try{await backgroundUpdates.ApplyWhenIdleAsync();}catch(OperationCanceledException){}catch(Exception ex){System.Diagnostics.Trace.TraceError(ex.ToString());}
        };
        periodicUpdateTimer.Start();
    }
    private void SchedulePendingUpdateWhenIdle()
    {
        if(!backgroundStopped&&backgroundUpdates?.HasPending==true&&idleApplyTimer!=null){idleApplyTimer.Stop();idleApplyTimer.Start();}
    }
    private void StopBackgroundUpdates()
    {
        backgroundStopped=true;periodicUpdateTimer?.Stop();idleApplyTimer?.Stop();periodicUpdateTimer=null;idleApplyTimer=null;
        backgroundUpdates?.Dispose();backgroundUpdates=null;
    }
    internal async Task<CommandResult> CheckModuleUpdatesAsync(CancellationToken ct=default,bool silent=false)
    {
        if(backgroundUpdates==null)return new(false,"기능 준비가 끝나지 않았습니다.");
        if(updateChecking)return new(false,"업데이트 확인이 진행 중입니다.");
        updateChecking=true;
        try
        {
            if(!silent&&!modularBusy)StatusText.Text="기능 업데이트 확인 중…";
            var progress=new InlineProgress<DistributionProgress>(p=>
            {
                if(silent||backgroundStopped||Dispatcher.HasShutdownStarted)return;
                void Show(){if(!backgroundStopped&&!modularBusy)StatusText.Text=ModuleSetupProgress.StageCaption(p.Stage,p.ModuleId==null?null:ModuleSetupProgress.Area(p.AreaLabel,p.ModuleId));}
                if(Dispatcher.CheckAccess())Show();else Dispatcher.Invoke(Show);
            });
            var result=await backgroundUpdates.CheckAsync(progress,ct);
            if(!silent&&!backgroundStopped&&!modularBusy)StatusText.Text=result.Message;
            SchedulePendingUpdateWhenIdle();return result;
        }
        catch(OperationCanceledException){return new(false,"업데이트 확인을 중단했습니다.");}
        catch(Exception ex){System.Diagnostics.Trace.TraceError(ex.ToString());var message=ModuleSetupProgress.ErrorCaption(ex);if(!silent&&!backgroundStopped&&!modularBusy)StatusText.Text=message;return new(false,message);}
        finally {updateChecking=false;}
    }    internal async Task<CommandResult> ApplyPreparedAreaUpdateAsync(PreparedModuleUpdate update,CancellationToken ct=default)
    {
        Dispatcher.VerifyAccess();
        if(runningCandidate==null||areaCoordinator==null||sharedDocument==null)return new(false,"실행 중인 기능이 없습니다.");
        if(modularBusy||confirmingExit||quiescenceDepth>0||sharedDocument.Document.IsDirty||OwnedWindows.Cast<Window>().Any(w=>w.IsVisible))return new(false,"문서 작업 또는 대화상자를 먼저 마쳐 주세요.");
        if(update.PreviousActive?.DirectoryName!=runningCandidate.DirectoryName)return new(false,"활성 기능 상태가 변경되어 다시 확인해야 합니다.");
        if(update.Candidate.DirectoryName==runningCandidate.DirectoryName)return new(true,"현재 최신 기능을 사용 중입니다.");
        var old=runningCandidate.Manifest.Modules.ToDictionary(m=>m.Id,StringComparer.Ordinal);
        var next=update.Candidate.Manifest.Modules;
        if(next.Length!=old.Count||next.Any(m=>!old.ContainsKey(m.Id)))return new(false,"기능 구성 변경은 앱을 다시 시작한 후 적용됩니다.");
        var changes=next.Where(m=>!SamePayload(old[m.Id],m)).ToArray();
        if(changes.Any(m=>m.Region is not (WorkspaceRegions.Ribbon or WorkspaceRegions.Sidebar or WorkspaceRegions.Workspace)||m.Region!=old[m.Id].Region))return new(false,"문서 엔진 변경은 앱을 다시 시작한 후 적용됩니다.");
        if(changes.Length==0)
        {
            try { await update.CommitAsync(ct);CommitRunningCandidate(update.Candidate);return new(true,"기능 상태를 확인했습니다."); }
            catch {await update.RollbackAsync();throw;}
        }
        var result=await areaCoordinator.SwapBatchAsync(changes.Select(m=>new AreaSwapChange(m.Region!,Manifest(m,update.Candidate.Manifest.Api),update.Candidate.DirectoryPath)).ToArray(),update.CommitAsync,update.RollbackAsync,ct);
        if(result.Success)CommitRunningCandidate(update.Candidate);
        return new(result.Success,result.Success?"변경된 기능을 적용했습니다.":result.Message);
    }
    internal static bool SamePayload(ModuleAsset a,ModuleAsset b)=>a.Id==b.Id&&a.Version==b.Version&&a.AssemblyName==b.AssemblyName&&a.Type==b.Type&&a.Region==b.Region&&a.Size==b.Size&&string.Equals(a.Sha256,b.Sha256,StringComparison.OrdinalIgnoreCase)&&SameResources(a.Files??[],b.Files??[]);
    private static bool SameResources(ModuleResource[] a,ModuleResource[] b)=>a.Length==b.Length&&a.All(x=>b.Any(y=>x.Kind==y.Kind&&x.Name==y.Name&&x.Size==y.Size&&string.Equals(x.Sha256,y.Sha256,StringComparison.OrdinalIgnoreCase)));
    private sealed class AreaSwapHost(MainWindow owner):IWpfAreaSwapHost<FrameworkElement>
    {
        public void VerifyAccess()=>owner.Dispatcher.VerifyAccess();
        public FrameworkElement? GetAttachedView(string region)=>owner.areaViews.GetValueOrDefault(region);
        public IDisposable? TryEnterQuiescence(string region,DocumentSnapshot expected)
        {
            VerifyAccess();var now=owner.sharedDocument?.Document;
            if(now==null||owner.modularBusy||owner.confirmingExit||now.IsDirty||now.DocumentId!=expected.DocumentId||now.Version!=expected.Version||(owner.runningCandidate!=null&&owner.OwnedWindows.Cast<Window>().Any(w=>w.IsVisible)))return null;
            var enabled=owner.ShellRoot.IsEnabled;owner.quiescenceDepth++;owner.ShellRoot.IsEnabled=false;
            return new Guard(()=>{owner.quiescenceDepth--;if(owner.quiescenceDepth==0)owner.ShellRoot.IsEnabled=enabled;});
        }
        public bool TryExchangeView(string region,FrameworkElement? expected,FrameworkElement? replacement)
        {
            VerifyAccess();if(!ReferenceEquals(GetAttachedView(region),expected))return false;
            if(replacement!=null&&(VisualTreeHelper.GetParent(replacement)!=null||LogicalTreeHelper.GetParent(replacement)!=null))return false;
            try
            {
                if(region==WorkspaceRegions.Ribbon)owner.RibbonScroller.Content=replacement;
                else
                {
                    if(expected!=null)owner.modularBody.Children.Remove(expected);
                    if(replacement!=null){Grid.SetColumn(replacement,region==WorkspaceRegions.Sidebar?0:2);owner.modularBody.Children.Add(replacement);}
                }
                if(replacement==null)owner.areaViews.Remove(region);else owner.areaViews[region]=replacement;
                return true;
            }
            catch
            {
                if(region==WorkspaceRegions.Ribbon)owner.RibbonScroller.Content=expected;
                else
                {
                    if(replacement!=null)owner.modularBody.Children.Remove(replacement);
                    if(expected!=null&&!owner.modularBody.Children.Contains(expected))owner.modularBody.Children.Add(expected);
                }
                throw;
            }
        }
        private sealed class Guard(Action release):IDisposable
        {
            private Action? action=release;
            public void Dispose()=>Interlocked.Exchange(ref action,null)?.Invoke();
        }
    }
}




