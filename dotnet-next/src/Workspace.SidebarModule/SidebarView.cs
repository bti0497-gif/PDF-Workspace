using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Workspace.Contracts;
namespace Workspace.SidebarModule;

public sealed class SidebarView : UserControl, IDisposable
{
    private readonly IWorkspaceHost host;
    private readonly PreviewQueue previews;
    private DocumentSnapshot document = DocumentSnapshot.Empty;
    private readonly WrapPanel cards = new() { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid surface = new() { Background = Brushes.Transparent, Focusable = true };
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Slider size = new() { Minimum = 64, Maximum = 220, Value = 112, SmallChange = 8, LargeChange = 24 };
    private readonly TextBlock count = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    private readonly System.Windows.Shapes.Rectangle marker = new() { Fill = Brushes.Red, Width = 3, Visibility = Visibility.Collapsed };
    private readonly System.Windows.Shapes.Rectangle marquee = new() { Stroke = Brushes.SteelBlue, Fill = new SolidColorBrush(Color.FromArgb(35,50,120,220)), Visibility = Visibility.Collapsed };
    private readonly List<Border> borders = new();
    private readonly List<Image> images = new();
    private readonly List<Button> retries = new();
    private readonly DispatcherTimer dragTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed, dragging, applyingSize, restoringScroll, scrollRestoreQueued;
    private Rect[] stableCardBounds = Array.Empty<Rect>();
    private double stableScrollOffset;
    private int layoutGeneration;
    private ViewportAnchor? pendingViewportAnchor;
    private readonly record struct ViewportAnchor(int CardIndex, double LocalOffset);
    private int anchor = 1, pressed = -2, insertion = -1;
    private Point start;
    private ModifierKeys modifiers;
    private int[] baseline = Array.Empty<int>();
    private int[]? marqueeSelection;
    public double ThumbnailSize => size.Value;
    public IReadOnlyList<Rect> CardBounds => borders.Select(b => b.TransformToAncestor(surface).TransformBounds(new Rect(b.RenderSize))).ToArray();
    public Rect InsertionMarkerBounds => marker.Visibility == Visibility.Visible ? new Rect(Canvas.GetLeft(marker), Canvas.GetTop(marker), marker.Width, marker.Height) : Rect.Empty;
    public int ReadyPreviewCount => images.Count(i => i.Source != null);
    public int FailureCount => retries.Count(b => b.Visibility == Visibility.Visible);
    public SidebarView(IWorkspaceHost host)
    {
        this.host = host; previews = new(host); previews.Completed += PreviewCompleted;
        Background = new SolidColorBrush(Color.FromRgb(244,245,247));
        var dock = new DockPanel(); var tools = new StackPanel { Margin = new Thickness(12,8,12,6) };
        tools.Children.Add(new TextBlock { Text = "페이지 축소판", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) });
        tools.Children.Add(size); tools.Children.Add(count); tools.Children.Add(status); DockPanel.SetDock(tools,Dock.Top); dock.Children.Add(tools);
        surface.Children.Add(cards); overlay.Children.Add(marquee); overlay.Children.Add(marker); surface.Children.Add(overlay); scroll.Content = surface; dock.Children.Add(scroll); Content = dock;
        AutomationProperties.SetAutomationId(size, "ThumbnailSizeSlider"); AutomationProperties.SetAutomationId(surface, "ThumbnailSelectionSurface"); AutomationProperties.SetAutomationId(marker, "ThumbnailInsertionMarker");
        size.ValueChanged += (_,_) => ResizeCards(); scroll.SizeChanged += (_,_) => ResizeCards();
        scroll.ScrollChanged += (_,e) =>
        {
            // An extent/viewport change can already have coerced VerticalOffset. Never
            // overwrite the pre-layout anchor with that temporary bottom-of-list offset.
            if (!scrollRestoreQueued && !restoringScroll && e.ExtentHeightChange == 0 && e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0)
                RememberViewport();
            PrioritizeVisible();
        };
        surface.LayoutUpdated += SurfaceLayoutUpdated;
        surface.PreviewMouseLeftButtonDown += MouseDownOnSurface; surface.PreviewMouseMove += (_,_) => MoveGesture();
        surface.PreviewMouseLeftButtonUp += async (_,e) => { await FinishGesture(e.GetPosition(surface)); e.Handled = true; };
        surface.LostMouseCapture += (_,_) => CancelGesture();
        surface.PreviewKeyDown += async (_,e) =>
        {
            if (e.Key == Key.Escape) { CancelGesture(); e.Handled = true; }
            if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { await SelectPagesAsync(Enumerable.Range(1,document.PageCount).ToArray()); e.Handled = true; }
            if (e.Key == Key.Delete) { await DispatchActionAsync("pages.delete"); e.Handled = true; }
        };
        dragTimer.Tick += (_,_) =>
        {
            if (!dragging) return; var p = Mouse.GetPosition(scroll);
            if (p.X < 0 || p.X > scroll.ViewportWidth || p.Y < 0 || p.Y > scroll.ViewportHeight) return;
            var delta = p.Y < 28 ? p.Y - 28 : p.Y > scroll.ViewportHeight - 28 ? p.Y - scroll.ViewportHeight + 28 : 0;
            if (delta != 0) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset + delta); scroll.UpdateLayout(); MoveGesture(); }
        };
        host.DocumentChanged += HostChanged; ApplyDocument(host.Document);
    }
    private void HostChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) ApplyDocument(e.Document);
        else Dispatcher.BeginInvoke(new Action(() => ApplyDocument(e.Document)));
    }
    private void ApplyDocument(DocumentSnapshot next)
    {
        if (disposed) return;
        var rebuild = next.DocumentId != document.DocumentId || next.ContentVersion != document.ContentVersion || next.PageCount != document.PageCount;
        document = next;
        if (rebuild)
        {
            layoutGeneration++; scrollRestoreQueued = false; pendingViewportAnchor = null; stableCardBounds = Array.Empty<Rect>();
            CancelGesture(); anchor = Math.Max(1,next.CurrentPage); cards.Children.Clear(); borders.Clear(); images.Clear(); retries.Clear();
            for (var page = 1; page <= next.PageCount; page++) AddCard(page);
            ResizeCards(); previews.Reset(next); Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PrioritizeVisible));
        }
        PaintSelection();
    }
    private void AddCard(int page)
    {
        var image = new Image { Stretch = Stretch.Uniform, Height = 150, IsHitTestVisible = false };
        var retry = new Button { Content = "미리보기 실패 · 재시도", Visibility = Visibility.Collapsed, FontSize = 10, Padding = new Thickness(1) };
        retry.Click += (_,_) => { retry.Visibility = Visibility.Collapsed; previews.Retry(page); };
        var paper = new Grid { Background = Brushes.White }; paper.Children.Add(image); paper.Children.Add(retry);
        var stack = new StackPanel(); stack.Children.Add(paper); stack.Children.Add(new TextBlock { Text = page.ToString(CultureInfo.InvariantCulture), TextAlignment = TextAlignment.Center, Margin = new Thickness(0,7,0,3), FontSize = 11 });
        var card = new Border { Child = stack, BorderThickness = new Thickness(2), Padding = new Thickness(7), Margin = new Thickness(6,6,6,10), HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
        AutomationProperties.SetAutomationId(card,"Thumbnail"+page);
        var menu = new ContextMenu();
        foreach (var (label, command, arg) in new[] { ("오른쪽 회전", "pages.rotate", "90"), ("왼쪽 회전", "pages.rotate", "-90"), ("페이지 삭제", "pages.delete", ""), ("페이지 추출…", "pages.extract", "") })
        { var item = new MenuItem { Header = label }; item.Click += async (_,_) => await DispatchActionAsync(command, arg.Length == 0 ? null : arg); menu.Items.Add(item); }
        var retryMenu = new MenuItem { Header = "축소판 다시 만들기" }; retryMenu.Click += (_,_) => previews.Retry(page); menu.Items.Add(retryMenu); card.ContextMenu = menu;
        card.PreviewMouseRightButtonDown += async (_,_) => { if (!document.SelectedPages.Contains(page)) await SelectThumbnailAsync(page); };
        cards.Children.Add(card); borders.Add(card); images.Add(image); retries.Add(retry);
    }
    private void PreviewCompleted(Guid id, long revision, int page, BitmapSource? image, string? error)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (disposed || document.DocumentId != id || document.ContentVersion != revision || page > images.Count) return;
            images[page-1].Source = image; ResizeCards(); retries[page-1].Visibility = error == null ? Visibility.Collapsed : Visibility.Visible; retries[page-1].ToolTip = error;
        }));
    }
    public void SetThumbnailSize(double value)
    { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); size.Value = Math.Clamp(value,64,220); }
    private void ResizeCards()
    {
        if (disposed || applyingSize) return;
        QueueViewportRestore(); applyingSize = true;
        try
        {
            var viewport = scroll.ViewportWidth > 0 ? scroll.ViewportWidth : Math.Max(170,ActualWidth)-18;
            var available = Math.Max(1,viewport-24); var columns = Math.Max(1,(int)(available/(size.Value+30)));
            var width = Math.Max(1,Math.Min(size.Value*1.2,available/columns-30));
            for (var i=0;i<borders.Count;i++) { borders[i].Width = width+18; images[i].Height = width * (images[i].Source is BitmapSource bitmap && bitmap.PixelWidth > 0 ? (double)bitmap.PixelHeight / bitmap.PixelWidth : 1.414); }
            size.ToolTip = $"축소판 {Math.Round(size.Value)}px";
        }
        finally { applyingSize = false; }
    }
    private void SurfaceLayoutUpdated(object? sender, EventArgs e) => RememberViewport();
    private void RememberViewport()
    {
        if (disposed || applyingSize || restoringScroll || scrollRestoreQueued || !surface.IsArrangeValid || scroll.ViewportHeight <= 0) return;
        stableCardBounds = CardBounds.ToArray();
        stableScrollOffset = scroll.VerticalOffset;
    }
    private void QueueViewportRestore()
    {
        if (scrollRestoreQueued) return;
        // SizeChanged may arrive after WrapPanel has reflowed. Use the last stable
        // arranged geometry, not the newly arranged row or already-clamped offset.
        pendingViewportAnchor = null;
        for (var i = 0; i < stableCardBounds.Length; i++)
        {
            var rect = stableCardBounds[i];
            if (rect.Height <= 0 || rect.Bottom <= stableScrollOffset) continue;
            pendingViewportAnchor = new(i, stableScrollOffset - rect.Top); break;
        }
        scrollRestoreQueued = true;
        var generation = layoutGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RestoreViewport(generation)));
    }
    private void RestoreViewport(int generation)
    {
        if (disposed || generation != layoutGeneration) return;
        restoringScroll = true;
        try
        {
            // One coalesced layout pass for a slider/splitter event burst. SizeChanged
            // can adjust card widths during this pass, but cannot queue another restore.
            scroll.UpdateLayout();
            if (pendingViewportAnchor is { } saved && saved.CardIndex < borders.Count)
            {
                var rect = borders[saved.CardIndex].TransformToAncestor(surface).TransformBounds(new Rect(borders[saved.CardIndex].RenderSize));
                var local = Math.Min(saved.LocalOffset, Math.Max(0, rect.Height - 1));
                var target = Math.Clamp(rect.Top + local, 0, Math.Max(0, scroll.ScrollableHeight));
                scroll.ScrollToVerticalOffset(target);
                scroll.UpdateLayout();
            }
        }
        finally
        {
            restoringScroll = false; scrollRestoreQueued = false; pendingViewportAnchor = null;
            RememberViewport(); PrioritizeVisible();
        }
    }
    private void PaintSelection()
    {
        var selected = marqueeSelection ?? document.SelectedPages.ToArray();
        for (var i=0;i<borders.Count;i++)
        {
            var yes = selected.Contains(i+1); borders[i].BorderBrush = yes ? Brushes.SteelBlue : Brushes.Transparent;
            borders[i].Background = yes ? new SolidColorBrush(Color.FromRgb(224,237,252)) : Brushes.Transparent;
        }
        count.Text = document.PageCount == 0 ? "문서를 열어 주세요" : $"{selected.Length}쪽 선택 / 전체 {document.PageCount}쪽";
    }
    private void PrioritizeVisible()
    {
        if (disposed || borders.Count == 0 || !surface.IsArrangeValid) return;
        var viewport = new Rect(0,scroll.VerticalOffset,Math.Max(1,scroll.ViewportWidth),Math.Max(1,scroll.ViewportHeight));
        previews.Prioritize(CardBounds.Select((rect,index)=>(rect,index)).Where(x=>x.rect.IntersectsWith(viewport)).Select(x=>x.index+1));
    }
    public async Task SelectThumbnailAsync(int page, bool control = false, bool shift = false)
    {
        if (page < 1 || page > document.PageCount) return;
        var selected = new HashSet<int>(document.SelectedPages);
        if (shift) { if (!control) selected.Clear(); for(var i=Math.Min(anchor,page);i<=Math.Max(anchor,page);i++) selected.Add(i); }
        else if (control) { if (!selected.Add(page)) selected.Remove(page); anchor = page; }
        else { selected.Clear(); selected.Add(page); anchor = page; }
        await SelectPagesAsync(selected.Order().ToArray(), !control && !shift ? page : null);
    }
    private Task SelectPagesAsync(int[] pages, int? current = null) => SendAsync("document.select", pages, current?.ToString(CultureInfo.InvariantCulture));
    public Task DispatchActionAsync(string command, string? argument = null) => SendAsync(command, document.SelectedPages.ToArray(), argument);
    private async Task SendAsync(string command, IReadOnlyList<int> pages, string? argument)
    {
        if (disposed || document.DocumentId == null) return;
        try { var result = await host.DispatchAsync(new(command,document.DocumentId,document.Version,pages,argument),lifetime.Token); if (!disposed) status.Text = result.Success ? "" : result.Message; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!disposed) status.Text = ex.Message; }
    }
    public int PreviewInsertionPoint(Point point)
    {
        var bounds = CardBounds; insertion = -1; marker.Visibility = Visibility.Collapsed;
        for(var i=0;i<bounds.Count;i++)
        {
            var rect = bounds[i]; if (!rect.Contains(point)) continue;
            var after = point.X >= rect.Left+rect.Width/2; insertion = i+(after?1:0);
            Canvas.SetLeft(marker,(after?rect.Right:rect.Left)-1.5); Canvas.SetTop(marker,rect.Top); marker.Height = rect.Height; marker.Visibility = Visibility.Visible; break;
        }
        return insertion;
    }
    public Task MoveSelectedAsync(int slot) => slot < 0 || slot > document.PageCount ? Task.CompletedTask : DispatchActionAsync("pages.reorder",slot.ToString(CultureInfo.InvariantCulture));
    private void MouseDownOnSurface(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null && node != surface; node = VisualTreeHelper.GetParent(node)) if (node is Button) return;
        CancelGesture(); start = e.GetPosition(surface); pressed = CardBounds.ToList().FindIndex(r=>r.Contains(start)); modifiers = Keyboard.Modifiers; baseline = document.SelectedPages.ToArray();
        surface.Focus(); if (!surface.CaptureMouse()) pressed = -2; e.Handled = true;
    }
    private void MoveGesture()
    {
        if (pressed == -2 || Mouse.LeftButton != MouseButtonState.Pressed) return;
        var point = Mouse.GetPosition(surface);
        if (!dragging && Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance) return;
        dragging = true; dragTimer.Start();
        if (pressed >= 0) { if (!document.SelectedPages.Contains(pressed+1)) { marqueeSelection = new[]{pressed+1}; PaintSelection(); } PreviewInsertionPoint(point); return; }
        var region = new Rect(start,point); Canvas.SetLeft(marquee,region.Left); Canvas.SetTop(marquee,region.Top); marquee.Width=region.Width; marquee.Height=region.Height; marquee.Visibility=Visibility.Visible;
        var selected = new HashSet<int>(modifiers != ModifierKeys.None ? baseline : Array.Empty<int>()); var bounds=CardBounds;
        for(var i=0;i<bounds.Count;i++) if (region.IntersectsWith(bounds[i])) { if (modifiers.HasFlag(ModifierKeys.Control) && baseline.Contains(i+1)) selected.Remove(i+1); else selected.Add(i+1); }
        marqueeSelection=selected.Order().ToArray(); PaintSelection();
    }
    private async Task FinishGesture(Point point)
    {
        if (pressed == -2) return;
        var page=pressed+1; var wasDragging=dragging; var slot=insertion; var mods=modifiers; var regionSelection=marqueeSelection; var onCard=page>0 && CardBounds[page-1].Contains(point);
        CancelGesture();
        if (wasDragging && page>0 && slot>=0) { if (!document.SelectedPages.Contains(page)) await SelectThumbnailAsync(page); await MoveSelectedAsync(slot); }
        else if (wasDragging && regionSelection!=null) await SelectPagesAsync(regionSelection);
        else if (!wasDragging && onCard) await SelectThumbnailAsync(page,mods.HasFlag(ModifierKeys.Control),mods.HasFlag(ModifierKeys.Shift));
    }
    private void CancelGesture()
    { pressed=-2; dragging=false; insertion=-1; marqueeSelection=null; dragTimer.Stop(); marker.Visibility=marquee.Visibility=Visibility.Collapsed; if (surface.IsMouseCaptured) surface.ReleaseMouseCapture(); PaintSelection(); }
    public void Dispose()
    { if(disposed)return; disposed=true; surface.LayoutUpdated-=SurfaceLayoutUpdated; host.DocumentChanged-=HostChanged; previews.Completed-=PreviewCompleted; previews.Dispose(); lifetime.Cancel(); dragTimer.Stop(); CancelGesture(); }
}
