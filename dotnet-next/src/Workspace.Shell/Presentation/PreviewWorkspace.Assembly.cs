namespace Workspace.Shell.Presentation;
internal sealed record AssembleSource(string Key,string Name,int Pages)
{public override string ToString()=>$"{Name} · 원본 1–{Pages}쪽";}
internal sealed class AssemblePlan(bool split,IReadOnlyList<AssembleSource> sources,string ranges,Func<IReadOnlyList<string>,string,bool> commit)
{
 internal bool Split {get;}=split;
 internal IReadOnlyList<AssembleSource> Sources {get;}=sources;
 internal string InitialRanges {get;}=ranges;
 private bool ended;
 internal bool Commit(IReadOnlyList<string> keys,string text){if(ended)return false;var ok=commit(keys,text);if(ok)ended=true;return ok;}
 internal void Cancel()=>ended=true;
}
public sealed partial class PreviewWorkspace
{
 private int assemblySequence;
 internal IReadOnlyList<string> AssemblyPageTitles=>Current?.Pages.Select(p=>p.Title).ToArray()??[];
 internal AssemblePlan? BeginAssembly(bool split)
 {
  if(_confirmingClose||IsCropping||Current==null||(split?PageCount<2:DocumentCount<2))return null;
  var docs=split?new[]{Current}:_documents.ToArray();
  var snapshots=docs.Select(d=>d.Pages.Select(CopyPage).ToArray()).ToArray();
  var entries=docs.Select((d,i)=>new AssembleSource(i.ToString(),d.Name,d.Pages.Length)).ToArray();
  var selected=SelectedPages.ToArray();var rest=Enumerable.Range(1,PageCount).Except(selected).ToArray();
  var ranges=selected.Length>0&&rest.Length>0?PageRanges(selected)+"; "+PageRanges(rest):"1; 2-"+PageCount;
  return new AssemblePlan(split,entries,ranges,(keys,text)=>
  {
   if(_confirmingClose||IsCropping||docs.Where((d,i)=>!_documents.Contains(d)||!SameContent(d.Pages,snapshots[i])).Any())return false;
   var outputs=new List<SampleDocument>();var sequence=assemblySequence+1;
   if(split)
   {
    if(!TryPartition(text,snapshots[0].Length,out var groups,out _))return false;
    foreach(var group in groups)outputs.Add(new SampleDocument($"분할 {sequence}-{outputs.Count+1} · {docs[0].Name}","메모리 전용 · 원본 "+PageRanges(group)+"쪽",group.Select(n=>NewAssemblyPage(snapshots[0][n-1])).ToArray()){SavedPages=null});
   }
   else
   {
    if(keys.Count<2||keys.Distinct().Count()!=keys.Count||keys.Any(k=>!entries.Any(e=>e.Key==k)))return false;
    var pages=keys.SelectMany(k=>snapshots[Array.FindIndex(entries,e=>e.Key==k)]).Select(NewAssemblyPage).ToArray();
    if(pages.Length==0)return false;
    outputs.Add(new SampleDocument($"병합 {sequence} [새 목업]","메모리 전용 · 선택한 원본 순서",pages){SavedPages=null});
   }
   if(_documents.Count+outputs.Count>VirtualSampleLimit)return false;
   var oldActive=_active;
   try{_documents.AddRange(outputs);SelectDocument(_documents.Count-outputs.Count);assemblySequence=sequence;FileNotice="새 샘플 탭 생성 · 메모리 전용 · 미저장 · 앱 종료 시 사라짐 · 실제 PDF/파일 없음";return true;}
   catch{foreach(var d in outputs)_documents.Remove(d);_active=oldActive;RefreshMutationView(false);return false;}
  });
 }
 private static SamplePage NewAssemblyPage(SamplePage p)=>new(p.Title,p.ShortTitle,p.Introduction,p.Section,p.Description,p.Columns.ToArray(),p.Rows.Select(r=>r.ToArray()).ToArray(),p.Notes.ToArray()){Crop=p.Crop,Rotation=p.Rotation,IsBlank=p.IsBlank};
 internal static bool TryPartition(string text,int count,out int[][] groups,out string error)
 {
  groups=[];error="각 쪽을 정확히 한 번 배정하세요. 예: 1-2; 3";
  if(count<2||string.IsNullOrWhiteSpace(text)||text.Length>4096)return false;
  var result=new List<int[]>();var seen=new HashSet<int>();
  foreach(var part in text.Split(';'))
  {
   var pages=new List<int>();
   foreach(var token in part.Split(','))
   {
    var ends=token.Trim().Split('-');if(ends.Length is <1 or >2||!int.TryParse(ends[0],out var first))return false;
    var last=first;if(ends.Length==2&&!int.TryParse(ends[1],out last))return false;
    if(first<1||last<first||last>count)return false;
    for(var n=first;n<=last;n++){if(!seen.Add(n))return false;pages.Add(n);}
   }
   if(pages.Count==0)return false;result.Add(pages.Order().ToArray());
  }
  if(result.Count<2||seen.Count!=count)return false;groups=result.ToArray();error="";return true;
 }
}
