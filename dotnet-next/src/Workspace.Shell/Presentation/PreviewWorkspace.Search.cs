using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Workspace.Shell.Presentation;

public sealed partial class PreviewWorkspace
{
    internal SampleSearchPanel SearchPanel { get; private set; } = null!;
    private IInputElement? searchReturnFocus;
    private void InitializeSearch(Grid root)
    {
        SearchPanel = new SampleSearchPanel(ReadSearchText, GoToPage);
        Grid.SetRow(SearchPanel, 1); Grid.SetRowSpan(SearchPanel, 2); Panel.SetZIndex(SearchPanel, 10); root.Children.Add(SearchPanel);
        root.SizeChanged += (_, _) => { SearchPanel.MaxWidth = Math.Max(0, root.ActualWidth - 16); SearchPanel.MaxHeight = Math.Max(0, root.ActualHeight - _tabs.ActualHeight - 20); };
        SearchPanel.CloseRequested += CloseSearch;
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseSearch(); };
    }
    public void OpenSearch(bool advanced)
    {
        if (Current == null) return;
        CancelNavigationGesture();
        if (SearchPanel.Visibility != Visibility.Visible) searchReturnFocus = Keyboard.FocusedElement;
        SearchPanel.Open(advanced, ActiveDocumentName);
    }
    public void CloseSearch()
    {
        if (SearchPanel.Visibility != Visibility.Visible) return;
        var hadFocus = SearchPanel.IsKeyboardFocusWithin; SearchPanel.Visibility = Visibility.Collapsed;
        if (hadFocus && IsVisible)
        {
            if (searchReturnFocus is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus();
            else _canvas.Focus();
        }
        searchReturnFocus = null;
    }
    private void RefreshSearchDocument()
    {
        if (Current == null) { CloseSearch(); return; }
        if (SearchPanel.Visibility == Visibility.Visible) SearchPanel.SetDocument(ActiveDocumentName);
    }
    // Reuse the actual synthetic renderer, including current numbering/footer and blank pages.
    // Each match is an original synthetic text fragment (including clipped content), not a PDF location.
    internal IReadOnlyList<SampleSearchHit> ReadSearchText()
    {
        var results = new List<SampleSearchHit>();
        if (Current is not { } document) return results;
        void Visit(DependencyObject node, int page)
        {
            if (node is TextBlock text && !string.IsNullOrWhiteSpace(text.Text)) results.Add(new(page, text.Text));
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Visit(child, page);
        }
        for (var i = 0; i < document.Pages.Length; i++) Visit(BuildOriginalPaper(document, i), i + 1);
        return results;
    }
}
