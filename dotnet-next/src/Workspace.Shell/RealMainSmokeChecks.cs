using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Workspace.Runtime;
using Workspace.Shell.PdfViewing;
namespace Workspace.Shell;
internal static class RealMainSmokeChecks
{
 internal static async Task<int> RunAsync(ModuleRegistry registry,ModuleLoadReport report,string fixture,string output)
 {
  Directory.CreateDirectory(output);var checks=new List<string>();MainWindow? window=null;
  void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);File.WriteAllText(Path.Combine(output,"progress.txt"),name);}
  try {
   Check(Environment.GetEnvironmentVariable(ReadOnlyPdfSession.EnvironmentKey)==null,"no engine environment override");
   Check(ReadOnlyPdfSession.DependencyPath().StartsWith(AppContext.BaseDirectory,StringComparison.OrdinalIgnoreCase),"runtime engine resolved inside published app");
   Check(report.Loaded.Count==1&&report.Errors.Count==0,"matching published module loaded");
   var source=Path.Combine(output,"ui source 한글 # &.pdf");File.Copy(fixture,source,false);
   var original=await PdfDocumentSession.FingerprintAsync(source,CancellationToken.None);
   window=new MainWindow(registry,report);var root=(FrameworkElement)window.Content;var view=window.RealWorkspace;
   var failures=new List<Exception>();view.ReportError=failures.Add;
   void Layout(){root.Measure(new Size(1280,820));root.Arrange(new Rect(0,0,1280,820));root.UpdateLayout();}
   void Capture(string name){Layout();var bitmap=new RenderTargetBitmap(1280,820,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name));encoder.Save(stream);}
   Button Button(string caption)=>view.RibbonButtons[caption];
   async Task Wait(){await view.LastAction.WaitAsync(TimeSpan.FromMinutes(3));await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout();Check(!view.IsBusy,"UI command completed without blocking dispatcher");if(failures.Count>0)throw new AggregateException(failures);}
   async Task Click(string caption){var b=Button(caption);if(((FrameworkElement)b.Parent).Parent is StackPanel stack&&stack.Parent is Border group&&group.Visibility!=Visibility.Visible){var category=caption=="인쇄"?"파일":caption is "축소" or "확대" or "실제 크기" or "너비 맞춤"?"보기":"문서";window.TopTabs.Children.OfType<Button>().Single(t=>Equals(t.Content,category)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();} Check(b.IsEnabled,caption+" enabled");b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Wait();}
   await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout();
   Check(root is Grid grid&&grid.Children.Contains(view)&&Grid.GetRow(view)==4,"real PDF workspace is main-window content, not another Window");
   Check(view.Session==null&&view.PageImage.Source==null&&view.ThumbnailList.Items.Count==0,"startup is a blank real workspace");
   Check(window.PreviewHost.Visibility==Visibility.Collapsed&&window.QuickAccessBar.Visibility==Visibility.Collapsed&&window.QuickAccessOverflow.Visibility==Visibility.Collapsed,"sample body and sample quick actions are collapsed");
   Check(window.PrototypeNotice.Visibility==Visibility.Collapsed&&!window.Model.PreviewActive,"no prominent sample-only notice or active sample");
   Check(ReferenceEquals(window.RibbonScroller.Content,view.Ribbon),"main ribbon contains real operation controls");
   Check(Button("파일 열기").IsEnabled&&!Button("저장").IsEnabled&&!Button("실행 취소").IsEnabled,"startup open available and document actions disabled");
   Check(new[]{"텍스트 편집","자르기","주석","인쇄"}.All(x=>!Button(x).IsEnabled),"unsupported actual PDF capabilities disabled");
   Check(view.Ribbon.Children.OfType<Border>().Count()==8&&view.RibbonButtons.Values.All(b=>b.ContentTemplate!=null&&ReferenceEquals(b.Style,window.FindResource("SpecialRibbonIconButton"))),"actual ribbon reuses approved vector-button and group styles");
   Check(window.TopTabs.Children.OfType<Button>().Where(b=>!b.IsEnabled).Select(b=>(string)b.Content).SequenceEqual(new[]{"기본","주석","도움말"}),"unimplemented top tabs are explicitly disabled");
   window.ViewTabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();Check(view.RibbonCategory=="보기"&&window.ViewTabButton.Tag is true&&((FrameworkElement)((FrameworkElement)Button("파일 열기").Parent).Parent).Parent is Border fileGroup&&fileGroup.Visibility==Visibility.Collapsed,"View tab selects distinct visible groups and active tab state");
   window.DocumentTabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();Check(view.RibbonCategory=="문서"&&window.DocumentTabButton.Tag is true&&window.DocumentTabButton.Background is SolidColorBrush activeBrush&&activeBrush.Color==Color.FromRgb(246,246,246),"Document tab selects actual page and merge tools");
   window.HomeTabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();Check(view.RibbonCategory=="홈"&&window.HomeTabButton.Tag is true,"Home tab restores real home groups");
   Capture("real-main-empty.png");
   int openCalls=0;view.PickOpenFile=()=>{openCalls++;return source;};
   var open=window.InputBindings.OfType<KeyBinding>().Single(k=>k.Key==Key.O&&k.Modifiers==ModifierKeys.Control);open.Command.Execute(null);await Wait();
   Check(openCalls==1&&view.Session?.PageCount==3,"Ctrl+O invokes native-dialog boundary and opens actual fixture");
   Check(view.PageImage.Source is BitmapSource bitmap&&bitmap.PixelWidth>100&&bitmap.PixelHeight>100,"main canvas has actual decoded engine image");
   Check(view.ThumbnailList.Items.Count==3&&view.ThumbnailList.Items.Cast<ListBoxItem>().All(i=>((StackPanel)i.Content).Children.OfType<Image>().Any(x=>x.Source!=null)),"three actual PDF thumbnail images");
   Check(Button("저장").IsEnabled&&!Button("실행 취소").IsEnabled&&!Button("다시 실행").IsEnabled,"open clean document has accurate action enablement");
   Capture("real-main-open.png");
   var saveAsTemplate=(StackPanel)Button("다른 이름으로 저장").ContentTemplate.LoadContent();Check(saveAsTemplate.Children[1] is TextBlock saveLabel&&saveLabel.Text=="다른 이름 저장"&&Button("다른 이름으로 저장").Width==104&&System.Windows.Automation.AutomationProperties.GetName(Button("다른 이름으로 저장"))=="다른 이름으로 저장","short Save As visible caption preserves stable automation name");
   await view.NavigateAsync(3);Layout();Capture("real-main-page3.png");await view.NavigateAsync(1);Layout();
   var rotateTemplate=(StackPanel)Button("90° 회전").ContentTemplate.LoadContent();Check(rotateTemplate.Children[0] is System.Windows.Shapes.Path rotateIcon&&ReferenceEquals(rotateIcon.Data,Presentation.RibbonLayout.Rotate)&&rotateTemplate.Children[1] is TextBlock,"rotation icon uses existing vector geometry above caption");
   window.DocumentTabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();Capture("real-main-document-ribbon.png");window.HomeTabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();
   string Pixels(){var b=(BitmapSource)view.PageImage.Source!;var data=new byte[b.PixelHeight*((b.PixelWidth*b.Format.BitsPerPixel+7)/8)];b.CopyPixels(data,(b.PixelWidth*b.Format.BitsPerPixel+7)/8,0);return Convert.ToHexString(SHA256.HashData(data));}
   var before=Pixels();await Click("90° 회전");Check(view.Session!.IsDirty&&window.Title.Contains('*')&&Pixels()!=before,"real ribbon edit renders new page and marks main title dirty");
   Check(Button("실행 취소").IsEnabled&&!Button("다시 실행").IsEnabled,"undo enabled after edit");
   Check(await PdfDocumentSession.FingerprintAsync(source,CancellationToken.None)==original,"ribbon edit preserves source bytes");
   Capture("real-main-edited.png");await Click("실행 취소");Check(!view.Session.IsDirty&&Button("다시 실행").IsEnabled&&Pixels()==before,"ribbon undo restores original rendering and clean state");await Click("다시 실행");
   int closeCalls=0;view.AskClose=()=>{closeCalls++;return 0;};await Click("문서 닫기");Check(view.Session?.IsDirty==true&&closeCalls==1,"close cancel retains changed real document");
   int replaceCalls=0;view.AskClose=()=>2;view.AskReplace=_=>{replaceCalls++;return false;};await Click("문서 닫기");Check(view.Session?.IsDirty==true&&replaceCalls==1,"close save with declined replacement stays open");
   var saved=Path.Combine(output,"ui saved.pdf");view.PickSaveFile=_=>saved;await Click("다른 이름으로 저장");Check(view.Session?.IsDirty==false&&File.Exists(saved),"main Save As publishes real PDF and clears dirty state");
   await Click("90° 회전");view.AskReplace=_=>true;await Click("저장");Check(view.Session?.IsDirty==false,"main Save confirms and replaces existing saved PDF");
   await Click("90° 회전");view.AskClose=()=>1;await Click("문서 닫기");Check(view.Session==null&&view.PageImage.Source==null&&view.ThumbnailList.Items.Count==0,"discard close clears main canvas and thumbnails");
   view.PickOpenFile=()=>saved;await Click("파일 열기");Check(view.Session?.PageCount==3&&!view.Session.IsDirty,"normal main Open button reopens saved PDF");
   Capture("real-main-reopened.png");Check(await PdfDocumentSession.FingerprintAsync(source,CancellationToken.None)==original,"original source protected across main UI workflow");
   await File.WriteAllTextAsync(Path.Combine(output,"real-main-smoke.json"),JsonSerializer.Serialize(new{Passed=true,Count=checks.Count,Checks=checks,Assembly=typeof(App).Assembly.Location,AssemblySha256=await PdfDocumentSession.FingerprintAsync(typeof(App).Assembly.Location,CancellationToken.None),Scope="Actual MainWindow offscreen layout and real control/keyboard-command routes; injected file/confirmation boundaries, real PdfCraft. No physical input, system-dialog or installed-app acceptance. Historical sample --smoke not run."},new JsonSerializerOptions{WriteIndented=true}));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(Path.Combine(output,"failure.txt"),error.ToString());return 1;}
  finally{if(window!=null)await window.RealWorkspace.DisposeAsync();}
 }
}
