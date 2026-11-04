using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
using Workspace.Runtime;
namespace Workspace.Shell.Presentation;

public sealed record ActivityItem(string Time,string Name,string Result,bool Success);
public sealed class ModuleItem(IWorkspaceModule module,Action open,Func<bool> canOpen)
{
    public IWorkspaceModule Module { get; } = module;
    public string Name => Module.Descriptor.Name;
    public string Details => $"{Module.Descriptor.Id} · v{Module.Descriptor.Version} · API {Module.Descriptor.ApiVersion}";
    public UiCommand Open { get; } = new(open,()=>module is IModuleViewProvider && canOpen());
}
public sealed class CommandItem(IWorkspaceCommand command,Action run,Func<bool> canRun)
{
    public string Id => command.Id;
    public string Caption => command.Caption;
    public UiCommand Run { get; } = new(run,canRun);
}

// Presentation state only. No PDF model, file writing or legacy application dependency.
public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly ModuleRegistry registry;
    private readonly Dictionary<string,FrameworkElement> views = new(StringComparer.Ordinal);
    private FrameworkElement? activeView;
    private bool ribbonVisible, sidebarVisible, isBusy, previewActive;
    private readonly List<UiCommand> previewCommands = new();
    public PreviewWorkspace Preview { get; } = new();
    public bool PreviewActive { get=>previewActive; private set {previewActive=value;Changed();Changed(nameof(IsStandardWorkspace));Changed(nameof(IsHome));Changed(nameof(ShowNavigation));foreach(var command in previewCommands)command.Refresh();} }
    public bool IsStandardWorkspace => !PreviewActive;
    private string activeTab="홈", panelTab="모듈", screenTitle="시작", status="문서 없음 · PDF 기능은 아직 연결하지 않았습니다.";
    public ObservableCollection<ModuleItem> Modules { get; } = new();
    public ObservableCollection<CommandItem> ModuleCommands { get; } = new();
    public ObservableCollection<ActivityItem> Activity { get; } = new();
    public IReadOnlyList<string> LoadErrors { get; }
    public bool HasLoadErrors => LoadErrors.Count>0;
    public bool HasModules => Modules.Count>0;
    public bool HasCommands => ModuleCommands.Count>0;
    public bool HasActivity => Activity.Count>0;
    public string ModuleSummary => $"연결된 모듈 {Modules.Count}개";
    public FrameworkElement? ActiveView { get=>activeView;private set {activeView=value;Changed();Changed(nameof(IsHome));Changed(nameof(HasActiveView));Changed(nameof(ShowNavigation));} }
    public bool IsHome => ActiveView is null && !PreviewActive;
    public bool HasActiveView => ActiveView is not null;
    public bool ShowNavigation => !PreviewActive && (HasActiveView || SidebarVisible);
    public IReadOnlyList<RibbonGroup> PreviewGroups => RibbonLayout.For(ActiveTab).Select(group=>new RibbonGroup(group.Name,group.Tools.Select(tool=>tool.Caption switch {
        "실행 취소"=>tool with {Command=Undo,Purpose="샘플 작업 실행 취소 (Ctrl+Z)"},
        "다시 실행"=>tool with {Command=Redo,Purpose="샘플 작업 다시 실행 (Ctrl+Y)"},
        "회전"=>tool with {Command=RotatePages,Purpose="선택한 샘플 페이지를 시계 방향으로 90도 회전"},
        "삭제"=>tool with {Command=DeletePages,Purpose="선택한 샘플 페이지 삭제 · 최소 1쪽 유지"},
        "삽입"=>tool with {Command=InsertPage,Purpose="선택한 페이지 뒤에 빈 샘플 페이지 삽입"},
        "추출"=>tool with {Command=ExtractPages,Purpose="선택한 페이지를 새 샘플 탭으로 복사 · 원본 유지"},
        _=>tool
    }).ToArray())).ToArray();
    public string UndoToolTip => Preview.CanUndo?$"실행 취소: {Preview.UndoDescription} (Ctrl+Z)\n샘플 문서 안에서만 동작합니다.":"실행 취소 (Ctrl+Z)\n되돌릴 샘플 작업이 없습니다.";
    public string RedoToolTip => Preview.CanRedo?$"다시 실행: {Preview.RedoDescription} (Ctrl+Y)\n샘플 문서 안에서만 동작합니다.":"다시 실행 (Ctrl+Y)\n다시 실행할 샘플 작업이 없습니다.";
    public bool IsPreviewTab => PreviewGroups.Count>0;
    public bool RibbonVisible { get=>ribbonVisible;private set {ribbonVisible=value;Changed();} }
    public bool SidebarVisible { get=>sidebarVisible;private set {sidebarVisible=value;Changed();Changed(nameof(ShowNavigation));} }
    public string ActiveTab { get=>activeTab;private set {activeTab=value;Changed();Changed(nameof(IsFileTab));Changed(nameof(IsViewTab));Changed(nameof(IsToolsTab));Changed(nameof(IsHomeTab));Changed(nameof(IsBasicTab));Changed(nameof(IsAnnotationTab));Changed(nameof(IsDocumentTab));Changed(nameof(PreviewGroups));Changed(nameof(IsPreviewTab));} }
    public bool IsFileTab=>ActiveTab=="파일";
    public bool IsViewTab=>ActiveTab=="보기";
    public bool IsToolsTab=>ActiveTab=="도움말";
    public bool IsHomeTab=>ActiveTab=="홈";
    public bool IsBasicTab=>ActiveTab=="기본";
    public bool IsAnnotationTab=>ActiveTab=="주석";
    public bool IsDocumentTab=>ActiveTab=="문서";
    public string PanelTab { get=>panelTab;private set {panelTab=value;Changed();Changed(nameof(IsModulesPanel));Changed(nameof(IsActivityPanel));} }
    public bool IsModulesPanel=>PanelTab=="모듈";
    public bool IsActivityPanel=>PanelTab=="기록";
    public string ScreenTitle { get=>screenTitle;private set {screenTitle=value;Changed();} }
    public string Status { get=>status;private set {status=value;Changed();} }
    public bool IsBusy { get=>isBusy;private set {isBusy=value;Changed();Changed(nameof(IsIdle));foreach(var c in ModuleCommands)c.Run.Refresh();foreach(var m in Modules)m.Open.Refresh();Home.Refresh();ResetLayout.Refresh();OpenPreview.Refresh();foreach(var command in previewCommands)command.Refresh();} }
    public bool IsIdle=>!IsBusy;
    public UiCommand Undo { get; }
    public UiCommand Redo { get; }
    public UiCommand RotatePages { get; }
    public UiCommand DeletePages { get; }
    public UiCommand InsertPage { get; }
    public UiCommand ExtractPages { get; }
    public UiCommand OpenPreview { get; }
    public UiCommand CloseDocument { get; }
    public UiCommand FitPage { get; }
    public UiCommand FitWidth { get; }
    public UiCommand ZoomIn { get; }
    public UiCommand ZoomOut { get; }
    public UiCommand SinglePage { get; }
    public UiCommand ContinuousPages { get; }
    public UiCommand NextPage { get; }
    public UiCommand PreviousPage { get; }
    public UiCommand Thumbnails { get; }
    public UiCommand Bookmarks { get; }
    public UiCommand Home { get; }
    public UiCommand ToggleRibbon { get; }
    public UiCommand ToggleSidebar { get; }
    public UiCommand ResetLayout { get; }
    public UiCommand ShowModules { get; }
    public UiCommand ShowActivity { get; }
    public UiCommand HomeTab { get; }
    public UiCommand BasicTab { get; }
    public UiCommand AnnotationTab { get; }
    public UiCommand DocumentTab { get; }
    public UiCommand FileTab { get; }
    public UiCommand ViewTab { get; }
    public UiCommand ToolsTab { get; }
    public UiCommand ClearActivity { get; }
    public event Action? LayoutResetRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ShellViewModel(ModuleRegistry registry,ModuleLoadReport report)
    {
        this.registry=registry;LoadErrors=report.Errors.ToArray();
        Home=new(ShowHome,()=>!IsBusy);
        OpenPreview=new(()=>{ActiveView=null;SidebarVisible=false;Preview.OpenSamples();PreviewActive=true;ScreenTitle="샘플 문서 · UI 검증";ActiveTab="보기";RibbonVisible=true;Status="샘플 문서 조작 중 · 실제 PDF가 아니며 저장되지 않습니다.";},()=>!IsBusy);
        UiCommand PreviewCommand(Action run){var command=new UiCommand(run,()=>PreviewActive&&!IsBusy);previewCommands.Add(command);return command;}
        UiCommand EditCommand(Action run,Func<bool> available){var command=new UiCommand(run,()=>PreviewActive&&!IsBusy&&available());previewCommands.Add(command);return command;}
        Undo=EditCommand(()=>{Preview.Undo();},()=>Preview.CanUndo);
        Redo=EditCommand(()=>{Preview.Redo();},()=>Preview.CanRedo);
        RotatePages=EditCommand(()=>{Preview.RotateSelectedPages(true);},()=>Preview.SelectedPages.Count>0);
        DeletePages=EditCommand(()=>{Preview.DeleteSelectedPages();},()=>Preview.CanDeleteSelectedPages);
        InsertPage=EditCommand(()=>{Preview.InsertBlankPage(true);},()=>Preview.PageCount>0);
        ExtractPages=EditCommand(()=>{Preview.ExtractSelectedPages();},()=>Preview.SelectedPages.Count>0);
        Preview.HistoryChanged+=()=>{foreach(var command in previewCommands)command.Refresh();Changed(nameof(UndoToolTip));Changed(nameof(RedoToolTip));};
        FitPage=PreviewCommand(Preview.FitPage);FitWidth=PreviewCommand(Preview.FitWidth);
        ZoomIn=PreviewCommand(Preview.ZoomIn);ZoomOut=PreviewCommand(Preview.ZoomOut);
        SinglePage=PreviewCommand(()=>Preview.SetContinuous(false));ContinuousPages=PreviewCommand(()=>Preview.SetContinuous(true));
        NextPage=PreviewCommand(Preview.NextPage);PreviousPage=PreviewCommand(Preview.PreviousPage);
        Thumbnails=PreviewCommand(Preview.ShowThumbnails);Bookmarks=PreviewCommand(Preview.ShowBookmarks);
        CloseDocument=new(()=>{if(PreviewActive)Preview.CloseCurrent();else ShowHome();},()=>!IsBusy);
        previewCommands.Add(CloseDocument);
        Preview.Empty+=ShowHome;
        ToggleRibbon=new(()=>RibbonVisible=!RibbonVisible);
        ToggleSidebar=new(()=>{if(PreviewActive)Preview.ToggleNavigation();else SidebarVisible=!SidebarVisible;});
        ShowModules=new(()=>{PreviewActive=false;PanelTab="모듈";SidebarVisible=true;});
        ShowActivity=new(()=>{PreviewActive=false;PanelTab="기록";SidebarVisible=true;});
        FileTab=new(()=>SelectTab("파일"));ViewTab=new(()=>SelectTab("보기"));ToolsTab=new(()=>SelectTab("도움말"));
        HomeTab=new(()=>SelectTab("홈"));BasicTab=new(()=>SelectTab("기본"));AnnotationTab=new(()=>SelectTab("주석"));DocumentTab=new(()=>SelectTab("문서"));
        ResetLayout=new(()=>{RibbonVisible=false;SidebarVisible=false;PanelTab="모듈";ActiveTab="홈";LayoutResetRequested?.Invoke();Status="작업 내용은 유지하고 화면 배치를 초기화했습니다.";},()=>!IsBusy);
        ClearActivity=new(()=>{Activity.Clear();Changed(nameof(HasActivity));Status="이번 실행의 명령 기록을 지웠습니다. 문서 파일은 변경하지 않았습니다.";});
        foreach(var module in registry.Modules) Modules.Add(new ModuleItem(module,()=>OpenModule(module),()=>!IsBusy));
        foreach(var command in registry.Commands) ModuleCommands.Add(new CommandItem(command,()=>_ = ExecuteModuleAsync(command.Id),()=>!IsBusy));
        if(HasLoadErrors) Status="일부 모듈을 연결하지 못했습니다. 모듈 패널에서 오류를 확인하세요.";
    }
    private void SelectTab(string name)
    {
        if(ActiveTab==name) RibbonVisible=!RibbonVisible;
        else {ActiveTab=name;RibbonVisible=true;}
    }
    private void ShowHome()
    {
        PreviewActive=false;ActiveView=null;ScreenTitle="시작";SidebarVisible=false;RibbonVisible=false;
        Status="문서 없음 · PDF 기능은 아직 연결하지 않았습니다.";
    }
    private void OpenModule(IWorkspaceModule module)
    {
        if(IsBusy || module is not IModuleViewProvider provider) return;
        try {
            if(!views.TryGetValue(module.Descriptor.Id,out var view)) {
                view=provider.CreateView(ExecuteModuleAsync);
                views.Add(module.Descriptor.Id,view);
            }
            PreviewActive=false;ActiveView=view;ScreenTitle=module.Descriptor.Name;RibbonVisible=false;
            Status=$"{module.Descriptor.Name} · 모듈 화면 · PDF 문서가 아닙니다.";
        } catch(Exception ex) {
            Record(module.Descriptor.Name,new CommandResult(false,"모듈 화면 오류: "+ex.Message));
        }
    }
    public async Task<CommandResult> ExecuteModuleAsync(string id)
    {
        if(IsBusy) return new(false,"다른 작업이 진행 중입니다.");
        IsBusy=true;
        Status="명령 실행 중: "+(ModuleCommands.FirstOrDefault(c=>c.Id==id)?.Caption??id);
        try {
            var result=await registry.ExecuteAsync(id);
            Record(ModuleCommands.FirstOrDefault(c=>c.Id==id)?.Caption??id,result);
            return result;
        } catch(Exception ex) {
            var result=new CommandResult(false,"실행 오류: "+ex.Message);Record(id,result);return result;
        } finally {IsBusy=false;}
    }
    private void Record(string name,CommandResult result)
    {
        Status=result.Message;
        Activity.Insert(0,new ActivityItem(DateTime.Now.ToString("HH:mm:ss"),name,result.Message,result.Success));
        while(Activity.Count>100) Activity.RemoveAt(Activity.Count-1);
        Changed(nameof(HasActivity));
    }
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
}
