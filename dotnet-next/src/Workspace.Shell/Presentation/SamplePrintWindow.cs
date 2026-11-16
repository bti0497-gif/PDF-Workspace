using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Workspace.Shell.Presentation;

// A real, owned modal window. Only the contents are synthetic; no printing or file output.
internal sealed class SamplePrintWindow : Window
{
    internal SamplePrintPanel Panel { get; }
    // Bound directly to WPF's native modal entry point, also inspectable without showing UI.
    internal Func<bool?> ShowModal { get; }

    internal SamplePrintWindow(PreviewWorkspace source)
    {
        Title = "인쇄 미리보기 · 합성 샘플";
        Width = 1040;
        Height = 740;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 249));
        Panel = new SamplePrintPanel(source);
        Content = Panel;
        ShowModal = ShowDialog;
        Panel.CloseRequested += Close;
        ContentRendered += FocusCloseButton;
        Closed += (_, _) =>
        {
            Panel.CloseRequested -= Close;
            ContentRendered -= FocusCloseButton;
        };
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    private void FocusCloseButton(object? sender, EventArgs e) => Panel.CloseButton.Focus();
}
