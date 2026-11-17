using System.Net.Http;
using Workspace.Shell.ModuleDistribution;
using Workspace.Shell.Startup;
namespace Workspace.Shell;

internal sealed record SetupModulePlan(string Id,string? Region,string PlannedVersion,string? InstalledVersion,string ChangeKind,string? PatchDetails=null);

/// <summary>Measured completed stages, not estimated time or download percentage.
/// One verification per planned module + runtime attachment + persisted activation.</summary>
internal sealed class ModuleSetupProgress
{
    private readonly Dictionary<string,ModuleSetupItem> items=new(StringComparer.Ordinal);
    private readonly HashSet<string> verified=new(StringComparer.Ordinal);
    private bool attached,completed;
    private long? received,total;
    private int? plannedCount;
    private int reportedVerified;
    internal ModuleSetupSnapshot Apply(DistributionProgress value)
    {
        if(completed)return Current;
        if(value.ModuleCount is >0)plannedCount=value.ModuleCount;
        if(value.VerifiedModuleCount is >=0)reportedVerified=value.VerifiedModuleCount.Value;
        if(value.ModuleId!=null&&!items.ContainsKey(value.ModuleId)&&(value.PlannedVersion??value.InstalledVersion) is { } version)
            SetPlan([new(value.ModuleId,value.AreaLabel,version,value.InstalledVersion,value.ChangeKind??"",value.PlannedVersion==null?"온라인 버전은 확인하지 못했습니다. 확인된 설치본을 사용합니다.":null)]);
        return ApplyStage(value.Stage,value.ModuleId,value.Bytes,value.TotalBytes);
    }
    private string status="필수 기능을 확인하고 있습니다.";
    internal ModuleSetupSnapshot Current=>Snapshot(completed?ModuleSetupOutcome.Completed:ModuleSetupOutcome.Running);
    internal void SetPlan(IEnumerable<SetupModulePlan> plan)
    {
        foreach(var p in plan)
        {
            var prior=items.GetValueOrDefault(p.Id);
            var previous=p.InstalledVersion??"미설치";
            var action=p.ChangeKind switch {"Install"=>"신규 설치","Update"=>"버전 업데이트","Reuse" or "Unchanged" or "NoChange"=>"설치된 버전 유지","Offline" or "Cached"=>"설치된 기능 사용",_=>p.InstalledVersion==null?"신규 설치":p.InstalledVersion==p.PlannedVersion?"설치된 버전 유지":"버전 업데이트"};
            var detail=$"{action}: {previous} → {p.PlannedVersion}";
            var patch=string.IsNullOrWhiteSpace(p.PatchDetails)?detail:detail+"\n"+Clean(p.PatchDetails!);
            items[p.Id]=new(p.Id,Area(p.Region,p.Id),p.PlannedVersion,prior?.Stage??ModuleSetupStage.Planned,p.InstalledVersion??"미설치",BytesReceived:prior?.BytesReceived,TotalBytes:prior?.TotalBytes,Detail:detail,PatchDetails:patch);
        }
    }
    internal void SetCandidatePlan(DistributionCandidate candidate,DistributionCandidate? previous)
    {
        plannedCount=candidate.Manifest.Modules.Length;
        SetPlan(candidate.Manifest.Modules.Where(m=>!items.ContainsKey(m.Id)).Select(m=>{
            var old=previous?.Manifest.Modules.SingleOrDefault(x=>x.Id==m.Id);
            return new SetupModulePlan(m.Id,m.Region,m.Version,old?.Version,old==null?"Install":old.Version==m.Version?"Reuse":"Update");
        }));
        foreach(var module in candidate.Manifest.Modules)verified.Add(module.Id);
    }
    internal ModuleSetupSnapshot ApplyStage(string stage,string? id,long bytes,long totalBytes,long? moduleBytes=null,long? moduleTotal=null)
    {
        if(completed)return Current;
        received=totalBytes>0?Math.Max(0,bytes):null;total=totalBytes>0?totalBytes:null;
        if(id!=null&&items.TryGetValue(id,out var item))
        {
            var state=stage switch {"Downloading"=>ModuleSetupStage.Downloading,"Reusing"=>ModuleSetupStage.Installing,"Verified" or "Verifying"=>ModuleSetupStage.Verifying,"Plan"=>ModuleSetupStage.Planned,_=>ModuleSetupStage.Checking};
            if(stage=="Verified")verified.Add(id);
            var detail=stage switch {"Downloading"=>"다운로드 중","Reusing"=>"기존 기능 파일 준비 중","Verified"=>"파일 확인 완료","Verifying"=>"파일 확인 중",_=>"설치 상태 확인 중"};
            items[id]=item with {Stage=state,BytesReceived=moduleBytes,TotalBytes=moduleTotal,Detail=detail};
        }
        if(stage is "NoChange" or "Offline" or "Ready")
            foreach(var key in items.Keys.ToArray()){verified.Add(key);items[key]=items[key] with {Stage=ModuleSetupStage.Verifying,Detail=stage=="Offline"?"설치된 기능 확인 완료":"파일 확인 완료"};}
        status=StageCaption(stage,id!=null&&items.TryGetValue(id,out var row)?row.Area:null);
        return Current;
    }
    internal ModuleSetupSnapshot Attaching()
    {
        foreach(var key in items.Keys.ToArray())items[key]=items[key] with {Stage=ModuleSetupStage.Installing,Detail="작업 화면 연결 중"};
        status="메뉴와 작업 화면을 연결하고 있습니다.";return Current;
    }
    internal ModuleSetupSnapshot Attached()
    {
        attached=true;status="기능 연결을 마쳤습니다. 설치 상태를 저장하고 있습니다.";return Current;
    }
    internal ModuleSetupSnapshot Completed()
    {
        completed=true;attached=true;foreach(var key in items.Keys.ToArray()){verified.Add(key);items[key]=items[key] with {Stage=ModuleSetupStage.Ready,InstalledVersion=items[key].PlannedVersion,Detail="설치 및 연결 완료"};}
        status="필요한 기능을 사용할 준비가 되었습니다.";return Snapshot(ModuleSetupOutcome.Completed);
    }
    internal ModuleSetupSnapshot Failed(Exception error)
    {
        var cancelled=error is OperationCanceledException;
        foreach(var key in items.Keys.ToArray())items[key]=items[key] with {Stage=cancelled?ModuleSetupStage.Cancelled:ModuleSetupStage.Failed};
        status=cancelled?"기능 준비를 취소했습니다.":"기능 준비를 완료하지 못했습니다.";
        return Snapshot(cancelled?ModuleSetupOutcome.Cancelled:ModuleSetupOutcome.Failed,cancelled?null:ErrorCaption(error));
    }
    private ModuleSetupSnapshot Snapshot(ModuleSetupOutcome outcome=ModuleSetupOutcome.Running,string? error=null)
    {
        int moduleCount=plannedCount??items.Count;
        int all=moduleCount+2,done=Math.Min(moduleCount,Math.Max(verified.Count,reportedVerified))+(attached?1:0)+(completed?1:0);
        double? percent=moduleCount==0?null:100d*done/all;
        return new(items.Values.ToArray(),status+(items.Count>0?$"  ·  완료 단계 {done}/{all}":""),outcome,percent,received,total,error,CanCancel:outcome==ModuleSetupOutcome.Running,CanRetry:outcome is ModuleSetupOutcome.Failed or ModuleSetupOutcome.Cancelled);
    }
    internal static string Area(string? region,string id)=>(region??id).ToLowerInvariant() switch {"ribbon"=>"메뉴","sidebar"=>"페이지 축소판","workspace" or "viewer"=>"PDF 보기","engine" or "pdfengine"=>"PDF 엔진",_=>"추가 기능"};
    internal static string StageCaption(string stage,string? area=null)=>stage switch
    {
        "Checking"=>"설치된 기능과 업데이트를 확인하고 있습니다.","Plan" or "Planned"=>"설치할 기능과 버전을 확인했습니다.",
        "Downloading"=>$"{area??"기능 파일"} 다운로드 중","Verifying"=>$"{area??"기능 파일"} 확인 중", "Verified"=>$"{area??"기능 파일"} 확인 완료",
        "Reusing"=>$"{area??"설치된 기능"} 준비 중","Offline"=>"설치된 기능을 확인했습니다.","NoChange"=>"현재 설치된 기능이 최신 상태입니다.","Ready"=>"기능 파일 준비를 마쳤습니다.",_=>"기능 설치를 준비하고 있습니다."
    };
    internal static string ErrorCaption(Exception error)=>error is HttpRequestException or TaskCanceledException?"다운로드하지 못했습니다. 연결을 확인하고 다시 시도하세요.":"기능을 연결하거나 설치 상태를 저장하지 못했습니다. 다시 시도해 주세요.";
    private static string Clean(string value)=>System.Text.RegularExpressions.Regex.Replace(value,@"https?://\S+|[A-Za-z]:\\[^\r\n]+","",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
}

internal sealed class InlineProgress<T>(Action<T> report):IProgress<T>
{
    public void Report(T value)=>report(value);
}
