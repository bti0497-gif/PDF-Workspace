using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
using Workspace.RibbonModule;
internal static class Program
{
    static int count;
    [STAThread] static int Main(string[] args)
    {
        try
        {
            var output=args.Length>0?args[0]:AppContext.BaseDirectory; Directory.CreateDirectory(output);
            var host=new FakeHost(); using var module=new RibbonModule();
            var view=(RibbonView)module.CreateView(host);
            Check(module.Descriptor.Id=="ribbon" && module.Region==WorkspaceRegions.Ribbon,"independent region descriptor");
            Check(typeof(RibbonModule).Assembly.GetReferencedAssemblies().All(a=>a.Name!="Workspace.Shell"),"no shell dependency");
            Check(host.Subscribers==1,"shared host event subscription");
            Check(view.Actions["document.open"].CanExecute(null),"open available with no document");
            Check(view.Actions.Where(a=>a.Key!="document.open" && a.Key!="app.checkUpdates").All(a=>!a.Value.CanExecute(null)),"no-file action gating");
            foreach(var category in new[]{"file","home","document","view","help"})
            {
                ((IWorkspaceRibbonView)view).SelectCategory(category); Layout(view,640,174);
                Check(Descendants<Button>(view).Any(),category+" grouped icon cards exist");
                Check(Descendants<TextBlock>(view).All(t=>!t.Text.Contains("TEST") && !t.Text.Contains("미구현") && !t.Text.Contains("보안")),category+" no warning/instruction labels");
                var image=Render(view,640,174); Save(image,Path.Combine(output,category+"-empty.png"));
                Check(WhiteCount(image)==0,category+" disabled cards contain no blank white rectangles");
            }
            Wait(view.ExecuteAsync("document.open")); Check(host.Intents.Last().CommandId=="document.open" && host.Intents.Last().Argument==null,"open delegates picker to shell host");
            view.SelectCategory("도움말"); Check(view.SelectedCategory=="help","Korean help alias");
            Check(view.CanExecute("app.checkUpdates"),"update check available with empty workspace");
            Wait(view.ExecuteAsync("app.checkUpdates")); Check(host.Intents.Last().CommandId=="app.checkUpdates" && host.Intents.Last().Argument==null,"update check delegates discovery to shell host");
            host.Publish(new(Guid.NewGuid(),"document.pdf",5,2,new[]{2,4},true,7,4,true,false));
            Check(!view.CanExecute("app.checkUpdates"),"dirty document blocks update check");
            Check(view.CanExecute("document.undo") && !view.CanExecute("document.redo"),"authoritative history capability");
            foreach(var id in new[]{"document.save","document.saveAs","document.close","pages.rotate","pages.delete","pages.extract","document.merge","document.undo","view.fitPage","view.fitWidth","view.singlePage","view.continuous"})
            {
                Wait(view.ExecuteAsync(id)); var intent=host.Intents.Last();
                Check(intent.CommandId==id && intent.DocumentId==host.Document.DocumentId && intent.ExpectedVersion==7,id+" shared intent identity/version");
                if(id.StartsWith("pages."))Check(intent.Pages!.SequenceEqual(new[]{2,4}),id+" exact selection");
                Check(intent.Argument==(id=="pages.rotate"?"90":null),id+" no fabricated picker/approval argument");
            }
            host.Publish(host.Document with {CanUndo=false,CanRedo=true,SelectedPages=new[]{1,2,3,4,5}});
            Check(!view.CanExecute("pages.delete") && !view.CanExecute("document.undo") && view.CanExecute("document.redo"),"event refresh updates all-page and history guards");
                        Wait(view.Actions["document.redo"].ExecuteAsync()); Check(host.Intents.Last().CommandId=="document.redo","redo bound action dispatch");
            view.SelectCategory("document"); Layout(view,640,174);
            var rotateButton=Descendants<Button>(view).Single(b=>Equals(b.Tag,"pages.rotate"));
            Check(rotateButton.Width==78 && rotateButton.Height==70,"approved 78x70 action card geometry");
            Check(Descendants<System.Windows.Shapes.Path>(rotateButton).Single().Width==28,"approved 28px line icon geometry");
            var clickCount=host.Intents.Count; rotateButton.Command.Execute(null); Pump();
            Check(host.Intents.Count==clickCount+1 && host.Intents.Last().CommandId=="pages.rotate","actual button ICommand bound to host action");
            host.Publish(host.Document with {IsDirty=false});
            Check(view.CanExecute("app.checkUpdates"),"clean document enables update check");
            host.Fail=true; var failure=Wait(view.ExecuteAsync("document.save")); Check(!failure.Success && view.LastResult==failure,"host failure preserved not fabricated success"); host.Fail=false;
            host.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously); var pending=view.ExecuteAsync("document.save");
            Check(!view.CanExecute("document.open"),"inflight command disables reentrancy");
            Check(!view.CanExecute("app.checkUpdates"),"busy action blocks update check");
            var previous=host.Intents.Count; Wait(view.ExecuteAsync("document.save"));Check(host.Intents.Count==previous,"duplicate dispatch blocked"); host.Pending.SetResult(new(true,"saved")); Wait(pending);host.Pending=null;
            view.SelectCategory("document"); Layout(view,1280,112);Save(Render(view,1280,112),Path.Combine(output,"document-active.png"));
            var orange=(SolidColorBrush)new BrushConverter().ConvertFromString("#C45C38")!;
            var tab=new Button{Content="문서",IsEnabled=false,Style=(Style)view.FindResource("RibbonCategoryButton"),Width=80,Height=30};
            var tabHost=new Border{Background=orange,Child=tab};Layout(tabHost,80,30);var tabs=Render(tabHost,80,30);Save(tabs,Path.Combine(output,"disabled-tab.png"));
            var pixels=new byte[80*30*4];tabs.CopyPixels(pixels,80*4,0);
            foreach(var p in new[]{(1,1),(40,1),(78,1),(1,28),(78,28)}) {var i=(p.Item2*80+p.Item1)*4;Check(pixels[i]==0x38 && pixels[i+1]==0x5C && pixels[i+2]==0xC4,"disabled tab orange continuity "+p);}
            Check(WhiteCount(tabs)==0,"disabled tab has no white rectangles");
            var replacement=(RibbonView)module.CreateView(host);Check(host.Subscribers==1 && !view.CanExecute("document.open"),"view replacement detaches old host");
            module.Dispose(); Check(host.Subscribers==0 && !replacement.CanExecute("document.open"),"dispose removes shared host subscriptions");
            Console.WriteLine($"PASS {count} ribbon checks. Offscreen WPF and fake host only; native input and real PDF operations not covered.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);count++;Console.WriteLine("PASS "+name);}
    static T Wait<T>(Task<T> task){while(!task.IsCompleted){Pump();Thread.Sleep(1);}return task.GetAwaiter().GetResult();}
    static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    static void Layout(FrameworkElement view,int width,int height){view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();Pump();}
    static RenderTargetBitmap Render(FrameworkElement view,int width,int height){var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);return bitmap;}
    static void Save(BitmapSource bitmap,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
    static int WhiteCount(BitmapSource bitmap){var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);var n=0;for(var i=0;i<pixels.Length;i+=4)if(pixels[i]==255&&pixels[i+1]==255&&pixels[i+2]==255&&pixels[i+3]==255)n++;return n;}
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var item in Descendants<T>(child))yield return item;}}
}
internal sealed class FakeHost:IWorkspaceHost
{
    EventHandler<DocumentChangedEventArgs>? changed;
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged{add=>changed+=value;remove=>changed-=value;}
    public int Subscribers=>changed?.GetInvocationList().Length??0;
    public DocumentSnapshot Document{get;private set;}=DocumentSnapshot.Empty;
    public List<WorkspaceIntent> Intents{get;}=new();
    public bool Fail;
    public TaskCompletionSource<CommandResult>? Pending;
    public void Publish(DocumentSnapshot document){Document=document;changed?.Invoke(this,new(document));}
    public Task<CommandResult> DispatchAsync(WorkspaceIntent intent,CancellationToken cancellationToken=default){Intents.Add(intent);return Pending?.Task ?? Task.FromResult(new CommandResult(!Fail,Fail?"host rejected":"host accepted"));}
    public Task<byte[]> RenderPageAsync(Guid documentId,long expectedContentVersion,int oneBasedPage,int dpi,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
}



