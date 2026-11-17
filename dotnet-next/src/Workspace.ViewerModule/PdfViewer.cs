using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
namespace Workspace.ViewerModule;

public sealed class PdfViewer : UserControl, IDisposable, IWorkspaceViewerView
{
    private readonly IWorkspaceHost host;
    private DocumentSnapshot document = DocumentSnapshot.Empty;
    private readonly PageSurface surface = new();
    public ScrollViewer Scroller { get; } = new() { Background = new SolidColorBrush(Color.FromRgb(116,123,131)), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, PanningMode = PanningMode.Both, Focusable = true };
    private readonly TextBlock total = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,8,0) };
    private readonly TextBox pageBox = new() { Width = 44, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock zoomLabel = new() { Width = 44, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly Dictionary<int, BitmapSource> cache = new();
    private readonly Dictionary<int, Border> visuals = new();
    private readonly HashSet<int> failed = new();
    private Size[] sizes = Array.Empty<Size>();
    private double[] tops = Array.Empty<double>();
    private CancellationTokenSource renderCancellation = new();
    private bool disposed, pumping, refreshQueued, restoring, navigationRunning;
    private int generation, pendingPage;
    private double scale = 1;
    private string fit = "width";
    public bool Continuous { get; private set; } = true;
    public int CurrentPage { get; private set; }
    public int CachedPageCount => cache.Count;
    public int RealizedPageCount => visuals.Count;
    public long CachedPixelBytes => cache.Values.Sum(x => (long)x.PixelWidth * x.PixelHeight * 4);
    public int ActiveRenderCount { get; private set; }
    public int CancelledRenderCount { get; private set; }
    public int RenderedPageCount { get; private set; }
    public double Zoom => scale;
    public double PageTop(int page) => page >= 1 && page <= tops.Length ? tops[page-1] : 0;
    public Size PageSize(int page) => sizes[page-1];

    public PdfViewer(IWorkspaceHost host)
    {
        this.host = host;
        FontFamily = new FontFamily("Malgun Gothic"); FontSize = 12; ClipToBounds = true;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Scroller.Content = surface; root.Children.Add(Scroller);
        AutomationProperties.SetName(Scroller, "PDF 문서 페이지, 세로 스크롤");
        var tools = new WrapPanel { Margin = new Thickness(4,2,4,2) };
        void Button(string text, string help, Action action) { var b = new Button { Content = text, ToolTip = help, Padding = new Thickness(7,3,7,3), Margin = new Thickness(1), MinHeight = 26 }; AutomationProperties.SetName(b, help); b.Click += (_,_) => action(); tools.Children.Add(b); }
        Button("‹", "이전 페이지", () => Navigate(CurrentPage-1));
        AutomationProperties.SetName(pageBox,"현재 페이지 번호, Enter로 이동"); tools.Children.Add(pageBox); tools.Children.Add(total);
        Button("›", "다음 페이지", () => Navigate(CurrentPage+1));
        Button("단일", "한 페이지씩 보기", () => SetContinuous(false)); Button("연속", "모든 페이지 연속 스크롤", () => SetContinuous(true));
        Button("−", "축소", () => SetZoom(scale/1.2)); tools.Children.Add(zoomLabel); Button("+", "확대", () => SetZoom(scale*1.2));
        Button("페이지 맞춤", "페이지 전체 맞춤", FitPage); Button("너비 맞춤", "페이지 너비 맞춤", FitWidth);
        tools.Children.Add(status);
        var footer = new Border { Background = new SolidColorBrush(Color.FromRgb(244,245,247)), Child = tools };
        Grid.SetRow(footer,1); root.Children.Add(footer); Content = root;
        pageBox.KeyDown += (_,e) => { if(e.Key == Key.Enter) { if(int.TryParse(pageBox.Text,out var p)) Navigate(p); e.Handled = true; } };
        Scroller.ScrollChanged += (_,e) => { if(disposed) return; if(!restoring && e.VerticalChange != 0 && Continuous) SyncScrollPage(); QueueRefresh(); };
        Scroller.SizeChanged += (_,_) => { if(fit != "none") Reflow(true); else QueueRefresh(); };
        Scroller.PreviewMouseWheel += (_,e) => { if((Keyboard.Modifiers & ModifierKeys.Control) != 0) { SetZoom(scale*(e.Delta>0 ? 1.2 : 1/1.2)); e.Handled = true; } };
        Scroller.PreviewKeyDown += (_,e) => { if(e.Key == Key.Home && (Keyboard.Modifiers & ModifierKeys.Control)!=0) { Navigate(1); e.Handled=true; } else if(e.Key == Key.End && (Keyboard.Modifiers & ModifierKeys.Control)!=0) { Navigate(document.PageCount); e.Handled=true; } else if(!Continuous && e.Key is Key.PageDown or Key.PageUp) { Navigate(CurrentPage+(e.Key==Key.PageDown?1:-1)); e.Handled=true; } };
        host.DocumentChanged += HostChanged; ApplyDocument(host.Document);
    }
    private void HostChanged(object? sender, DocumentChangedEventArgs e) { if(Dispatcher.CheckAccess()) ApplyDocument(e.Document); else Dispatcher.BeginInvoke(() => ApplyDocument(e.Document)); }
    private void ApplyDocument(DocumentSnapshot next)
    {
        if(disposed) return;
        bool contentChanged = document.DocumentId != next.DocumentId || document.ContentVersion != next.ContentVersion || document.PageCount != next.PageCount;
        int oldPage = CurrentPage; document = next; CurrentPage = next.CurrentPage;
        if(contentChanged) { InvalidateRenders(); cache.Clear(); failed.Clear(); sizes = Enumerable.Repeat(new Size(816,1056),next.PageCount).ToArray(); tops = new double[next.PageCount]; Reflow(false); GoToCurrent(); }
        else if(oldPage != CurrentPage) { if(!Continuous) Reflow(false); GoToCurrent(); }
        UpdateLabels(); QueueRefresh();
    }
    private void UpdateLabels() { pageBox.Text=CurrentPage.ToString(); total.Text=$"/ {document.PageCount}"; zoomLabel.Text=$"{scale:P0}"; if(document.PageCount==0) status.Text="PDF를 열어 주세요"; }
    public void SetContinuous(bool value) { if(Continuous==value) return; Continuous=value; InvalidateRenders(); Reflow(false); GoToCurrent(); }
    public bool ApplyViewCommand(string commandId)
    {
        if (disposed) return false;
        switch (commandId)
        {
            case "view.fitPage": FitPage(); return true;
            case "view.fitWidth": FitWidth(); return true;
            case "view.singlePage": SetContinuous(false); return true;
            case "view.continuous": SetContinuous(true); return true;
            case "view.zoomIn": SetZoom(scale * 1.2); return true;
            case "view.zoomOut": SetZoom(scale / 1.2); return true;
            case "view.actualSize": SetZoom(1); return true;
            default: return false;
        }
    }
    public void FitPage() { fit="page"; Reflow(false); GoToCurrent(); }
    public void FitWidth() { fit="width"; Reflow(true); }
    public void SetZoom(double value) { fit="none"; scale=Math.Clamp(value,.1,4); InvalidateRenders(); Reflow(false); GoToCurrent(); }
    private void Reflow(bool preserve)
    {
        if(disposed || sizes.Length==0) { surface.ExtentWidth=0; surface.ExtentHeight=0; surface.Children.Clear(); surface.Positions.Clear(); visuals.Clear(); surface.InvalidateMeasure(); return; }
        double oldTop=PageTop(CurrentPage), local=Scroller.VerticalOffset-oldTop;
        if(fit != "none") { var s=sizes[Math.Clamp(CurrentPage-1,0,sizes.Length-1)]; double w=Math.Max(80,Scroller.ActualWidth-48),h=Math.Max(80,Scroller.ActualHeight-32); scale=Math.Clamp(fit=="page"?Math.Min(w/s.Width,h/s.Height):w/s.Width,.05,4); }
        double top=16,maxWidth=0;
        for(int i=0;i<sizes.Length;i++) { tops[i]=top; if(Continuous || i==CurrentPage-1) { top+=sizes[i].Height*scale+16; maxWidth=Math.Max(maxWidth,sizes[i].Width*scale); } }
        surface.ExtentWidth=Math.Max(1,maxWidth+32); surface.ExtentHeight=top;
        surface.InvalidateMeasure(); UpdateLabels();
        if(preserve) RestoreOffset(PageTop(CurrentPage)+local); QueueRefresh();
    }
    private void RestoreOffset(double value)
    {
        restoring=true; Scroller.ScrollToVerticalOffset(Math.Max(0,value));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { restoring=false; QueueRefresh(); }));
    }
    private void GoToCurrent() => RestoreOffset(Continuous?PageTop(CurrentPage)-16:0);
    public void Navigate(int page)
    {
        if(disposed || document.PageCount==0) return;
        pendingPage=Math.Clamp(page,1,document.PageCount); _=FlushNavigation();
    }
    private async Task FlushNavigation()
    {
        if(navigationRunning) return; navigationRunning=true;
        try { while(!disposed && pendingPage>0) { int target=pendingPage; pendingPage=0; if(host.Document.CurrentPage==target) continue; var d=host.Document;
            var result=await host.DispatchAsync(new WorkspaceIntent("document.navigate",d.DocumentId,d.Version,new[]{target})); if(!result.Success) status.Text=result.Message;
        } } catch(Exception ex) { status.Text=ex.Message; } finally { navigationRunning=false; }
    }
    private void SyncScrollPage()
    {
        if(tops.Length==0) return;
        double at=Scroller.VerticalOffset+Math.Min(100,Scroller.ViewportHeight/3);
        int p=Array.BinarySearch(tops,at); p=p>=0?p:Math.Max(0,~p-1); p=Math.Clamp(p+1,1,tops.Length);
        if(CurrentPage==p) return;
        CurrentPage=p; UpdateLabels(); Navigate(p);
    }
    private void QueueRefresh() { if(disposed||refreshQueued) return; refreshQueued=true; Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(() => { refreshQueued=false; if(!disposed) RefreshVisible(); })); }
    private void RefreshVisible()
    {
        if(sizes.Length==0) return;
        double start=Scroller.VerticalOffset-250,end=Scroller.VerticalOffset+Math.Max(100,Scroller.ViewportHeight)+250;
        var visible=Enumerable.Range(1,sizes.Length).Where(p => (!Continuous ? p==CurrentPage : tops[p-1]+sizes[p-1].Height*scale >= start && tops[p-1]<=end)).Take(12).ToHashSet();
        if(!visible.SetEquals(visuals.Keys)) InvalidateRenders();
        foreach(var p in visuals.Keys.Where(p=>!visible.Contains(p)).ToArray()) { surface.Children.Remove(visuals[p]); surface.Positions.Remove(visuals[p]); visuals.Remove(p); }
        foreach(var p in cache.Keys.Where(p=>!visible.Contains(p)).ToArray()) cache.Remove(p);
        foreach(int p in visible)
        {
            if(!visuals.TryGetValue(p,out var border)) { border=new Border { Background=Brushes.White, BorderBrush=Brushes.DimGray, BorderThickness=new Thickness(1), Focusable=true }; AutomationProperties.SetName(border,$"PDF {p} 페이지"); visuals[p]=border; surface.Children.Add(border); }
            border.Child=cache.TryGetValue(p,out var bitmap)?new Image { Source=bitmap, Stretch=Stretch.Fill }:new TextBlock { Text=failed.Contains(p)?$"{p} 페이지 표시 실패":$"{p} 페이지 불러오는 중", Margin=new Thickness(12), Foreground=Brushes.DimGray };
            var size=sizes[p-1]; surface.Positions[border]=new Rect(Math.Max(16,(Math.Max(surface.ExtentWidth,Scroller.ViewportWidth)-size.Width*scale)/2),tops[p-1],size.Width*scale,size.Height*scale);
        }
        surface.InvalidateMeasure(); _=PumpRendering();
    }
    private void InvalidateRenders() { generation++; renderCancellation.Cancel(); renderCancellation.Dispose(); renderCancellation=new CancellationTokenSource(); }
    private async Task PumpRendering()
    {
        if(pumping||disposed) return; pumping=true;
        try
        {
            while(!disposed)
            {
                int p=visuals.Keys.Where(p=>!cache.ContainsKey(p)&&!failed.Contains(p)).OrderBy(p=>Math.Abs(p-CurrentPage)).FirstOrDefault();
                if(p==0 || document.DocumentId is not Guid id) break;
                int stamp=generation; long contentVersion=document.ContentVersion; var token=renderCancellation.Token;
                try
                {
                    ActiveRenderCount=1;
                    var bytes=await host.RenderPageAsync(id,contentVersion,p,96,token);
                    var decoded=await Task.Run(() => Decode(bytes,token),token);
                    var bitmap=decoded.Bitmap;
                    if(disposed || token.IsCancellationRequested || stamp!=generation || document.DocumentId!=id || document.ContentVersion!=contentVersion || !visuals.ContainsKey(p)) { CancelledRenderCount++; continue; }
                    var actual=decoded.Geometry; bool changed=Math.Abs(sizes[p-1].Width-actual.Width)>.5 || Math.Abs(sizes[p-1].Height-actual.Height)>.5;
                    sizes[p-1]=actual; cache[p]=bitmap; RenderedPageCount++; status.Text="";
                    if(changed) Reflow(true); RefreshVisible();
                }
                catch(OperationCanceledException) { CancelledRenderCount++; }
                catch(Exception ex) { if(stamp==generation && !disposed) { failed.Add(p); status.Text=$"페이지 {p}: {ex.Message}"; RefreshVisible(); } }
                finally { ActiveRenderCount=0; }
            }
        }
        finally { pumping=false; }
    }
    private sealed record DecodedPage(BitmapSource Bitmap, Size Geometry);
    private static DecodedPage Decode(byte[] bytes,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if(bytes.Length>64*1024*1024) throw new InvalidDataException("페이지 이미지가 너무 큽니다.");
        using var input=new MemoryStream(bytes,false);
        var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnDemand);
        var frame=decoder.Frames[0]; int width=frame.PixelWidth,height=frame.PixelHeight;
        if(width<1 || height<1 || width>100000 || height>100000) throw new InvalidDataException("페이지 이미지 크기 제한을 초과했습니다.");
        // Read geometry BEFORE downsampling. At most 1024² decoded pixels per cached page,
        // 12 pages / 48 MiB of BGRA pixels, plus one encoded render in flight.
        input.Position=0;
        var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth=Math.Max(1,(int)(width*Math.Min(1,1024d/Math.Max(width,height))));
        bitmap.StreamSource=input; bitmap.EndInit(); bitmap.Freeze();
        token.ThrowIfCancellationRequested(); return new DecodedPage(bitmap,new Size(width,height));
    }
    public void Dispose() { if(disposed) return; disposed=true; host.DocumentChanged-=HostChanged; renderCancellation.Cancel(); renderCancellation.Dispose(); cache.Clear(); visuals.Clear(); surface.Children.Clear(); surface.Positions.Clear(); }
}
