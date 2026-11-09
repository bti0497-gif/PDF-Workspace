using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;

// Actual WPF content trees and command routes, rendered offscreen. Not native input/DPI acceptance.
internal static partial class ShellSmokeChecks
{
    public static async Task<int> RunAsync(ModuleRegistry registry,ModuleLoadReport report,string output)
    {
        Directory.CreateDirectory(output);
        foreach(var file in new[]{"smoke.json","failure.txt","foundation-home.png","workspace-module.png"}) File.Delete(Path.Combine(output,file));
        var checks=new List<string>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);File.WriteAllText(Path.Combine(output,"progress.txt"),checks.Count+": "+name);}
        try {
            Check(report.Errors.Count==0&&report.Loaded.Count==1,"Bundled DLL loaded successfully");
            var window=new MainWindow(registry,report);
            window.Model.Preview.ConfirmClose=_=>SampleCloseDecision.Discard;
            var root=(FrameworkElement)window.Content;
            var vm=window.Model;
            void Layout(double width,double height){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
            bool HasIconAboveCaption(Button button)
            {
                if(button.Content is not StackPanel content)return false;
                var icon=Descendants<System.Windows.Shapes.Path>(content).SingleOrDefault();
                var caption=Descendants<TextBlock>(content).SingleOrDefault();
                return icon is not null&&caption is not null&&Bounds(icon,root).Bottom<=Bounds(caption,root).Top+.5;
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Layout(1280,820);
            Check(vm.IsHome&&!vm.RibbonVisible&&!vm.SidebarVisible,"Initial home has no ribbon or empty sidebar");
            Check(window.SidebarColumn.ActualWidth==0,"Hidden sidebar reserves no width");
            var homeRect=Bounds(window.HomeScreen.HomeContent,root);
            var area=Bounds(window.HomeScreen,root);
            Check(Math.Abs(homeRect.Left+homeRect.Width/2-(area.Left+area.Width/2))<2,"Start content horizontally centered");
            Check(Math.Abs(homeRect.Top+homeRect.Height/2-(area.Top+area.Height/2))<3,"Start content vertically centered");
            Capture(root,1280,820,1,Path.Combine(output,"foundation-home.png"));

            // Real loaded DLL identity mismatches must remain failures.
            var original=JsonSerializer.Deserialize<ModuleCatalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"modules.json")))!;
            var badPath=Path.Combine(output,"mismatch-catalog.json");
            try {
                foreach(var entry in new[]{original.Modules[0] with {Version="9.9.9"},original.Modules[0] with {Type="NoSuchType"}}){
                    File.WriteAllText(badPath,JsonSerializer.Serialize(new ModuleCatalog(1,new[]{entry})));
                    var rejected=ModuleLoader.Load(badPath,Path.Combine(AppContext.BaseDirectory,"Modules"),new ModuleRegistry());
                    Check(rejected.Errors.Count==1&&rejected.Loaded.Count==0,"Real DLL identity mismatch rejected: "+entry.Version+" / "+entry.Type);
                }
            } finally {File.Delete(badPath);}
            var cleanEmpty=new ShellViewModel(new ModuleRegistry(),new ModuleLoadReport(Array.Empty<string>(),Array.Empty<string>()));
            Check(!cleanEmpty.HasModules&&!cleanEmpty.HasCommands&&!cleanEmpty.HasLoadErrors,"Clean empty catalog supported without a false error");
            var empty=new MainWindow(new ModuleRegistry(),new ModuleLoadReport(Array.Empty<string>(),new[]{"Test: module missing"}));
            empty.Model.Preview.ConfirmClose=_=>SampleCloseDecision.Discard;
            Check(!empty.Model.HasModules&&!empty.Model.HasCommands&&empty.Model.HasLoadErrors,"No feature module: empty shell and visible load-error model");
            empty.Model.ShowModules.Execute(null);
            var emptyRoot=(FrameworkElement)empty.Content;emptyRoot.Measure(new Size(640,480));emptyRoot.Arrange(new Rect(0,0,640,480));emptyRoot.UpdateLayout();
            Check(empty.SidebarPanel.ActualWidth>=200,"Empty module catalog still has a functioning sidebar");

            Check(Descendants<Button>(window.TopTabs).Select(b=>b.Content?.ToString()).SequenceEqual(new[]{"파일","홈","기본","주석","문서","보기","도움말"}),"Reference-oriented ribbon tab order is preserved");
            Check(Descendants<Button>(window.QuickAccessBar).Where(b=>b.Command is null).All(b=>!b.IsEnabled),"Unconnected quick-access PDF actions are disabled");
            var quickIcons=Descendants<Button>(window.QuickAccessBar).ToArray();
            Check(quickIcons.Length==6&&quickIcons.All(b=>b.Content is System.Windows.Shapes.Path),"Quick access uses six icon-only buttons including undo/redo, rather than text labels");
            Check(quickIcons.All(b=>!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(b))&&b.ToolTip is string text&&text.Length>3),"Icon-only actions retain accessible names and descriptive tooltips");
            Check(new[]{window.QuickPrintButton}.All(b=>!b.IsEnabled&&ToolTipService.GetShowOnDisabled(b)&&b.ToolTip.ToString()!.Contains("아직 연결되지")),"Unavailable icon actions remain disabled but explain status on hover");
            Check(window.QuickSaveButton.Command==vm.SaveSample&&!window.QuickSaveButton.IsEnabled,"Sample save shares the host command and is disabled without dirty active sample");
            Check(window.QuickUndoButton.Command==vm.Undo&&window.QuickRedoButton.Command==vm.Redo,"Undo/redo icons share the sample workspace commands");
            Check(Descendants<Button>(window.PageViewBar).All(b=>!b.IsEnabled),"Page/zoom toolbar never pretends a PDF is open");
            vm.HomeTab.Execute(null);Layout(1280,820);
            var pdfTools=Descendants<Button>(window.PdfRibbonGroups).ToArray();
            Check(pdfTools.Length==9&&pdfTools.All(b=>!b.IsEnabled&&b.Command is null),"Home ribbon has nine explicitly unavailable PDF tools, not fake actions");
            var searchTools=RibbonLayout.For("홈").Single(g=>g.Name=="검색").Tools;
            Check(searchTools.Select(t=>t.Caption).SequenceEqual(new[]{"문서 찾기","상세 검색"})&&!ReferenceEquals(searchTools[0].Icon,searchTools[1].Icon),"Find and advanced search have distinct purpose labels and independently drawn icons");
            Check(searchTools[0].Availability.Contains("현재 문서")&&searchTools[1].Availability.Contains("여러 PDF/폴더")&&searchTools.All(t=>t.Availability.Contains("미연결")),"Search tooltips distinguish scope without claiming implemented search");
            vm.FileTab.Execute(null);Layout(1280,820);
            var fileRibbonButtons=Descendants<Button>(window.FileRibbonPanel).ToArray();
            Check(fileRibbonButtons.Length==6&&fileRibbonButtons.All(HasIconAboveCaption),"File ribbon keeps six captioned vector-icon buttons with icons above captions");
            Check(fileRibbonButtons[0].Command==vm.OpenPreview&&fileRibbonButtons[1].Command==vm.SaveSample&&fileRibbonButtons[2].Command==vm.ReopenSavedSample&&!fileRibbonButtons[3].IsEnabled&&fileRibbonButtons[4].Command==vm.Home&&fileRibbonButtons[5].IsEnabled==vm.IsIdle,"File ribbon retains command bindings and unavailable/idle states");
            vm.ViewTab.Execute(null);Layout(1280,820);
            var viewRibbonButtons=Descendants<Button>(window.ViewRibbonPanel).ToArray();
            Check(viewRibbonButtons.Length==8&&viewRibbonButtons.All(HasIconAboveCaption),"View ribbon keeps eight captioned vector-icon buttons with icons above captions");
            Check(viewRibbonButtons.Select(b=>b.Command).SequenceEqual(new[]{vm.FitPage,vm.FitWidth,vm.SinglePage,vm.ContinuousPages,vm.Thumbnails,vm.Bookmarks,vm.ToggleSidebar,vm.ResetLayout}),"View ribbon retains all view command bindings");
            var documentIcons=RibbonLayout.For("문서").SelectMany(g=>g.Tools).ToArray();
            Check(documentIcons.Select(t=>t.Icon.ToString(System.Globalization.CultureInfo.InvariantCulture)).Distinct().Count()==documentIcons.Length,"Document crop/delete/insert/extract/reorder/merge/split tools use distinct geometry");
            var basicIcons=RibbonLayout.For("기본").SelectMany(g=>g.Tools).ToArray();
            Check(basicIcons.Select(t=>t.Icon.ToString(System.Globalization.CultureInfo.InvariantCulture)).Distinct().Count()==basicIcons.Length,"Basic hand/select/copy/paste/undo/redo/fit tools use distinct geometry");
            var homeIcons=RibbonLayout.For("홈").SelectMany(g=>g.Tools).ToDictionary(t=>t.Caption,t=>t.Icon);
            var commentIcons=RibbonLayout.For("주석").SelectMany(g=>g.Tools).ToDictionary(t=>t.Caption,t=>t.Icon);
            Check(new[]{"노트","텍스트 상자","강조 표시","밑줄","취소선"}.All(c=>ReferenceEquals(homeIcons[c],commentIcons[c])),"Same annotation command keeps identical geometry across Home and Comment");
            var viewIcons=viewRibbonButtons.Select(b=>Descendants<System.Windows.Shapes.Path>(b).Single().Data).ToArray();
            Check(viewIcons.Select(icon=>icon.ToString(System.Globalization.CultureInfo.InvariantCulture)).Distinct().Count()==8,"View fit-page/fit-width/single/continuous/thumbnails tools use distinct geometry");
            foreach(var tabCapture in new[]{("파일",vm.FileTab),("홈",vm.HomeTab),("기본",vm.BasicTab),("주석",vm.AnnotationTab),("문서",vm.DocumentTab),("보기",vm.ViewTab),("도움말",vm.ToolsTab)}) {
                tabCapture.Item2.Execute(null);Layout(1280,820);
                Capture(root,1280,820,1,Path.Combine(output,$"ribbon-{tabCapture.Item1}.png"));
            }
            vm.HomeTab.Execute(null);vm.ToolsTab.Execute(null);Layout(1280,820);
            var helpRibbonButtons=Descendants<Button>(window.HelpRibbonPanel).ToArray();
            Check(helpRibbonButtons.Length==4&&helpRibbonButtons.All(HasIconAboveCaption),"Help ribbon keeps contributed and app-info captioned vector-icon buttons with icons above captions");
            var semanticIcons=new[]{"홈","기본","주석","문서"}.SelectMany(tab=>RibbonLayout.For(tab)).SelectMany(g=>g.Tools)
                .Select(t=>(Caption:t.Caption,Shape:t.Icon.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .Concat(fileRibbonButtons.Concat(viewRibbonButtons).Concat(helpRibbonButtons).Select(b=>(Caption:Descendants<TextBlock>(b).Single().Text,Shape:Descendants<System.Windows.Shapes.Path>(b).Single().Data.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
            Check(semanticIcons.GroupBy(t=>t.Shape).All(g=>g.Select(t=>t.Caption).Distinct().Count()==1),"Different ribbon commands never reuse identical path data across all seven tabs");
            Check(semanticIcons.GroupBy(t=>t.Caption).All(g=>g.Select(t=>t.Shape).Distinct().Count()==1),"The same ribbon command retains consistent path data across tabs");
            Check(helpRibbonButtons[0].Command==vm.ModuleCommands[0].Run&&helpRibbonButtons[1].Command is null&&helpRibbonButtons[2].Command==vm.ShowModules&&helpRibbonButtons[3].Command==vm.ShowActivity,"Help ribbon retains contributed command and app-info bindings");
            Check(window.NavigationRail.Visibility==Visibility.Collapsed,"Empty home has no phantom navigation rail space");
            Check(vm.RibbonVisible&&vm.IsToolsTab,"Tools tab reveals contributed commands");
            var toolButton=Descendants<Button>(window.ModuleCommandItems).Single();
            Check(toolButton.Command==vm.ModuleCommands[0].Run,"Ribbon button binds to contributed command, not hardcoded feature");
            toolButton.Command.Execute(null);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(vm.Activity.Count==1&&vm.Activity[0].Success,"Ribbon execution produces real activity result");
            vm.ShowModules.Execute(null);Layout(1280,820);
            vm.Modules[0].Open.Execute(null);Layout(1280,820);
            Check(!vm.IsHome&&vm.ActiveView is not null&&!vm.RibbonVisible,"Module screen opens and collapses ribbon");
            var firstView=vm.ActiveView!;
            var run=(Button)firstView.FindName("RunButton");run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(vm.Activity.Count==2&&vm.Activity[0].Success,"Module screen uses same execution and activity route");
            Check(((TextBlock)firstView.FindName("ResultText")).Text.Contains("2회"),"Module view displays actual second result");
            vm.Home.Execute(null);Layout(1280,820);
            Check(vm.IsHome&&!vm.SidebarVisible&&!vm.RibbonVisible,"Return home restores centered empty layout");
            vm.Modules[0].Open.Execute(null);Layout(1280,820);
            Check(ReferenceEquals(firstView,vm.ActiveView),"Returning to module preserves its existing view state");
            vm.ShowActivity.Execute(null);Layout(1280,820);
            Check(vm.IsActivityPanel&&vm.HasActivity,"Activity panel displays command receipts");
            Capture(root,1280,820,1,Path.Combine(output,"workspace-module.png"));

            // A panel resize is routed through the same DragCompleted handler as the splitter.
            window.SidebarColumn.Width=new GridLength(340);Layout(1280,820);
            window.PanelSplitter.RaiseEvent(new DragCompletedEventArgs(80,0,false){RoutedEvent=Thumb.DragCompletedEvent});Layout(1280,820);
            Check(Math.Abs(window.SidebarColumn.ActualWidth-340)<2,"Splitter completion handler retains programmatically assigned width (not native drag)");
            vm.ToggleSidebar.Execute(null);Layout(1280,820);vm.ToggleSidebar.Execute(null);Layout(1280,820);
            Check(Math.Abs(window.SidebarColumn.ActualWidth-340)<2,"Panel width preserved across hide/show");
            vm.ResetLayout.Execute(null);Layout(1280,820);
            Check(ReferenceEquals(firstView,vm.ActiveView)&&!vm.SidebarVisible&&!vm.RibbonVisible,"Reset affects layout, not module work");
            vm.ShowModules.Execute(null);Layout(1280,820);
            Check(Math.Abs(window.SidebarColumn.ActualWidth-260)<2,"Reset restores default panel width");

            foreach(var size in new[]{(640,480),(800,600),(1280,820),(1920,1080),(2560,1440)}) {
                foreach(var tab in new[]{vm.FileTab,vm.HomeTab,vm.BasicTab,vm.AnnotationTab,vm.DocumentTab,vm.ViewTab,vm.ToolsTab}) {
                    vm.ShowModules.Execute(null);tab.Execute(null);
                    if(!vm.RibbonVisible)vm.ToggleRibbon.Execute(null);
                    Layout(size.Item1,size.Item2);
                    Check(window.WorkAreaGrid.ActualWidth>=320&&window.WorkAreaGrid.ActualHeight>=120,$"Usable workspace {size} / {vm.ActiveTab}");
                    foreach(var button in Descendants<Button>(window.HeaderBar).Where(b=>window.QuickAccessBar.Visibility==Visibility.Visible||!Descendants<Button>(window.QuickAccessBar).Contains(b))){
                        var b=Bounds(button,root);Check(b.Left>=-.5&&b.Right<=size.Item1+.5&&b.Top>=-.5,$"Header control in bounds {size} / {button.Content}");
                    }
                    Check(window.HeaderBar.ActualHeight<=35&&window.ShellRoot.RowDefinitions[0].ActualHeight==0,$"Tab strip and quick actions occupy one row without an empty top strip {size}");
                    Check((size.Item1<700?window.QuickAccessOverflow.Visibility==Visibility.Visible&&window.QuickAccessBar.Visibility==Visibility.Collapsed:window.QuickAccessBar.Visibility==Visibility.Visible&&window.QuickAccessOverflow.Visibility==Visibility.Collapsed),$"Quick actions overflow instead of wrapping at narrow widths {size}");
                    Check(window.RibbonPanel.ActualHeight<=182,$"Ribbon height bounded {size} / {vm.ActiveTab}");
                    Check(window.SidebarColumn.ActualWidth<=size.Item1-325,$"Sidebar leaves document space {size}");
                }
            }
            Layout(640,480);
            Check(window.ModuleViewport.ScrollableHeight>0,"Small-window module content remains vertically scrollable");
            Check(window.RailViewport.ViewportWidth>=38&&window.RailViewport.ExtentWidth<=window.RailViewport.ViewportWidth+.5,"Narrow navigation rail keeps complete icon width while scrolling vertically");
            Capture(root,640,480,1,Path.Combine(output,"workspace-narrow.png"));
            vm.HomeTab.Execute(null);if(!vm.RibbonVisible)vm.ToggleRibbon.Execute(null);Layout(640,480);
            Capture(root,640,480,1,Path.Combine(output,"nes-style-narrow-home.png"));
            vm.HomeTab.Execute(null);if(!vm.RibbonVisible)vm.ToggleRibbon.Execute(null);vm.ShowModules.Execute(null);Layout(1280,820);
            Check(window.NavigationRail.ActualWidth==39&&window.NavigationRail.Visibility==Visibility.Visible,"Module workspace has the narrow vertical navigation rail");
            Capture(root,1280,820,1,Path.Combine(output,"workspace-ribbon.png"));
            vm.Home.Execute(null);vm.HomeTab.Execute(null);if(!vm.RibbonVisible)vm.ToggleRibbon.Execute(null);Layout(1280,820);
            Capture(root,1280,820,1,Path.Combine(output,"nes-style-home-ribbon.png"));
            vm.Home.Execute(null);Layout(1280,820);
            foreach(var scale in new[]{1.0,1.25,1.5,2.0}){
                Capture(root,1280,820,scale,Path.Combine(output,$"workspace-home-{scale:0.00}x.png"));
                Check(window.HomeScreen.ActualWidth>1200,$"Raster scale {scale} preserves logical layout (not physical DPI)");
            }
            Check(((MenuItem)window.QuickAccessOverflowMenu.Items[0]).Command==vm.Home&&((MenuItem)window.QuickAccessOverflowMenu.Items[1]).Command==vm.OpenPreview,"Narrow quick-access menu retains the same start/sample commands");
            var shortcut=window.InputBindings.OfType<KeyBinding>().Single(k=>k.Key==Key.F4);
            Check(shortcut.Command==vm.ToggleSidebar,"F4 bound to shared panel action");shortcut.Command.Execute(null);Layout(1280,820);
            Check(vm.SidebarVisible,"F4 command route toggles sidebar");
            window.InputBindings.OfType<KeyBinding>().Single(k=>k.Key==Key.Home).Command.Execute(null);Layout(1280,820);
            Check(vm.IsHome&&!vm.SidebarVisible,"Ctrl+Home command route resets home layout");
            vm.ClearActivity.Execute(null);Check(vm.Activity.Count==0&&!vm.HasActivity,"Explicit clear affects activity only");
            for(var i=0;i<105;i++)await vm.ExecuteModuleAsync("foundation.check");
            Check(vm.Activity.Count==100,"Activity memory bounded to latest 100 commands");

            var gate=new TaskCompletionSource<CommandResult>();
            var testRegistry=new ModuleRegistry();
            testRegistry.Register(new TestModule("slow",new TestCommand("slow.run",()=>gate.Task)));
            testRegistry.Register(new TestModule("fail",new TestCommand("fail.run",()=>throw new InvalidOperationException("test failure"))));
            var tested=new ShellViewModel(testRegistry,new ModuleLoadReport(new[]{"slow","fail"},Array.Empty<string>()));
            tested.Modules[1].Open.Execute(null);
            Check(tested.ScreenTitle=="fail","Multiple modules: requested module selected, not first module");
            var pending=tested.ExecuteModuleAsync("slow.run");
            Check(tested.IsBusy&&!tested.Home.CanExecute(null)&&tested.ModuleCommands.All(c=>!c.Run.CanExecute(null))&&tested.Modules.All(m=>!m.Open.CanExecute(null)),"Busy command disables screen switching and duplicate execution; layout controls stay available");
            Check(!(await tested.ExecuteModuleAsync("slow.run")).Success,"Second module request rejected while busy");
            gate.SetResult(new(true,"finished"));await pending;
            Check(!tested.IsBusy&&tested.Home.CanExecute(null),"Command completion restores UI availability");
            Check(!(await tested.ExecuteModuleAsync("fail.run")).Success&&!tested.Activity[0].Success&&!tested.IsBusy,"Failure visible in activity and UI recovers");
            Check(!(await tested.ExecuteModuleAsync("unknown.run")).Success,"Unknown command is not reported as success");

            // Interactive mock document workspace: UI state only, never a PDF engine.
            vm.OpenPreview.Execute(null);Layout(1280,820);
            var preview=vm.Preview;
            Check(vm.PreviewActive&&!vm.IsHome&&preview.DocumentCount==2,"Sample open creates two labeled UI-only documents");
            Check(window.PreviewHost.ActualWidth>1200&&window.PreviewHost.ActualHeight>300,"Sample workspace occupies document region, not diagnostic scroll container");
            preview.SelectDocument(0);preview.GoToPage(2);Layout(1280,820);
            Check(preview.PageNumber==2&&preview.PageCount==3,"Sample page navigation updates selected page");
            var zoomBefore=preview.ZoomPercent;vm.ZoomIn.Execute(null);Layout(1280,820);
            Check(preview.ZoomPercent>zoomBefore,"Zoom button changes actual sample visual scale");
            var rememberedZoom=preview.ZoomPercent;
            preview.SelectDocument(1);preview.GoToPage(3);Layout(1280,820);
            preview.SelectDocument(0);Layout(1280,820);
            Check(preview.PageNumber==2&&preview.ZoomPercent==rememberedZoom,"Document tab returns to its own page and zoom state");
            vm.ContinuousPages.Execute(null);Layout(1280,820);Check(preview.Continuous,"Continuous sample view selectable");
            vm.SinglePage.Execute(null);Layout(1280,820);Check(!preview.Continuous,"Single sample view selectable");
            var navBefore=preview.NavigationVisible;vm.ToggleSidebar.Execute(null);Layout(1280,820);
            Check(preview.NavigationVisible!=navBefore,"F4 shared route toggles sample navigation, not diagnostic sidebar");
            vm.Thumbnails.Execute(null);Layout(1280,820);Check(preview.NavigationVisible,"Thumbnail command reveals sample navigation");
            vm.Bookmarks.Execute(null);Layout(1280,820);Check(preview.NavigationVisible,"Bookmark command reveals sample navigation");
            preview.GoToPage(999);Check(preview.PageNumber==3,"Sample page input clamps to last page");
            preview.GoToPage(-1);Check(preview.PageNumber==1,"Sample page input clamps to first page");
            vm.Home.Execute(null);Layout(1280,820);Check(!vm.PreviewActive&&vm.IsHome&&preview.DocumentCount==2,"Home keeps open sample tabs without pretending to save");
            vm.OpenPreview.Execute(null);Layout(1280,820);Check(preview.DocumentCount==2,"Reopening sample workspace does not duplicate tabs");
            preview.SelectDocument(0);preview.GoToPage(1);preview.ShowThumbnails();preview.FitPage();Layout(1280,820);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout(1280,820);
            Capture(root,1280,820,1,Path.Combine(output,"ui-prototype-desktop.png"));
            Layout(640,480);Check(window.PreviewHost.ActualWidth>=630&&window.PreviewHost.ActualHeight>=120,"Small-window sample host retains a usable region");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout(640,480);
            Capture(root,640,480,1,Path.Combine(output,"ui-prototype-narrow.png"));
            Layout(1280,820);vm.CloseDocument.Execute(null);Layout(1280,820);Check(preview.DocumentCount==1&&vm.PreviewActive,"Close removes only the current sample tab");
            vm.CloseDocument.Execute(null);Layout(1280,820);Check(preview.DocumentCount==0&&vm.IsHome&&!vm.PreviewActive,"Closing last sample returns to empty start screen");
            vm.OpenPreview.Execute(null);Layout(1280,820);Check(preview.DocumentCount==2,"Closed samples can be recreated for another UX trial");

            // Thumbnail selection uses arranged WPF card geometry, including wrapped rows.
            async Task ThumbnailLayout(){Layout(1280,820);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout(1280,820);}
            preview.ShowThumbnails();preview.SelectDocument(0);preview.SetThumbnailSize(180);preview.SetNavigationWidth(200);await ThumbnailLayout();
            var thumbWidthNarrow=preview.ThumbnailCardBounds[0].Width;
            preview.SetNavigationWidth(240);await ThumbnailLayout();
            Check(preview.ThumbnailCardBounds[0].Width>thumbWidthNarrow+15,"Thumbnail paper and card grow with sidebar width within a single column");
            preview.SetNavigationWidth(340);preview.SetThumbnailSize(64);await ThumbnailLayout();
            var smallCards=preview.ThumbnailCardBounds.ToArray();
            Check(smallCards[0].Width<120&&Math.Abs(smallCards[0].Top-smallCards[1].Top)<1,"Small thumbnail size forms a genuine multi-column row");
            Check(smallCards.All(r=>r.Left>=12&&r.Right<=preview.NavigationViewportWidth-11),"Responsive cards preserve blank side gutters and fit inside viewport");
            preview.SelectThumbnailRegion(smallCards[1]);
            Check(preview.SelectedPages.SequenceEqual(new[]{2}),"Rectangle selection uses both axes in a wrapped thumbnail row");
            preview.SetThumbnailSize(180);await ThumbnailLayout();
            Check(preview.ThumbnailCardBounds[0].Width>smallCards[0].Width+60,"Thumbnail size control changes rendered card size");
            preview.SelectThumbnail(1);preview.SelectThumbnail(3,control:true);
            Check(preview.SelectedPages.SequenceEqual(new[]{1,3})&&preview.PageNumber==1,"Ctrl click selects nonadjacent pages without changing viewed page");
            preview.SelectThumbnail(1,control:true);
            Check(preview.SelectedPages.SequenceEqual(new[]{3}),"Ctrl click toggles an already-selected page off");
            preview.SelectThumbnail(1);preview.SelectThumbnail(3,shift:true);
            Check(preview.SelectedPages.SequenceEqual(new[]{1,2,3})&&preview.PageNumber==1,"Shift click selects an anchored range separately from current page");
            preview.SelectDocument(1);preview.SelectThumbnail(2);preview.SelectDocument(0);await ThumbnailLayout();
            Check(preview.SelectedPages.SequenceEqual(new[]{1,2,3}),"Each sample document retains its own page selection");
            preview.SetNavigationWidth(240);preview.SetThumbnailSize(180);await ThumbnailLayout();
            var cards=preview.ThumbnailCardBounds.ToArray();
            var firstTwo=new Rect(cards[0].Left-3,cards[0].Top-3,cards[0].Width+6,cards[1].Bottom-cards[0].Top+6);
            var viewedPageBefore=preview.PageNumber;preview.SelectThumbnailRegion(firstTwo);
            Check(preview.SelectedPages.SequenceEqual(new[]{1,2})&&preview.PageNumber==viewedPageBefore,"Blank-area rectangle selects intersecting cards without page navigation");
            preview.SelectThumbnailRegion(new Rect(1,1,5,5));
            Check(preview.SelectedPages.Count==0&&preview.PageNumber==viewedPageBefore,"Empty gutter rectangle clears selection without moving current view");
            preview.SelectThumbnail(3);preview.SelectThumbnailRegion(firstTwo,additive:true);
            Check(preview.SelectedPages.SequenceEqual(new[]{1,2,3}),"Additive rectangle selection unions with existing page selection");
            preview.ShowBookmarks();preview.GoToPage(2);preview.ShowThumbnails();await ThumbnailLayout();
            Check(preview.SelectedPages.SequenceEqual(new[]{1,2,3}),"Bookmark navigation does not destroy thumbnail multi-selection");
            var selectionSurface=Descendants<Grid>(preview).Single(g=>g.Name=="ThumbnailSelectionSurface");
            Check(selectionSurface.Background is not null&&selectionSurface.Focusable&&selectionSurface.ActualHeight>0,"Blank selection surface is hit-testable and focusable, not a stretched page button");
            var actualCards=preview.ThumbnailCardBounds;
            Check(actualCards[0].Top>=12&&actualCards[0].Left>=12,"Top and side whitespace remain available for a marquee start");
            preview.SelectThumbnail(1);preview.SelectThumbnail(2,control:true);preview.SetNavigationWidth(340);preview.SetThumbnailSize(112);await ThumbnailLayout();
            vm.HomeTab.Execute(null);if(!vm.RibbonVisible)vm.ToggleRibbon.Execute(null);await ThumbnailLayout();
            Capture(root,1280,820,1,Path.Combine(output,"thumbnail-selection-desktop.png"));
            Layout(640,480);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout(640,480);
            Check(preview.ThumbnailCardBounds.All(r=>r.Right<=preview.NavigationViewportWidth+0.5),"Resized small window keeps thumbnail hit boxes inside visible width");
            var thumbnailScroller=Descendants<ScrollViewer>(preview).Single(s=>System.Windows.Automation.AutomationProperties.GetAutomationId(s)=="NavigationScroll");
            Check(thumbnailScroller.ViewportHeight>=60,"Minimum-size window leaves an actual thumbnail selection viewport with ribbon expanded");
            Capture(root,640,480,1,Path.Combine(output,"thumbnail-selection-narrow.png"));

            // Page management: identity-preserving in-memory reorder, not PDF editing.
            CloseAllSamplesForTest(preview);vm.OpenPreview.Execute(null);preview.ShowThumbnails();preview.SetNavigationWidth(240);preview.SetThumbnailSize(180);await ThumbnailLayout();
            var originalPageIds=preview.PageIdentityOrder.ToArray();
            Check(preview.ThumbnailPageLabels.SequenceEqual(new[]{"1","2","3"}),"Thumbnail captions contain page numbers only, without chapter titles");
            var reorderCards=preview.ThumbnailCardBounds.ToArray();
            var columnX=reorderCards[0].Left+reorderCards[0].Width/2;
            Check(preview.GetThumbnailInsertionSlot(new Point(columnX,reorderCards[0].Top-5))==0,"Insertion target before first page is gap zero");
            Check(preview.GetThumbnailInsertionSlot(new Point(columnX,(reorderCards[0].Bottom+reorderCards[1].Top)/2))==1,"Single-column page gap maps to insertion between first and second pages");
            Check(preview.GetThumbnailInsertionSlot(new Point(columnX,reorderCards[2].Bottom+5))==3,"Insertion target after last page is final gap");
            Check(preview.GetThumbnailInsertionSlot(new Point(-10,-10))==-1,"Outside navigation coordinates are not valid drop targets");
            preview.SelectThumbnail(1);Check(preview.MoveSelectedPages(3),"Selected first page moves to end");await ThumbnailLayout();
            Check(preview.PageIdentityOrder.SequenceEqual(new[]{originalPageIds[1],originalPageIds[2],originalPageIds[0]}),"Reorder changes content identity order, not just displayed numbers");
            Check(preview.PageNumber==3&&preview.SelectedPages.SequenceEqual(new[]{3})&&preview.PageIdentityOrder[preview.PageNumber-1]==originalPageIds[0],"Viewed content and selected identity follow their new page positions");
            Check(preview.ThumbnailPageLabels.SequenceEqual(new[]{"1","2","3"}),"Page captions renumber after content moves");
            Check(preview.MoveSelectedPages(0),"Selected last page moves to the very front");await ThumbnailLayout();
            Check(preview.PageIdentityOrder.SequenceEqual(originalPageIds)&&preview.PageNumber==1&&preview.SelectedPages.SequenceEqual(new[]{1}),"Front insertion restores content order and preserves viewed identity");
            preview.SelectThumbnail(3);Check(preview.MoveSelectedPages(1),"Selected last page moves between first and second pages");await ThumbnailLayout();
            Check(preview.PageIdentityOrder.SequenceEqual(new[]{originalPageIds[0],originalPageIds[2],originalPageIds[1]}),"Middle insertion uses the original document gap correctly");
            var beforeMulti=preview.PageIdentityOrder.ToArray();preview.SelectThumbnail(1);preview.SelectThumbnail(3,control:true);
            Check(preview.MoveSelectedPages(3),"Nonadjacent selected pages can move as one group");await ThumbnailLayout();
            Check(preview.PageIdentityOrder.SequenceEqual(new[]{beforeMulti[1],beforeMulti[0],beforeMulti[2]})&&preview.SelectedPages.SequenceEqual(new[]{2,3}),"Multi-page move preserves original relative order and selected identities");
            var afterMulti=preview.PageIdentityOrder.ToArray();
            Check(!preview.MoveSelectedPages(3)&&preview.PageIdentityOrder.SequenceEqual(afterMulti),"Dropping the trailing selected group in its existing slot is a no-op");
            Check(!preview.MoveSelectedPages(-1)&&!preview.MoveSelectedPages(4)&&preview.PageIdentityOrder.SequenceEqual(afterMulti),"Invalid insertion slots do not mutate page order");
            preview.SelectThumbnail(1);preview.SelectThumbnail(3,shift:true);
            Check(!preview.MoveSelectedPages(2)&&preview.PageIdentityOrder.SequenceEqual(afterMulti),"Moving every selected page cannot scramble the document");
            preview.SelectDocument(1);var otherPageIds=preview.PageIdentityOrder.ToArray();preview.SelectDocument(0);await ThumbnailLayout();
            Check(preview.PageIdentityOrder.SequenceEqual(afterMulti),"Reordered sample survives document tab switching");
            preview.SelectDocument(1);Check(preview.PageIdentityOrder.SequenceEqual(otherPageIds),"Reorder in one sample leaves the other sample unchanged");preview.SelectDocument(0);
            preview.SetNavigationWidth(340);preview.SetThumbnailSize(64);await ThumbnailLayout();var wrappedCards=preview.ThumbnailCardBounds.ToArray();
            Check(Math.Abs(wrappedCards[0].Top-wrappedCards[1].Top)<1&&preview.GetThumbnailInsertionSlot(new Point((wrappedCards[0].Right+wrappedCards[1].Left)/2,wrappedCards[0].Top+wrappedCards[0].Height/2))==1,"Wrapped-row gap uses both coordinates for insertion slot");
            var twoRows=new[]{new Rect(18,18,90,140),new Rect(120,18,90,140),new Rect(18,174,90,140),new Rect(120,174,90,140)};
            Check(ThumbnailReorder.FindSlot(twoRows,new Size(240,340),new Point(115,166))==2,"Diagonal blank space beside the second card resolves to its after gap");
            Check(Enumerable.Range(0,twoRows.Length+1).All(slot=>ThumbnailReorder.Marker(twoRows,slot).Width==3),"Every insertion marker is vertical, including wrapped-row boundaries");
            var afterSecond=ThumbnailReorder.Resolve(twoRows,new Size(240,340),new Point(twoRows[1].Right+4,twoRows[1].Top+70));
            var beforeThird=ThumbnailReorder.Resolve(twoRows,new Size(240,340),new Point(twoRows[2].Left-4,twoRows[2].Top+70));
            Check(afterSecond.Slot==2&&afterSecond.CardIndex==1&&afterSecond.After&&beforeThird.Slot==2&&beforeThird.CardIndex==2&&!beforeThird.After,"After page two and before page three share a gap but retain distinct hovered-card anchors");
            Check(ThumbnailReorder.Marker(twoRows,afterSecond).Left>twoRows[1].Right&&ThumbnailReorder.Marker(twoRows,afterSecond).Top==twoRows[1].Top&&ThumbnailReorder.Marker(twoRows,beforeThird).Right<twoRows[2].Left&&ThumbnailReorder.Marker(twoRows,beforeThird).Top==twoRows[2].Top,"Wrapped boundary marker follows the chosen card side, never the whole row");
            var endpointLayouts=new[]{twoRows,new[]{new Rect(18,18,100,140),new Rect(18,174,100,140),new Rect(18,330,100,140)},new[]{twoRows[0],twoRows[1],twoRows[2]}};
            foreach(var endpointCards in endpointLayouts){
                for(var cardIndex=0;cardIndex<endpointCards.Length;cardIndex++){
                    var card=endpointCards[cardIndex];
                    var before=ThumbnailReorder.Resolve(endpointCards,new Size(400,520),new Point(card.Left+card.Width*.25,card.Top+card.Height/2));
                    var after=ThumbnailReorder.Resolve(endpointCards,new Size(400,520),new Point(card.Left+card.Width*.75,card.Top+card.Height/2));
                    Check(before.Slot==cardIndex&&!before.After&&after.Slot==cardIndex+1&&after.After,"Every card in single, full and ragged rows uses left/right halves for before/after");
                    Check(ThumbnailReorder.Marker(endpointCards,before).Width==3&&ThumbnailReorder.Marker(endpointCards,after).Width==3,"No single-column or wrapped-row card can produce a horizontal marker");
                }
                var firstEdge=ThumbnailReorder.Marker(endpointCards,0);var lastEdge=ThumbnailReorder.Marker(endpointCards,endpointCards.Length);
                Check(firstEdge.Width==3&&firstEdge.Right<endpointCards[0].Left&&firstEdge.Top==endpointCards[0].Top&&firstEdge.Height==endpointCards[0].Height,"First-page marker is vertically beside that card, never above the row");
                Check(lastEdge.Width==3&&lastEdge.Left>endpointCards[^1].Right&&lastEdge.Top==endpointCards[^1].Top&&lastEdge.Height==endpointCards[^1].Height,"Last-page marker is vertically beside that card, never below the row");
                Check(ThumbnailReorder.FindSlot(endpointCards,new Size(400,520),new Point(endpointCards[0].Left-6,endpointCards[0].Top+endpointCards[0].Height/2))==0,"First-card left gutter hit agrees with the displayed start marker");
                Check(ThumbnailReorder.FindSlot(endpointCards,new Size(400,520),new Point(endpointCards[^1].Right+6,endpointCards[^1].Top+endpointCards[^1].Height/2))==endpointCards.Length,"Last-card right gutter hit agrees with the displayed end marker");
            }
            preview.SelectThumbnail(2);preview.SetThumbnailSize(112);await ThumbnailLayout();
            Capture(root,1280,820,1,Path.Combine(output,"page-manager-reordered.png"));
            Check(preview.PreviewInsertionSlot(1)&&preview.ThumbnailInsertionSlot==1&&!preview.ThumbnailInsertionMarkerBounds.IsEmpty,"Red insertion marker uses the same rendered geometry as drag targets");await ThumbnailLayout();
            Capture(root,1280,820,1,Path.Combine(output,"page-manager-insertion-line.png"));
            preview.PreviewInsertionSlot(0);await ThumbnailLayout();Capture(root,1280,820,1,Path.Combine(output,"page-manager-first-edge.png"));
            preview.PreviewInsertionSlot(preview.PageCount);await ThumbnailLayout();Capture(root,1280,820,1,Path.Combine(output,"page-manager-last-edge.png"));
            var pointerCards=preview.ThumbnailCardBounds;
            preview.PreviewInsertionPoint(new Point(pointerCards[1].Right+4,pointerCards[1].Top+pointerCards[1].Height/2));await ThumbnailLayout();
            Check(preview.ThumbnailInsertionSlot==2&&preview.ThumbnailInsertionMarkerBounds.Left>pointerCards[1].Right&&preview.ThumbnailInsertionMarkerBounds.Top==pointerCards[1].Top,"Actual preview pointer route puts the after-page-two marker at page two's right edge");
            Capture(root,1280,820,1,Path.Combine(output,"page-manager-after-second.png"));
            preview.PreviewInsertionPoint(new Point(pointerCards[2].Left-4,pointerCards[2].Top+pointerCards[2].Height/2));await ThumbnailLayout();
            Check(preview.ThumbnailInsertionSlot==2&&preview.ThumbnailInsertionMarkerBounds.Right<pointerCards[2].Left&&preview.ThumbnailInsertionMarkerBounds.Top==pointerCards[2].Top,"Actual preview pointer route puts the before-page-three marker at page three's left edge");
            Capture(root,1280,820,1,Path.Combine(output,"page-manager-before-third.png"));
            preview.ClearInsertionSlot();Check(preview.ThumbnailInsertionSlot==-1&&preview.ThumbnailInsertionMarkerBounds.IsEmpty,"Insertion marker clears cleanly after completion/cancel");
            Check(!preview.PreviewInsertionSlot(-1)&&preview.ThumbnailInsertionSlot==-1,"Invalid insertion preview cannot leave a stale red marker");

            await CheckPageManagement(window,root,Check,output,ThumbnailLayout);
            await CheckSampleClosing(Check,output);
            File.WriteAllText(Path.Combine(output,"smoke.json"),JsonSerializer.Serialize(new{Passed=true,Count=checks.Count,Checks=checks,Scope="Real offscreen WPF content/layout, DLL commands, binding routes, deterministic synthetic failures; no native keyboard/mouse, physical monitor DPI, PDF or installer validation"},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());return 1;}
    }
    private static Rect Bounds(FrameworkElement element,Visual root)=>element.TransformToAncestor(root).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T item)yield return item;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
    private static void Capture(Visual root,int width,int height,double scale,string path)
    {
        var bitmap=new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    private sealed class TestCommand(string id,Func<Task<CommandResult>> run):IWorkspaceCommand
    {
        public string Id=>id;public string Caption=>id;public Task<CommandResult> ExecuteAsync(CancellationToken token)=>run();
    }
    private sealed class TestModule(string id,IWorkspaceCommand command):IWorkspaceModule,IModuleViewProvider
    {
        public ModuleDescriptor Descriptor=>new(id,id,new Version(0,1,0),ModuleApi.Version);
        public IReadOnlyList<IWorkspaceCommand> Commands=>new[]{command};
        public FrameworkElement CreateView(Func<string,Task<CommandResult>> execute)=>new TextBlock{Text=id};
    }
}
