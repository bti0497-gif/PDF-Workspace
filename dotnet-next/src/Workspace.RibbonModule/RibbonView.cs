using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
using ShapePath = System.Windows.Shapes.Path;
namespace Workspace.RibbonModule;

public sealed class RibbonView : UserControl, IWorkspaceRibbonView, IDisposable
{
    private readonly IWorkspaceHost host;
    private readonly WrapPanel groups = new();
    private readonly Dictionary<string, RibbonAction> actions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed, busy;
    public string SelectedCategory { get; private set; } = "file";
    public CommandResult? LastResult { get; private set; }
    public event EventHandler? CommandCompleted;
    public IReadOnlyDictionary<string, RibbonAction> Actions => actions;
    public RibbonView(IWorkspaceHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        FontFamily = new FontFamily("Malgun Gothic"); FontSize = 12;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Workspace.RibbonModule;component/RibbonStyles.xaml", UriKind.Relative) });
        Background = Brush("#F6F6F6");
        Content = new Border { Background = Background, BorderBrush = Brush("#C9CDD2"), BorderThickness = new Thickness(0,0,0,1), Child = new ScrollViewer { Padding=new Thickness(6,7,6,5), MaxHeight=174, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Content=groups } };
        foreach (var id in new[]{"document.open","document.save","document.saveAs","document.close","document.undo","document.redo","pages.rotate","pages.delete","pages.extract","document.merge","view.fitPage","view.fitWidth","view.singlePage","view.continuous","app.checkUpdates"})
            actions[id] = new RibbonAction(this,id);
        host.DocumentChanged += OnDocumentChanged;
        Build();
    }
    public void SelectCategory(string category)
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed,this);
        var normalized=category switch { "파일"=>"file", "홈" or "기본"=>"home", "문서"=>"document", "보기"=>"view", "도움말"=>"help", _=>category.ToLowerInvariant() };
        if (normalized is not ("file" or "home" or "document" or "view" or "help")) throw new ArgumentOutOfRangeException(nameof(category));
        SelectedCategory=normalized; Build();
    }
    public bool CanExecute(string id)
    {
        if (disposed || busy || !actions.ContainsKey(id)) return false;
        var doc=host.Document;
        if (id=="document.open") return true;
        if (id=="app.checkUpdates") return !doc.IsDirty;
        if (doc.DocumentId is null || doc.PageCount<=0) return false;
        return id switch {
            "document.undo" => doc.CanUndo,
            "document.redo" => doc.CanRedo,
            "pages.rotate" or "pages.extract" => doc.SelectedPages.Count>0,
            "pages.delete" => doc.SelectedPages.Count>0 && doc.SelectedPages.Distinct().Count()<doc.PageCount,
            _=>true
        };
    }
    public async Task<CommandResult> ExecuteAsync(string id, CancellationToken cancellationToken=default)
    {
        Dispatcher.VerifyAccess();
        if (!CanExecute(id)) return new CommandResult(false,"현재 사용할 수 없는 작업입니다.");
        var doc=host.Document;
        // Snapshot selection/identity before opening a host-owned modal. No UI owns a writer or path picker.
        var intent=new WorkspaceIntent(id,doc.DocumentId,doc.Version,id.StartsWith("pages.",StringComparison.Ordinal)?doc.SelectedPages.ToArray():null,id=="pages.rotate"?"90":null);
        busy=true; Refresh();
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellationToken);
        try { LastResult=await host.DispatchAsync(intent,linked.Token); return LastResult; }
        catch(OperationCanceledException) { LastResult=new(false,"작업이 취소되었습니다."); return LastResult; }
        catch(Exception ex) { LastResult=new(false,ex.Message); return LastResult; }
        finally
        {
            // Also safe when an offscreen host has no DispatcherSynchronizationContext.
            if(Dispatcher.CheckAccess()) Complete(); else await Dispatcher.InvokeAsync(Complete);
        }
        void Complete() { busy=false; if(!disposed) { Refresh(); CommandCompleted?.Invoke(this,EventArgs.Empty); } }
    }
    private void OnDocumentChanged(object? sender,DocumentChangedEventArgs e)
    {
        if(disposed) return;
        if(Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(DispatcherPriority.DataBind,new Action(()=>{if(!disposed)Refresh();}));
    }
    private void Refresh() { foreach(var action in actions.Values) action.Refresh(); }
    private void Build()
    {
        groups.Children.Clear();
        switch(SelectedCategory)
        {
            case "file":
                Group("파일",Tool("열기",RibbonIcons.Folder,"document.open",88),Tool("저장",RibbonIcons.Save,"document.save",88),Tool("다른 이름으로 저장",RibbonIcons.SaveAs,"document.saveAs",136));
                Group("작업 공간",Tool("문서 닫기",RibbonIcons.Exit,"document.close",88)); break;
            case "home":
                Group("편집",Tool("실행 취소",RibbonIcons.Undo,"document.undo"),Tool("다시 실행",RibbonIcons.Redo,"document.redo"));
                Group("페이지",Tool("회전",RibbonIcons.Rotate,"pages.rotate"),Tool("삭제",RibbonIcons.DeletePage,"pages.delete")); break;
            case "document":
                Group("페이지",Tool("회전",RibbonIcons.Rotate,"pages.rotate"),Tool("삭제",RibbonIcons.DeletePage,"pages.delete"));
                Group("어셈블",Tool("추출",RibbonIcons.Extract,"pages.extract"));
                Group("문서 구성",Tool("병합",RibbonIcons.Merge,"document.merge"));
                Group("편집",Tool("실행 취소",RibbonIcons.Undo,"document.undo"),Tool("다시 실행",RibbonIcons.Redo,"document.redo")); break;
            case "help":
                Group("업데이트",Tool("업데이트 확인",RibbonIcons.Restore,"app.checkUpdates",112)); break;
            case "view":
                Group("확대/축소",Tool("페이지 맞춤",RibbonIcons.FitPage,"view.fitPage",88),Tool("너비 맞춤",RibbonIcons.FitWidth,"view.fitWidth",88));
                Group("페이지 보기",Tool("단일 페이지",RibbonIcons.SinglePage,"view.singlePage",88),Tool("연속 보기",RibbonIcons.ContinuousPages,"view.continuous",88)); break;
        }
    }
    private Button Tool(string caption, Geometry icon,string? id,double width=78)
    {
        var content=new StackPanel();
        content.Children.Add(new ShapePath { Data=icon,Width=28,Height=30,Stretch=Stretch.Uniform,Stroke=Brush("#526D88"),StrokeThickness=1.8,Margin=new Thickness(0,0,0,5) });
        content.Children.Add(new TextBlock { Text=caption,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brush("#30343A") });
        var button=new Button { Content=content,Width=width,Style=(Style)FindResource("RibbonIconButton"),ToolTip=caption,Tag=id };
        AutomationProperties.SetName(button,caption);
        if(id is null) button.IsEnabled=false; else button.Command=actions[id];
        return button;
    }
    private void Group(string caption,params Button[] tools)
    {
        var row=new WrapPanel(); foreach(var tool in tools)row.Children.Add(tool);
        var stack=new StackPanel(); stack.Children.Add(row); stack.Children.Add(new TextBlock { Text=caption,TextAlignment=TextAlignment.Center,Foreground=Brush("#6A6D72"),FontSize=11,Margin=new Thickness(0,5,0,0) });
        groups.Children.Add(new Border { BorderBrush=Brush("#D1D3D7"),BorderThickness=new Thickness(0,0,1,0),Margin=new Thickness(0,0,7,4),Padding=new Thickness(6,0,12,0),Child=stack });
    }
    private static Brush Brush(string value) { var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(value)!; brush.Freeze(); return brush; }
    public void Dispose()
    {
        if(disposed)return; disposed=true; host.DocumentChanged-=OnDocumentChanged; lifetime.Cancel(); Refresh(); lifetime.Dispose(); CommandCompleted=null;
    }
}
public sealed class RibbonAction : ICommand
{
    private readonly RibbonView owner;
    public string Id { get; }
    internal RibbonAction(RibbonView owner,string id) { this.owner=owner; Id=id; }
    public bool CanExecute(object? parameter)=>owner.CanExecute(Id);
    public async void Execute(object? parameter)=>await owner.ExecuteAsync(Id);
    public Task<CommandResult> ExecuteAsync(CancellationToken cancellationToken=default)=>owner.ExecuteAsync(Id,cancellationToken);
    public event EventHandler? CanExecuteChanged;
    internal void Refresh()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}



