using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Workspace.Shell.Presentation;

/// <summary>
/// In-memory, synthetic document UI. No PDF parser, filesystem, network or module calls.
/// The host owns its ribbon and keyboard shortcuts; all commands below run on the UI thread.
/// </summary>
public sealed partial class PreviewWorkspace : UserControl
{
    private const double PaperWidth = 700, PaperHeight = 990, PageGap = 20, CanvasInset = 16;
    private static readonly Brush Ink = BrushOf("#293440"), Muted = BrushOf("#697683"),
        Accent = BrushOf("#B85032"), Line = BrushOf("#D5DAE0"), Pale = BrushOf("#F5F7F9");
    private readonly List<SampleDocument> _documents = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _pages = new() { Margin = new Thickness(CanvasInset), HorizontalAlignment = HorizontalAlignment.Center };
    // All thumbnail geometry is measured in this scroll content's coordinates, not viewport coordinates.
    private readonly Grid _navigation = new() { Background = Brushes.Transparent, Focusable = true, ClipToBounds = true };
    private readonly WrapPanel _thumbnailItems = new() { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel _bookmarkItems = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _selectionOverlay = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly System.Windows.Shapes.Rectangle _marquee = new()
    {
        Stroke = Accent, StrokeThickness = 1.5, Fill = BrushOf("#32B85032"),
        Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    private readonly System.Windows.Shapes.Rectangle _insertionMarker = new()
    {
        Fill = Brushes.Red, Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    private readonly DispatcherTimer _reorderTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly List<TextBlock> _thumbnailLabels = new();
    private bool _reorderPending, _reorderDragging, _reorderControl, _reorderShift;
    private int _reorderPage, _reorderAnchor, _insertionSlot = -1;
    private Point _reorderStart;
    private HashSet<int> _reorderBaseline = new();
    private readonly List<Border> _thumbnailPapers = new();
    private readonly Slider _thumbnailSizeSlider = new() { Minimum = 64, Maximum = 220, Value = 112, SmallChange = 8, LargeChange = 24, MinWidth = 50 };
    private readonly TextBlock _thumbnailSizeLabel = Label("축소판 112px", 10);
    private readonly TextBlock _selectionCount = Label("선택 0쪽 · 보는 쪽 0", 10);
    private readonly StackPanel _thumbnailTools = new() { Margin = new Thickness(12, 0, 12, 6) };
    private readonly DispatcherTimer _marqueeTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private HashSet<int> _marqueeBaseline = new();
    private Point _marqueeStart;
    private bool _marqueePending, _marqueeDragging, _marqueeControl, _marqueeAdditive, _updatingThumbnailLayout;
    private double _thumbnailSize = 112;
    private readonly List<Border> _paperVisuals = new();
    private readonly List<Button> _navigationButtons = new();
    private readonly List<Button> _documentCommands = new();
    private readonly ScrollViewer _canvas = new()
    {
        Background = BrushOf("#747B83"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false,
        PanningMode = PanningMode.Both, Focusable = true
    };
    private readonly ScrollViewer _navigationScroll = new()
    { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, Focusable = false };
    private readonly ColumnDefinition _sidebarColumn = new() { Width = new GridLength(190), MinWidth = 170, MaxWidth = 600 };
    private readonly ColumnDefinition _splitterColumn = new() { Width = new GridLength(5) };
    private readonly Border _sidebar = new() { Background = Pale, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 0) };
    private readonly GridSplitter _splitter = new()
    {
        Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        Background = BrushOf("#D4D9DE"), ToolTip = "드래그하여 탐색 패널 너비 조절"
    };
    private readonly TextBlock _navigationTitle = Label("페이지 축소판", 12, true);
    private readonly TextBox _pageInput = new()
    {
        Width = 42, Height = 28, Margin = new Thickness(3, 0, 2, 0), Padding = new Thickness(3),
        TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        ToolTip = "페이지 번호 입력 후 Enter", FontSize = 12
    };
    private readonly TextBlock _pageTotal = Label("/ 0"), _zoom = Label("100%"), _status = Label("목업 · 실제 PDF 아님", 11);
    private readonly Button _singleButton, _continuousButton, _previousButton, _nextButton, _firstButton, _lastButton;
    private readonly Button _thumbnailRail, _bookmarkRail;
    private int _active = -1, _viewRevision;
    private bool _navigationVisible = true, _bookmarks, _restoring, _fitQueued;
    private double _savedSidebarWidth = 190;
    private SampleDocument? Current => _active >= 0 && _active < _documents.Count ? _documents[_active] : null;

    public int DocumentCount => _documents.Count;
    public int PageNumber => Current?.Page ?? 0;
    public int PageCount => Current?.Pages.Length ?? 0;
    public int ZoomPercent => Current is { } d ? (int)Math.Round(d.Scale * 100) : 100;
    public bool Continuous => Current?.Continuous ?? false;
    public bool NavigationVisible => _navigationVisible;
    public string ActiveDocumentName => Current?.Name ?? string.Empty;
    /// <summary>Sorted, one-based selected pages in the active document. Independent of PageNumber.</summary>
    public IReadOnlyList<int> SelectedPages => Current?.Selected.OrderBy(n => n).ToArray() ?? Array.Empty<int>();
    /// <summary>Preferred paper width in device-independent pixels; narrow sidebars constrain the actual size.</summary>
    public double ThumbnailSize => _thumbnailSize;
    public double NavigationViewportWidth => _navigationScroll.ViewportWidth;
    /// <summary>
    /// Actual arranged button bounds in the navigation scroll CONTENT coordinates (DIPs), indexed by page - 1.
    /// Includes each card's border/caption, excludes its outer gutter. Does not subtract scroll offsets.
    /// Empty when bookmarks are shown or no document is open. Measure/arrange the workspace before inspecting.
    /// </summary>
    public IReadOnlyList<Rect> ThumbnailCardBounds
    {
        get
        {
            _navigation.UpdateLayout();
            return GetThumbnailBounds();
        }
    }
    /// <summary>Stable synthetic content identities in current document order, independent of page numbers.</summary>
    public IReadOnlyList<string> PageIdentityOrder => Current?.Pages.Select(p => p.Identity).ToArray() ?? Array.Empty<string>();
    /// <summary>Actual visible thumbnail captions; empty while bookmarks are shown.</summary>
    public IReadOnlyList<string> ThumbnailPageLabels => _thumbnailLabels.Select(label => label.Text).ToArray();
    public int ThumbnailInsertionSlot => _insertionSlot;
    /// <summary>Actual red marker bounds in navigation content coordinates, or Rect.Empty when hidden.</summary>
    public Rect ThumbnailInsertionMarkerBounds => _insertionMarker.Visibility == Visibility.Visible
        ? new Rect(Canvas.GetLeft(_insertionMarker), Canvas.GetTop(_insertionMarker), _insertionMarker.Width, _insertionMarker.Height)
        : Rect.Empty;
    public event Action? Empty;

    public PreviewWorkspace()
    {
        FontFamily = new FontFamily("Malgun Gothic");
        FontSize = 12;
        Foreground = Ink;
        Background = Pale;
        MinHeight = 180;
        ClipToBounds = true;
        // Local control styles deliberately do not inherit host ribbon button sizing.
        Resources.Add(typeof(Button), new Style(typeof(Button))
        {
            Setters =
            {
                new Setter(Control.FontSizeProperty, 12d), new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
                new Setter(FrameworkElement.MarginProperty, new Thickness(2)), new Setter(Control.MinHeightProperty, 28d),
                new Setter(Control.BackgroundProperty, Brushes.Transparent), new Setter(Control.BorderBrushProperty, Line),
                new Setter(Control.ForegroundProperty, Ink), new Setter(Control.BorderThicknessProperty, new Thickness(1)),
                new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(FrameworkElement.CursorProperty, Cursors.Hand)
            }
        });
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tabScroller = new ScrollViewer
        {
            Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = BrushOf("#E6E9ED"), MaxHeight = 58
        };
        root.Children.Add(tabScroller);
        var body = new Grid();
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        body.ColumnDefinitions.Add(_sidebarColumn);
        body.ColumnDefinitions.Add(_splitterColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
        var rail = new StackPanel { Margin = new Thickness(1, 4, 1, 0) };
        _thumbnailRail = Button("쪽", "페이지 축소판", ShowThumbnails);
        _bookmarkRail = Button("목차", "책갈피", ShowBookmarks);
        foreach (var b in new[] { _thumbnailRail, _bookmarkRail })
        { b.Width = 34; b.Height = 34; b.Padding = new Thickness(0); rail.Children.Add(b); }
        var collapse = Button("‹", "탐색 패널 표시 / 숨기기", ToggleNavigation);
        collapse.Width = 34;
        rail.Children.Add(collapse);
        body.Children.Add(new ScrollViewer
        {
            Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = BrushOf("#ECEFF2")
        });
        var sideDock = new DockPanel();
        var sideHead = new DockPanel { Margin = new Thickness(10, 5, 4, 5) };
        var hide = Button("×", "탐색 패널 접기", ToggleNavigation);
        hide.Width = 28; hide.Padding = new Thickness(0); hide.BorderThickness = new Thickness(0);
        DockPanel.SetDock(hide, Dock.Right); sideHead.Children.Add(hide);
        sideHead.Children.Add(_navigationTitle);
        DockPanel.SetDock(sideHead, Dock.Top); sideDock.Children.Add(sideHead);
        _thumbnailTools.Children.Add(_thumbnailSizeLabel);
        _thumbnailTools.Children.Add(_thumbnailSizeSlider);
        _selectionCount.Margin = new Thickness(0, 4, 0, 0);
        _thumbnailTools.Children.Add(_selectionCount);
        DockPanel.SetDock(_thumbnailTools, Dock.Top); sideDock.Children.Add(_thumbnailTools);
        _navigation.Children.Add(_thumbnailItems); _navigation.Children.Add(_bookmarkItems);
        _selectionOverlay.Children.Add(_marquee); _selectionOverlay.Children.Add(_insertionMarker); _navigation.Children.Add(_selectionOverlay);
        _navigationScroll.Content = _navigation;
        sideDock.Children.Add(_navigationScroll); _sidebar.Child = sideDock;
        Grid.SetColumn(_sidebar, 1); body.Children.Add(_sidebar);
        Grid.SetColumn(_splitter, 2); body.Children.Add(_splitter);
        _canvas.Content = _pages; Grid.SetColumn(_canvas, 3); body.Children.Add(_canvas);
        root.Children.Add(body);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 3, 5, 3) };
        _firstButton = CommandButton("|‹", "첫 페이지", () => GoToPage(1));
        _previousButton = CommandButton("‹", "이전 페이지", PreviousPage);
        _nextButton = CommandButton("›", "다음 페이지", NextPage);
        _lastButton = CommandButton("›|", "마지막 페이지", () => GoToPage(PageCount));
        footer.Children.Add(_firstButton); footer.Children.Add(_previousButton);
        footer.Children.Add(_pageInput); _pageTotal.Margin = new Thickness(3, 0, 8, 0); footer.Children.Add(_pageTotal);
        footer.Children.Add(_nextButton); footer.Children.Add(_lastButton);
        AddSeparator(footer);
        _singleButton = CommandButton("단일", "한 페이지씩 보기", () => SetContinuous(false));
        _continuousButton = CommandButton("연속", "모든 페이지를 이어서 스크롤", () => SetContinuous(true));
        footer.Children.Add(_singleButton); footer.Children.Add(_continuousButton);
        AddSeparator(footer);
        footer.Children.Add(CommandButton("−", "축소 (Ctrl + 마우스 휠)", ZoomOut));
        _zoom.Width = 48; _zoom.TextAlignment = TextAlignment.Center; footer.Children.Add(_zoom);
        footer.Children.Add(CommandButton("+", "확대 (Ctrl + 마우스 휠)", ZoomIn));
        footer.Children.Add(CommandButton("페이지 맞춤", "현재 화면에 페이지 전체 맞춤", FitPage));
        footer.Children.Add(CommandButton("너비 맞춤", "화면 너비에 맞춤", FitWidth));
        AddSeparator(footer);
        _status.Foreground = Muted; _status.Margin = new Thickness(8, 0, 10, 0); footer.Children.Add(_status);
        var footerScroller = new ScrollViewer
        {
            Content = footer, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 58,
            Background = BrushOf("#F4F5F7"), ToolTip = "좁은 창에서는 아래 가로 스크롤로 모든 보기 도구를 사용할 수 있습니다."
        };
        Grid.SetRow(footerScroller, 2); root.Children.Add(footerScroller);
        Content = root;
        AutomationProperties.SetName(this, "합성 문서 목업 작업 공간");
        AutomationProperties.SetName(_pageInput, "페이지 번호");
        AutomationProperties.SetName(_canvas, "목업 문서 페이지 보기");
        _navigation.Name = "ThumbnailSelectionSurface";
        AutomationProperties.SetName(_navigation, "축소판 선택 영역 · 빈 곳 드래그로 여러 쪽 선택");
        AutomationProperties.SetAutomationId(_navigation, "ThumbnailSelectionSurface");
        AutomationProperties.SetAutomationId(_navigationScroll, "NavigationScroll");
        AutomationProperties.SetAutomationId(_thumbnailSizeSlider, "ThumbnailSizeSlider");
        AutomationProperties.SetName(_thumbnailSizeSlider, "축소판 크기");
        AutomationProperties.SetAutomationId(_selectionCount, "ThumbnailSelectionCount");
        AutomationProperties.SetAutomationId(_thumbnailSizeLabel, "ThumbnailSizeLabel");
        AutomationProperties.SetAutomationId(_marquee, "ThumbnailMarquee");
        AutomationProperties.SetName(_marquee, "축소판 드래그 선택 범위");
        AutomationProperties.SetName(_splitter, "탐색 패널 너비 조절");
        AutomationProperties.SetAutomationId(_splitter, "NavigationSplitter");
        _thumbnailSizeSlider.ValueChanged += (_, e) => SetThumbnailSize(e.NewValue);
        _navigation.PreviewMouseLeftButtonDown += NavigationMouseDown;
        _navigation.PreviewMouseRightButtonUp += NavigationContextMenu;
        AutomationProperties.SetAutomationId(_insertionMarker, "ThumbnailInsertionMarker");
        AutomationProperties.SetName(_insertionMarker, "페이지 이동 삽입 위치");
        _navigation.PreviewMouseMove += (_, _) => { UpdateMarquee(); UpdateThumbnailDrag(); };
        _navigation.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_reorderPending) { FinishThumbnailDrag(e); return; }
            if (!_marqueePending) return;
            UpdateMarquee();
            if (!_marqueeDragging && Current is { } d) { d.Selected.Clear(); d.SelectionAnchor = 0; }
            EndMarquee(false); UpdateControls(); e.Handled = true;
        };
        _navigation.LostMouseCapture += (_, _) => CancelNavigationGesture();
        _navigation.PreviewKeyDown += NavigationKeyDown;
        _navigationScroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
            { CancelNavigationGesture(); UpdateThumbnailLayout(); }
            if (_reorderDragging && e.VerticalChange != 0) UpdateThumbnailDrag();
            if (_marqueePending && e.VerticalChange != 0) UpdateMarquee();
        };
        _navigationScroll.SizeChanged += (_, _) => { CancelNavigationGesture(); UpdateThumbnailLayout(); };
        _reorderTimer.Tick += (_, _) => AutoScrollThumbnailDrag();
        _marqueeTimer.Tick += (_, _) => AutoScrollMarquee();
        Unloaded += (_, _) => CancelNavigationGesture();
        _pageInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (int.TryParse(_pageInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= PageCount)
            { GoToPage(n); _pageInput.ToolTip = "페이지 번호 입력 후 Enter"; }
            else { _pageInput.Text = PageNumber.ToString(CultureInfo.InvariantCulture); _pageInput.ToolTip = $"1 ~ {PageCount} 사이의 페이지를 입력하세요."; }
            _pageInput.SelectAll(); e.Handled = true;
        };
        _pageInput.GotKeyboardFocus += (_, _) => _pageInput.SelectAll();
        _pageInput.LostKeyboardFocus += (_, _) => _pageInput.Text = PageNumber.ToString(CultureInfo.InvariantCulture);
        _canvas.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            { if (e.Delta > 0) ZoomIn(); else if (e.Delta < 0) ZoomOut(); e.Handled = true; }
            else
            { _canvas.ScrollToVerticalOffset(_canvas.VerticalOffset - e.Delta); e.Handled = true; }
        };
        _canvas.ScrollChanged += CanvasScrolled;
        _canvas.SizeChanged += (_, _) => QueueFit();
        SizeChanged += (_, _) =>
        {
            CancelNavigationGesture();
            _sidebarColumn.MaxWidth = Math.Max(170, Math.Min(600, ActualWidth * .55));
            QueueFit();
        };
        _splitter.DragCompleted += (_, _) => { _savedSidebarWidth = _sidebarColumn.ActualWidth; QueueFit(); };
        Loaded += (_, _) => { UpdateThumbnailLayout(); QueueFit(); };
        UpdateControls();
    }

    public void OpenSamples()
    {
        if (_documents.Count != 0) return;
        _documents.AddRange(CreateSamples());
        SelectDocument(0);
    }

    public void SelectDocument(int index)
    {
        if (index < 0 || index >= _documents.Count || index == _active) return;
        CancelNavigationGesture(); SaveScroll(); _active = index;
        RebuildTabs(); RebuildNavigation(); RebuildPages(); UpdateControls(); QueueFit();
    }

    public void CloseCurrent() { if (_active >= 0) CloseDocument(_active); }

    private void CloseDocument(int index)
    {
        if (_confirmingClose) { _closeReentered = true; return; }
        if (index < 0 || index >= _documents.Count) return;
        var closing = _documents[index];
        if (DocumentDirty(closing))
        {
            var decision = AskClose(false, new[] { closing });
            if (decision == SampleCloseDecision.Cancel) return;
            if (decision == SampleCloseDecision.Save) Checkpoint(closing);
        }
        if (closing.SavedPages is { } saved)
            _savedClosedSamples.Add(new SavedSample(closing.Name, closing.Category, saved.Select(CopyPage).ToArray()));
        CancelNavigationGesture(); SaveScroll();
        var selected = Current;
        _documents.RemoveAt(index);
        if (_documents.Count == 0)
        {
            _active = -1; ++_viewRevision; _restoring = false;
            _tabs.Children.Clear(); _pages.Children.Clear(); _paperVisuals.Clear(); _thumbnailItems.Children.Clear(); _bookmarkItems.Children.Clear(); _thumbnailPapers.Clear(); _thumbnailLabels.Clear(); _navigationButtons.Clear();
            UpdateControls(); Empty?.Invoke(); return;
        }
        _active = selected != null && _documents.Contains(selected) ? _documents.IndexOf(selected) : Math.Min(index, _documents.Count - 1);
        RebuildTabs(); RebuildNavigation(); RebuildPages(); UpdateControls(); QueueFit();
    }

    public void ToggleNavigation()
    {
        CancelNavigationGesture();
        if (_navigationVisible) _savedSidebarWidth = _sidebarColumn.ActualWidth;
        _navigationVisible = !_navigationVisible;
        ApplyNavigationVisibility();
    }
    /// <summary>Restore navigation layout and fit the current page without changing selection.</summary>
    public void ResetLayout()
    {
        CancelNavigationGesture();
        _savedSidebarWidth = 190; _navigationVisible = true; _bookmarks = false;
        ApplyNavigationVisibility(); RebuildNavigation(); FitPage();
    }
    public void ShowThumbnails() => ShowNavigation(false);
    public void ShowBookmarks() => ShowNavigation(true);
    private void ShowNavigation(bool bookmarks)
    {
        CancelNavigationGesture(); _bookmarks = bookmarks; _navigationVisible = true;
        ApplyNavigationVisibility(); RebuildNavigation();
    }
    private void ApplyNavigationVisibility()
    {
        _sidebar.Visibility = _splitter.Visibility = _navigationVisible ? Visibility.Visible : Visibility.Collapsed;
        _sidebarColumn.MinWidth = _navigationVisible ? 170 : 0;
        _sidebarColumn.Width = new GridLength(_navigationVisible ? Math.Clamp(_savedSidebarWidth, 170, _sidebarColumn.MaxWidth) : 0);
        _splitterColumn.Width = new GridLength(_navigationVisible ? 5 : 0);
        UpdateControls(); QueueFit();
    }

    public void FitPage()
    {
        if (Current is not { } d) return;
        d.Fit = FitKind.Page; ApplyFit();
        d.HorizontalOffset = 0; d.VerticalOffset = d.Continuous ? PageTop(d.Page, d.Scale) : 0;
        RestoreScroll();
    }
    public void FitWidth() { if (Current is { } d) { d.Fit = FitKind.Width; ApplyFit(); } }
    public void ZoomIn() => ZoomBy(10);
    public void ZoomOut() => ZoomBy(-10);
    private void ZoomBy(int delta)
    {
        if (Current is not { } d) return;
        d.Fit = FitKind.None;
        SetScale(Math.Clamp(ZoomPercent + delta, 5, 400) / 100d);
    }
    public void SetContinuous(bool continuous)
    {
        if (Current is not { } d || d.Continuous == continuous) return;
        d.Continuous = continuous; d.VerticalOffset = continuous ? PageTop(d.Page, d.Scale) : 0;
        RebuildPages(); UpdateControls(); QueueFit();
    }
    public void NextPage() => GoToPage(PageNumber + 1);
    public void PreviousPage() => GoToPage(PageNumber - 1);
    public void GoToPage(int page)
    {
        if (Current is not { } d) return;
        d.Page = Math.Clamp(page, 1, d.Pages.Length);
        d.VerticalOffset = d.Continuous ? PageTop(d.Page, d.Scale) : 0;
        if (d.Continuous) RestoreScroll(); else RebuildPages();
        UpdateControls(); QueueFit();
        if (_navigationButtons.Count >= d.Page) _navigationButtons[d.Page - 1].BringIntoView();
    }

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        for (var i = 0; i < _documents.Count; i++)
        {
            var index = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var dirty = DocumentDirty(_documents[i]);
            var description = _documents[i].Name + (dirty ? " · 저장하지 않은 샘플 변경" : " · 샘플 메모리 기준 상태") + " · 실제 파일 아님 · 앱 종료 시 사라짐";
            var choose = Button(_documents[i].Name + (dirty ? " *" : ""), description, () => SelectDocument(index));
            AutomationProperties.SetAutomationId(choose, "SampleDocumentTab" + index);
            AutomationProperties.SetName(choose, description);
            AutomationProperties.SetHelpText(choose, description);
            choose.BorderThickness = new Thickness(0); choose.Margin = new Thickness(0); choose.Padding = new Thickness(13, 8, 6, 8);
            choose.FontWeight = i == _active ? FontWeights.SemiBold : FontWeights.Normal;
            var close = Button("×", _documents[i].Name + " 닫기", () => CloseDocument(index));
            close.BorderThickness = new Thickness(0); close.Width = 28; close.Padding = new Thickness(0);
            row.Children.Add(choose); row.Children.Add(close);
            _tabs.Children.Add(new Border
            {
                Child = row, Margin = new Thickness(0, 0, 1, 0),
                Background = i == _active ? Brushes.White : BrushOf("#E6E9ED"),
                BorderBrush = i == _active ? Accent : Line, BorderThickness = new Thickness(0, 0, 0, i == _active ? 3 : 1)
            });
        }
    }

    private void RebuildNavigation()
    {
        CancelNavigationGesture();
        _thumbnailItems.Children.Clear(); _bookmarkItems.Children.Clear(); _navigationButtons.Clear(); _thumbnailPapers.Clear(); _thumbnailLabels.Clear();
        _navigationTitle.Text = _bookmarks ? "책갈피" : "페이지 축소판";
        _thumbnailItems.Visibility = _thumbnailTools.Visibility = _bookmarks ? Visibility.Collapsed : Visibility.Visible;
        _bookmarkItems.Visibility = _bookmarks ? Visibility.Visible : Visibility.Collapsed;
        if (Current is not { } d) return;
        if (_bookmarks)
            _bookmarkItems.Children.Add(new TextBlock { Text = "목업 보고서 목차", Margin = new Thickness(12, 8, 8, 12), Foreground = Muted });
        for (var i = 0; i < d.Pages.Length; i++)
        {
            var number = i + 1;
            var item = Button("", _bookmarks ? $"{number}쪽 · {d.Pages[i].Title}" : number.ToString(CultureInfo.InvariantCulture), () =>
            {
                if (_bookmarks) GoToPage(number);
                else SelectThumbnail(number, (Keyboard.Modifiers & ModifierKeys.Control) != 0,
                    (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            });
            item.Name = (_bookmarks ? "Bookmark" : "Thumbnail") + number;
            AutomationProperties.SetAutomationId(item, item.Name);
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            item.HorizontalAlignment = HorizontalAlignment.Left;
            item.Margin = _bookmarks ? new Thickness(9, 3, 9, 3) : new Thickness(6, 6, 6, 10);
            item.Padding = new Thickness(7);
            // Fixed border thickness keeps selection from changing the hit-test geometry.
            item.BorderThickness = new Thickness(2);
            item.ClipToBounds = true;
            var content = new StackPanel();
            if (!_bookmarks)
            {
                var thumbnail = BuildPaper(d, i);
                thumbnail.HorizontalAlignment = HorizontalAlignment.Center;
                thumbnail.IsHitTestVisible = false;
                thumbnail.ClipToBounds = true;
                _thumbnailPapers.Add(thumbnail); content.Children.Add(thumbnail);
            }
            var caption = Label(_bookmarks ? $"{number:00}  {d.Pages[i].Title}" : number.ToString(CultureInfo.InvariantCulture), 11);
            if (!_bookmarks) _thumbnailLabels.Add(caption);
            caption.TextWrapping = TextWrapping.Wrap;
            caption.Margin = new Thickness(0, _bookmarks ? 3 : 7, 0, 3);
            caption.TextAlignment = _bookmarks ? TextAlignment.Left : TextAlignment.Center;
            content.Children.Add(caption); item.Content = content;
            if (_bookmarks) _bookmarkItems.Children.Add(item); else _thumbnailItems.Children.Add(item);
            _navigationButtons.Add(item);
        }
        UpdateThumbnailLayout(); UpdateControls();
    }

    public void SetThumbnailSize(double size)
    {
        if (!double.IsFinite(size)) throw new ArgumentOutOfRangeException(nameof(size));
        CancelNavigationGesture();
        _thumbnailSize = Math.Clamp(size, 64, 220);
        if (_thumbnailSizeSlider.Value != _thumbnailSize) _thumbnailSizeSlider.Value = _thumbnailSize;
        UpdateThumbnailLayout();
    }

    /// <summary>Uses the same column and responsive layout as dragging NavigationSplitter.</summary>
    public void SetNavigationWidth(double width)
    {
        if (!double.IsFinite(width)) throw new ArgumentOutOfRangeException(nameof(width));
        CancelNavigationGesture();
        _savedSidebarWidth = Math.Clamp(width, 170, _sidebarColumn.MaxWidth);
        ApplyNavigationVisibility(); UpdateLayout(); UpdateThumbnailLayout(); UpdateLayout();
    }

    private void UpdateThumbnailLayout()
    {
        if (_updatingThumbnailLayout) return;
        _updatingThumbnailLayout = true;
        try
        {
            var viewport = _navigationScroll.ViewportWidth;
            if (!double.IsFinite(viewport) || viewport <= 0)
                viewport = Math.Max(1, _savedSidebarWidth - SystemParameters.VerticalScrollBarWidth - 1);
            _navigation.MinHeight = Math.Max(0, _navigationScroll.ViewportHeight);
            // Keep outer gutters and inter-card margins even when a row contains only one card.
            // Expand up to 20% within a column as the sidebar grows; wrap when another preferred card fits.
            var available = Math.Max(1, viewport - 24);
            var columns = Math.Max(1, (int)Math.Floor(available / (_thumbnailSize + 30)));
            var paperWidth = Math.Max(1, Math.Min(_thumbnailSize * 1.2, available / columns - 30));
            for (var i = 0; i < _thumbnailPapers.Count; i++)
            {
                _navigationButtons[i].Width = paperWidth + 18;
                var data = Current!.Pages[i];
                var scale = paperWidth / RotatedWidth(data);
                _thumbnailPapers[i].LayoutTransform = PaperTransform(data, scale);
            }
            if (_bookmarks)
                foreach (var button in _navigationButtons) button.Width = Math.Max(1, viewport - 18);
            _thumbnailSizeLabel.Text = $"축소판 {Math.Round(paperWidth)}px";
            _thumbnailSizeSlider.ToolTip = $"선호 크기 {Math.Round(_thumbnailSize)}px · 패널 너비에 자동 맞춤";
        }
        finally { _updatingThumbnailLayout = false; }
    }

    /// <summary>One-based page click. Ctrl toggles; Shift selects from the last unmodified/Ctrl anchor.
    /// Ctrl+Shift unions that range. Only unmodified clicks navigate; modified clicks keep the viewed page.</summary>
    public void SelectThumbnail(int page, bool control = false, bool shift = false)
    {
        if (Current is not { } d || page < 1 || page > d.Pages.Length) return;
        CancelNavigationGesture();
        if (shift)
        {
            var anchor = d.SelectionAnchor > 0 ? d.SelectionAnchor : d.Page;
            if (!control) d.Selected.Clear();
            for (var n = Math.Min(anchor, page); n <= Math.Max(anchor, page); n++) d.Selected.Add(n);
            d.SelectionAnchor = anchor;
        }
        else if (control)
        {
            if (!d.Selected.Add(page)) d.Selected.Remove(page);
            d.SelectionAnchor = page;
        }
        else
        {
            d.Selected.Clear(); d.Selected.Add(page); d.SelectionAnchor = page;
            GoToPage(page);
        }
        UpdateControls();
    }

    /// <summary>Selects cards intersecting a content-coordinate rectangle, using the actual arranged card bounds.
    /// Additive means union with the existing selection. Never changes the viewed page.</summary>
    public void SelectThumbnailRegion(Rect region, bool additive = false)
    {
        if (_bookmarks || Current is not { } d) return;
        CancelNavigationGesture(); _navigation.UpdateLayout();
        ApplyThumbnailRegion(region, additive ? new HashSet<int>(d.Selected) : new HashSet<int>(), false);
    }

    private Rect[] GetThumbnailBounds()
    {
        if (_bookmarks) return Array.Empty<Rect>();
        return _navigationButtons.Select(button => button.TransformToAncestor(_navigation)
            .TransformBounds(new Rect(new Point(), button.RenderSize))).ToArray();
    }

    private void ApplyThumbnailRegion(Rect region, HashSet<int> baseline, bool toggle)
    {
        if (Current is not { } d) return;
        d.Selected.Clear(); d.Selected.UnionWith(baseline);
        var bounds = GetThumbnailBounds();
        if (!region.IsEmpty && region.Width > 0 && region.Height > 0)
            for (var i = 0; i < bounds.Length; i++)
            {
                if (bounds[i].Width <= 0 || bounds[i].Height <= 0 || !region.IntersectsWith(bounds[i])) continue;
                if (toggle && baseline.Contains(i + 1)) d.Selected.Remove(i + 1);
                else d.Selected.Add(i + 1);
            }
        UpdateControls();
    }

    /// <summary>Original-order gap 0..PageCount in navigation CONTENT coordinates; -1 outside the content.</summary>
    public int GetThumbnailInsertionSlot(Point contentPoint)
    {
        if (_bookmarks || !_navigationVisible || Current == null) return -1;
        _navigation.UpdateLayout();
        return ThumbnailReorder.FindSlot(GetThumbnailBounds(), _navigation.RenderSize, contentPoint);
    }

    /// <summary>Displays the same red insertion marker used by native dragging. Does not change selection/order.</summary>
    public bool PreviewInsertionSlot(int insertionSlot)
    {
        if (_bookmarks || !_navigationVisible || Current == null) { ClearInsertionSlot(); return false; }
        _navigation.UpdateLayout();
        return ShowInsertionMarker(insertionSlot, ThumbnailReorder.Marker(GetThumbnailBounds(), insertionSlot));
    }

    /// <summary>Native drag and inspection share card-relative targeting, including wrapped row boundaries.</summary>
    public bool PreviewInsertionPoint(Point contentPoint)
    {
        if (_bookmarks || !_navigationVisible || Current == null) { ClearInsertionSlot(); return false; }
        _navigation.UpdateLayout();
        var cards = GetThumbnailBounds();
        var target = ThumbnailReorder.Resolve(cards, _navigation.RenderSize, contentPoint);
        return ShowInsertionMarker(target.Slot, ThumbnailReorder.Marker(cards, target));
    }

    private bool ShowInsertionMarker(int slot, Rect rect)
    {
        if (rect.IsEmpty) { ClearInsertionSlot(); return false; }
        _insertionSlot = slot;
        Canvas.SetLeft(_insertionMarker, rect.Left); Canvas.SetTop(_insertionMarker, rect.Top);
        _insertionMarker.Width = rect.Width; _insertionMarker.Height = rect.Height;
        _insertionMarker.Visibility = Visibility.Visible;
        return true;
    }

    public void ClearInsertionSlot()
    {
        _insertionSlot = -1; _insertionMarker.Visibility = Visibility.Collapsed;
    }

    /// <summary>Moves selected content to an original-order gap. Preserves group order, selection identities,
    /// anchor and viewed content. Returns false for invalid gaps, empty selection and unchanged order.</summary>
    public bool MoveSelectedPages(int insertionSlot)
    {
        if (Current is not { } d || insertionSlot < 0 || insertionSlot > d.Pages.Length || d.Selected.Count == 0) return false;
        var selectedIds = d.Selected.Select(n => d.Pages[n - 1].Identity).ToHashSet();
        var moving = d.Pages.Where(p => selectedIds.Contains(p.Identity)).ToArray();
        var remaining = d.Pages.Where(p => !selectedIds.Contains(p.Identity)).ToList();
        var target = insertionSlot - d.Selected.Count(n => n <= insertionSlot);
        remaining.InsertRange(target, moving);
        if (!remaining.Where((p, i) => !ReferenceEquals(p, d.Pages[i])).Any()) return false;
        EndThumbnailDrag(false); EndMarquee(false);
        var before = CapturePageState(d);
        RestorePageState(d, before with { Pages = remaining.ToArray() });
        CommitPageMutation(d, before, "페이지 이동");
        return true;
    }

    private void CancelNavigationGesture()
    {
        EndThumbnailDrag(true); EndMarquee(true); ClearInsertionSlot();
    }

    private bool PointerInsideNavigation(Point point) => _navigationVisible && !_bookmarks &&
        point.X >= 0 && point.Y >= 0 && point.X < _navigationScroll.ViewportWidth && point.Y < _navigationScroll.ViewportHeight;

    private void UpdateThumbnailDrag()
    {
        if (!_reorderPending || Current is not { } d) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) { EndThumbnailDrag(true); return; }
        var viewportPoint = Mouse.GetPosition(_navigationScroll);
        if (!_reorderDragging)
        {
            if (Math.Abs(viewportPoint.X - _reorderStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(viewportPoint.Y - _reorderStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _reorderDragging = true;
            // An already-selected card carries its entire group, regardless of current modifiers.
            if (!d.Selected.Contains(_reorderPage))
            { d.Selected.Clear(); d.Selected.Add(_reorderPage); d.SelectionAnchor = _reorderPage; UpdateControls(); }
            _reorderTimer.Start();
        }
        if (!PointerInsideNavigation(viewportPoint)) { ClearInsertionSlot(); return; }
        // WPF transforms include the ScrollContentPresenter's current scroll offset.
        PreviewInsertionPoint(Mouse.GetPosition(_navigation));
    }

    private void FinishThumbnailDrag(MouseButtonEventArgs e)
    {
        var wasDragging = _reorderDragging;
        var page = _reorderPage; var control = _reorderControl; var shift = _reorderShift;
        var inside = PointerInsideNavigation(e.GetPosition(_navigationScroll));
        var slot = wasDragging && inside ? GetThumbnailInsertionSlot(e.GetPosition(_navigation)) : -1;
        var releasedOnCard = !wasDragging && inside && GetThumbnailBounds()[page - 1].Contains(e.GetPosition(_navigation));
        EndThumbnailDrag(wasDragging && slot < 0);
        if (wasDragging && slot >= 0) MoveSelectedPages(slot);
        else if (releasedOnCard) SelectThumbnail(page, control, shift);
        e.Handled = true;
    }

    private void AutoScrollThumbnailDrag()
    {
        if (!_reorderDragging) return;
        var point = Mouse.GetPosition(_navigationScroll);
        if (!PointerInsideNavigation(point)) { ClearInsertionSlot(); return; }
        const double edge = 28;
        var delta = point.Y < edge ? -Math.Min(28, edge - point.Y) :
            point.Y > _navigationScroll.ViewportHeight - edge ? Math.Min(28, point.Y - _navigationScroll.ViewportHeight + edge) : 0;
        if (delta != 0)
        { _navigationScroll.ScrollToVerticalOffset(_navigationScroll.VerticalOffset + delta); _navigationScroll.UpdateLayout(); }
        UpdateThumbnailDrag();
    }

    private void EndThumbnailDrag(bool cancel)
    {
        var pending = _reorderPending;
        _reorderPending = _reorderDragging = false; _reorderTimer.Stop(); ClearInsertionSlot();
        if (!pending) return;
        if (cancel && Current is { } d)
        { d.Selected.Clear(); d.Selected.UnionWith(_reorderBaseline); d.SelectionAnchor = _reorderAnchor; }
        if (Mouse.Captured == _navigation) Mouse.Capture(null);
        UpdateControls();
    }

    private void NavigationMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_bookmarks || Current == null || e.ChangedButton != MouseButton.Left) return;
        CancelNavigationGesture();
        var point = e.GetPosition(_navigation);
        var hit = Array.FindIndex(GetThumbnailBounds(), rect => rect.Contains(point));
        if (hit >= 0)
        {
            _navigation.Focus();
            _reorderPage = hit + 1; _reorderStart = e.GetPosition(_navigationScroll);
            _reorderBaseline = new HashSet<int>(Current.Selected); _reorderAnchor = Current.SelectionAnchor;
            _reorderControl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _reorderShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            _reorderPending = true; _reorderDragging = false;
            if (!Mouse.Capture(_navigation, CaptureMode.Element)) { _reorderPending = false; return; }
            // Suppress Button's mouse-down selection/capture. A non-drag click is replayed on mouse-up.
            e.Handled = true; return;
        }
        _navigation.Focus();
        _marqueeStart = point;
        _marqueeBaseline = new HashSet<int>(Current.Selected);
        _marqueeControl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _marqueeAdditive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _marqueePending = true; _marqueeDragging = false;
        if (!Mouse.Capture(_navigation, CaptureMode.Element)) { _marqueePending = false; return; }
        _marqueeTimer.Start(); e.Handled = true;
    }

    private void UpdateMarquee()
    {
        if (!_marqueePending || Current == null) return;
        var point = Mouse.GetPosition(_navigation);
        if (!_marqueeDragging && Math.Abs(point.X - _marqueeStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _marqueeStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _marqueeDragging = true;
        point.X = Math.Clamp(point.X, 0, Math.Max(0, _navigation.ActualWidth));
        point.Y = Math.Clamp(point.Y, 0, Math.Max(0, _navigation.ActualHeight));
        var region = new Rect(_marqueeStart, point);
        Canvas.SetLeft(_marquee, region.Left); Canvas.SetTop(_marquee, region.Top);
        _marquee.Width = region.Width; _marquee.Height = region.Height; _marquee.Visibility = Visibility.Visible;
        // Reapply against the drag-start snapshot, so crossing a card repeatedly never re-toggles it.
        ApplyThumbnailRegion(region, _marqueeControl || _marqueeAdditive ? _marqueeBaseline : new HashSet<int>(), _marqueeControl);
    }

    private void AutoScrollMarquee()
    {
        if (!_marqueePending || !_marqueeDragging) return;
        var point = Mouse.GetPosition(_navigationScroll);
        var height = _navigationScroll.ViewportHeight;
        const double edge = 28;
        var delta = point.Y < edge ? -Math.Min(28, edge - point.Y) :
            point.Y > height - edge ? Math.Min(28, point.Y - height + edge) : 0;
        if (delta == 0) return;
        _navigationScroll.ScrollToVerticalOffset(_navigationScroll.VerticalOffset + delta);
        _navigationScroll.UpdateLayout(); UpdateMarquee();
    }

    private void EndMarquee(bool cancel)
    {
        if (!_marqueePending) return;
        _marqueePending = false; _marqueeDragging = false;
        _marqueeTimer.Stop(); _marquee.Visibility = Visibility.Collapsed;
        if (cancel && Current is { } d) { d.Selected.Clear(); d.Selected.UnionWith(_marqueeBaseline); }
        if (Mouse.Captured == _navigation) Mouse.Capture(null);
        UpdateControls();
    }

    private void NavigationKeyDown(object sender, KeyEventArgs e)
    {
        if (_bookmarks) return;
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None && _navigation.IsKeyboardFocusWithin)
        { DeleteSelectedPages(); e.Handled = true; return; }
        if (e.Key == Key.Escape && (_marqueePending || _reorderPending || _insertionSlot >= 0)) { CancelNavigationGesture(); e.Handled = true; return; }
        if (e.Key != Key.A || (Keyboard.Modifiers & ModifierKeys.Control) == 0 ||
            !_navigation.IsKeyboardFocusWithin || Current is not { } d) return;
        CancelNavigationGesture(); d.Selected.Clear();
        for (var n = 1; n <= d.Pages.Length; n++) d.Selected.Add(n);
        UpdateControls(); e.Handled = true;
    }

    private void RebuildPages()
    {
        _restoring = true; _pages.Children.Clear(); _paperVisuals.Clear();
        if (Current is not { } d) { _restoring = false; return; }
        var first = d.Continuous ? 0 : d.Page - 1;
        var end = d.Continuous ? d.Pages.Length : d.Page;
        for (var i = first; i < end; i++)
        {
            var paper = BuildPaper(d, i);
            paper.LayoutTransform = PaperTransform(d.Pages[i], d.Scale);
            _paperVisuals.Add(paper);
            _pages.Children.Add(new Border { Child = paper, Margin = new Thickness(0, 0, 0, PageGap) });
        }
        RestoreScroll();
    }

    private void SaveScroll()
    {
        if (!_restoring && Current is { } d)
        { d.HorizontalOffset = _canvas.HorizontalOffset; d.VerticalOffset = _canvas.VerticalOffset; }
    }
    private void RestoreScroll()
    {
        var d = Current;
        var revision = ++_viewRevision;
        _restoring = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (revision != _viewRevision || d != Current || d == null) return;
            _canvas.UpdateLayout();
            _canvas.ScrollToHorizontalOffset(d.HorizontalOffset);
            _canvas.ScrollToVerticalOffset(d.VerticalOffset);
            _canvas.UpdateLayout();
            _restoring = false;
        }));
    }
    private void CanvasScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportHeightChange != 0 || e.ViewportWidthChange != 0) QueueFit();
        if (_restoring || Current is not { } d) return;
        SaveScroll();
        if (!d.Continuous) return;
        var position = Math.Max(0, _canvas.VerticalOffset + Math.Min(120, _canvas.ViewportHeight / 3) - CanvasInset);
        var page = 1;
        var top = 0d;
        while (page < d.Pages.Length)
        {
            top += RotatedHeight(d.Pages[page - 1]) * d.Scale + PageGap;
            if (position < top) break;
            page++;
        }
        if (d.Page != page) { d.Page = page; UpdateControls(); QueueFit(); }
    }
    private double PageTop(int page, double scale) => Current is { } d
        ? d.Pages.Take(Math.Max(0, page - 1)).Sum(p => RotatedHeight(p) * scale + PageGap) : 0;
    private static double RotatedWidth(SamplePage page) => page.Rotation % 180 == 0 ? PaperWidth : PaperHeight;
    private static double RotatedHeight(SamplePage page) => page.Rotation % 180 == 0 ? PaperHeight : PaperWidth;
    private static Transform PaperTransform(SamplePage page, double scale) => new TransformGroup
    {
        Children = new TransformCollection { new RotateTransform(page.Rotation), new ScaleTransform(scale, scale) }
    };
    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { _fitQueued = false; ApplyFit(); }));
    }
    private void ApplyFit()
    {
        if (Current is not { } d || d.Fit == FitKind.None || _canvas.ActualWidth < 60 || _canvas.ActualHeight < 60) return;
        // Reserve the vertical scrollbar so fit-width remains stable when it appears.
        var width = Math.Max(1, _canvas.ActualWidth - CanvasInset * 2 - SystemParameters.VerticalScrollBarWidth);
        var height = Math.Max(1, _canvas.ActualHeight - CanvasInset * 2 - PageGap);
        var data = d.Pages[d.Page - 1];
        var scale = d.Fit == FitKind.Page ? Math.Min(width / RotatedWidth(data), height / RotatedHeight(data)) : width / RotatedWidth(data);
        SetScale(Math.Clamp(Math.Floor(scale * 100) / 100, .05, 4));
    }
    private void SetScale(double scale)
    {
        if (Current is not { } d || Math.Abs(d.Scale - scale) < .0001) return;
        SaveScroll();
        var previousScale = d.Scale;
        var pageOrigin = d.Continuous ? PageTop(d.Page, previousScale) : 0;
        var localOffset = Math.Max(0, d.VerticalOffset - pageOrigin);
        d.Scale = scale;
        d.VerticalOffset = (d.Continuous ? PageTop(d.Page, scale) : 0) + localOffset * scale / previousScale;
        d.HorizontalOffset *= scale / previousScale;
        _restoring = true;
        for (var i = 0; i < _paperVisuals.Count; i++)
            _paperVisuals[i].LayoutTransform = PaperTransform(d.Pages[d.Continuous ? i : d.Page - 1], scale);
        RestoreScroll(); UpdateControls();
    }
    private void UpdateControls()
    {
        var d = Current;
        foreach (var button in _documentCommands) button.IsEnabled = d != null;
        _pageInput.IsEnabled = d != null;
        _pageInput.Text = PageNumber.ToString(CultureInfo.InvariantCulture);
        _pageTotal.Text = $"/ {PageCount}";
        _zoom.Text = $"{ZoomPercent}%";
        _previousButton.IsEnabled = _firstButton.IsEnabled = PageNumber > 1;
        _nextButton.IsEnabled = _lastButton.IsEnabled = d != null && PageNumber < PageCount;
        SelectAppearance(_singleButton, d != null && !d.Continuous);
        SelectAppearance(_continuousButton, d?.Continuous == true);
        SelectAppearance(_thumbnailRail, _navigationVisible && !_bookmarks);
        SelectAppearance(_bookmarkRail, _navigationVisible && _bookmarks);
        for (var i = 0; i < _navigationButtons.Count; i++)
        {
            var button = _navigationButtons[i];
            var selected = !_bookmarks && d?.Selected.Contains(i + 1) == true;
            SelectAppearance(button, _bookmarks ? i + 1 == PageNumber : selected);
            if (!_bookmarks)
            {
                // Warm fill marks selection; blue outline independently marks the page being viewed.
                button.BorderBrush = i + 1 == PageNumber ? BrushOf("#377BB5") : selected ? Accent : Line;
                AutomationProperties.SetHelpText(button, (selected ? "선택됨" : "선택 안 됨") +
                    (i + 1 == PageNumber ? " · 현재 보는 쪽" : "") + " · Ctrl: 토글 · Shift: 범위 선택");
            }
        }
        _selectionCount.Text = $"선택 {d?.Selected.Count ?? 0}쪽 · 보는 쪽 {PageNumber}";
        _thumbnailSizeSlider.IsEnabled = d != null;
        _status.Text = d == null ? "샘플 문서 없음" : (IsDirty ? "* 저장하지 않은 샘플 변경" : "샘플 메모리 기준 상태 (체크포인트)") + " · 실제 파일 아님 · 앱 종료 시 사라짐";
        HistoryChanged?.Invoke();
    }

    private static Border BuildPaper(SampleDocument document, int index)
    {
        var data = document.Pages[index];
        if (data.IsBlank)
            return new Border
            {
                Width = PaperWidth, Height = PaperHeight, Background = Brushes.White,
                BorderBrush = BrushOf("#555D65"), BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true, ToolTip = "합성 빈 페이지 · 메모리 전용 · 실제 PDF 아님"
            };
        var layout = new Grid { Margin = new Thickness(48, 40, 48, 35) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 26) };
        var mock = Label("SAMPLE / 합성 목업", 11, true); mock.Foreground = Accent; DockPanel.SetDock(mock, Dock.Right); header.Children.Add(mock);
        var identity = Label("환경시설 운영관리  /  DOCUMENT PREVIEW", 11); identity.Foreground = Muted; header.Children.Add(identity);
        layout.Children.Add(header);
        var body = new StackPanel(); Grid.SetRow(body, 1); layout.Children.Add(body);
        var category = Label(document.Category, 12, true); category.Foreground = Accent; category.Margin = new Thickness(0, 0, 0, 12); body.Children.Add(category);
        var title = Label(data.Title, 29, true); title.TextWrapping = TextWrapping.Wrap; title.Margin = new Thickness(0, 0, 0, 17); body.Children.Add(title);
        body.Children.Add(new Border { Background = Accent, Height = 3, Margin = new Thickness(0, 0, 0, 17) });
        var metadata = Label("대상: 가상 환경시설  |  기준: 가상 운영기간  |  용도: 화면 검토", 12); metadata.Foreground = Muted;
        metadata.Margin = new Thickness(0, 0, 0, 22); body.Children.Add(metadata);
        body.Children.Add(Paragraph(data.Introduction));
        body.Children.Add(Section("01", data.Section));
        body.Children.Add(Paragraph(data.Description));
        body.Children.Add(BuildTable(data.Columns, data.Rows));
        body.Children.Add(Section("02", "검토 메모"));
        foreach (var note in data.Notes)
            body.Children.Add(Paragraph("•  " + note, 13, new Thickness(0, 0, 0, 9)));
        body.Children.Add(new Border
        {
            Background = BrushOf("#F6F0E9"), BorderBrush = BrushOf("#E4D5C7"), BorderThickness = new Thickness(1),
            Padding = new Thickness(14), Margin = new Thickness(0, 15, 0, 0),
            Child = Paragraph("이 문서의 시설명, 수치, 점검 내용은 화면 검토를 위한 가상 자료입니다. 실제 점검 결과나 법정 보고서가 아니며 PDF 파일을 열거나 저장하지 않습니다.", 12, new Thickness(0))
        });
        var foot = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = Label(document.Name + "  ·  UI ONLY", 10); left.Foreground = Muted; foot.Children.Add(left);
        var right = Label($"{index + 1:00} / {document.Pages.Length:00}", 11, true); Grid.SetColumn(right, 1); foot.Children.Add(right);
        var footer = new Border { Child = foot, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        return new Border
        {
            Width = PaperWidth, Height = PaperHeight, Background = Brushes.White, BorderBrush = BrushOf("#555D65"),
            BorderThickness = new Thickness(1), Child = layout, SnapsToDevicePixels = true,
            ToolTip = "직접 구성한 WPF 목업 페이지 · PDF 렌더링 아님"
        };
    }
    private static UIElement BuildTable(string[] columns, string[][] rows)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 13) };
        for (var c = 0; c < columns.Length; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c == 0 ? 1.35 : 1, GridUnitType.Star) });
        for (var r = 0; r <= rows.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < columns.Length; c++)
            {
                var text = Label(r == 0 ? columns[c] : rows[r - 1][c], 12, r == 0);
                text.TextWrapping = TextWrapping.Wrap; text.TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Center;
                if (r == 0) text.Foreground = Brushes.White;
                var cell = new Border
                {
                    Background = r == 0 ? BrushOf("#43596B") : r % 2 == 0 ? Pale : Brushes.White,
                    BorderBrush = Line, BorderThickness = new Thickness(c == 0 ? 1 : 0, 0, 1, 1),
                    Padding = new Thickness(10, 12, 10, 12), Child = text
                };
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c); grid.Children.Add(cell);
            }
        }
        return grid;
    }
    private static TextBlock Paragraph(string text, double size = 14, Thickness? margin = null) => new()
    {
        Text = text, FontSize = size, Foreground = Ink, TextWrapping = TextWrapping.Wrap,
        LineHeight = size * 1.7, Margin = margin ?? new Thickness(0, 0, 0, 15)
    };
    private static TextBlock Section(string number, string title) => new()
    {
        Text = number + "  " + title, FontSize = 17, FontWeight = FontWeights.SemiBold,
        Foreground = Ink, Margin = new Thickness(0, 15, 0, 12)
    };
    private static TextBlock Label(string text, double size = 12, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Ink, VerticalAlignment = VerticalAlignment.Center
    };
    private static Button Button(string text, string tooltip, Action action)
    {
        var button = new Button { Content = text, ToolTip = tooltip };
        AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => action(); return button;
    }
    private Button CommandButton(string text, string tooltip, Action action)
    { var button = Button(text, tooltip, action); _documentCommands.Add(button); return button; }
    private static void SelectAppearance(Button button, bool selected)
    {
        button.Background = selected ? BrushOf("#F9E9E0") : Brushes.Transparent;
        button.BorderBrush = selected ? Accent : Line;
        button.Foreground = selected ? Accent : Ink;
        AutomationProperties.SetHelpText(button, selected ? "선택됨" : string.Empty);
    }
    private static void AddSeparator(Panel panel) => panel.Children.Add(new Border
    { Width = 1, Background = Line, Margin = new Thickness(7, 5, 7, 5) });
    private static Brush BrushOf(string color)
    { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    private enum FitKind { None, Page, Width }
    private sealed class SampleDocument(string name, string category, SamplePage[] pages)
    {
        public string Name { get; } = name;
        public string Category { get; } = category;
        public SamplePage[] Pages { get; set; } = pages;
        public SamplePage[]? SavedPages { get; set; } = pages.Select(CopyPage).ToArray();
        public List<PageHistoryEntry> UndoHistory { get; } = new();
        public List<PageHistoryEntry> RedoHistory { get; } = new();
        public int Page = 1;
        public HashSet<int> Selected { get; } = new() { 1 };
        public int SelectionAnchor = 1;
        public double Scale = .85, VerticalOffset, HorizontalOffset;
        public bool Continuous;
        public FitKind Fit = FitKind.None;
    }
    private sealed record SamplePage(string Title, string ShortTitle, string Introduction, string Section,
        string Description, string[] Columns, string[][] Rows, string[] Notes)
    {
        public string Identity { get; } = Guid.NewGuid().ToString("N");
        public int Rotation { get; init; }
        public bool IsBlank { get; init; }
    }

    private static SampleDocument[] CreateSamples() =>
    [
        new("가상 휴게소 시설점검 [목업]", "시설 운영점검 보고서 · 합성 샘플 A",
        [
            new("폐수처리시설 운영점검", "점검 개요",
                "가상 휴게소의 폐수처리시설을 대상으로 운영점검 보고서의 구성과 읽기 흐름을 검토합니다. 아래 모든 항목은 UI 검증용으로 작성한 합성 예시입니다.",
                "시설 개요 및 점검 범위", "유입, 생물처리, 방류의 세 구간으로 나누어 문서의 표와 본문 가독성을 확인합니다.",
                ["점검 구간", "확인 항목", "목업 표시", "구분"],
                [["유입조", "수위 / 유입", "예시 정상", "가상"], ["폭기조", "송풍 / 순환", "예시 확인", "가상"], ["방류조", "배관 / 계측", "예시 관찰", "가상"], ["제어반", "표시 / 알림", "예시 점검", "가상"]],
                ["축소판 또는 책갈피를 선택하여 원하는 페이지로 이동할 수 있습니다.", "시설의 실제 상태나 성능을 평가한 자료가 아닙니다."]),
            new("설비별 점검 기록", "설비 점검",
                "펌프, 송풍기 및 계측 장비의 기록을 가상 점검표로 구성했습니다. 각 행은 문서 작업 화면의 정렬과 표 크기를 확인하기 위한 예시입니다.",
                "설비 점검표", "운전시간, 압력, 경보 기록은 실제 계측값이 아니며 외부 장비와 연결되어 있지 않습니다.",
                ["설비명", "확인 위치", "가상 기록", "후속 항목"],
                [["이송펌프 A", "유입 구간", "표시 확인", "기록 검토"], ["이송펌프 B", "예비 구간", "대기 예시", "상태 확인"], ["송풍기", "생물처리", "운전 예시", "소음 확인"], ["수위계", "방류 구간", "표시 예시", "교정 검토"]],
                ["가로 폭이 좁으면 ‘너비 맞춤’을 사용하고 아래로 스크롤하세요.", "단일 보기와 연속 보기를 전환해도 선택한 페이지는 유지됩니다."]),
            new("조치 계획 및 검토 의견", "조치 계획",
                "점검 후 조치 사항을 요약하는 보고서 페이지의 목업입니다. 아래 일정과 담당 구분은 작업 흐름을 보여 주기 위한 가상 예시입니다.",
                "후속 검토 목록", "실제 정비 지시, 발주, 외부 전송 또는 저장 작업은 수행되지 않습니다.",
                ["검토 항목", "우선순위", "가상 시점", "상태 예시"],
                [["기록 양식 확인", "일반", "검토 1일차", "검토 대기"], ["계측 표시 확인", "일반", "검토 2일차", "확인 대기"], ["설비 목록 정리", "일반", "검토 3일차", "작성 예시"], ["보고서 구성 검토", "일반", "검토 4일차", "내부 검토"]],
                ["문서 탭을 전환하면 문서별 페이지, 배율 및 보기 방식이 복원됩니다.", "모든 문서를 닫으면 호스트가 시작 화면으로 돌아갈 수 있습니다."])
        ]),
        new("가상 처리시설 월간요약 [목업]", "운영 현황 요약 · 합성 샘플 B",
        [
            new("월간 운영현황 요약", "운영 요약",
                "가상 처리시설의 운영 현황을 요약하는 합성 문서입니다. 처리량과 수질 수치는 표의 표시 형식만 확인하기 위한 임의의 값입니다.",
                "운영지표 표시 예시", "표의 값은 측정 결과가 아니며 기준 적합성이나 처리 성능을 뜻하지 않습니다.",
                ["표시 항목", "가상 값", "단위", "데이터 구분"],
                [["일평균 유입량", "48.0", "㎥/일", "합성"], ["예시 운전시간", "12.5", "시간/일", "합성"], ["예시 점검 횟수", "4", "회", "합성"], ["예시 기록 건수", "24", "건", "합성"]],
                ["화면의 숫자는 실제 운영 자료나 법적 판단에 사용하지 마세요.", "Ctrl + 마우스 휠로 페이지 크기를 확대하거나 축소할 수 있습니다."]),
            new("수질 항목 표시 검토", "수질 표 예시",
                "수질 항목을 보고서 표로 나타내는 가상 페이지입니다. 단위와 소수점 표시, 열의 간격 및 한글 본문의 읽기 편의성을 살펴봅니다.",
                "가상 수질 데이터", "실험, 채수, 분석을 수행한 결과가 아니며 방류수 기준 판정은 제공하지 않습니다.",
                ["표시 항목", "합성 값 A", "합성 값 B", "단위"],
                [["BOD", "3.2", "3.6", "mg/L"], ["TOC", "5.1", "5.4", "mg/L"], ["SS", "2.8", "3.0", "mg/L"], ["pH", "7.1", "7.2", "- (무차원)"]],
                ["연속 보기에서는 마우스 휠로 세 페이지를 이어서 볼 수 있습니다.", "하단 페이지 번호를 입력하고 Enter를 누르면 바로 이동합니다."]),
            new("운영 기록 검토 체크리스트", "검토 체크리스트",
                "문서 검토자가 마지막으로 확인할 항목을 정리한 합성 체크리스트입니다. 읽기와 페이지 탐색만 구현되어 있으며 실제 문서 편집 기능은 없습니다.",
                "검토 항목 요약", "별도 모듈 없이 WPF 화면 요소만으로 구성한 미리보기입니다.",
                ["구분", "검토 내용", "목업 상태", "비고"],
                [["본문", "한글 가독성", "확인 가능", "화면 전용"], ["표", "행 / 열 정렬", "확인 가능", "합성 자료"], ["탐색", "페이지 이동", "사용 가능", "3쪽 한정"], ["저장", "파일 내보내기", "구현 안 함", "디스크 미사용"]],
                ["축소판 패널 경계를 드래그하면 너비를 바꿀 수 있습니다.", "이 샘플은 PDF 엔진, 외부 API, 파일 입출력을 사용하지 않습니다."])
        ])
    ];
}
