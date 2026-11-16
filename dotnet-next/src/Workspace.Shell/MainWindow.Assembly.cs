using System.Windows;
using System.Windows.Input;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
public partial class MainWindow
{
 private SampleAssembleWindow? assembleWindow;
 internal Action<SampleAssembleWindow>? AssembleDialogPresenter {get;set;}
 private void InitializeAssembly()=>Model.AssembleRequested+=OpenAssembly;
 private void OpenAssembly(bool split)
 {
  if(assembleWindow!=null||Model.IsPrinting||Model.IsFileDialog||Model.Preview.IsCropping||Model.IsBusy)return;
  var plan=Model.Preview.BeginAssembly(split);if(plan==null)return;
  var focus=Keyboard.FocusedElement;SampleAssembleWindow? dialog=null;Model.SetAssembling(true);
  try{dialog=new SampleAssembleWindow(plan);assembleWindow=dialog;dialog.Owner=this;if(AssembleDialogPresenter is {} present)present(dialog);else dialog.ShowModal();}
  catch{ /* Fail closed on presentation errors. */ }
  finally{try{dialog?.Close();}finally{plan.Cancel();assembleWindow=null;Model.SetAssembling(false);if(focus is UIElement element&&element.IsEnabled&&element.IsVisible)element.Focus();else Model.Preview.Focus();}}
 }
}
