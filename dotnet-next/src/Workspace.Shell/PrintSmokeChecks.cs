using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
internal static partial class ShellSmokeChecks
{
 private static async Task CheckPrint(Action<bool,string> check,string output)
 {
  var window=new MainWindow(new ModuleRegistry(),new ModuleLoadReport(Array.Empty<string>(),Array.Empty<string>()));var vm=window.Model;var p=vm.Preview;var root=(FrameworkElement)window.Content;
  // Ownership requires a native owner handle, but neither window is ever shown.
  new WindowInteropHelper(window).EnsureHandle();
  void Layout(int w=1280,int h=820){root.Measure(new Size(w,h));root.Arrange(new Rect(0,0,w,h));root.UpdateLayout();}
  check(window.PrintDialogPresenter==null,"Production print uses native modal presentation by default");
  window.PrintDialogPresenter=_=>throw new InvalidOperationException("Print must not open without a sample");
  check(!vm.PrintSample.CanExecute(null),"Print unavailable without active sample");vm.PrintSample.Execute(null);check(window.PrintPanel==null,"No phantom print without sample");
  vm.OpenPreview.Execute(null);Layout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout();
  check(window.QuickPrintButton.Command==vm.PrintSample&&window.InputBindings.OfType<KeyBinding>().Count(k=>k.Key==Key.P&&k.Modifiers==ModifierKeys.Control&&k.Command==vm.PrintSample)==1,"Quick print and single Ctrl+P binding share command");
  p.BeginCrop();check(!vm.PrintSample.CanExecute(null),"Print blocked during crop");vm.PrintSample.Execute(null);p.SelectCropDrag(new Point(50,80),new Point(620,820));p.ApplyCrop();p.RotateSelectedPages(true);Layout();
  var crop=p.CurrentCrop;var rotation=p.PageRotations.ToArray();var ids=p.PageIdentityOrder.ToArray();var selected=p.SelectedPages.ToArray();var undo=p.UndoDescription;var dirty=p.IsDirty;var page=p.PageNumber;var docs=p.DocumentCount;
  var children=window.ShellRoot.Children.Cast<UIElement>().ToArray();var enabled=children.Select(c=>c.IsEnabled).ToArray();
  SamplePrintWindow? lastDialog=null;var closedCount=0;
  void Configure(SamplePrintWindow dialog)
  {
   lastDialog=dialog;dialog.Closed+=(_,_)=>closedCount++;
   check(ReferenceEquals(dialog.Owner,window)&&window.OwnedWindows.Cast<Window>().Contains(dialog),"Print is an actual Window owned by MainWindow");
   check(dialog.ShowModal.Target==dialog&&dialog.ShowModal.Method==typeof(Window).GetMethod(nameof(Window.ShowDialog),Type.EmptyTypes),"Production entry point binds directly to WPF Window.ShowDialog");
   check(!dialog.IsVisible&&!window.IsVisible,"Smoke never shows native owner or modal");
   check(dialog.WindowStartupLocation==WindowStartupLocation.CenterOwner&&dialog.WindowStyle==WindowStyle.SingleBorderWindow&&dialog.ResizeMode==ResizeMode.CanResize&&!dialog.ShowInTaskbar&&!dialog.Topmost,"Native resizable title bar, centered owner, no extra taskbar or topmost window");
   check(dialog.Width==1040&&dialog.Height==740&&dialog.MinWidth==640&&dialog.MinHeight==480,"Sensible default and supported minimum window sizes");
   check(window.ShellRoot.Children.Cast<UIElement>().SequenceEqual(children)&&children.Select(c=>c.IsEnabled).SequenceEqual(enabled),"Owner tree intact: no overlay or synthetic disabling layer");
   check(vm.IsPrinting&&p.SearchPanel.Visibility!=Visibility.Visible,"Modal session closes search and owns command gate");
   check(!vm.PrintSample.CanExecute(null)&&!vm.Home.CanExecute(null)&&!vm.CloseDocument.CanExecute(null)&&!vm.OpenPreview.CanExecute(null)&&!vm.FindSample.CanExecute(null)&&!vm.Undo.CanExecute(null),"Modal blocks duplicate print, host navigation, editing and search");
   vm.PrintSample.Execute(null);check(ReferenceEquals(window.PrintWindow,dialog),"Direct command cannot create duplicate dialog");
  }
  window.PrintDialogPresenter=dialog=>
  {
   Configure(dialog);var panel=dialog.Panel;
   check(panel.IsValid&&panel.ChosenPages.SequenceEqual(new[]{page})&&panel.Paper!.Width==crop.Width&&panel.Paper.Height==crop.Height,"Current page uses cropped source dimensions without reflow");
   check(panel.Paper!.LayoutTransform is TransformGroup group&&group.Children.OfType<RotateTransform>().Single().Angle==rotation[page-1],"Print renderer retains source rotation");
   void Render(int width,int height,string name)
   {
    panel.Measure(new Size(width,height));panel.Arrange(new Rect(0,0,width,height));panel.UpdateLayout();
    Capture(panel,width,height,1,Path.Combine(output,name));
    var bounds=Bounds(panel.CloseButton,panel);var cancel=Bounds(panel.CancelButton,panel);
    check(bounds.Left>=0&&bounds.Right<=width&&bounds.Bottom<=height&&cancel.Right<=width&&cancel.Bottom<=height&&panel.PaperView.ActualWidth>100&&panel.PaperView.ActualHeight>100,$"Modal inner view usable at {width}x{height}");
   }
   Render(1280,820,"print-preview-desktop.png");Render(640,480,"print-preview-narrow.png");
   // Also reserve native borders/title bar inside a 640x480 OUTER window.
   Render(608,420,"print-preview-minimum-client.png");Render(800,600,"print-preview-800.png");
   panel.Scope.SelectedIndex=1;check(panel.ChosenPages.Length==p.PageCount,"All pages selected");panel.Pages.SelectedItem=p.PageCount;check(p.PageNumber==page,"Preview navigation does not move document");
   panel.Scope.SelectedIndex=2;panel.Range.Text="1, 2-3,2";check(panel.IsValid&&panel.ChosenPages.SequenceEqual(new[]{1,2,3}),"Range deduplicates and sorts accurately");
   foreach(var bad in new[]{"","0","-1","2-1","1,","1,,2","1.5","1-2-3","999999999999","1-999"}){panel.Range.Text=bad;check(!panel.IsValid&&panel.Paper==null,"Invalid range rejected: "+bad);}
   panel.Range.Text="1";foreach(var bad in new[]{"0","1000","-1","1.5","","2147483648"}){panel.Copies.Text=bad;check(!panel.IsValid,"Invalid copies rejected: "+bad);}panel.Copies.Text="999";check(panel.IsValid&&panel.Summary.Text.Contains("999"),"Copies affect quantity only");
   check(Descendants<Button>(panel).Any(b=>Equals(b.Content,"인쇄 · 미연결")&&!b.IsEnabled),"Actual print remains disabled, no fake output");
   check(!panel.HandleKey(Key.P),"Panel does not duplicate owner print shortcut");check(panel.HandleKey(Key.Escape),"Esc handled by print content");
  };
  vm.FindSample.Execute(null);vm.PrintSample.Execute(null);
  void Released(string path){check(window.PrintWindow==null&&window.PrintPanel==null&&!vm.IsPrinting&&vm.PrintSample.CanExecute(null)&&!window.OwnedWindows.Cast<Window>().Contains(lastDialog!),path+" releases ownership and command gate");}
  Released("Escape");
  foreach(var close in new[]{"Cancel","Close button","Native Close"})
  {
   window.PrintDialogPresenter=dialog=>{Configure(dialog);if(close=="Cancel")dialog.Panel.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else if(close=="Close button")dialog.Panel.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else dialog.Close();};
   vm.PrintSample.Execute(null);Released(close);
  }
  check(closedCount==4,"Each print window closes exactly once");
  window.PrintDialogPresenter=dialog=>{Configure(dialog);throw new InvalidOperationException("Injected presentation failure");};
  try{vm.PrintSample.Execute(null);}catch(InvalidOperationException e) when(e.Message=="Injected presentation failure"){}
  Released("Presentation failure");
  var confirmations=0;p.ConfirmClose=_=>{confirmations++;return SampleCloseDecision.Cancel;};
  window.PrintDialogPresenter=dialog=>{Configure(dialog);window.Close();};vm.PrintSample.Execute(null);Released("Owner close request");
  check(confirmations==0&&p.IsDirty,"Owner close while printing only dismisses preview and retains dirty document");
  window.Close();check(confirmations==1&&p.IsDirty,"Subsequent owner exit still runs dirty confirmation and honors cancel");
  check(p.CurrentCrop==crop&&p.PageRotations.SequenceEqual(rotation)&&p.PageIdentityOrder.SequenceEqual(ids)&&p.SelectedPages.SequenceEqual(selected)&&p.IsDirty==dirty&&p.UndoDescription==undo&&p.PageNumber==page&&p.DocumentCount==docs,"Print controls and all closing paths preserve document state and history");
  vm.Home.Execute(null);check(!vm.PrintSample.CanExecute(null),"Hidden sample on home cannot print");p.ConfirmClose=_=>SampleCloseDecision.Discard;window.Close();
 }
}
