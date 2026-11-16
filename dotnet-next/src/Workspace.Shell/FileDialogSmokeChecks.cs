using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
internal static partial class ShellSmokeChecks
{
 private static void CheckFileDialogs(Action<bool,string> check,string output)
 {
  var w=new MainWindow(new ModuleRegistry(),new ModuleLoadReport(Array.Empty<string>(),Array.Empty<string>()));new WindowInteropHelper(w).EnsureHandle();var vm=w.Model;var p=vm.Preview;
  check(!vm.SaveSampleAs.CanExecute(null),"Save as unavailable without document");
  check(w.InputBindings.OfType<KeyBinding>().Count(k=>k.Key==Key.O&&k.Modifiers==ModifierKeys.Control&&System.Windows.Data.BindingOperations.GetBinding(k,InputBinding.CommandProperty)?.Path.Path==nameof(vm.OpenSample))==1,"Ctrl+O binds to virtual chooser exactly once (native key routing separately verified)");
  w.FileDialogPresenter=d=>{check(d.Owner==w&&d.ShowModal.Method.Name==nameof(Window.ShowDialog)&&!d.IsVisible,"File chooser native owner and ShowDialog contract; never shown by smoke");check(vm.IsFileDialog&&!vm.Home.CanExecute(null)&&!vm.OpenSample.CanExecute(null),"File modal blocks nested commands");check(!d.Accept.IsEnabled,"Open requires selection");var panel=d.Panel;panel.Measure(new Size(676,430));panel.Arrange(new Rect(0,0,676,430));panel.UpdateLayout();Capture(panel,676,430,1,Path.Combine(output,"sample-open-dialog.png"));d.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
  vm.OpenSample.Execute(null);check(p.DocumentCount==0&&!vm.IsFileDialog,"Open cancellation leaves empty workspace and releases gate");
  w.FileDialogPresenter=d=>{d.Catalog.SelectedIndex=0;d.Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};vm.OpenSample.Execute(null);check(p.DocumentCount==1&&vm.PreviewActive,"Chooser opens selected built-in only");
  p.RotateSelectedPages(true);var ids=p.PageIdentityOrder.ToArray();w.FileDialogPresenter=d=>d.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));vm.SaveSample.Execute(null);check(p.IsDirty&&p.VirtualCatalog.Count==2,"First save cancel does not create checkpoint");
  w.FileDialogPresenter=d=>{d.NameInput.Text="";check(!d.Accept.IsEnabled,"Blank name rejected");d.NameInput.Text="C:\\bad.pdf";check(!d.Accept.IsEnabled,"Physical path rejected");d.NameInput.Text="검토본";check(d.Accept.IsEnabled,"Valid virtual name accepted");var panel=d.Panel;panel.Measure(new Size(516,330));panel.Arrange(new Rect(0,0,516,330));panel.UpdateLayout();Capture(panel,516,330,1,Path.Combine(output,"sample-save-as-dialog.png"));d.Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};vm.SaveSample.Execute(null);check(!p.IsDirty&&p.ActiveDocumentName=="검토본"&&p.VirtualCatalog.Count==3,"Save commits named checkpoint only in memory");
  p.CloseCurrent();check(p.OpenVirtualSample("saved:검토본")&&p.PageIdentityOrder.SequenceEqual(ids)&&p.PageRotations[0]==90,"Named checkpoint survives close and reopens content");
  check(PreviewWorkspace.ValidateSampleName(" 검토본 ",new[]{"검토본"})!=null&&PreviewWorkspace.ValidateSampleName("NAME",new[]{"name"})!=null,"Trimmed and case-insensitive collisions rejected");
  p.RotateSelectedPages(true);w.FileDialogPresenter=_=>throw new InvalidOperationException();vm.SaveSampleAs.Execute(null);check(p.IsDirty&&!vm.IsFileDialog&&p.VirtualCatalog.Count==3,"Presenter error preserves dirty document and releases modal");
  p.SelectThumbnail(1,false,false);p.ExtractSelectedPages();p.SelectThumbnail(1,false,false);p.ExtractSelectedPages();var count=p.DocumentCount;var names=p.DirtyDocumentNames.ToArray();var prompts=0;p.ConfirmClose=_=>SampleCloseDecision.Save;p.RequestSampleName=(_,_)=>++prompts==1?"first":null;
  check(!p.ConfirmApplicationExit()&&p.DocumentCount==count&&p.DirtyDocumentNames.SequenceEqual(names)&&p.VirtualCatalog.Count==3,"Multi-document exit cancel is atomic: no checkpoint, name, catalog or tab changes");
  p.RequestSampleName=(_,_)=>throw new InvalidOperationException();p.CloseCurrent();check(p.DocumentCount==count&&p.IsDirty,"Close-save exception preserves current document");
  p.RequestSampleName=(_,_)=>"copy"+(++prompts);check(p.ConfirmApplicationExit()&&!p.HasDirtyDocuments&&p.DocumentCount==count,"Multi-document exit saves all only after names accepted");
  var fresh=new PreviewWorkspace();check(fresh.VirtualCatalog.Count==2,"New session has no saved checkpoints");
  p.ConfirmClose=_=>SampleCloseDecision.Discard;w.Close();
 }
}
