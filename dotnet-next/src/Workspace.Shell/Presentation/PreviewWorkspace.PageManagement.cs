using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Workspace.Shell.Presentation;

public sealed partial class PreviewWorkspace
{
    private const int HistoryLimit = 50;
    private int _extractionSequence;

    public bool CanUndo => Current?.UndoHistory.Count > 0;
    public bool CanRedo => Current?.RedoHistory.Count > 0;
    public string UndoDescription => Current is { UndoHistory.Count: > 0 } d ? d.UndoHistory[^1].Description : string.Empty;
    public string RedoDescription => Current is { RedoHistory.Count: > 0 } d ? d.RedoHistory[^1].Description : string.Empty;
    public IReadOnlyList<int> PageRotations => Current?.Pages.Select(p => p.Rotation).ToArray() ?? Array.Empty<int>();
    public bool CanDeleteSelectedPages => Current is { } d && d.Selected.Count > 0 && d.Selected.Count < d.Pages.Length;
    /// <summary>History availability, active document or selection state changed. Also refreshes host command enablement.</summary>
    public event Action? HistoryChanged;

    // Arrays belong exclusively to these private snapshots. Every capture/restore deep-copies mutable
    // content arrays; rotation itself is init-only and always replaced using a new page record.
    // View scale, fit mode and single/continuous mode deliberately do not belong to mutation history.
    private sealed record PageState(SamplePage[] Pages, string ViewedId, int ViewedIndex,
        string[] SelectedIds, string? AnchorId, double LocalOffset, double HorizontalOffset);
    private sealed record PageHistoryEntry(string Description, PageState Before, PageState After);

    private static SamplePage CopyPage(SamplePage page) => page with
    {
        Columns = page.Columns.ToArray(), Rows = page.Rows.Select(row => row.ToArray()).ToArray(),
        Notes = page.Notes.ToArray()
    };

    private PageState CapturePageState(SampleDocument d)
    {
        SaveScroll();
        return new PageState(d.Pages.Select(CopyPage).ToArray(), d.Pages[d.Page - 1].Identity, d.Page,
            d.Selected.OrderBy(n => n).Select(n => d.Pages[n - 1].Identity).ToArray(),
            d.SelectionAnchor > 0 ? d.Pages[d.SelectionAnchor - 1].Identity : null,
            (d.VerticalOffset - (d.Continuous ? PageTop(d.Page, d.Scale) : 0)) / d.Scale,
            d.HorizontalOffset / d.Scale);
    }

    private void RestorePageState(SampleDocument d, PageState state)
    {
        d.Pages = state.Pages.Select(CopyPage).ToArray();
        var viewed = Array.FindIndex(d.Pages, p => p.Identity == state.ViewedId);
        d.Page = viewed >= 0 ? viewed + 1 : Math.Clamp(state.ViewedIndex, 1, d.Pages.Length);
        d.Selected.Clear();
        var selected = state.SelectedIds.ToHashSet();
        for (var i = 0; i < d.Pages.Length; i++) if (selected.Contains(d.Pages[i].Identity)) d.Selected.Add(i + 1);
        d.SelectionAnchor = state.AnchorId == null ? 0 : Array.FindIndex(d.Pages, p => p.Identity == state.AnchorId) + 1;
        var local = Math.Clamp(state.LocalOffset * d.Scale, 0, Math.Max(0, RotatedHeight(d.Pages[d.Page - 1]) * d.Scale - 1));
        d.VerticalOffset = (d.Continuous ? PageTop(d.Page, d.Scale) : 0) + local;
        d.HorizontalOffset = state.HorizontalOffset * d.Scale;
        // Do not read the old canvas offset while this model awaits rebuilding.
        _restoring = true;
    }

    private void RefreshMutationView(bool refit)
    {
        RebuildTabs(); RebuildNavigation(); RebuildPages(); UpdateControls();
        if (refit) QueueFit();
    }

    private void CommitPageMutation(SampleDocument d, PageState before, string description)
    {
        var after = CapturePageState(d);
        d.UndoHistory.Add(new PageHistoryEntry(description, before, after));
        if (d.UndoHistory.Count > HistoryLimit) d.UndoHistory.RemoveAt(0);
        d.RedoHistory.Clear();
        RefreshMutationView(true);
    }

    public bool Undo()
    {
        if (Current is not { UndoHistory.Count: > 0 } d) return false;
        CancelNavigationGesture();
        var entry = d.UndoHistory[^1]; d.UndoHistory.RemoveAt(d.UndoHistory.Count - 1);
        d.RedoHistory.Add(entry); RestorePageState(d, entry.Before);
        // Keep manual zoom; when fitted, recompute scale for the restored page orientation.
        RefreshMutationView(true); return true;
    }

    public bool Redo()
    {
        if (Current is not { RedoHistory.Count: > 0 } d) return false;
        CancelNavigationGesture();
        var entry = d.RedoHistory[^1]; d.RedoHistory.RemoveAt(d.RedoHistory.Count - 1);
        d.UndoHistory.Add(entry); RestorePageState(d, entry.After);
        RefreshMutationView(true); return true;
    }

    public bool RotateSelectedPages(bool clockwise)
    {
        if (Current is not { } d || d.Selected.Count == 0) return false;
        CancelNavigationGesture();
        if (d.Selected.Count == 0) return false;
        var before = CapturePageState(d);
        var pages = d.Pages.Select((p, i) => d.Selected.Contains(i + 1)
            ? p with { Rotation = (p.Rotation + (clockwise ? 90 : 270)) % 360 } : p).ToArray();
        RestorePageState(d, before with { Pages = pages });
        CommitPageMutation(d, before, clockwise ? "오른쪽 90° 회전" : "왼쪽 90° 회전");
        return true;
    }

    public bool DeleteSelectedPages()
    {
        if (!CanDeleteSelectedPages || Current is not { } d) return false;
        CancelNavigationGesture();
        if (!CanDeleteSelectedPages) return false;
        var before = CapturePageState(d);
        var remaining = d.Pages.Where((_, i) => !d.Selected.Contains(i + 1)).ToArray();
        // If viewed content is removed, choose the nearest surviving ORIGINAL neighbor, preferring
        // the following page on a tie. Array-index clamping alone can skip a closer neighbor.
        var nearest = d.Pages.Select((p, i) => (Page: p, Number: i + 1))
            .Where(p => !d.Selected.Contains(p.Number))
            .OrderBy(p => Math.Abs(p.Number - d.Page)).ThenBy(p => p.Number < d.Page ? 1 : 0).First().Page.Identity;
        RestorePageState(d, before with { Pages = remaining, ViewedId = nearest });
        // Deleted selections cannot remain selected. The remaining viewed content becomes the target.
        d.Selected.Add(d.Page); d.SelectionAnchor = d.Page;
        CommitPageMutation(d, before, "페이지 삭제"); return true;
    }

    public bool InsertBlankPage(bool afterSelection)
    {
        if (Current is not { } d) return false;
        CancelNavigationGesture();
        var before = CapturePageState(d);
        var edge = d.Selected.Count == 0 ? d.Page : afterSelection ? d.Selected.Max() : d.Selected.Min();
        var index = afterSelection ? edge : edge - 1;
        var blank = new SamplePage("빈 페이지", "빈 페이지", "", "", "", Array.Empty<string>(),
            Array.Empty<string[]>(), Array.Empty<string>()) { IsBlank = true };
        var pages = d.Pages.ToList(); pages.Insert(index, blank);
        RestorePageState(d, before with { Pages = pages.ToArray() });
        d.Selected.Clear(); d.Selected.Add(index + 1); d.SelectionAnchor = index + 1;
        CommitPageMutation(d, before, afterSelection ? "뒤에 빈 페이지 삽입" : "앞에 빈 페이지 삽입");
        return true;
    }

    /// <summary>Copies selected synthetic content in document order to a new independently editable tab.
    /// The source, its selection and both history stacks are unchanged; no file or PDF is created.</summary>
    public bool ExtractSelectedPages()
    {
        if (Current is not { } d || d.Selected.Count == 0) return false;
        CancelNavigationGesture();
        if (d.Selected.Count == 0) return false;
        SaveScroll();
        var pages = d.Pages.Where((_, i) => d.Selected.Contains(i + 1)).Select(CopyPage).ToArray();
        var extracted = new SampleDocument($"추출 {++_extractionSequence} · {d.Name}", d.Category, pages)
        {
            SavedPages = null, Scale = d.Scale, Fit = d.Fit, Continuous = d.Continuous,
            Page = Math.Max(1, Array.FindIndex(pages, p => p.Identity == d.Pages[d.Page - 1].Identity) + 1)
        };
        extracted.Selected.Clear();
        for (var i = 1; i <= pages.Length; i++) extracted.Selected.Add(i);
        extracted.SelectionAnchor = 1;
        _documents.Add(extracted); SelectDocument(_documents.Count - 1);
        // SelectDocument switches Current before calculating rotation-aware page offsets.
        extracted.VerticalOffset = extracted.Continuous ? PageTop(extracted.Page, extracted.Scale) : 0;
        RestoreScroll(); return true;
    }

    /// <summary>Shared by native right-click and menu inspection. Blank space preserves the group;
    /// a selected card preserves it too; an unselected card becomes the sole selected page.
    /// Management selection never changes the page being viewed.</summary>
    public void PreparePageContextSelection(int? page)
    {
        CancelNavigationGesture();
        if (Current is { } d && page is int n && n >= 1 && n <= d.Pages.Length && !d.Selected.Contains(n))
        { d.Selected.Clear(); d.Selected.Add(n); d.SelectionAnchor = n; }
        UpdateControls();
    }

    public ContextMenu CreatePageContextMenu(int? page = null)
    {
        PreparePageContextSelection(page);
        var d = Current;
        var menu = new ContextMenu();
        AutomationProperties.SetAutomationId(menu, "PageContextMenu");
        AutomationProperties.SetName(menu, "페이지 관리");
        var selected = SelectedPages.ToArray();
        var hasSelection = selected.Length > 0;
        var summary = $"선택 {selected.Length}쪽 ({PageRanges(selected)})";
        menu.Items.Add(PageMenuItem(summary, "PageSelectionSummary", false, null));
        menu.Items.Add(new Separator());
        void Add(string title, string id, bool enabled, Func<bool> action, string shortcut = "", string? tip = null)
        {
            menu.Items.Add(PageMenuItem(title, id, enabled, () => { if (Current == d) action(); }, shortcut, tip));
        }
        Add("오른쪽 90° 회전", "PageRotateClockwise", hasSelection, () => RotateSelectedPages(true));
        Add("왼쪽 90° 회전", "PageRotateCounterclockwise", hasSelection, () => RotateSelectedPages(false));
        var keepOne = d != null && selected.Length == d.Pages.Length;
        Add(keepOne ? "페이지 삭제 (최소 1쪽 유지)" : "페이지 삭제", "PageDelete", CanDeleteSelectedPages,
            DeleteSelectedPages, "Delete", "문서에 최소 1쪽은 남겨야 합니다. 모든 페이지는 삭제할 수 없습니다.");
        menu.Items.Add(new Separator());
        Add("앞에 빈 페이지 삽입", "PageInsertBefore", d != null, () => InsertBlankPage(false), tip: "합성 빈 페이지 1쪽 · 선택이 없으면 현재 보는 쪽 기준");
        Add("뒤에 빈 페이지 삽입", "PageInsertAfter", d != null, () => InsertBlankPage(true), tip: "합성 빈 페이지 1쪽 · 선택이 없으면 현재 보는 쪽 기준");
        Add("선택 페이지 추출 (새 목업 탭)", "PageExtract", hasSelection, ExtractSelectedPages,
            tip: "문서 순서대로 복사 · 원본 유지 · PDF 파일 생성 없음");
        menu.Items.Add(new Separator());
        Add(CanUndo ? "실행 취소 · " + UndoDescription : "실행 취소", "PageUndo", CanUndo, Undo, "Ctrl+Z");
        Add(CanRedo ? "다시 실행 · " + RedoDescription : "다시 실행", "PageRedo", CanRedo, Redo, "Ctrl+Y");
        return menu;
    }

    private static MenuItem PageMenuItem(string header, string id, bool enabled, Action? action,
        string shortcut = "", string? tip = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = shortcut, ToolTip = tip };
        AutomationProperties.SetAutomationId(item, id); AutomationProperties.SetName(item, header);
        if (tip != null) { AutomationProperties.SetHelpText(item, tip); ToolTipService.SetShowOnDisabled(item, true); }
        if (action != null) item.Click += (_, _) => action();
        return item;
    }

    private static string PageRanges(int[] pages)
    {
        if (pages.Length == 0) return "없음";
        var ranges = new List<string>();
        for (var i = 0; i < pages.Length; i++)
        {
            var first = pages[i]; var last = first;
            while (i + 1 < pages.Length && pages[i + 1] == last + 1) last = pages[++i];
            ranges.Add(first == last ? first.ToString(CultureInfo.InvariantCulture) : $"{first}-{last}");
        }
        return string.Join(", ", ranges);
    }

    private void NavigationContextMenu(object sender, MouseButtonEventArgs e)
    {
        if (_bookmarks || Current == null) return;
        _navigation.UpdateLayout();
        var hit = Array.FindIndex(GetThumbnailBounds(), rect => rect.Contains(e.GetPosition(_navigation)));
        var menu = CreatePageContextMenu(hit >= 0 ? hit + 1 : null);
        _navigation.Focus(); menu.PlacementTarget = _navigation; menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true; e.Handled = true;
    }
}
