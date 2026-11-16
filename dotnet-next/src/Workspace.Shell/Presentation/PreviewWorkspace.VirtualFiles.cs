using System;
using System.Collections.Generic;
using System.Linq;
namespace Workspace.Shell.Presentation;
public sealed record VirtualSampleEntry(string Key, string Name, string Kind, int Pages)
{ public override string ToString() => $"{Name}   ·   {Pages}쪽   ·   {Kind}"; }
public sealed partial class PreviewWorkspace
{
    public const int VirtualSampleLimit = 32;
    private readonly Dictionary<string, SavedSample> _catalog = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, IReadOnlyList<string>, string?>? RequestSampleName { get; set; }
    public string FileNotice { get; private set; } = "합성 샘플 · 이번 실행 메모리만 사용 · 실제 파일 아님";
    public IReadOnlyList<VirtualSampleEntry> VirtualCatalog => CreateSamples().Select((d,i)=>new VirtualSampleEntry("builtin:"+i,d.Name,"내장 샘플",d.Pages.Length))
        .Concat(_catalog.Select(p=>new VirtualSampleEntry("saved:"+p.Key,p.Value.Name,"이번 실행 저장본",p.Value.Pages.Length))).ToArray();
    public static string? ValidateSampleName(string? name, IReadOnlyList<string> occupied)
    {
        var value=name?.Trim()??"";
        if(value.Length==0||value.Length>80) return "이름은 1~80자로 입력하세요.";
        if(value.Any(c=>char.IsControl(c)||"\\/:*?\"<>|".Contains(c))||value is "." or "..") return "경로가 아닌 샘플 이름을 입력하세요.";
        if(occupied.Contains(value,StringComparer.OrdinalIgnoreCase)) return "이미 있는 이름입니다. 다른 이름을 입력하세요.";
        return null;
    }
    public bool OpenVirtualSample(string key)
    {
        if(_confirmingClose||IsCropping) return false;
        SampleDocument? document=null;
        if(key.StartsWith("builtin:")&&int.TryParse(key[8..],out var index)&&index>=0&&index<2)
        {
            document=CreateSamples()[index];
            var existing=_documents.FindIndex(d=>d.Name==document.Name);
            if(existing>=0){SelectDocument(existing);return true;}
        }
        else if(key.StartsWith("saved:")&&_catalog.TryGetValue(key[6..],out var saved))
        {
            var existing=_documents.FindIndex(d=>string.Equals(d.VirtualName,saved.Name,StringComparison.OrdinalIgnoreCase));
            if(existing>=0){SelectDocument(existing);return true;}
            document=new SampleDocument(saved.Name,saved.Category,saved.Pages.Select(CopyPage).ToArray()){VirtualName=saved.Name};
        }
        if(document==null||_documents.Count>=VirtualSampleLimit){FileNotice="열 수 없는 항목이거나 열린 샘플 한도(32개)에 도달했습니다.";return false;}
        _documents.Add(document);SelectDocument(_documents.Count-1);return true;
    }
    public bool SaveCurrentSampleAs() => !_confirmingClose && Current is {} d && SaveDocuments(new[]{d},true);
    private bool SaveDocuments(SampleDocument[] documents,bool saveAs=false)
    {
        if(_confirmingClose) return false;
        _confirmingClose=true;_closeReentered=false;
        var open=_documents.ToArray();var before=open.Select(d=>d.Pages.Select(CopyPage).ToArray()).ToArray();
        var planned=new List<(SampleDocument Doc,string Name,SamplePage[] Pages)>();
        var occupied=_catalog.Keys.Concat(CreateSamples().Select(d=>d.Name)).ToList();
        try
        {
            foreach(var d in documents)
            {
                var name=d.VirtualName;
                if(saveAs||name==null)
                {
                    // Standalone model checks retain the original memory-checkpoint contract.
                    name=RequestSampleName==null ? d.Name : RequestSampleName(d.Name,occupied);
                    if(name==null){FileNotice="저장을 취소했습니다. 열린 샘플은 그대로 유지됩니다.";return false;}
                    name=name.Trim();
                    if(RequestSampleName!=null&&ValidateSampleName(name,occupied) is {} error){FileNotice=error;return false;}
                }
                if(!_catalog.ContainsKey(name)&&planned.All(p=>!string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))&&_catalog.Count+planned.Count(p=>!_catalog.ContainsKey(p.Name))>=VirtualSampleLimit)
                {FileNotice="이번 실행 저장본은 최대 32개입니다. 기존 저장본을 저장하거나 앱을 새로 시작하세요.";return false;}
                planned.Add((d,name,d.Pages.Select(CopyPage).ToArray()));occupied.Add(name);
            }
            if(_closeReentered||!open.SequenceEqual(_documents)||open.Where((d,i)=>!SameContent(d.Pages,before[i])).Any()) return false;
            // No checkpoint or catalog mutation until every modal succeeds.
            foreach(var p in planned){_catalog[p.Name]=new SavedSample(p.Name,p.Doc.Category,p.Pages);p.Doc.VirtualName=p.Name;p.Doc.Name=p.Name;Checkpoint(p.Doc);}
            FileNotice="이번 실행의 샘플 메모리에 저장했습니다. 실제 파일 없음 · 앱 종료 시 사라짐";
            RebuildTabs();UpdateControls();return true;
        }
        catch {FileNotice="샘플 저장을 완료하지 못했습니다. 문서를 닫지 않았습니다.";return false;}
        finally {_confirmingClose=false;}
    }
}
