using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;

internal static partial class ShellSmokeChecks
{
    private static async Task CheckPageManagement(MainWindow window,FrameworkElement root,Action<bool,string> check,string output,Func<Task> layout)
    {
        var vm=window.Model;var preview=vm.Preview;
        async Task Reset(){CloseAllSamplesForTest(preview);vm.OpenPreview.Execute(null);preview.SelectDocument(0);preview.ShowThumbnails();preview.SetNavigationWidth(340);preview.SetThumbnailSize(112);await layout();}
        MenuItem Item(ContextMenu menu,string id)=>menu.Items.OfType<MenuItem>().Single(item=>AutomationProperties.GetAutomationId(item)==id);
        void Invoke(ContextMenu menu,string id){var item=Item(menu,id);check(item.IsEnabled,"Context operation enabled: "+id);item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,item));}
        await Reset();
        var original=preview.PageIdentityOrder.ToArray();
        check(original.Length==3&&!preview.CanUndo&&!preview.CanRedo&&!window.QuickUndoButton.IsEnabled&&!window.QuickRedoButton.IsEnabled,"Fresh samples have independent empty history and disabled undo/redo icons");
        check(window.InputBindings.OfType<KeyBinding>().Any(k=>k.Key==Key.Z&&k.Modifiers==ModifierKeys.Control&&k.Command==vm.Undo)&&window.InputBindings.OfType<KeyBinding>().Any(k=>k.Key==Key.Y&&k.Modifiers==ModifierKeys.Control&&k.Command==vm.Redo)&&window.InputBindings.OfType<KeyBinding>().Any(k=>k.Key==Key.Z&&k.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&k.Command==vm.Redo),"Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z bind shared sample history commands");
        preview.SelectThumbnail(1);check(preview.MoveSelectedPages(3),"Sample reorder creates a history transaction");await layout();
        check(preview.CanUndo&&!preview.CanRedo&&window.QuickUndoButton.IsEnabled&&!window.QuickRedoButton.IsEnabled&&vm.UndoToolTip.Contains(preview.UndoDescription),"Mutation refreshes real toolbar availability and action tooltip");
        vm.Undo.Execute(null);await layout();
        check(preview.PageIdentityOrder.SequenceEqual(original)&&preview.SelectedPages.SequenceEqual(new[]{1})&&preview.PageNumber==1&&preview.CanRedo,"Toolbar undo restores order, selection and viewed identity");
        vm.Redo.Execute(null);await layout();
        check(preview.PageIdentityOrder.SequenceEqual(new[]{original[1],original[2],original[0]})&&preview.SelectedPages.SequenceEqual(new[]{3})&&preview.PageNumber==3,"Toolbar redo restores the same moved identities");
        preview.SelectDocument(1);await layout();check(!preview.CanUndo&&!preview.CanRedo&&!vm.Undo.CanExecute(null),"Other document never inherits first document history");
        preview.SelectDocument(0);await layout();check(preview.CanUndo&&vm.Undo.CanExecute(null),"History availability returns on document switch");
        preview.Undo();await layout();var redoLabel=preview.RedoDescription;
        check(!preview.MoveSelectedPages(0)&&preview.CanRedo&&preview.RedoDescription==redoLabel,"No-op reorder preserves redo branch");

        preview.SelectThumbnail(1);preview.SelectThumbnail(3,control:true);preview.GoToPage(2);await layout();
        var groupMenu=preview.CreatePageContextMenu(3);
        check(preview.SelectedPages.SequenceEqual(new[]{1,3})&&preview.PageNumber==2&&Item(groupMenu,"PageSelectionSummary").Header.ToString()!.Contains("1, 3"),"Right-click on selected card preserves group, viewed page and numbered summary");
        preview.CreatePageContextMenu();check(preview.SelectedPages.SequenceEqual(new[]{1,3}),"Blank-space context menu preserves selected group");
        preview.CreatePageContextMenu(2);check(preview.SelectedPages.SequenceEqual(new[]{2})&&preview.PageNumber==2,"Right-click unselected card replaces only management selection");
        preview.SelectThumbnail(1);preview.SelectThumbnail(3,control:true);preview.GoToPage(1);preview.FitPage();await layout();
        var portraitFit=preview.ZoomPercent;
        Invoke(preview.CreatePageContextMenu(3),"PageRotateClockwise");await layout();
        check(preview.PageRotations.SequenceEqual(new[]{90,0,90})&&!preview.CanRedo,"Group clockwise rotation updates only selected pages and invalidates old redo branch");
        var landscapeThumbs=Descendants<Border>(preview).Where(b=>b.LayoutTransform is TransformGroup g&&g.Children.OfType<RotateTransform>().Any(t=>t.Angle==90)&&g.Children.OfType<ScaleTransform>().Any(t=>t.ScaleX<.3)).ToArray();
        check(landscapeThumbs.Length==2&&landscapeThumbs.All(b=>Bounds(b,preview).Width>Bounds(b,preview).Height),"Selected thumbnail papers have actual landscape geometry even when wrap-row card heights match");
        check(Descendants<Border>(preview).Any(b=>b.LayoutTransform is TransformGroup g&&g.Children.OfType<RotateTransform>().Any(t=>t.Angle==90)),"Rendered page tree contains real 90-degree transformation");
        Capture(root,1280,820,1,Path.Combine(output,"page-actions-rotated.png"));
        preview.Undo();await layout();check(preview.PageRotations.All(r=>r==0)&&preview.SelectedPages.SequenceEqual(new[]{1,3})&&preview.ZoomPercent==portraitFit,"Rotation undo restores group and portrait page fit");
        preview.Redo();await layout();check(preview.PageRotations.SequenceEqual(new[]{90,0,90}),"Rotation redo restores orientation snapshot without shallow-copy leakage");
        Invoke(preview.CreatePageContextMenu(),"PageRotateCounterclockwise");await layout();check(preview.PageRotations.All(r=>r==0),"Counterclockwise command returns selected pages to portrait");
        preview.ZoomIn();preview.SetContinuous(true);await layout();var manualZoom=preview.ZoomPercent;
        preview.Undo();await layout();check(preview.ZoomPercent==manualZoom&&preview.Continuous,"Mutation undo preserves manually chosen zoom and continuous mode");
        for(var number=1;number<=3;number++){preview.GoToPage(number);await layout();check(preview.PageNumber==number,"Mixed rotated page heights retain continuous navigation page "+number);}
        preview.SelectThumbnail(2);preview.DeleteSelectedPages();await layout();
        check(preview.PageIdentityOrder.SequenceEqual(new[]{original[0],original[2]})&&preview.PageIdentityOrder[preview.PageNumber-1]==original[2]&&preview.SelectedPages.SequenceEqual(new[]{2}),"Deleting viewed middle page chooses nearest following survivor");
        preview.Undo();await layout();check(preview.PageIdentityOrder.SequenceEqual(original)&&preview.PageNumber==2&&preview.SelectedPages.SequenceEqual(new[]{2}),"Delete undo restores removed content, viewed page and selection");
        preview.SelectThumbnail(1);preview.SelectThumbnail(preview.PageCount,shift:true);var historyBeforeBlocked=preview.UndoDescription;
        var allMenu=preview.CreatePageContextMenu(2);
        check(!preview.CanDeleteSelectedPages&&!Item(allMenu,"PageDelete").IsEnabled&&!vm.DeletePages.CanExecute(null)&&!preview.DeleteSelectedPages()&&preview.PageCount==3&&preview.UndoDescription==historyBeforeBlocked,"Deleting all pages is blocked in menu, ribbon command and handler without recording history");
        preview.SelectThumbnail(2);var viewedBeforeInsert=preview.PageIdentityOrder[preview.PageNumber-1];
        Invoke(preview.CreatePageContextMenu(),"PageInsertAfter");await layout();var insertedOrder=preview.PageIdentityOrder.ToArray();
        check(preview.PageCount==4&&insertedOrder[0]==original[0]&&insertedOrder[1]==original[1]&&insertedOrder[3]==original[2]&&preview.SelectedPages.SequenceEqual(new[]{3})&&preview.PageIdentityOrder[preview.PageNumber-1]==viewedBeforeInsert,"Insert-after places one blank after selection while retaining viewed content");
        preview.Undo();await layout();check(preview.PageIdentityOrder.SequenceEqual(original),"Blank insertion undo restores exact original order");
        preview.Redo();await layout();check(preview.PageIdentityOrder.SequenceEqual(insertedOrder)&&preview.PageRotations[2]==0,"Blank insertion redo restores the same blank identity and zero rotation");
        preview.Undo();preview.SelectThumbnail(1);Invoke(preview.CreatePageContextMenu(),"PageInsertBefore");await layout();
        check(preview.PageCount==4&&preview.PageIdentityOrder.Skip(1).SequenceEqual(original)&&preview.SelectedPages.SequenceEqual(new[]{1}),"Insert-before works at the first boundary");
        preview.Undo();await layout();
        preview.SelectThumbnail(1);preview.SelectThumbnail(3,control:true);var sourceIds=preview.PageIdentityOrder.ToArray();var sourceRotations=preview.PageRotations.ToArray();var sourceHistory=preview.UndoDescription;var sourceRedo=preview.RedoDescription;var countBefore=preview.DocumentCount;
        Invoke(preview.CreatePageContextMenu(),"PageExtract");await layout();
        check(preview.DocumentCount==countBefore+1&&preview.PageCount==2&&preview.PageIdentityOrder.SequenceEqual(new[]{sourceIds[0],sourceIds[2]})&&preview.PageRotations.SequenceEqual(new[]{sourceRotations[0],sourceRotations[2]})&&!preview.CanUndo&&!preview.CanRedo,"Extraction copies selected pages in order to independent tab with empty history");
        preview.RotateSelectedPages(true);await layout();var extractedRotations=preview.PageRotations.ToArray();
        preview.SelectDocument(0);await layout();check(preview.PageIdentityOrder.SequenceEqual(sourceIds)&&preview.PageRotations.SequenceEqual(sourceRotations)&&preview.UndoDescription==sourceHistory&&preview.RedoDescription==sourceRedo,"Extracted tab editing cannot mutate source content or its undo/redo stacks");
        preview.SelectDocument(countBefore);await layout();check(preview.PageRotations.SequenceEqual(extractedRotations)&&preview.CanUndo,"Extracted tab keeps its own edit history across tab switch");
        preview.CloseCurrent();preview.SelectDocument(0);await layout();check(preview.DocumentCount==countBefore&&preview.PageIdentityOrder.SequenceEqual(sourceIds),"Closing extracted mock tab leaves source unchanged");
        var staleMenu=preview.CreatePageContextMenu(1);preview.SelectDocument(1);var otherRotations=preview.PageRotations.ToArray();
        Invoke(staleMenu,"PageRotateClockwise");check(preview.PageRotations.SequenceEqual(otherRotations),"Stale context menu cannot act on another active document");

        await Reset();preview.SelectThumbnail(1);preview.SelectThumbnail(1,control:true);await layout();
        var emptyMenu=preview.CreatePageContextMenu();check(preview.SelectedPages.Count==0&&!Item(emptyMenu,"PageRotateClockwise").IsEnabled&&!Item(emptyMenu,"PageDelete").IsEnabled&&!Item(emptyMenu,"PageExtract").IsEnabled&&Item(emptyMenu,"PageInsertAfter").IsEnabled,"Empty selection disables selection actions but allows insertion beside viewed page");
        check(!preview.RotateSelectedPages(true)&&!preview.DeleteSelectedPages()&&!preview.ExtractSelectedPages()&&!preview.CanUndo,"Empty selection handlers are no-ops without history");
        preview.SelectThumbnail(1);for(var i=0;i<55;i++)preview.RotateSelectedPages(true);
        var undoCount=0;while(preview.Undo()){undoCount++;if(undoCount>60)throw new InvalidOperationException("Unbounded undo");}
        check(undoCount==50&&!preview.CanUndo&&preview.CanRedo,"Per-document undo is bounded to newest 50 transactions");
        var redoCount=0;while(preview.Redo()){redoCount++;if(redoCount>60)throw new InvalidOperationException("Unbounded redo");}
        check(redoCount==50&&preview.PageRotations[0]==270,"All retained undo entries redo to final rotation");
        await layout();

        await Reset();preview.SelectThumbnail(1);preview.SelectThumbnail(3,control:true);preview.RotateSelectedPages(true);await layout();
        vm.DocumentTab.Execute(null);await layout();
        var connected=Descendants<Button>(root).Where(b=>b.Name=="UnavailablePdfTool"&&b.Command!=null).ToArray();
        check(connected.Length==4&&connected.All(b=>b.IsEnabled)&&vm.PreviewGroups.SelectMany(g=>g.Tools).Single(t=>t.Caption=="삭제").Command==vm.DeletePages,"Document ribbon exposes exactly four sample page actions through shared host commands");
        Capture(root,1280,820,1,Path.Combine(output,"page-actions-document-ribbon.png"));
        vm.BasicTab.Execute(null);await layout();check(vm.PreviewGroups.SelectMany(g=>g.Tools).Single(t=>t.Caption=="실행 취소").Command==vm.Undo&&vm.PreviewGroups.SelectMany(g=>g.Tools).Single(t=>t.Caption=="다시 실행").Command==vm.Redo,"Basic ribbon uses same undo/redo commands as top icons and keyboard");
        var menuProof=preview.CreatePageContextMenu(3);menuProof.FontFamily=new FontFamily("Malgun Gothic");menuProof.FontSize=13;menuProof.Measure(new Size(500,900));menuProof.Arrange(new Rect(new Point(),menuProof.DesiredSize));menuProof.UpdateLayout();
        check(menuProof.ActualWidth>200&&menuProof.ActualHeight>200,"Actual context menu control measures to readable nonempty layout");
        Capture(menuProof,(int)Math.Ceiling(menuProof.ActualWidth),(int)Math.Ceiling(menuProof.ActualHeight),1,Path.Combine(output,"page-actions-context-menu.png"));
        Invoke(preview.CreatePageContextMenu(),"PageUndo");await layout();check(preview.PageRotations.All(r=>r==0)&&preview.CanRedo,"Context undo operates the same sample history");
        Invoke(preview.CreatePageContextMenu(),"PageRedo");await layout();check(preview.PageRotations.SequenceEqual(new[]{90,0,90}),"Context redo operates the same sample history");
        CloseAllSamplesForTest(preview);await layout();
        check(!vm.PreviewActive&&!vm.Undo.CanExecute(null)&&!vm.Redo.CanExecute(null)&&!vm.DeletePages.CanExecute(null)&&!vm.InsertPage.CanExecute(null)&&!preview.Undo()&&!preview.Redo(),"Closing every sample disables all history/page actions and safely returns home");
    }
}
