using System.Windows;
using System.Windows.Controls;

namespace Workspace.Shell;

// Intentionally separate from ShellSmokeChecks.cs so the focused ribbon assertions can
// be wired into its existing RunAsync sequence without changing the established checks.
internal static partial class ShellSmokeChecks
{
    internal static void CheckRibbonPolish(MainWindow window, Action<bool, string> check)
    {
        var shared = (Style)window.FindResource("RibbonIconButton");
        var special = (Style)window.FindResource("SpecialRibbonIconButton");

        check(shared.TargetType == typeof(Button), "Ribbon icon interaction style targets buttons");
        check(shared.Setters.OfType<Setter>().Any(s => s.Property == Button.TemplateProperty), "Ribbon icon interaction style supplies shared chrome");
        check(ReferenceEquals(special.BasedOn, shared), "File/View/Help ribbon buttons inherit shared interaction chrome");

        var pdfTools = Descendants<Button>(window.PdfRibbonGroups).Where(button => button.Name == "UnavailablePdfTool").ToArray();
        check(pdfTools.Length == 9 && pdfTools.All(button => ReferenceEquals(button.Style?.BasedOn, shared)), "Regular PDF ribbon buttons inherit shared interaction chrome");
        check(pdfTools.All(button => !button.IsEnabled), "Unconnected PDF ribbon buttons remain disabled");
    }
}
