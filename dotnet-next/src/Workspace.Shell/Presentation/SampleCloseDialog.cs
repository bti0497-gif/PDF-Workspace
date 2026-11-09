using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Workspace.Shell.Presentation;

/// <summary>Session-only sample confirmation. No filesystem or native message box.</summary>
public sealed class SampleCloseDialog : Window
{
    public SampleCloseDecision Decision { get; private set; } = SampleCloseDecision.Cancel;
    public Button SaveButton { get; }
    public Button DiscardButton { get; }
    public Button CancelButton { get; }
    public SampleCloseDialog(SampleCloseRequest request)
    {
        Title = request.IsApplicationExit ? "앱 종료 · 샘플 변경 확인" : "문서 닫기 · 샘플 변경 확인";
        Width = 540; Height = 360; MinWidth = 360; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Malgun Gothic"); FontSize = 13;
        Background = System.Windows.Media.Brushes.White;
        var root = new DockPanel { Background = System.Windows.Media.Brushes.White, Margin = new Thickness(20) };
        var header = new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var warning = new TextBlock { Text = "저장하지 않은 샘플 변경이 있습니다.\n샘플 메모리에만 저장, 실제 파일 아님, 앱 종료 시 사라짐.\n" +
            (request.IsApplicationExit ? "모두 저장해도 앱 종료 후에는 복원할 수 없습니다. 취소하면 앱과 모든 문서를 유지합니다." : "저장 후 닫은 샘플은 이번 실행 중에만 다시 열 수 있습니다."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        DockPanel.SetDock(warning, Dock.Top); root.Children.Add(warning);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        SaveButton = MakeButton(request.IsApplicationExit ? "모두 샘플 저장 후 종료" : "샘플 저장 후 닫기", "SampleSaveAndClose", SampleCloseDecision.Save);
        DiscardButton = MakeButton(request.IsApplicationExit ? "모두 버리고 종료" : "변경 버리고 닫기", "SampleDiscardAndClose", SampleCloseDecision.Discard);
        CancelButton = MakeButton("취소", "SampleCancelClose", SampleCloseDecision.Cancel);
        CancelButton.IsDefault = true;
        actions.Children.Add(SaveButton); actions.Children.Add(DiscardButton); actions.Children.Add(CancelButton);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var names = new StackPanel();
        foreach (var name in request.DocumentNames) names.Children.Add(new TextBlock { Text = "• " + name, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,3,0,3) });
        root.Children.Add(new ScrollViewer { Content = names, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Decide(SampleCloseDecision.Cancel); e.Handled = true; } };
        Loaded += (_, _) => CancelButton.Focus();
    }
    private Button MakeButton(string text, string id, SampleCloseDecision decision)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(10,7,10,7) };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, text);
        button.Click += (_, _) => Decide(decision); return button;
    }
    private void Decide(SampleCloseDecision decision)
    {
        Decision = decision;
        // Offscreen rendered tests use the real button without showing a native window.
        if (IsVisible) Close();
    }
}
