using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Workspace.Shell.Presentation;

internal sealed record SampleSearchHit(int Page, string Text)
{
    public override string ToString() => $"{Page}쪽 · {Text}";
}

// UI-only search over text supplied by the synthetic page renderer. No PDF or IO dependencies.
internal sealed class SampleSearchPanel : Border
{
    internal readonly TextBox Query = new() { MinHeight = 30, Margin = new Thickness(0, 6, 0, 6), MaxLength = 200 };
    internal readonly CheckBox MatchCase = new() { Content = "대/소문자 구분", Margin = new Thickness(0, 4, 0, 4) };
    internal readonly CheckBox WholeWord = new() { Content = "단어 단위 (공백·문장부호 경계)", Margin = new Thickness(0, 4, 0, 4) };
    internal readonly ListBox Results = new() { MinHeight = 60, Height = 150, Margin = new Thickness(0, 5, 0, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    internal readonly TextBlock Summary = Text("검색어를 입력하세요.");
    internal readonly Button Previous, Next;
    private readonly TextBlock title = Text("문서 찾기 · 합성 샘플");
    private readonly TextBlock scope = Text("");
    private readonly StackPanel advanced = new();
    private readonly Func<IReadOnlyList<SampleSearchHit>> source;
    private readonly Action<int> navigate;
    private bool refreshing;
    internal bool IsAdvanced { get; private set; }
    internal event Action? CloseRequested;

    internal SampleSearchPanel(Func<IReadOnlyList<SampleSearchHit>> source, Action<int> navigate)
    {
        this.source = source; this.navigate = navigate;
        Width = 360; HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(8); Padding = new Thickness(12); Background = Brushes.White;
        BorderBrush = Brushes.SlateGray; BorderThickness = new Thickness(1); Visibility = Visibility.Collapsed;
        var content = new StackPanel();
        var head = new DockPanel(); var close = MakeButton("닫기 ×", () => CloseRequested?.Invoke());
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close); head.Children.Add(title); content.Children.Add(head);
        content.Children.Add(Text("합성 샘플 원문 텍스트 검색 (자르기로 가린 영역 포함). 실제 PDF·파일·폴더 검색 아님."));
        content.Children.Add(scope); content.Children.Add(Text("검색어 (예: 점검, 가상, BOD)")); content.Children.Add(Query);
        advanced.Children.Add(Text("검색 범위: 현재 합성 샘플 문서"));
        var disabled = new WrapPanel();
        foreach (var label in new[] { "모든 열린 문서 (미지원)", "폴더 선택 (미지원)" })
        {
            var button = new Button { Content = label, IsEnabled = false, Margin = new Thickness(0, 2, 5, 2), ToolTip = "이번 목업은 현재 합성 샘플만 지원합니다." };
            ToolTipService.SetShowOnDisabled(button, true); disabled.Children.Add(button);
        }
        advanced.Children.Add(disabled); advanced.Children.Add(MatchCase); advanced.Children.Add(WholeWord); content.Children.Add(advanced);
        var actions = new WrapPanel(); actions.Children.Add(MakeButton("찾기", Refresh));
        Previous = MakeButton("이전", () => Move(-1)); Next = MakeButton("다음", () => Move(1));
        actions.Children.Add(Previous); actions.Children.Add(Next); content.Children.Add(actions);
        content.Children.Add(Summary); content.Children.Add(Results);
        content.Children.Add(Text("결과는 일치 문구 단위입니다. 선택하면 해당 쪽으로 이동합니다. 본문 강조·내용 수정은 하지 않습니다. Esc: 닫기"));
        Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ScrollViewer.SetHorizontalScrollBarVisibility(Results, ScrollBarVisibility.Disabled);
        var template = new DataTemplate(); var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding()); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        template.VisualTree = text; Results.ItemTemplate = template;
        Query.TextChanged += (_, _) => Refresh(); MatchCase.Checked += (_, _) => Refresh(); MatchCase.Unchecked += (_, _) => Refresh();
        WholeWord.Checked += (_, _) => Refresh(); WholeWord.Unchecked += (_, _) => Refresh();
        Results.SelectionChanged += (_, _) => { if (!refreshing && Results.SelectedItem is SampleSearchHit hit) { navigate(hit.Page); UpdateSummary(); } };
        PreviewKeyDown += (_, e) =>
        {
            e.Handled = HandleSearchKey(e.Key, Keyboard.Modifiers, e.OriginalSource == Query);
        };
        AutomationProperties.SetName(Query, "합성 샘플 검색어"); AutomationProperties.SetAutomationId(Query, "SampleSearchQuery");
        AutomationProperties.SetName(Results, "합성 샘플 검색 결과 · 선택하면 해당 쪽 이동");
    }
    internal void Open(bool detailed, string document)
    {
        IsAdvanced = detailed; title.Text = detailed ? "상세 검색 · 합성 샘플" : "문서 찾기 · 합성 샘플";
        advanced.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible; SetDocument(document);
        Query.Focus(); Query.SelectAll();
        Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) { Query.Focus(); Query.SelectAll(); } }), System.Windows.Threading.DispatcherPriority.Input);
    }
    internal void SetDocument(string document) { scope.Text = "현재 문서: " + document; Refresh(); }
    internal void Refresh()
    {
        if (Visibility != Visibility.Visible) return;
        var query = Query.Text.Trim(); refreshing = true;
        Results.ItemsSource = query.Length == 0 ? Array.Empty<SampleSearchHit>() : source().Where(hit => Matches(hit.Text, query)).ToArray();
        Results.SelectedIndex = -1; refreshing = false; UpdateSummary();
    }
    private bool Matches(string text, string query)
    {
        var comparison = IsAdvanced && MatchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var start = 0; start <= text.Length - query.Length;)
        {
            var index = text.IndexOf(query, start, comparison); if (index < 0) return false;
            if (!IsAdvanced || WholeWord.IsChecked != true ||
                ((index == 0 || !Word(text[index - 1])) && (index + query.Length == text.Length || !Word(text[index + query.Length])))) return true;
            start = index + query.Length;
        }
        return false;
    }
    private static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';
    internal bool HandleSearchKey(Key key, ModifierKeys modifiers, bool fromQuery)
    {
        if (key == Key.Escape) { CloseRequested?.Invoke(); return true; }
        if (key == Key.Enter && fromQuery) { Move(modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); return true; }
        return false;
    }
    internal void Move(int delta)
    {
        if (Results.Items.Count == 0) return;
        var index = Results.SelectedIndex;
        Results.SelectedIndex = index < 0 ? (delta < 0 ? Results.Items.Count - 1 : 0) : (index + delta + Results.Items.Count) % Results.Items.Count;
        Results.ScrollIntoView(Results.SelectedItem);
    }
    private void UpdateSummary()
    {
        Previous.IsEnabled = Next.IsEnabled = Results.Items.Count > 0;
        Summary.Text = string.IsNullOrWhiteSpace(Query.Text) ? "검색어를 입력하세요." : Results.Items.Count == 0 ? "일치하는 문구가 없습니다." :
            $"{Results.Items.Count}개 문구 · {(Results.SelectedIndex < 0 ? "결과를 선택하세요" : $"{Results.SelectedIndex + 1} / {Results.Items.Count}")} · 끝에서 순환";
    }
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
    private static Button MakeButton(string caption, Action action)
    { var b = new Button { Content = caption, Margin = new Thickness(2), Padding = new Thickness(7, 4, 7, 4) }; b.Click += (_, _) => action(); return b; }
}
