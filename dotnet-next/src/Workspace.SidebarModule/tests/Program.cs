using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Workspace.Contracts;
using Workspace.SidebarModule;

internal static class Program
{
    private static int checks;
    [STAThread] private static int Main()
    {
        try
        {
            var host = new FakeHost(129); using var module = new SidebarModule();
            var view = (SidebarView)module.CreateView(host);
            Layout(view,510); Check(module.Region == "sidebar", "module region");
            Check(view.CardBounds.Count == 129,"all 129 cards created");
            var rects=view.CardBounds; Check(rects[0].Top == rects[1].Top && rects[1].Left > rects[0].Right,"multiple columns");
            Check(rects[0].Width < 240,"selection remains card-sized not full row");
            Wait(view.SelectThumbnailAsync(3)); Check(host.Document.SelectedPages.SequenceEqual(new[]{3}) && host.Document.CurrentPage==3,"click selection/navigation");
            Wait(view.SelectThumbnailAsync(5,true)); Check(host.Document.SelectedPages.SequenceEqual(new[]{3,5}) && host.Document.CurrentPage==3,"ctrl additive non-navigation");
            Wait(view.SelectThumbnailAsync(8,false,true)); Check(host.Document.SelectedPages.SequenceEqual(new[]{5,6,7,8}),"shift anchor range");
            Wait(view.SelectThumbnailAsync(11,true,true)); Check(host.Document.SelectedPages.SequenceEqual(Enumerable.Range(5,7)),"ctrl shift union");
            Wait(view.SelectThumbnailAsync(6,true)); Check(!host.Document.SelectedPages.Contains(6),"ctrl toggle");
            var painted=Descendants<Border>(view).Where(x=>x.BorderBrush==Brushes.SteelBlue).Count(); Check(painted==host.Document.SelectedPages.Count,"host-confirmed visible selection");
            Wait(view.ClearSelectionAsync());
            Check(host.Document.SelectedPages.Count==0,"clear selection empties selected pages");
            Check(Descendants<Border>(view).All(x=>x.BorderBrush!=Brushes.SteelBlue),"clear selection unhighlights all borders");
            Wait(view.SelectThumbnailAsync(3)); Check(host.Document.SelectedPages.SequenceEqual(new[]{3}),"re-selection after clear");
            host.SimulateNavigate(95); Pump();
            Check(Descendants<Border>(view).Where(x=>x.BorderBrush==Brushes.SteelBlue).Count()==1,"external navigate updates sidebar highlight");
            var card95=view.CardBounds[94];
            Check(card95.Bottom>Descendants<ScrollViewer>(view).Single().VerticalOffset && card95.Top<Descendants<ScrollViewer>(view).Single().VerticalOffset+Descendants<ScrollViewer>(view).Single().ViewportHeight,"external navigate brings thumbnail card into view");
            view.SetThumbnailSize(1); Layout(view,510); Check(view.ThumbnailSize==64,"minimum size clamp");
            view.SetThumbnailSize(999); Layout(view,510); Check(view.ThumbnailSize==220,"maximum size clamp");
            view.SetThumbnailSize(112); Layout(view,510); rects=view.CardBounds;
            var target=rects[2]; Check(view.PreviewInsertionPoint(new Point(target.Left+1,target.Top+5))==2,"left card insertion slot");
            Check(Math.Abs(view.InsertionMarkerBounds.Left-(target.Left-1.5))<.01 && view.InsertionMarkerBounds.Top==target.Top,"left marker actual hovered card");
            Check(view.PreviewInsertionPoint(new Point(target.Right-1,target.Top+5))==3,"right card insertion slot");
            Check(Math.Abs(view.InsertionMarkerBounds.Left-(target.Right-1.5))<.01,"right marker actual hovered card");
            var wrapped=rects.First(r=>r.Top>rects[0].Top); var index=rects.ToList().IndexOf(wrapped);
            Check(view.PreviewInsertionPoint(new Point(wrapped.Left+1,wrapped.Top+5))==index && view.InsertionMarkerBounds.Top==wrapped.Top,"wrapped row marker not previous card");
            Check(view.PreviewInsertionPoint(new Point(0,0))==-1 && view.InsertionMarkerBounds.IsEmpty,"no marker in gutter");
            Wait(view.MoveSelectedAsync(25)); Check(host.Intents.Last().CommandId=="pages.reorder" && host.Intents.Last().Argument=="25","genuine reorder intent original slot");
            Wait(view.DispatchActionAsync("pages.rotate","90")); Check(host.Intents.Last().CommandId=="pages.rotate","genuine rotation intent");
            Until(()=>view.ReadyPreviewCount==129,15000); Check(view.ReadyPreviewCount==129,"129 asynchronous previews complete");
            Check(host.MaxActive<=2 && host.MaxActive>0,"render concurrency bounded to two");
            Check(host.Requests.All(r=>r.Dpi==40),"small per-page render DPI");
            var rendered=host.Requests.Count; Wait(view.SelectThumbnailAsync(100)); Pump(); Check(host.Requests.Count==rendered,"selection keeps content preview cache");
            CheckScrolledResize(view,host);
            var image = new RenderTargetBitmap(510,700,96,96,PixelFormats.Pbgra32); image.Render(view); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"sidebar-offscreen.png"))) encoder.Save(output);
            host.Delay=150; host.Switch(129); Until(()=>host.Active>0,2000); var old=host.Document.DocumentId;
            host.Switch(3); Until(()=>view.ReadyPreviewCount==3,10000); Check(view.CardBounds.Count==3 && view.ReadyPreviewCount==3,"stale document results cannot replace new document"); Check(host.Cancelled>0,"old document render cancelled");
            Layout(view,510);
            var surfaceViewer=Descendants<ScrollViewer>(view).Single();
            var selectionSurface=Descendants<Grid>(view).First(g=>System.Windows.Automation.AutomationProperties.GetAutomationId(g)=="ThumbnailSelectionSurface");
            Check(selectionSurface.MinHeight>=surfaceViewer.ViewportHeight && surfaceViewer.ViewportHeight>0,"selection surface fills visible scroll viewport height");
            host.Delay=5; host.FailPage=2; host.Switch(3); Until(()=>view.FailureCount==1 && view.ReadyPreviewCount==2,5000); Check(view.FailureCount==1,"render failure visible");
            host.FailPage=0; var retry=Descendants<Button>(view).First(b=>b.Visibility==Visibility.Visible && b.Content is string s && s.Contains("재시도")); retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(()=>view.ReadyPreviewCount==3,5000); Check(view.FailureCount==0,"failed preview retry recovers");
            module.Dispose(); Check(host.SubscriberCount==0,"dispose unsubscribes host");
            var priorityHost=new FakeHost(129) { Delay=80 }; using(var queue=new PreviewQueue(priorityHost))
            {
                queue.Reset(priorityHost.Document); Until(()=>priorityHost.Requests.Count>=2,2000); queue.Prioritize(new[]{129});
                Until(()=>priorityHost.Requests.Count>=4,3000); Check(priorityHost.Requests.Take(4).Any(r=>r.Page==129),"visible late page promoted ahead of background fill");
            }
            Console.WriteLine($"PASS {checks} checks; 129 thumbnails, bounded rendering, stale cancellation, offscreen WPF. Native mouse/monitor and real PDF engine not tested."); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void CheckScrolledResize(SidebarView view, FakeHost host)
    {
        var viewer = Descendants<ScrollViewer>(view).Single();
        view.SetThumbnailSize(112); Layout(view,510);
        viewer.ScrollToVerticalOffset(view.CardBounds[84].Top + 17); Layout(view,510);
        var selection = host.Document.SelectedPages.ToArray(); var current = host.Document.CurrentPage;
        var version = host.Document.Version; var requestCount = host.Requests.Count; var intentCount = host.Intents.Count;
        var first = view.CardBounds.ToList().FindIndex(r=>r.Bottom>viewer.VerticalOffset);
        Check(first >= 79 && first <= 95,"resize regression starts at visible pages 80-96");
        void Change(double width, double preferredSize, string label)
        {
            var before = view.CardBounds;
            var index = before.ToList().FindIndex(r=>r.Bottom>viewer.VerticalOffset);
            var local = viewer.VerticalOffset-before[index].Top;
            view.SetThumbnailSize(preferredSize); Layout(view,width); Pump(); Layout(view,width);
            var after = view.CardBounds[index];
            var expected = Math.Clamp(after.Top+Math.Min(local,Math.Max(0,after.Height-1)),0,viewer.ScrollableHeight);
            Check(Math.Abs(viewer.VerticalOffset-expected)<=1,$"{label}: visible card/local offset preserved within 1 DIP");
            Check(after.Bottom>viewer.VerticalOffset && after.Top<viewer.VerticalOffset+viewer.ViewportHeight,$"{label}: original anchor card remains visible");
            var settled=viewer.VerticalOffset;
            for(var i=0;i<8;i++) { Pump(); view.UpdateLayout(); }
            Check(Math.Abs(viewer.VerticalOffset-settled)<=.01,$"{label}: dispatcher/layout settles without scroll loop");
        }
        Change(510,64,"112 to 64 while scrolled");
        Check(viewer.VerticalOffset < viewer.ScrollableHeight-100,"shrinking thumbnails does not jump to document bottom");
        Change(510,220,"64 to 220 while scrolled");
        Change(510,112,"220 to 112 while scrolled");
        Change(760,112,"splitter wider with column-count change");
        Change(250,112,"splitter narrower with column-count change");
        Change(510,112,"splitter restored");
        // Multiple slider inputs before the dispatcher/layout runs must use one old anchor.
        var bounds=view.CardBounds; var index2=bounds.ToList().FindIndex(r=>r.Bottom>viewer.VerticalOffset);
        var offset=viewer.VerticalOffset-bounds[index2].Top;
        view.SetThumbnailSize(160); view.SetThumbnailSize(80); view.SetThumbnailSize(64); Layout(view,510);
        Check(Math.Abs(viewer.VerticalOffset-Math.Clamp(view.CardBounds[index2].Top+offset,0,viewer.ScrollableHeight))<=1,"coalesced slider burst retains original anchor");
        // Near the end, exact preservation may be impossible: clamp to the new extent.
        view.SetThumbnailSize(220); Layout(view,250); viewer.ScrollToBottom(); Layout(view,250);
        Change(760,64,"end-of-document extent clamp");
        Check(viewer.VerticalOffset>=0 && viewer.VerticalOffset<=viewer.ScrollableHeight,"restored offset is bounded by extent");
        Check(host.Document.CurrentPage==current && host.Document.Version==version && host.Document.SelectedPages.SequenceEqual(selection),"resize does not change host current page or selection");
        Check(host.Intents.Count==intentCount,"resize dispatches no selection/navigation intent");
        Check(host.Requests.Count==requestCount && view.ReadyPreviewCount==129,"resize reuses all 129 rendered previews without rerender");
        view.SetThumbnailSize(112); Layout(view,510);
    }
    private static void Check(bool value,string name) { if(!value)throw new Exception("FAIL "+name); checks++; Console.WriteLine("PASS "+name); }
    private static void Layout(FrameworkElement view,double width) { view.Measure(new Size(width,700)); view.Arrange(new Rect(0,0,width,700)); view.UpdateLayout(); Pump(); }
    private static void Wait(Task task) { Until(()=>task.IsCompleted,5000); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition,int timeout) { var watch=Stopwatch.StartNew(); while(!condition()) { if(watch.ElapsedMilliseconds>timeout)throw new TimeoutException(); Pump(); Thread.Sleep(2); } Pump(); }
    private static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject { for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); if(child is T item)yield return item; foreach(var item2 in Descendants<T>(child))yield return item2; } }
}
internal sealed class FakeHost : IWorkspaceHost
{
    private EventHandler<DocumentChangedEventArgs>? changed;
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged { add=>changed+=value; remove=>changed-=value; }
    public int SubscriberCount=>changed?.GetInvocationList().Length??0;
    public DocumentSnapshot Document {get;private set;}
    public List<WorkspaceIntent> Intents {get;}=new();
    public ConcurrentQueue<(Guid Id,int Page,int Dpi)> Requests {get;}=new();
    public int Active, MaxActive, Cancelled, Delay=5, FailPage;
    private readonly byte[] png;
    public FakeHost(int count)
    {
        Document=new(Guid.NewGuid(),"fake.pdf",count,1,new[]{1},false,1,1);
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{255,255,255,255,200,200,200,255,200,200,200,255,255,255,255,255},8);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream=new MemoryStream(); encoder.Save(stream); png=stream.ToArray();
    }
    public void Switch(int count) { Document=new(Guid.NewGuid(),"new.pdf",count,1,new[]{1},false,Document.Version+1,Document.ContentVersion+1); changed?.Invoke(this,new(Document)); }
    public void SimulateNavigate(int page) { Document=Document with { CurrentPage=page, SelectedPages=Document.SelectedPages.Count<=1?new[]{page}:Document.SelectedPages, Version=Document.Version+1 }; changed?.Invoke(this,new(Document)); }
    public Task<CommandResult> DispatchAsync(WorkspaceIntent intent,CancellationToken cancellationToken=default)
    {
        if(intent.DocumentId!=Document.DocumentId || intent.ExpectedVersion!=Document.Version) return Task.FromResult(new CommandResult(false,"stale"));
        Intents.Add(intent);
        if(intent.CommandId=="document.select") { Document=Document with { SelectedPages=intent.Pages??Array.Empty<int>(),CurrentPage=int.TryParse(intent.Argument,out var page)?page:Document.CurrentPage,Version=Document.Version+1 }; changed?.Invoke(this,new(Document)); }
        return Task.FromResult(new CommandResult(true,"accepted"));
    }
    public async Task<byte[]> RenderPageAsync(Guid id,long revision,int page,int dpi,CancellationToken cancellationToken=default)
    {
        Requests.Enqueue((id,page,dpi)); var active=Interlocked.Increment(ref Active); int prior; do {prior=MaxActive;} while(active>prior && Interlocked.CompareExchange(ref MaxActive,active,prior)!=prior);
        try { await Task.Delay(Delay,cancellationToken); if(page==FailPage)throw new IOException("fake render error"); return png; }
        catch(OperationCanceledException) {Interlocked.Increment(ref Cancelled); throw;}
        finally {Interlocked.Decrement(ref Active);}
    }
}
