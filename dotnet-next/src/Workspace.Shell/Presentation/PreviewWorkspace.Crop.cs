using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Workspace.Shell.Presentation;

public sealed partial class PreviewWorkspace
{
    private readonly Grid cropLayer = new() { Background = BrushOf("#747B83"), Visibility = Visibility.Collapsed, Focusable = true };
    private readonly Viewbox cropView = new() { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
    private readonly TextBlock cropInfo = new() { Height = 64, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), Foreground = Brushes.White };
    private Button cropApply = null!;
    private readonly Dictionary<UIElement, bool> cropBackground = new();
    private Canvas? cropSurface;
    private Rectangle? cropSelection;
    private System.Windows.Shapes.Path? cropShade;
    private SampleDocument? cropDocument;
    private SamplePage? cropPage;
    private Rect cropPending = Rect.Empty;
    private Point? cropStart;
    public bool IsCropping => cropLayer.Visibility == Visibility.Visible;
    public Rect CurrentCrop => Current is { } d ? d.Pages[d.Page - 1].Crop : Rect.Empty;
    internal Rect PendingCrop => cropPending;
    internal FrameworkElement CropSurface => cropSurface!;
    internal FrameworkElement CropLayer => cropLayer;
    internal bool CropApplyEnabled => cropApply.IsEnabled;

    private static Border BuildPaper(SampleDocument document, int index)
    {
        var crop = document.Pages[index].Crop;
        var content = BuildOriginalPaper(document, index);
        var surface = new Canvas { Width = crop.Width, Height = crop.Height, ClipToBounds = true, Background = Brushes.White };
        Canvas.SetLeft(content, -crop.X); Canvas.SetTop(content, -crop.Y); surface.Children.Add(content);
        return new Border { Width = crop.Width, Height = crop.Height, Child = surface, ClipToBounds = true };
    }

    private void InitializeCrop(Grid root)
    {
        Grid.SetRowSpan(cropLayer, 3); Panel.SetZIndex(cropLayer, 30);
        cropLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cropLayer.RowDefinitions.Add(new RowDefinition());
        cropLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cropLayer.Children.Add(cropInfo);
        Grid.SetRow(cropView, 1); cropLayer.Children.Add(cropView);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        cropApply = Button("적용 · 샘플 1쪽", "선택 영역만 남기기 · 실행 취소 가능 · 실제 PDF 저장 없음", () => ApplyCrop());
        actions.Children.Add(cropApply);
        actions.Children.Add(Button("선택 지우기", "영역을 다시 드래그하세요", () => SetCropSelection(Rect.Empty)));
        actions.Children.Add(Button("취소 (Esc)", "변경 없이 자르기 종료", CancelCrop));
        Grid.SetRow(actions, 2); cropLayer.Children.Add(actions); root.Children.Add(cropLayer);
        AutomationProperties.SetAutomationId(cropLayer, "SampleCropOverlay");
        AutomationProperties.SetAutomationId(cropApply, "SampleCropApply");
        PreviewKeyDown += (_, e) => { if (HandleCropKey(e.Key)) e.Handled = true; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelCrop(); };
    }

    public void BeginCrop()
    {
        if (IsCropping || Current is not { } d) return;
        CancelNavigationGesture(); CloseSearch();
        cropDocument = d; cropPage = d.Pages[d.Page - 1]; cropPending = Rect.Empty;
        var paper = BuildPaper(d, d.Page - 1);
        cropSurface = new Canvas { Width = cropPage.Crop.Width, Height = cropPage.Crop.Height, Background = Brushes.Transparent,
            Cursor = Cursors.Cross, Focusable = true, ClipToBounds = true };
        cropShade = new System.Windows.Shapes.Path { Fill = BrushOf("#88747B83"), IsHitTestVisible = false };
        cropSelection = new Rectangle { Stroke = Brushes.OrangeRed, StrokeThickness = 3, IsHitTestVisible = false };
        cropSurface.Children.Add(cropShade); cropSurface.Children.Add(cropSelection);
        var stack = new Grid { Width = cropPage.Crop.Width, Height = cropPage.Crop.Height,
            LayoutTransform = new RotateTransform(cropPage.Rotation) };
        stack.Children.Add(paper); stack.Children.Add(cropSurface); cropView.Child = stack;
        cropSurface.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None) return;
            var point = e.GetPosition(cropSurface);
            if (!new Rect(0, 0, cropSurface.Width, cropSurface.Height).Contains(point)) return;
            if (!cropSurface.CaptureMouse()) return;
            cropStart = point; SetCropSelection(Rect.Empty); cropSurface.Focus(); e.Handled = true;
        };
        cropSurface.MouseMove += (_, e) =>
        {
            if (cropStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            SelectCropDrag(start, e.GetPosition(cropSurface)); e.Handled = true;
        };
        cropSurface.MouseLeftButtonUp += (_, e) =>
        {
            if (cropStart is not { } start) return;
            SelectCropDrag(start, e.GetPosition(cropSurface)); cropStart = null;
            cropSurface.ReleaseMouseCapture(); e.Handled = true;
        };
        cropSurface.LostMouseCapture += (_, _) => { if (cropStart != null) { cropStart = null; SetCropSelection(Rect.Empty); } };
        // A resize mid-gesture cancels the tentative rectangle; stable selections scale with the page.
        cropView.SizeChanged += CropViewportChanged;
        foreach (UIElement sibling in ((Grid)Content).Children)
            if (sibling != cropLayer) { cropBackground[sibling] = sibling.IsEnabled; sibling.IsEnabled = false; }
        cropLayer.Visibility = Visibility.Visible; SetCropSelection(Rect.Empty); cropLayer.Focus();
        UpdateControls();
    }
    private void CropViewportChanged(object sender, SizeChangedEventArgs e)
    {
        if (cropStart == null) return;
        cropStart = null; cropSurface?.ReleaseMouseCapture(); SetCropSelection(Rect.Empty);
    }
    internal void SelectCropDrag(Point start, Point end)
    {
        if (!IsCropping || cropPage == null) return;
        Point Clamp(Point p) => new(Math.Clamp(p.X, 0, cropPage.Crop.Width), Math.Clamp(p.Y, 0, cropPage.Crop.Height));
        if (!double.IsFinite(start.X + start.Y + end.X + end.Y)) { SetCropSelection(Rect.Empty); return; }
        var rect = new Rect(Clamp(start), Clamp(end));
        // Minimum in original paper coordinates, plus 8 screen DIPs to reject accidental tiny drags.
        var displayScale = cropSurface == null || cropView.ActualWidth <= 0 ? 1 :
            Math.Min(cropView.ActualWidth / RotatedWidth(cropPage), cropView.ActualHeight / RotatedHeight(cropPage));
        SetCropSelection(rect.Width >= 24 && rect.Height >= 24 && rect.Width * displayScale >= 8 && rect.Height * displayScale >= 8 ? rect : Rect.Empty);
    }
    private void SetCropSelection(Rect rect)
    {
        cropPending = rect;
        if (cropPage == null || cropSelection == null || cropShade == null) return;
        cropSelection.Visibility = rect.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (!rect.IsEmpty)
        {
            Canvas.SetLeft(cropSelection, rect.X); Canvas.SetTop(cropSelection, rect.Y);
            cropSelection.Width = rect.Width; cropSelection.Height = rect.Height;
            cropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, cropPage.Crop.Width, cropPage.Crop.Height)), new RectangleGeometry(rect));
        }
        else cropShade.Data = null;
        cropApply.IsEnabled = !rect.IsEmpty && rect != new Rect(0, 0, cropPage.Crop.Width, cropPage.Crop.Height);
        cropInfo.Text = $"현재 보는 {cropDocument?.Page}쪽만 자르기 · 합성 샘플 · 화면에 맞춘 미리보기\n" +
            (rect.IsEmpty ? "페이지 안에서 드래그하세요. 최소 24×24 (화면 8px)." : $"남길 영역 {rect.Width:0} × {rect.Height:0} · 어두운 영역은 가려집니다. 다시 드래그하여 변경") +
            " · 적용 전에는 변경 없음 · 원문 유지 / Undo 가능";
    }
    internal bool HandleCropKey(Key key) { if (!IsCropping || key != Key.Escape) return false; CancelCrop(); return true; }
    public void CancelCrop()
    {
        if (!IsCropping) return;
        cropStart = null; cropSurface?.ReleaseMouseCapture(); cropView.SizeChanged -= CropViewportChanged;
        cropLayer.Visibility = Visibility.Collapsed; cropView.Child = null; cropDocument = null; cropPage = null;
        foreach (var pair in cropBackground) pair.Key.IsEnabled = pair.Value; cropBackground.Clear();
        cropPending = Rect.Empty; UpdateControls(); _canvas.Focus();
    }
    public bool ApplyCrop()
    {
        if (!IsCropping || !cropApply.IsEnabled || Current != cropDocument || cropPage == null || Current is not { } d ||
            !ReferenceEquals(d.Pages[d.Page - 1], cropPage)) return false;
        var rect = cropPending; var original = cropPage.Crop;
        var before = CapturePageState(d);
        var pages = d.Pages.ToArray();
        pages[d.Page - 1] = cropPage with { Crop = new Rect(original.X + rect.X, original.Y + rect.Y, rect.Width, rect.Height) };
        CancelCrop(); RestorePageState(d, before with { Pages = pages, LocalOffset = 0, HorizontalOffset = 0 });
        CommitPageMutation(d, before, "현재 샘플 1쪽 자르기"); return true;
    }
}
