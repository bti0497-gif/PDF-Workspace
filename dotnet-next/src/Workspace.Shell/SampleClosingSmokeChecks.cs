using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;

internal static partial class ShellSmokeChecks
{
    private static void CloseAllSamplesForTest(PreviewWorkspace preview)
    {
        var remaining=preview.DocumentCount;
        while(remaining>0){preview.CloseCurrent();if(preview.DocumentCount>=remaining)throw new InvalidOperationException($"Cleanup close blocked: {preview.ActiveDocumentName}; dirty={preview.IsDirty}; callback={preview.ConfirmClose!=null}");remaining=preview.DocumentCount;}
    }
    private static async Task CheckSampleClosing(Action<bool,string> check,string output)
    {
        var hosts=new Dictionary<PreviewWorkspace,FrameworkElement>();
        ShellViewModel Fresh(){var window=new MainWindow(new ModuleRegistry(),new ModuleLoadReport(Array.Empty<string>(),Array.Empty<string>()));var model=window.Model;model.Preview.RequestSampleName=null;model.Preview.ConfirmClose=_=>SampleCloseDecision.Discard;hosts[model.Preview]=(FrameworkElement)window.Content;model.OpenPreview.Execute(null);return model;}
        async Task Layout(FrameworkElement root,double width=1200,double height=800){if(root is PreviewWorkspace workspace&&hosts.TryGetValue(workspace,out var hostRoot))root=hostRoot;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);root.UpdateLayout();}
        var vm=Fresh();var p=vm.Preview;await Layout(p);
        check(!p.IsDirty&&!p.HasDirtyDocuments&&!vm.SaveSample.CanExecute(null),"Initial samples are clean and no sample save is needed");
        p.GoToPage(2);p.ZoomIn();p.SetContinuous(true);p.SelectThumbnail(1);await Layout(p);
        check(!p.IsDirty,"Viewing, zoom, mode and selection do not mark content dirty");
        p.RotateSelectedPages(true);await Layout(p);
        check(p.IsDirty&&p.HasDirtyDocuments&&vm.SaveSample.CanExecute(null),"Content mutation enables sample save");
        var tab=Descendants<Button>(p).Single(b=>AutomationProperties.GetAutomationId(b)=="SampleDocumentTab0");
        check(tab.Content.ToString()!.EndsWith("*")&&AutomationProperties.GetName(tab).Contains("저장하지 않은"),"Dirty star and accessible tab description match mutation");
        p.Undo();check(!p.IsDirty&&p.CanRedo,"Undo to initial baseline clears dirty without erasing redo");p.Redo();
        vm.SaveSample.Execute(null);check(!p.IsDirty&&p.CanUndo&&!vm.SaveSample.CanExecute(null),"Sample checkpoint clears dirty but preserves undo history");
        p.Undo();check(p.IsDirty,"Undo across saved baseline marks dirty");p.Redo();check(!p.IsDirty,"Redo to saved baseline becomes clean again");
        for(var i=0;i<4;i++)p.RotateSelectedPages(true);check(!p.IsDirty&&p.CanUndo,"Content equality, not transaction count, determines dirty after full rotation cycle");
        p.MoveSelectedPages(3);check(p.IsDirty,"Order mutation marks dirty");p.Undo();check(!p.IsDirty,"Order undo restores saved equality");
        p.InsertBlankPage(true);check(p.IsDirty,"Insertion marks dirty");p.Undo();check(!p.IsDirty,"Insertion undo restores saved equality");
        p.SelectThumbnail(2);p.DeleteSelectedPages();check(p.IsDirty,"Deletion marks dirty");p.Undo();check(!p.IsDirty,"Deletion undo restores saved equality");
        p.SelectThumbnail(1);p.RotateSelectedPages(true);await Layout(p);
        var ids=p.PageIdentityOrder.ToArray();var rotations=p.PageRotations.ToArray();var selected=p.SelectedPages.ToArray();var page=p.PageNumber;var zoom=p.ZoomPercent;var history=p.UndoDescription;
        SampleCloseRequest? last=null;var prompts=0;p.ConfirmClose=request=>{last=request;prompts++;return SampleCloseDecision.Cancel;};
        vm.CloseDocument.Execute(null);check(prompts==1&&last is {IsApplicationExit:false}&&last.DocumentNames.Count==1&&p.DocumentCount==2&&p.IsDirty,"CtrlW command routes dirty document through cancelable single-document request");
        check(p.PageIdentityOrder.SequenceEqual(ids)&&p.PageRotations.SequenceEqual(rotations)&&p.SelectedPages.SequenceEqual(selected)&&p.PageNumber==page&&p.ZoomPercent==zoom&&p.UndoDescription==history,"Cancel preserves content, selection, viewed page, zoom and history");
        p.ConfirmClose=null;p.CloseCurrent();check(p.DocumentCount==2&&p.IsDirty,"Absent confirmation fails closed");
        p.ConfirmClose=_=>throw new InvalidOperationException("synthetic dialog failure");p.CloseCurrent();check(p.DocumentCount==2&&p.IsDirty,"Confirmation exception fails closed");
        p.ConfirmClose=_=>(SampleCloseDecision)99;p.CloseCurrent();check(p.DocumentCount==2&&p.IsDirty,"Unknown confirmation result fails closed");
        prompts=0;p.ConfirmClose=_=>{prompts++;p.CloseCurrent();return SampleCloseDecision.Cancel;};p.CloseCurrent();check(prompts==1&&p.DocumentCount==2,"Nested close cannot recurse or remove a document");
        p.SelectDocument(1);await Layout(p);var originalName=p.DirtyDocumentNames.Single();last=null;
        p.ConfirmClose=request=>{last=request;return SampleCloseDecision.Cancel;};
        var inactiveClose=Descendants<Button>(p).Single(b=>b.ToolTip?.ToString()==originalName+" 닫기");inactiveClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,inactiveClose));
        check(last?.DocumentNames.Single()==originalName&&p.DocumentCount==2&&!p.IsDirty&&p.HasDirtyDocuments,"Inactive dirty tab close asks for correct document without switching clean active tab");
        p.SelectDocument(0);p.ConfirmClose=_=>SampleCloseDecision.Save;p.CloseCurrent();
        check(p.DocumentCount==1&&p.SavedClosedSampleCount==1&&vm.ReopenSavedSample.CanExecute(null),"Save-and-close retains session checkpoint in reopen shelf");
        vm.ReopenSavedSample.Execute(null);await Layout(p);
        check(p.DocumentCount==2&&!p.IsDirty&&!p.CanUndo&&!p.CanRedo&&p.PageIdentityOrder.SequenceEqual(ids)&&p.PageRotations.SequenceEqual(rotations),"Reopen saved sample restores exact content clean with fresh history");
        p.SelectThumbnail(1);p.RotateSelectedPages(true);p.ConfirmClose=_=>SampleCloseDecision.Discard;p.CloseCurrent();p.ReopenLastSavedSample();
        check(!p.IsDirty&&p.PageRotations.SequenceEqual(rotations),"Discard-and-reopen restores checkpoint rather than discarded edits");
        p.SelectThumbnail(1);p.ExtractSelectedPages();check(p.IsDirty&&!p.CanUndo,"New extracted document starts unsaved even with empty history");
        var shelf=p.SavedClosedSampleCount;p.ConfirmClose=_=>SampleCloseDecision.Discard;p.CloseCurrent();check(p.SavedClosedSampleCount==shelf,"Unsaved discarded extraction is not resurrected from saved shelf");
        p.SelectThumbnail(1);p.ExtractSelectedPages();p.SaveCurrentSample();p.CloseCurrent();check(p.SavedClosedSampleCount==shelf+1,"Saved extraction is retained when closed");p.ReopenLastSavedSample();check(p.PageCount==1&&!p.IsDirty&&!p.CanUndo,"Saved extraction reopens independently and clean");

        var registry=new ModuleRegistry();var gate=new TaskCompletionSource<Workspace.Contracts.CommandResult>();registry.Register(new TestModule("close-slow",new TestCommand("close-slow.run",()=>gate.Task)));
        var host=new MainWindow(registry,new ModuleLoadReport(Array.Empty<string>(),Array.Empty<string>()));var model=host.Model;var q=model.Preview;q.RequestSampleName=null;model.OpenPreview.Execute(null);await Layout((FrameworkElement)host.Content);
        q.RotateSelectedPages(true);q.SelectDocument(1);q.RotateSelectedPages(true);var dirty=q.DirtyDocumentNames.ToArray();var current=q.ActiveDocumentName;
        var guard=typeof(MainWindow).GetMethod("GuardClosing",BindingFlags.Instance|BindingFlags.NonPublic)!;
        CancelEventArgs Exit(){var args=new CancelEventArgs();guard.Invoke(host,new object?[]{host,args});return args;}
        prompts=0;q.ConfirmClose=request=>{last=request;prompts++;return SampleCloseDecision.Cancel;};model.Home.Execute(null);var cancelled=Exit();
        check(cancelled.Cancel&&prompts==1&&last is {IsApplicationExit:true}&&last.DocumentNames.SequenceEqual(dirty)&&q.DocumentCount==2&&q.HasDirtyDocuments&&q.ActiveDocumentName==current,"Application exit while home checks all dirty documents once and cancel is atomic");
        q.ConfirmClose=_=>SampleCloseDecision.Save;check(!Exit().Cancel&&!q.HasDirtyDocuments&&q.DocumentCount==2,"Aggregate save checkpoints every dirty sample without premature tab removal");
        model.OpenPreview.Execute(null);q.RotateSelectedPages(true);q.ConfirmClose=_=>SampleCloseDecision.Discard;check(!Exit().Cancel&&q.HasDirtyDocuments,"Aggregate discard approves shutdown without fake saves");
        prompts=0;q.ConfirmClose=_=>{prompts++;return SampleCloseDecision.Save;};var pending=model.ExecuteModuleAsync("close-slow.run");
        check(Exit().Cancel&&prompts==0,"Busy application blocks exit before showing any save dialog");gate.SetResult(new(true,"done"));await pending;
        q.ConfirmClose=_=>SampleCloseDecision.Cancel;
        check(host.InputBindings.OfType<KeyBinding>().Any(k=>k.Key==Key.S&&k.Modifiers==ModifierKeys.Control&&k.Command==model.SaveSample)&&host.QuickSaveButton.Command==model.SaveSample,"CtrlS and toolbar share the actual sample-save route");
        check(Descendants<Button>((FrameworkElement)host.Content).Any(b=>b.Command==model.ReopenSavedSample),"Saved sample reopening has a visible UI route");

        foreach(var exit in new[]{false,true})foreach(var size in new[]{new Size(540,360),new Size(360,300)}){
            var dialog=new SampleCloseDialog(new SampleCloseRequest(exit,exit?new[]{"첫 번째 긴 이름의 샘플 문서 [목업]","두 번째 샘플 문서 [목업]"}:new[]{"첫 번째 긴 이름의 샘플 문서 [목업]"}));var root=(FrameworkElement)dialog.Content;await Layout(root,size.Width,size.Height);
            check(dialog.Decision==SampleCloseDecision.Cancel&&dialog.CancelButton.IsDefault,"Close dialog defaults to cancel, never destructive acceptance");
            foreach(var button in new[]{dialog.SaveButton,dialog.DiscardButton,dialog.CancelButton}){var bounds=Bounds(button,root);check(bounds.Left>=-1&&bounds.Right<=root.ActualWidth+1&&bounds.Top>=-1&&bounds.Bottom<=root.ActualHeight+1,"Dialog action fits rendered size "+size+" / "+button.Content);}
            check(Descendants<TextBlock>(root).Any(t=>t.Text.Contains("실제 파일 아님")&&t.Text.Contains("앱 종료 시 사라짐")),"Dialog explicitly discloses session-only save and shutdown loss");
            Capture(root,(int)size.Width,(int)size.Height,1,Path.Combine(output,$"sample-close-{(exit?"application":"document")}-{size.Width}.png"));
            dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,dialog.CancelButton));check(dialog.Decision==SampleCloseDecision.Cancel,"Real cancel button route returns cancel");
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,dialog.SaveButton));check(dialog.Decision==SampleCloseDecision.Save,"Real sample-save button route returns save");
            dialog.DiscardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,dialog.DiscardButton));check(dialog.Decision==SampleCloseDecision.Discard,"Real discard button route returns discard");
        }
    }
}
