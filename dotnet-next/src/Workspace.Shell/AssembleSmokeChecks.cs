using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
internal static partial class ShellSmokeChecks
{
 private static void CheckAssembly(Action<bool,string> check,string output)
 {
  var w=new MainWindow(new ModuleRegistry(),new ModuleLoadReport([],[]));new WindowInteropHelper(w).EnsureHandle();var vm=w.Model;var p=vm.Preview;
  check(!vm.MergeSamples.CanExecute(null)&&!vm.SplitSample.CanExecute(null),"Assembly disabled without samples");vm.OpenPreview.Execute(null);
  var root=(FrameworkElement)w.Content;root.Measure(new Size(1280,820));root.Arrange(new Rect(0,0,1280,820));root.UpdateLayout();
  p.SelectDocument(0);p.RotateSelectedPages(true);p.BeginCrop();p.SelectCropDrag(new Point(30,40),new Point(430,540));p.ApplyCrop();var crop=p.CurrentCrop;
  var ids=p.PageIdentityOrder.ToArray();var titles=p.AssemblyPageTitles.ToArray();var selected=p.SelectedPages.ToArray();var undo=p.UndoDescription;
  var invalid=p.BeginAssembly(false)!;check(!invalid.Commit([],"")&&!invalid.Commit(["0"],"")&&!invalid.Commit(["0","0"],"")&&!invalid.Commit(["0","missing"],""),"Merge rejects empty, no-op, duplicate and unknown sources");invalid.Cancel();check(!invalid.Commit(["0","1"],""),"Canceled assembly plan cannot commit");
  var stale=p.BeginAssembly(false)!;p.RotateSelectedPages(true);check(!stale.Commit(["0","1"],""),"Stale source content rejected");p.Undo();
  w.AssembleDialogPresenter=d=>{check(d.Owner==w&&d.ShowModal.Method.Name==nameof(Window.ShowDialog)&&!d.IsVisible,"Assembly owned native ShowDialog contract, never displayed in smoke");check(vm.IsAssembling&&!vm.OpenSample.CanExecute(null)&&!vm.PrintSample.CanExecute(null)&&!vm.CropPage.CanExecute(null)&&!vm.MergeSamples.CanExecute(null)&&!vm.CloseDocument.CanExecute(null),"Assembly blocks file, print, crop, nested assembly and close commands");check(!d.Accept.IsEnabled,"Merge initially empty and cannot commit");d.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};vm.MergeSamples.Execute(null);check(p.DocumentCount==2&&!vm.IsAssembling,"Cancel leaves source tabs unchanged and restores gate");
  w.AssembleDialogPresenter=_=>throw new InvalidOperationException();vm.MergeSamples.Execute(null);check(p.DocumentCount==2&&!vm.IsAssembling&&vm.PrintSample.CanExecute(null),"Presenter failure releases gate without creating tabs");
  p.SelectDocument(1);var second=p.AssemblyPageTitles.ToArray();p.SelectDocument(0);
  w.AssembleDialogPresenter=d=>{d.Order.Add((AssembleSource)d.Available.Items[0]);d.Order.Add((AssembleSource)d.Available.Items[1]);d.Ordered.SelectedIndex=1;d.Move(-1);check(d.Order[0].Key=="1","Merge reorder changes explicit source order");foreach(var width in new[]{1280,800,640,560}){d.Panel.Measure(new Size(width,400));d.Panel.Arrange(new Rect(0,0,width,400));d.Panel.UpdateLayout();check(Bounds(d.Accept,d.Panel).Right<=d.Panel.ActualWidth+1&&Bounds(d.Accept,d.Panel).Bottom<=d.Panel.ActualHeight+1,"Merge actions fit "+width);Capture(d.Panel,width,400,1,Path.Combine(output,$"assemble-merge-{width}.png"));}d.Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};vm.MergeSamples.Execute(null);
  check(p.DocumentCount==3&&p.IsDirty&&!p.CanUndo&&p.PageCount==6,"Merge creates independent dirty tab with empty history");check(p.AssemblyPageTitles.SequenceEqual(second.Concat(titles)),"Merge preserves requested source and internal page order");var mergedIds=p.PageIdentityOrder.ToArray();check(mergedIds.Distinct().Count()==6&&!mergedIds.Intersect(ids).Any(),"Merge pages have unique new identities");p.GoToPage(4);check(p.CurrentCrop==crop&&p.PageRotations[3]==90,"Merge carries crop and rotation");p.SelectThumbnail(4,false,false);p.RotateSelectedPages(true);
  p.SelectDocument(0);check(p.PageIdentityOrder.SequenceEqual(ids)&&p.SelectedPages.SequenceEqual(selected)&&p.UndoDescription==undo&&p.CurrentCrop==crop&&p.PageRotations[0]==90,"Editing result leaves source identity, selection, history, crop and rotation unchanged");
  foreach(var text in new[]{"","1-3","1;2","1;2;2-3","0;1-3","1;4","3-1;2","1;;2-3"})check(!PreviewWorkspace.TryPartition(text,3,out _,out _),"Invalid partition rejected: "+text);
  check(PreviewWorkspace.TryPartition("3,1;2",3,out var groups,out _)&&groups[0].SequenceEqual(new[]{1,3}),"Partition covers each page once in original internal order");
  w.AssembleDialogPresenter=d=>{d.Ranges.Text="1;1-3";check(!d.Accept.IsEnabled,"Split validation blocks overlap");d.Ranges.Text="1,3;2";foreach(var width in new[]{1280,800,640,560}){d.Panel.Measure(new Size(width,400));d.Panel.Arrange(new Rect(0,0,width,400));d.Panel.UpdateLayout();check(Bounds(d.Accept,d.Panel).Right<=d.Panel.ActualWidth+1&&Bounds(d.Accept,d.Panel).Bottom<=d.Panel.ActualHeight+1,"Split actions fit "+width);Capture(d.Panel,width,400,1,Path.Combine(output,$"assemble-split-{width}.png"));}d.Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};vm.SplitSample.Execute(null);
  check(p.DocumentCount==5&&p.PageCount==2&&p.IsDirty&&p.AssemblyPageTitles.SequenceEqual(new[]{titles[0],titles[2]})&&p.CurrentCrop==crop&&p.PageRotations[0]==90,"Split creates first dirty output in specified partition with transforms");var splitIds=p.PageIdentityOrder.ToArray();p.SelectDocument(4);check(p.PageCount==1&&p.IsDirty&&p.AssemblyPageTitles[0]==titles[1]&&!p.PageIdentityOrder.Intersect(splitIds.Concat(ids).Concat(mergedIds)).Any(),"Second split output independent with unique page identity");
  var count=p.DocumentCount;var names=p.DirtyDocumentNames.ToArray();p.ConfirmClose=_=>SampleCloseDecision.Cancel;check(!p.ConfirmApplicationExit()&&p.DocumentCount==count&&p.DirtyDocumentNames.SequenceEqual(names),"Multi-output lifecycle cancel is all-or-none");p.ConfirmClose=_=>SampleCloseDecision.Save;var prompts=0;p.RequestSampleName=(_,_)=>++prompts==1?"assembly-checkpoint":null;check(!p.ConfirmApplicationExit()&&p.VirtualCatalog.Count==2&&p.DirtyDocumentNames.SequenceEqual(names),"Multi-output save-name cancellation creates no partial checkpoints");
  check(new PreviewWorkspace().DocumentCount==0&&new PreviewWorkspace().VirtualCatalog.Count==2,"Assembly has no persistence across sessions");p.ConfirmClose=_=>SampleCloseDecision.Discard;w.Close();
 }
}
