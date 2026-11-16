using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Workspace.Shell.PdfOperations;
namespace Workspace.Shell.PdfViewing;
internal static class RealSessionChecks
{
 public static async Task RunAsync(string fixture,string output)
 {
  Directory.CreateDirectory(output);var source=Path.Combine(output,"source 한글 # &.pdf");File.Copy(fixture,source,false);
  static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
  var original=Hash(source);var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
  await using(var session=await PdfDocumentSession.OpenAsync(source,CancellationToken.None))
  {
   Check(session.PageCount==3&&!session.IsDirty,"real open 3 pages, clean");
   var before=await session.Viewer.RenderAsync(1,CancellationToken.None);Check(before.Length>100,"real PNG rendering");
   await session.ModifyAsync(PdfOperationKind.Rotate,[1],null,CancellationToken.None);Check(session.IsDirty&&session.CanUndo,"rotation marks dirty");
   var after=await session.Viewer.RenderAsync(1,CancellationToken.None);Check(!before.SequenceEqual(after),"rotation visible in rendered page");Check(Hash(source)==original,"source hash protected after edit");
   await session.UndoAsync(false,CancellationToken.None);Check(!session.IsDirty&&session.CanRedo,"undo to baseline is clean");await session.UndoAsync(true,CancellationToken.None);Check(session.IsDirty,"redo dirty");
   try{await session.SaveAsync(source,false,CancellationToken.None);throw new Exception("unapproved overwrite accepted");}catch(IOException){checks.Add("unapproved replacement rejected");}
   Check(Hash(source)==original,"rejected replacement preserves source");
   Check(!await session.ConfirmCloseAsync(()=>0,()=>Task.CompletedTask),"close cancel preserves dirty session");
   Check(await session.ConfirmCloseAsync(()=>1,()=>Task.CompletedTask)&&session.IsDirty,"discard permits close without saving");
   Check(!await session.ConfirmCloseAsync(()=>2,()=>Task.CompletedTask),"cancelled save blocks close");
   var saved=Path.Combine(output,"saved.pdf");await session.SaveAsync(saved,false,CancellationToken.None);Check(!session.IsDirty&&File.Exists(saved),"save as clean and published");Check(await session.ConfirmCloseAsync(()=>0,()=>Task.CompletedTask),"clean close requires no prompt");
   await session.ModifyAsync(PdfOperationKind.Delete,[3],null,CancellationToken.None);Check(session.PageCount==2&&session.IsDirty,"delete actual page");
   using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await session.SaveAsync(saved,true,cancel.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){checks.Add("cancelled save does not publish");}}
   await session.SaveAsync(saved,true,CancellationToken.None);Check(!session.IsDirty,"confirmed atomic replacement clean");
   await using(var reopened=await PdfDocumentSession.OpenAsync(saved,CancellationToken.None))Check(reopened.PageCount==2&&!reopened.IsDirty,"saved replacement reopens with 2 pages");
   await session.ModifyAsync(PdfOperationKind.Reorder,[2,1],null,CancellationToken.None);Check(session.IsDirty,"reorder actual PDF");
   await session.ModifyAsync(PdfOperationKind.Merge,null,[source],CancellationToken.None);Check(session.PageCount==5,"merge actual PDFs");
   var extracted=Path.Combine(output,"extracted.pdf");await new PdfCraftOperationsService(ReadOnlyPdfSession.DependencyPath()).ExecuteAsync(new(PdfOperationKind.Extract,[session.CurrentPath],extracted,[1,3]));
   await using(var extractedSession=await PdfDocumentSession.OpenAsync(extracted,CancellationToken.None))Check(extractedSession.PageCount==2,"extract reopens with 2 pages");
   Check(Hash(source)==original,"source unchanged across all operations");
  }
  var external=Path.Combine(output,"external-change.pdf");File.Copy(fixture,external,false);
  await using(var conflict=await PdfDocumentSession.OpenAsync(external,CancellationToken.None)){
   await conflict.ModifyAsync(PdfOperationKind.Rotate,[1],null,CancellationToken.None);
   await File.AppendAllTextAsync(external,"\n%external edit before save");var edited=Hash(external);
   try{await conflict.SaveAsync(external,true,CancellationToken.None);throw new Exception("external changes overwritten");}catch(IOException){checks.Add("external edit since open blocks replacement");}
   Check(Hash(external)==edited&&conflict.IsDirty,"external bytes and dirty revision retained");
  }
  var between=Path.Combine(output,"between-confirm-publish.pdf");File.Copy(fixture,between,false);
  await using(var conflict=await PdfDocumentSession.OpenAsync(between,CancellationToken.None)){
   await conflict.ModifyAsync(PdfOperationKind.Rotate,[1],null,CancellationToken.None);
   var approved=Hash(between);conflict.BeforePublishTestHook=()=>File.AppendAllTextAsync(between,"\n%external edit during save");
   try{await conflict.SaveAsync(between,true,CancellationToken.None,approved);throw new Exception("concurrent change overwritten");}catch(IOException){checks.Add("edit between approval and publish blocks replacement");}
   Check(Hash(between)!=approved&&conflict.IsDirty,"concurrent external edit survives failed publish");
  }
  await File.WriteAllTextAsync(Path.Combine(output,"session-checks.json"),JsonSerializer.Serialize(new{Passed=true,Count=checks.Count,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
 }
}
