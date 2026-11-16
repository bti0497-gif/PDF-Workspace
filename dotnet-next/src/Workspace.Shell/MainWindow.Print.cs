using System.Windows;
using System.Windows.Input;
using Workspace.Shell.Presentation;

namespace Workspace.Shell;

public partial class MainWindow
{
    private SamplePrintWindow? printWindow;
    private IInputElement? printFocus;
    internal SamplePrintPanel? PrintPanel => printWindow?.Panel;
    internal SamplePrintWindow? PrintWindow => printWindow;
    // Offscreen checks replace only the blocking presentation step, not window creation/ownership.
    internal Action<SamplePrintWindow>? PrintDialogPresenter { get; set; }

    private void InitializePrint() => Model.PrintRequested += OpenPrint;

    private void OpenPrint()
    {
        if (printWindow != null || !Model.PreviewActive || Model.Preview.PageCount == 0 || Model.Preview.IsCropping) return;
        printFocus = Keyboard.FocusedElement;
        Model.Preview.CloseSearch();
        var dialog = new SamplePrintWindow(Model.Preview);
        printWindow = dialog;
        Model.SetPrinting(true);
        try
        {
            dialog.Owner = this;
            if (PrintDialogPresenter is { } present) present(dialog);
            else dialog.ShowModal();
        }
        finally
        {
            // Includes native X/Alt+F4, Esc, buttons, and presentation failures.
            dialog.Close();
            printWindow = null;
            Model.SetPrinting(false);
            if (printFocus is UIElement target && target.IsEnabled && target.IsVisible) target.Focus();
            else Model.Preview.Focus();
            printFocus = null;
        }
    }

    internal void ClosePrint() => printWindow?.Close();
}
