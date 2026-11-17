using Workspace.Shell.ModuleDistribution;
using Workspace.Shell.Startup;
namespace Workspace.Shell;
internal static class SetupProgressChecks
{
    internal static IReadOnlyList<string> Run()
    {
        var checks=new List<string>();void Check(bool valid,string label){if(!valid)throw new InvalidOperationException(label);checks.Add(label);}
        var model=new ModuleSetupProgress();
        var unknown=model.Apply(new("Checking",null,0,0,"https://example.invalid/private/manifest.json"));
        Check(unknown.OverallPercent==null&&unknown.Items.Count==0&&!unknown.Status.Contains("http"),"unknown manifest has no invented versions/percentage/URL");
        var plans=new[]{("pdfengine","engine","1.0.1",(string?)null,"Install"),("ribbon","ribbon","1.0.0","0.9.0","Update"),("sidebar","sidebar","1.0.0","1.0.0","Reuse"),("viewer","workspace","0.1.0","0.1.0","Reuse")};
        foreach(var (id,area,target,current,change) in plans)model.Apply(new("Plan",id,0,1000,"not publisher authentication",target,current,AreaLabel:area,ChangeKind:change,ModuleCount:4,VerifiedModuleCount:0));
        var plan=model.Current;
        Check(plan.Items.Select(i=>i.PlannedVersion).SequenceEqual(new[]{"1.0.1","1.0.0","1.0.0","0.1.0"}),"actual mixed module versions are preserved");
        Check(plan.Items.Select(i=>i.Area).SequenceEqual(new[]{"PDF 엔진","메뉴","페이지 축소판","PDF 보기"}),"all four regions have human area names");
        Check(plan.Items[0].InstalledVersion=="미설치"&&plan.Items[1].PatchDetails!.Contains("0.9.0 → 1.0.0")&&plan.Items[2].PatchDetails!.Contains("유지"),"new install, old-to-new update and reuse details are explicit");
        var bytes=model.Apply(new("Downloading","pdfengine",500,1000,"https://example.invalid/native.exe","1.0.1",ModuleCount:4,VerifiedModuleCount:0));
        Check(bytes.BytesReceived==500&&bytes.TotalBytes==1000&&bytes.Items[0].BytesReceived==null,"cumulative file bytes are not mislabeled as per-module bytes");
        Check(bytes.OverallPercent==0&&!bytes.Status.Contains("http"),"half-downloaded bytes do not claim half-installed completion");
        for(var i=0;i<plans.Length;i++)model.Apply(new("Verified",plans[i].Item1,1000,1000,"security boilerplate",ModuleCount:4,VerifiedModuleCount:i+1));
        var prepared=model.Apply(new("Ready",null,1000,1000,"staged only",ModuleCount:4,VerifiedModuleCount:4));
        Check(Math.Abs(prepared.OverallPercent!.Value-100d*4/6)<0.00001&&prepared.Outcome==ModuleSetupOutcome.Running&&prepared.Items.All(i=>i.Stage!=ModuleSetupStage.Ready),"all files verified means four of six stages, not installed-ready");
        model.Attaching();var attached=model.Attached();Check(Math.Abs(attached.OverallPercent!.Value-100d*5/6)<0.00001,"actual runtime attachment completes fifth stage");
        var done=model.Completed();Check(done.OverallPercent==100&&done.Outcome==ModuleSetupOutcome.Completed&&done.Items.All(i=>i.InstalledVersion==i.PlannedVersion&&i.Stage==ModuleSetupStage.Ready),"only activation completion reports installed versions and100percent");
        Check(done.Items[1].PatchDetails!.Contains("0.9.0 → 1.0.0"),"preinstallation-to-target patch history survives completion");
        Check(model.Apply(new("Downloading","viewer",0,1000,"late event")).Outcome==ModuleSetupOutcome.Completed,"late delivery cannot regress completed setup");
        var offline=new ModuleSetupProgress();var local=offline.Apply(new("Offline","viewer",0,0,"unknown online version",InstalledVersion:"0.1.0",AreaLabel:"workspace",ChangeKind:"Cached",ModuleCount:1,VerifiedModuleCount:1));
        Check(local.Items.Single().PlannedVersion=="0.1.0"&&local.Items.Single().PatchDetails!.Contains("온라인 버전은 확인하지 못"),"offline uses actual installed version without inventing online target");
        var failed=new ModuleSetupProgress().Failed(new InvalidOperationException("https://example.invalid/C:/secret security warning"));
        Check(failed.Error!=null&&!failed.Error.Contains("http")&&!failed.Error.Contains("secret")&&failed.Outcome==ModuleSetupOutcome.Failed,"errors are actionable without URL/path/security boilerplate");
        return checks;
    }
}
