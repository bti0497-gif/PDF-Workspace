using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
using Workspace.ViewerModule;

internal static class Program
{
 [STAThread] static int Main(string[] args)
 {
  try { var app=new Application(); var host=new FakeHost(); using var viewer=new PdfViewer(host); Layout(viewer,1000,760); Pump(250);
  Check(viewer.Scroller.ExtentHeight>100000,"129-page continuous extent");
  Check(viewer.RenderedPageCount>0,"asynchronous images complete");
  foreach(var p in new[]{2,3,24,25,64,128,129,1}) { viewer.Scroller.ScrollToVerticalOffset(viewer.PageTop(p)); Pump(180); Check(host.Document.CurrentPage==p,$"scroll sync {p}, got {host.Document.CurrentPage}"); Check(viewer.CachedPageCount<=12 && viewer.RealizedPageCount<=12,"bounded visuals/cache"); }
  Check(host.Dispatches<30,"no navigation feedback loop");
  IWorkspaceViewerView commands=viewer;
  int beforeCommands=host.Dispatches;
  Check(commands.ApplyViewCommand("view.singlePage"),"bridge accepts single page"); Pump(100); Check(!viewer.Continuous && viewer.RealizedPageCount==1,"bridge single page effect");
  Check(commands.ApplyViewCommand("view.continuous"),"bridge accepts continuous"); Pump(100); Check(viewer.Continuous,"bridge continuous effect");
  Check(commands.ApplyViewCommand("view.actualSize") && Math.Abs(viewer.Zoom-1)<.001,"bridge actual size");
  Check(commands.ApplyViewCommand("view.zoomIn") && Math.Abs(viewer.Zoom-1.2)<.001,"bridge zoom in");
  Check(commands.ApplyViewCommand("view.zoomOut") && Math.Abs(viewer.Zoom-1)<.001,"bridge zoom out");
  Check(commands.ApplyViewCommand("view.fitPage"),"bridge accepts fit page"); Pump(100); Check(viewer.PageSize(viewer.CurrentPage).Height*viewer.Zoom<=viewer.Scroller.ViewportHeight,"bridge fit page effect");
  Check(commands.ApplyViewCommand("view.fitWidth"),"bridge accepts fit width"); Pump(100); Check(viewer.PageSize(viewer.CurrentPage).Width*viewer.Zoom<=viewer.Scroller.ViewportWidth,"bridge fit width effect");
  var unchangedZoom=viewer.Zoom; var unchangedMode=viewer.Continuous;
  Check(!commands.ApplyViewCommand("view.unknown"),"unknown bridge command rejected");
  Check(viewer.Zoom==unchangedZoom && viewer.Continuous==unchangedMode && host.Dispatches==beforeCommands,"bridge is view-only and unknown leaves state unchanged");

  host.ExternalNavigate(40); Pump(200); Check(viewer.CurrentPage==40,"external sidebar sync");
  viewer.SetContinuous(false); Pump(150); Check(viewer.RealizedPageCount==1,"single-page realized");
  viewer.FitPage(); Pump(150); Check(viewer.PageSize(40).Height*viewer.Zoom<=viewer.Scroller.ViewportHeight,"fit page height");
  viewer.SetContinuous(true); viewer.SetZoom(1.5); Pump(100); Check(Math.Abs(viewer.Zoom-1.5)<.01,"manual zoom");
  host.ExternalNavigate(3); Pump(180); Check(viewer.PageSize(3).Width>viewer.PageSize(3).Height,"rotated geometry from bitmap");
  host.Delay=100; host.ExternalNavigate(70); Pump(20); host.ExternalNavigate(100); viewer.SetZoom(.8); Pump(300); Check(viewer.CurrentPage==100,"stale rendering does not change navigation"); Check(viewer.CancelledRenderCount>0,"stale cancellation exercised");
  Check(host.MaxActive==1,"one active renderer"); Check(viewer.CachedPixelBytes<64*1024*1024,"cache byte bound on fixture");
  Layout(viewer,360,420); viewer.FitWidth(); Pump(200); Check(viewer.Scroller.ActualHeight>200,"small viewport usable"); Check(viewer.PageSize(100).Width*viewer.Zoom<=viewer.Scroller.ViewportWidth,"fit width small");
  Layout(viewer,1280,800); Pump(100);
  if(args.Length>0) { Directory.CreateDirectory(args[0]); var image=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32); image.Render(viewer); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var f=File.Create(Path.Combine(args[0],"viewer-offscreen.png")); png.Save(f); }
  Console.WriteLine($"PASS: 129 pages; dispatches={host.Dispatches}; renders={viewer.RenderedPageCount}; canceled={viewer.CancelledRenderCount}; cache={viewer.CachedPageCount}; bytes={viewer.CachedPixelBytes}; maxActive={host.MaxActive}"); viewer.Dispose(); host.ExternalNavigate(1); Pump(100); Check(viewer.CachedPageCount==0,"dispose clears"); Check(!commands.ApplyViewCommand("view.actualSize"),"disposed bridge rejects commands"); return 0;
  } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
 }
 static void Check(bool b,string s) { if(!b) throw new Exception(s); Console.WriteLine("PASS "+s); }
 static void Layout(FrameworkElement v,int w,int h) { v.Width=w; v.Height=h; v.Measure(new Size(w,h)); v.Arrange(new Rect(0,0,w,h)); v.UpdateLayout(); }
 static void Pump(int ms) { var frame=new DispatcherFrame(); var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)}; timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start(); Dispatcher.PushFrame(frame); }
 sealed class FakeHost : IWorkspaceHost
 {
  public DocumentSnapshot Document {get;private set;} = new(Guid.NewGuid(),"129-page fixture",129,1,new[]{1},false,1,1);
  public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
  public int Dispatches,Active,MaxActive,Delay=10;
  readonly byte[] portrait=Image(816,1056),landscape=Image(1056,816);
  public Task<CommandResult> DispatchAsync(WorkspaceIntent intent,CancellationToken cancellationToken=default) { Dispatches++; ExternalNavigate(intent.Pages![0]); return Task.FromResult(new CommandResult(true,"")); }
  public void ExternalNavigate(int p) { Document=Document with {CurrentPage=p,Version=Document.Version+1}; DocumentChanged?.Invoke(this,new(Document)); }
  public async Task<byte[]> RenderPageAsync(Guid id,long version,int p,int dpi,CancellationToken cancellationToken=default) { Active++;MaxActive=Math.Max(MaxActive,Active);try { await Task.Delay(Delay,cancellationToken);return p%3==0?landscape:portrait;} finally {Active--;} }
  static byte[] Image(int w,int h) { var pixels=new byte[w*h*4]; Array.Fill(pixels,(byte)245); var bitmap=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4); var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray(); }
 }
}
