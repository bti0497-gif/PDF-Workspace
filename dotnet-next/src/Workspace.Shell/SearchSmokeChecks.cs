using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Workspace.Runtime;
using Workspace.Shell.Presentation;

namespace Workspace.Shell;
internal static partial class ShellSmokeChecks
{
    private static async Task CheckSearch(Action<bool,string> check, string output)
    {
        var window = new MainWindow(new ModuleRegistry(), new ModuleLoadReport(Array.Empty<string>(), Array.Empty<string>()));
        var root = (FrameworkElement)window.Content; var vm = window.Model; var preview = vm.Preview;
        void Layout(int w, int h) { root.Measure(new Size(w,h)); root.Arrange(new Rect(0,0,w,h)); root.UpdateLayout(); }
        check(!vm.FindSample.CanExecute(null) && !vm.AdvancedSearchSample.CanExecute(null), "Search disabled outside sample workspace");
        vm.OpenPreview.Execute(null); vm.HomeTab.Execute(null); Layout(1280,820);
        var tools = vm.PreviewGroups.Single(g => g.Name == "검색").Tools;
        check(tools[0].Command == vm.FindSample && tools[1].Command == vm.AdvancedSearchSample, "Home search tools share sample command instances");
        check(window.InputBindings.OfType<KeyBinding>().Any(k => k.Key == Key.F && k.Modifiers == ModifierKeys.Control && k.Command == vm.FindSample) &&
              window.InputBindings.OfType<KeyBinding>().Any(k => k.Key == Key.F && k.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && k.Command == vm.AdvancedSearchSample), "Find shortcuts share ribbon commands");
        var order = preview.PageIdentityOrder.ToArray(); var selected = preview.SelectedPages.ToArray();
        vm.FindSample.Execute(null); var search = preview.SearchPanel;
        check(search.Visibility == Visibility.Visible && !search.IsAdvanced, "Find opens sample-only panel");
        search.Query.Text = "점검"; check(search.Results.Items.Count > 0, "Korean sample text yields real displayed fragments");
        search.Move(1); var first = (SampleSearchHit)search.Results.SelectedItem;
        check(preview.PageNumber == first.Page && preview.ReadSearchText().Contains(first), "Selected search result navigates to matching displayed page");
        search.Move(-1); check(search.Results.SelectedIndex == search.Results.Items.Count - 1, "Previous search result wraps");
        search.Move(1); check(search.Results.SelectedIndex == 0, "Next search result wraps");
        check(!preview.IsDirty && !preview.CanUndo && preview.PageIdentityOrder.SequenceEqual(order) && preview.SelectedPages.SequenceEqual(selected), "Searching changes no content, dirty state, selection or history");
        Layout(1280,820); Capture(root,1280,820,1,Path.Combine(output,"search-find-desktop.png"));
        Layout(640,480); Capture(root,640,480,1,Path.Combine(output,"search-find-narrow.png"));
        vm.AdvancedSearchSample.Execute(null); check(search.IsAdvanced, "Advanced command opens distinct scope/options view");
        check(Descendants<Button>(search).Count(b => !b.IsEnabled && b.Content is string s && s.Contains("미지원")) == 2, "Multi-document and folder controls explicitly disabled");
        search.Query.Text = "xyz-no-match"; check(search.Results.Items.Count == 0 && !search.Next.IsEnabled && !search.Previous.IsEnabled, "No-match disables navigation");
        search.Query.Text = "   "; check(search.Results.Items.Count == 0, "Whitespace query returns no synthetic fabricated hits");
        preview.SelectDocument(1); search.Query.Text = "bod"; check(search.Results.Items.Count == 1, "Document switch updates search to current rendered text");
        search.MatchCase.IsChecked = true; check(search.Results.Items.Count == 0, "Case-sensitive option filters uppercase table cell");
        search.Query.Text = "BOD"; search.WholeWord.IsChecked = true; check(search.Results.Items.Count == 1, "Whole-word search matches exact table cell");
        search.Query.Text = "BO"; check(search.Results.Items.Count == 0, "Whole-word rejects substring");
        search.Query.Text = "BOD"; search.Move(1); Layout(1280,820); Capture(root,1280,820,1,Path.Combine(output,"search-advanced-desktop.png"));
        Layout(640,480); Capture(root,640,480,1,Path.Combine(output,"search-advanced-narrow.png"));
        check(search.ActualWidth <= 624 && search.ActualHeight <= preview.ActualHeight, "Search overlay is bounded in 640x480 layout and scrolls internally");
        preview.PreparePageContextSelection(2); preview.MoveSelectedPages(0);
        check(search.Results.Items.Cast<SampleSearchHit>().Single().Page == 1, "Reorder remaps search hit to current content position");
        preview.Undo();
        preview.PreparePageContextSelection(2); preview.InsertBlankPage(false);
        check(search.Results.Items.Cast<SampleSearchHit>().Single().Page == 3 && !preview.ReadSearchText().Any(h => h.Page == 2), "Blank insertion updates result page; blank has no searchable text");
        preview.PreparePageContextSelection(3); preview.DeleteSelectedPages(); check(search.Results.Items.Count == 0, "Deleted page cannot leave stale results");
        preview.Undo(); check(search.Results.Items.Cast<SampleSearchHit>().Single().Page == 3, "Undo refreshes search result numbering");
        preview.PreparePageContextSelection(3); preview.ExtractSelectedPages();
        check(search.Results.Items.Cast<SampleSearchHit>().Single().Page == 1, "Extracted sample searches its actual one-page contents");
        check(search.HandleSearchKey(Key.Escape, ModifierKeys.None, true) && search.Visibility == Visibility.Collapsed, "Escape search handler hides overlay without closing document");
        vm.FindSample.Execute(null); search.Query.Text = "BOD";
        check(search.Results.Items.Count == 1 && !search.IsAdvanced, "Reopening find retains query and ignores hidden advanced filters");
        check(search.HandleSearchKey(Key.Enter, ModifierKeys.None, true) && search.Results.SelectedIndex == 0, "Query Enter uses next-result handler");
        check(search.HandleSearchKey(Key.Enter, ModifierKeys.Shift, true) && search.Results.SelectedIndex == 0, "Query Shift Enter uses previous-result handler");
        check(!search.HandleSearchKey(Key.Delete, ModifierKeys.None, true), "Search handler leaves ordinary editing keys alone");
        preview.CloseSearch(); check(search.Visibility == Visibility.Collapsed, "Search close command hides overlay");
        vm.Home.Execute(null); Layout(640,480); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!vm.FindSample.CanExecute(null), "Search unavailable on home after sample workspace exit");
        preview.ConfirmClose = _ => SampleCloseDecision.Discard; window.Close();
    }
}
