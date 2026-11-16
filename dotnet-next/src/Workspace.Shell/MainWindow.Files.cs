using System.Windows;
using System.Windows.Input;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
public partial class MainWindow
{
 private SampleFileWindow? fileWindow;
 internal Action<SampleFileWindow>? FileDialogPresenter {get;set;}
 private void InitializeFileDialogs()
 {
  Model.OpenSampleRequested+=()=>{var key=PresentFileDialog();if(key!=null&&Model.Preview.OpenVirtualSample(key))Model.ActivateSample();};
  Model.Preview.RequestSampleName=(name,occupied)=>PresentFileDialog(name,occupied);
 }
 private string? PresentFileDialog(string? name=null,IReadOnlyList<string>? occupied=null)
 {
  if(fileWindow!=null||Model.IsPrinting)return null;
  var focus=Keyboard.FocusedElement;
  var dialog=new SampleFileWindow(Model.Preview.VirtualCatalog,name,occupied);fileWindow=dialog;Model.SetFileDialog(true);
  try{dialog.Owner=this;if(FileDialogPresenter is {} presenter)presenter(dialog);else dialog.ShowModal();return dialog.Result;}
  catch{return null;}
  finally{dialog.Close();fileWindow=null;Model.SetFileDialog(false);if(focus is UIElement element&&element.IsEnabled&&element.IsVisible)element.Focus();}
 }
}
