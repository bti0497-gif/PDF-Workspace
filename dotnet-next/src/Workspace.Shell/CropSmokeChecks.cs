using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
internal static partial class ShellSmokeChecks
{
    private static async Task CheckCrop(Action<bool,string> check, string output)
    {
        var window = new MainWindow(new ModuleRegistry(), new ModuleLoadReport(Array.Empty<string>(),Array.Empty<string>()));
        var root = (FrameworkElement)window.Content; var vm = window.Model; var p = vm.Preview;
        void Layout(int w=1280,int h=820) { root.Measure(new Size(w,h));root.Arrange(new Rect(0,0,w,h));root.UpdateLayout(); }
        check(!vm.CropPage.CanExecute(null),"Crop unavailable without active sample");
        vm.OpenPreview.Execute(null);vm.DocumentTab.Execute(null);Layout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout();
        var identity=p.PageIdentityOrder.ToArray();var selection=p.SelectedPages.ToArray();var full=p.CurrentCrop;
        check(vm.PreviewGroups.SelectMany(g=>g.Tools).Single(t=>t.Caption=="자르기").Command==vm.CropPage,"Crop ribbon shares sample command");
        vm.CropPage.Execute(null);Layout();
        check(p.IsCropping&&!p.CropApplyEnabled&&!vm.RotatePages.CanExecute(null)&&!vm.FindSample.CanExecute(null),"Crop mode blocks competing sample commands and requires explicit region");
        p.SelectCropDrag(new Point(10,10),new Point(12,13));check(!p.CropApplyEnabled&&!p.ApplyCrop(),"Tiny accidental drag cannot apply");
        p.SelectCropDrag(new Point(-50,-50),new Point(900,1200));check(!p.CropApplyEnabled,"Full-page clamped drag is no-op, not success");
        p.SelectCropDrag(new Point(50,80),new Point(620,820));
        check(!p.IsDirty&&!p.CanUndo&&p.CurrentCrop==full,"Preview does not mutate content or dirty history");
        Layout();Capture(root,1280,820,1,Path.Combine(output,"crop-selection-desktop.png"));
        p.CancelCrop();check(!p.IsCropping&&!p.IsDirty&&!p.CanUndo&&p.CurrentCrop==full,"Cancel discards pending crop");
        vm.CropPage.Execute(null);Layout(640,480);p.SelectCropDrag(new Point(50,80),new Point(620,820));
        Layout(640,480);Capture(root,640,480,1,Path.Combine(output,"crop-selection-narrow.png"));
        check(p.CropLayer.ActualWidth<=640&&p.CropLayer.ActualHeight<=480&&p.CropSurface.ActualWidth==700,"Crop surface remains original-coordinate size in narrow fitted overlay");
        check(p.ApplyCrop()&&p.IsDirty&&p.CanUndo&&p.CurrentCrop==new Rect(50,80,570,740),"Apply changes only synthetic crop and marks dirty");
        check(p.PageIdentityOrder.SequenceEqual(identity)&&p.SelectedPages.SequenceEqual(selection),"Crop preserves page identities and selection");
        check(p.Undo()&&!p.IsDirty&&p.CurrentCrop==full&&p.Redo()&&p.CurrentCrop==new Rect(50,80,570,740),"Crop undo/redo restores exact geometry and dirty baseline");
        Layout();p.FitPage();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Layout();
        Capture(root,1280,820,1,Path.Combine(output,"crop-applied-desktop.png"));
        for(var rotation=90;rotation<=360;rotation+=90)
        {
            p.RotateSelectedPages(true);Layout();p.BeginCrop();Layout();
            var local=new Point(30,40);var screen=p.CropSurface.TranslatePoint(local,root);
            var inverse=root.TranslatePoint(screen,p.CropSurface);
            check(Math.Abs(local.X-inverse.X)<.01&&Math.Abs(local.Y-inverse.Y)<.01,$"Rotation {rotation%360} maps displayed crop coordinates back to page");
            p.SelectCropDrag(new Point(30,40),new Point(400,500));
            Layout();if(rotation==90)Capture(root,1280,820,1,Path.Combine(output,"crop-rotated-selection.png"));
            if(rotation==90){check(p.ApplyCrop()&&p.CurrentCrop==new Rect(80,120,370,460)&&p.PageRotations[0]==90,"Repeated crop composes original coordinates without losing rotation");check(p.Undo()&&p.CurrentCrop==new Rect(50,80,570,740)&&p.PageRotations[0]==90,"Undo repeated crop preserves prior rotation");}
            else p.CancelCrop();
        }
        p.BeginCrop();Layout();p.SelectCropDrag(new Point(600,700),new Point(-100,-100));check(p.PendingCrop==new Rect(0,0,570,700),"Reverse drag clamps both endpoints to current crop bounds");p.CancelCrop();
        p.RequestSampleName=null;p.SaveCurrentSample();check(!p.IsDirty,"Crop included in sample save checkpoint");
        var saved=p.CurrentCrop;p.CloseCurrent();check(p.ReopenLastSavedSample()&&p.CurrentCrop==saved&&!p.IsDirty,"Save close reopen retains crop without filesystem");
        p.PreparePageContextSelection(1);check(p.ExtractSelectedPages()&&p.CurrentCrop==saved,"Extraction retains cropped sample geometry");
        check(p.ReadSearchText().Any(hit=>hit.Text.Contains("SAMPLE")),"Search scope explicitly includes preserved original text outside crop");
        p.BeginCrop();Layout();p.SelectCropDrag(new Point(30,40),new Point(300,400));
        check(p.HandleCropKey(Key.Escape)&&!p.IsCropping&&p.CurrentCrop==saved,"Escape discards pending selection without altering sample");
        p.CancelCrop();p.ConfirmClose=_=>SampleCloseDecision.Discard;window.Close();
    }
}
