using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
internal static partial class ShellSmokeChecks
{
 private static async Task CheckCompactLayout(Action<bool,string> check,string output)
 {
  var w=new MainWindow(new ModuleRegistry(),new ModuleLoadReport(Array.Empty<string>(),Array.Empty<string>()));
  var vm=w.Model;var p=vm.Preview;var root=(FrameworkElement)w.Content;
  vm.OpenPreview.Execute(null);p.SelectDocument(0);
  for(var i=0;i<3;i++){p.SelectThumbnail(1);p.ExtractSelectedPages();}
  p.SelectDocument(0);p.ShowThumbnails();p.FitPage();
  async Task Layout(int width,int height){for(var i=0;i<3;i++){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);}}
  var rows=new List<object>();
  foreach(var size in new[]{(640,480),(624,441),(624,431),(800,600),(784,561),(1280,820)})
  foreach(var tab in new[]{vm.FileTab,vm.HomeTab,vm.BasicTab,vm.AnnotationTab,vm.DocumentTab,vm.ViewTab,vm.ToolsTab})
  {
   tab.Execute(null);if(!vm.RibbonVisible)vm.ToggleRibbon.Execute(null);await Layout(size.Item1,size.Item2);p.FitPage();await Layout(size.Item1,size.Item2);
   var canvas=Descendants<ScrollViewer>(p).Single(s=>AutomationProperties.GetName(s)=="목업 문서 페이지 보기");
   rows.Add(new{Width=size.Item1,Height=size.Item2,Tab=vm.ActiveTab,BodyViewport=canvas.ViewportHeight,BodyWidth=canvas.ViewportWidth,Zoom=p.ZoomPercent,Ribbon=w.RibbonScroller.ActualHeight,Host=w.PreviewHost.ActualHeight});
   check(vm.RibbonVisible&&p.DocumentCount==5,$"Compact {size}/{vm.ActiveTab}: expanded preference and five documents retained");
   check(canvas.ViewportHeight>=180,$"Compact {size}/{vm.ActiveTab}: document body >=180 DIP, not merely host height");
   var pages=(StackPanel)canvas.Content;var paper=(Border)pages.Children[0];
   var paperBounds=Bounds(paper,canvas);
   check(paperBounds.Top>=0&&paperBounds.Bottom<=canvas.ViewportHeight+1&&paperBounds.Left>=0&&paperBounds.Right<=canvas.ViewportWidth+1,$"Compact {size}/{vm.ActiveTab}: whole fitted page is inside viewport");
   check(p.FontSize==12&&w.FontSize==12,$"Compact {size}/{vm.ActiveTab}: unchanged menu and workspace fonts");
   if(w.RibbonScroller.ScrollableHeight>0){w.RibbonScroller.ScrollToBottom();await Layout(size.Item1,size.Item2);check(w.RibbonScroller.VerticalOffset>0,$"Compact {size}/{vm.ActiveTab}: ribbon overflow remains reachable");w.RibbonScroller.ScrollToTop();await Layout(size.Item1,size.Item2);}
   if(vm.ActiveTab=="문서")Capture(root,size.Item1,size.Item2,1,Path.Combine(output,$"compact-{size.Item1}x{size.Item2}.png"));
  }
  vm.DocumentTab.Execute(null);if(!vm.RibbonVisible)vm.ToggleRibbon.Execute(null);
  await Layout(624,441);var thumbSurface=Descendants<Grid>(p).Single(g=>g.Name=="ThumbnailSelectionSurface");
  var thumbScroll=Descendants<ScrollViewer>(p).Single(s=>ReferenceEquals(s.Content,thumbSurface));
  check(thumbScroll.ViewportHeight>=80,"Compact outer-window allowance keeps thumbnail scroll viewport >=80 DIP");
  thumbScroll.ScrollToBottom();await Layout(624,441);check(thumbScroll.VerticalOffset>0,"Compact thumbnails remain scrollable");
  foreach(var scale in new[]{1d,1.25,1.5,2d})Capture(root,624,441,scale,Path.Combine(output,$"compact-raster-{scale:0.##}.png"));
  await Layout(1280,820);check(vm.RibbonVisible&&w.RibbonScroller.MaxHeight==174&&w.StatusText.TextWrapping==TextWrapping.Wrap,"Returning to normal restores chrome with expanded preference unchanged");
  vm.ToggleRibbon.Execute(null);await Layout(624,441);await Layout(1280,820);check(!vm.RibbonVisible,"Resize roundtrip also preserves collapsed ribbon preference");
  check(w.MinWidth==640&&w.MinHeight==480,"Native minimum remains 640x480");
  File.WriteAllText(Path.Combine(output,"compact-geometry.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
  p.ConfirmClose=_=>SampleCloseDecision.Discard;w.Close();
 }
}
