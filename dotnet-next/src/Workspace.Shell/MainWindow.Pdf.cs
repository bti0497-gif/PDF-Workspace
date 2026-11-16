using System.Windows;
using Workspace.Shell.PdfViewing;
using Workspace.Shell.PdfOperations;
namespace Workspace.Shell;
public partial class MainWindow
{
    private ReadOnlyPdfWindow? pdfViewer;
    private void OpenRealPdf(object sender, RoutedEventArgs e) => OpenReadOnlyPdf(null);
    internal void OpenReadOnlyPdf(string? path)
    {
        if (pdfViewer == null) {
            // Independent lifetime: shell close must not bypass awaited viewer cleanup.
            pdfViewer = new ReadOnlyPdfWindow(new PdfCraftOperationsService(ReadOnlyPdfSession.DependencyPath()));
            pdfViewer.Closed += (_, _) => pdfViewer = null;
            pdfViewer.Show();
        }
        pdfViewer.Activate();
        if (path != null) _ = pdfViewer.OpenPdfAsync(path);
    }
}
