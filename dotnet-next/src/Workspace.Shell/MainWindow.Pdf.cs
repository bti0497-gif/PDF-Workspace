using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace.Shell.PdfViewing;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
public partial class MainWindow
{
 private PdfWorkspaceView realWorkspace=null!;
 private bool realExitApproved;
 internal PdfWorkspaceView RealWorkspace=>realWorkspace;
 private void InitializeRealWorkspace()
 {
  realWorkspace=new PdfWorkspaceView();
  // Keep the approved main chrome; replace the sample body and ribbon with actual operations.
  foreach(UIElement child in ShellRoot.Children)if(Grid.GetRow(child) is 4 or 5)child.Visibility=Visibility.Collapsed;
  Grid.SetRow(realWorkspace,4);Grid.SetRowSpan(realWorkspace,2);ShellRoot.Children.Add(realWorkspace);
  RibbonScroller.Content=realWorkspace.Ribbon;RibbonPanel.Visibility=Visibility.Visible;
  PrototypeNotice.Visibility=Visibility.Collapsed;
  QuickAccessBar.Visibility=Visibility.Collapsed;QuickAccessOverflow.Visibility=Visibility.Collapsed;
  InputBindings.Clear();
  void Bind(Key key,ModifierKeys mods,Func<Task> run)=>InputBindings.Add(new KeyBinding(new UiCommand(async()=>await realWorkspace.TrackAsync(run)),key,mods));
  Bind(Key.O,ModifierKeys.Control,realWorkspace.OpenDialogAsync);Bind(Key.S,ModifierKeys.Control,()=>realWorkspace.SaveAsync(false));Bind(Key.S,ModifierKeys.Control|ModifierKeys.Shift,()=>realWorkspace.SaveAsync(true));Bind(Key.W,ModifierKeys.Control,realWorkspace.CloseDocumentAsync);Bind(Key.Z,ModifierKeys.Control,()=>realWorkspace.UndoAsync(false));Bind(Key.Y,ModifierKeys.Control,()=>realWorkspace.UndoAsync(true));
  foreach(var button in TopTabs.Children.OfType<Button>()){
   button.Command=null;var category=(string)button.Content;button.IsEnabled=PdfWorkspaceView.SupportsRibbonCategory(category);button.Tag=category=="홈";ApplyRealTabAppearance(button,category=="홈");
   if(!button.IsEnabled){button.ToolTip="현재 실제 PDF 작업에서 지원하지 않는 탭입니다.";ToolTipService.SetShowOnDisabled(button,true);continue;}
   button.Click+=(_,_)=>{realWorkspace.SelectRibbonCategory(category);RibbonPanel.Visibility=Visibility.Visible;foreach(var tab in TopTabs.Children.OfType<Button>()){var selected=Equals(tab.Content,category);tab.Tag=selected;ApplyRealTabAppearance(tab,selected);}};
  }
  realWorkspace.StateChanged+=()=>{Title=realWorkspace.Session==null?"더존 PDF Workspace":System.IO.Path.GetFileName(realWorkspace.Session.SourcePath)+(realWorkspace.Session.IsDirty?" *":"")+" · 더존 PDF Workspace";};
  StatusText.Text="실제 PDF 작업 공간 · 미지원 기능은 비활성화 · 암호화 PDF 미지원";
 }
 private static void ApplyRealTabAppearance(Button button,bool selected)
 {
  // The historic sample model keeps RibbonVisible=false; real tabs own their visual state.
  button.Background=selected?new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(246,246,246)):System.Windows.Media.Brushes.Transparent;
  button.Foreground=selected?new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156,71,46)):System.Windows.Media.Brushes.White;
 }
 private void OpenRealPdf(object sender,RoutedEventArgs e)=>_=realWorkspace.OpenDialogAsync();
 internal void OpenReadOnlyPdf(string? path){if(path==null)_=realWorkspace.OpenDialogAsync();else _=realWorkspace.OpenAsync(path);}
 private async void GuardRealClosing(System.ComponentModel.CancelEventArgs e)
 {
  if(realExitApproved)return;e.Cancel=true;if(realWorkspace.IsBusy||confirmingExit)return;confirmingExit=true;
  try{if(await realWorkspace.ConfirmCloseAsync()){await realWorkspace.DisposeAsync();realExitApproved=true;_ = Dispatcher.BeginInvoke(Close);}}finally{confirmingExit=false;}
 }
}
