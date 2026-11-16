using System.IO;
using System.Security.Cryptography;
using Workspace.Shell.PdfOperations;
namespace Workspace.Shell.PdfViewing;
internal sealed class PdfDocumentSession : IAsyncDisposable
{
 private readonly string root=Path.Combine(Path.GetTempPath(),"DeojonPdfSession-"+Guid.NewGuid().ToString("N"));
 private readonly List<string> revisions=new();
 private readonly PdfCraftOperationsService engine=new(ReadOnlyPdfSession.DependencyPath());
 private int position; private string savedRevision="";
 private string sourceFingerprint="";
 internal Func<Task>? BeforePublishTestHook {get;set;}
 internal static async Task<string> FingerprintAsync(string path,CancellationToken token){await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);return Convert.ToHexString(await SHA256.HashDataAsync(stream,token));}
 public string SourcePath {get;private set;}="";
 public string CurrentPath=>revisions[position];
 public bool IsDirty=>CurrentPath!=savedRevision;
 public bool CanUndo=>position>0; public bool CanRedo=>position+1<revisions.Count;
 public int PageCount=>Viewer.PageCount;
 public ReadOnlyPdfSession Viewer {get;private set;}=new();
 public static async Task<PdfDocumentSession> OpenAsync(string path,CancellationToken token)
 {
  var s=new PdfDocumentSession();try {Directory.CreateDirectory(s.root);s.SourcePath=ReadOnlyPdfSession.ValidateLocalPath(path);
   var first=Path.Combine(s.root,"original.pdf");
   await using(var input=new FileStream(s.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true))
   await using(var output=new FileStream(first,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)) await input.CopyToAsync(output,token);
   s.revisions.Add(first);s.savedRevision=first;s.sourceFingerprint=await FingerprintAsync(first,token);await s.Viewer.OpenAsync(first,token);return s;
  }catch{await s.DisposeAsync();throw;}
 }
 private async Task ActivateAsync(string path,CancellationToken token)
 {
  var next=new ReadOnlyPdfSession();try{await next.OpenAsync(path,token);}catch{await next.DisposeAsync();throw;}
  var old=Viewer;Viewer=next;await old.DisposeAsync();
 }
 public async Task ModifyAsync(PdfOperationKind kind,int[]? pages,string[]? additions,CancellationToken token)
 {
  var output=Path.Combine(root,Guid.NewGuid().ToString("N")+".pdf");
  await engine.ExecuteAsync(new(kind,new[]{CurrentPath}.Concat(additions??[]).ToArray(),output,pages),token);
  await ActivateAsync(output,token);if(CanRedo)revisions.RemoveRange(position+1,revisions.Count-position-1);revisions.Add(output);position++;
 }
 public async Task UndoAsync(bool redo,CancellationToken token)
 {
  var next=position+(redo?1:-1);if(next<0||next>=revisions.Count)return;await ActivateAsync(revisions[next],token);position=next;
 }
 public async Task SaveAsync(string destination,bool replacementApproved,CancellationToken token,string? approvedFingerprint=null)
 {
  var target=Path.GetFullPath(destination);if(!target.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase))throw new IOException("PDF 파일 이름이 필요합니다.");
  ReadOnlyPdfSession.ValidateLocalPath(Path.GetDirectoryName(target)!);var exists=File.Exists(target);
  if(exists)ReadOnlyPdfSession.ValidateLocalPath(target);if(exists&&!replacementApproved)throw new IOException("기존 파일 교체 승인이 필요합니다.");
  if(exists){var now=await FingerprintAsync(target,token);if(string.Equals(target,SourcePath,StringComparison.OrdinalIgnoreCase)&&now!=sourceFingerprint)throw new IOException("파일이 외부에서 변경되었습니다. 다른 이름으로 저장하거나 문서를 다시 여십시오.");if(approvedFingerprint!=null&&now!=approvedFingerprint)throw new IOException("교체 확인 후 대상 파일이 변경되었습니다.");approvedFingerprint=now;}
  else if(string.Equals(target,SourcePath,StringComparison.OrdinalIgnoreCase))throw new IOException("원본 파일이 외부에서 제거되었습니다. 다른 이름으로 저장하십시오.");
  var stage=Path.Combine(Path.GetDirectoryName(target)!,".pdf-workspace-"+Guid.NewGuid().ToString("N")+".pdf");
  try {await using var validation=new ReadOnlyPdfSession();await validation.OpenAsync(CurrentPath,token);
   await using(var input=new FileStream(CurrentPath,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true))
   await using(var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){await input.CopyToAsync(output,token);await output.FlushAsync(token);output.Flush(true);}
   if(BeforePublishTestHook!=null)await BeforePublishTestHook();
   token.ThrowIfCancellationRequested();if(exists){if(!File.Exists(target)||await FingerprintAsync(target,token)!=approvedFingerprint)throw new IOException("저장 중 대상 파일이 변경되었습니다. 교체하지 않았습니다.");File.Replace(stage,target,null);}else File.Move(stage,target,false);
   SourcePath=target;savedRevision=CurrentPath;sourceFingerprint=await FingerprintAsync(CurrentPath,CancellationToken.None);
  }finally{if(File.Exists(stage))File.Delete(stage);}
 }
 // 0 = cancel, 1 = discard, 2 = save. A failed/cancelled save cannot authorize close.
 public async Task<bool> ConfirmCloseAsync(Func<int> choose,Func<Task> save)
 {
  if(!IsDirty)return true;var choice=choose();if(choice==0)return false;if(choice==1)return true;
  if(choice!=2)return false;await save();return !IsDirty;
 }
 public async ValueTask DisposeAsync(){await Viewer.DisposeAsync();try{if(Directory.Exists(root))Directory.Delete(root,true);}catch(IOException){}}
}
