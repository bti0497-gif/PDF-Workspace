using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
using Workspace.Runtime;
using Workspace.Shell.ModuleDistribution;

using Workspace.Shell.Startup;

namespace Workspace.Shell;
public partial class MainWindow
{
    private bool modularExitApproved, modularBusy, confirmingExit;
    private readonly List<ModuleLease> moduleLeases = new();
    private readonly List<string> loadedModuleIds = new();
    private IsolatedModuleManager? isolatedModules;
    private IWorkspaceHost? documentEngine;
    private ShellDocumentHost? sharedDocument;
    private readonly IRecentFilesStore recentFiles;
    private readonly Grid modularBody = new();
    internal IWorkspaceHost? ModularHost => sharedDocument;
    private void InitializeModularChrome()
    {
        // Retain the approved orange tab chrome; remove the entire sample presentation tree.
        foreach (UIElement child in ShellRoot.Children.Cast<UIElement>().Where(c => Grid.GetRow(c) is 3 or 4 or 5).ToArray()) ShellRoot.Children.Remove(child);
        RibbonScroller.Content = null;
        RibbonPanel.Visibility = Visibility.Visible;

        StatusText.Text = ""; StatusText.ToolTip = null;
        InputBindings.Clear();
        RibbonToggle.Command = null;
        RibbonToggle.Click += (_, _) => RibbonPanel.Visibility = RibbonPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in TopTabs.Children.OfType<Button>()) { button.Command = null; button.IsEnabled = false; }
        modularBody.ColumnDefinitions.Add(new() { Width = new GridLength(270), MinWidth = 200 });
        modularBody.ColumnDefinitions.Add(new() { Width = new GridLength(5) });
        modularBody.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1); modularBody.Children.Add(splitter);
        Grid.SetRow(modularBody, 4); Grid.SetRowSpan(modularBody, 2); ShellRoot.Children.Add(modularBody);
    }
    // Preparation returns verified inert bytes. Runtime attachment must succeed before activation.
    internal async Task StartModularAsync(Func<IProgress<DistributionProgress>, CancellationToken, Task<PreparedModuleUpdate>> prepare, string? initialPath = null, Func<CancellationToken, Task<DistributionCandidate?>>? tryLoadInstalled = null)
    {
        prepareModules=prepare;
        StatusText.Text = "설치된 기능 버전을 확인하고 있습니다…";
        if (tryLoadInstalled != null)
        {
            DistributionCandidate? installed = null;
            try { installed = await tryLoadInstalled(CancellationToken.None); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Installed module fast path skipped: " + ex.Message); }
            if (installed != null)
            {
                try
                {
                    await AttachVerifiedModulesAsync(installed);
                    CommitRunningCandidate(installed);
                    StartBackgroundUpdates();
                    _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => _ = CheckModuleUpdatesAsync(silent: true)));
                    if (initialPath != null) await sharedDocument!.DispatchAsync(new("document.open", Argument: initialPath));
                    else await ShowStartupOpenAsync();
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("Fast attach failed, falling back to module preparation: " + ex.Message);
                    await DisposeModularAsync();
                }
            }
        }
        var progressModel = new ModuleSetupProgress();
        ModuleSetupWindow? setup = null;
        CancellationTokenSource? cancellation = null;
        bool running = false, needsSetupUi = false, setupOpened = false;
        var initialAttemptDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ReportSnapshot(ModuleSetupSnapshot snapshot) => setup?.Report(snapshot);

        void EnsureSetupWindow()
        {
            if (setupOpened) return;
            if (modularExitApproved || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { cancellation?.Cancel(); initialAttemptDone.TrySetResult(false); return; }
            setupOpened = true;
            setup = new ModuleSetupWindow(this, progressModel.Current) { AutoContinueOnSuccess=true, Title=ProductBrand.DialogTitle("필수 기능 설치") };
            setup.ActionRequested += action => { if (action == ModuleSetupAction.Cancel) cancellation?.Cancel(); else if (action == ModuleSetupAction.Retry) _ = Attempt(); };
            var outcome = setup.ShowModal();
            cancellation?.Dispose(); cancellation = null;
            if (outcome != ModuleSetupAction.Continue) { initialAttemptDone.TrySetResult(false); Close(); return; }
            initialAttemptDone.TrySetResult(true);
        }

        async Task Attempt()
        {
            if (running) return;
            running = true; cancellation?.Dispose(); cancellation = new();
            progressModel = new ModuleSetupProgress();
            ReportSnapshot(progressModel.Current);
            try
            {
                var progress = new InlineProgress<DistributionProgress>(p => {
                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                    void Apply()
                    {
                        var snapshot = progressModel.Apply(p);
                        if (!needsSetupUi && (p.Stage == "Downloading" || p.ChangeKind is "Install" or "Update"))
                        {
                            needsSetupUi = true;
                            _ = Dispatcher.BeginInvoke(new Action(EnsureSetupWindow));
                        }
                        ReportSnapshot(snapshot);
                    }
                    if(Dispatcher.CheckAccess())Apply();else Dispatcher.Invoke(Apply);
                });
                var update = await prepare(progress, cancellation.Token);
                var candidate=update.Candidate;
                cancellation.Token.ThrowIfCancellationRequested();
                progressModel.SetCandidatePlan(candidate,update.PreviousActive);
                ReportSnapshot(progressModel.Attaching());
                await ActivatePreparedModulesAsync(update,cancellation.Token,()=>ReportSnapshot(progressModel.Attached()));
                ReportSnapshot(progressModel.Completed());
                if (!needsSetupUi)
                {
                    cancellation.Dispose(); cancellation = null;
                    initialAttemptDone.TrySetResult(true);
                }
            }
            catch (OperationCanceledException ex)
            {
                var failed = progressModel.Failed(ex);
                if (!needsSetupUi) { needsSetupUi = true; _ = Dispatcher.BeginInvoke(new Action(EnsureSetupWindow)); }
                ReportSnapshot(failed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
                var failed = progressModel.Failed(ex);
                if (!needsSetupUi) { needsSetupUi = true; _ = Dispatcher.BeginInvoke(new Action(EnsureSetupWindow)); }
                ReportSnapshot(failed);
            }
            finally { running = false; }
        }

        _ = Attempt();
        if (!await initialAttemptDone.Task) return;
        StartBackgroundUpdates();
        if (initialPath != null) await sharedDocument!.DispatchAsync(new("document.open", Argument:initialPath));
        else await ShowStartupOpenAsync();
    }
    internal async Task ActivatePreparedModulesAsync(PreparedModuleUpdate update,CancellationToken ct=default,Action? runtimeAttached=null)
    {
        await update.AttachAndCommitAsync(async()=>{await AttachVerifiedModulesAsync(update.Candidate);runtimeAttached?.Invoke();},DisposeModularAsync,ct);
        CommitRunningCandidate(update.Candidate);
    }
    internal async Task AttachVerifiedModulesAsync(DistributionCandidate candidate)
    {
        if (documentEngine != null) throw new InvalidOperationException("실행 중인 기능 교체는 앱 재시작이 필요합니다.");
        isolatedModules = new(typeof(IWorkspaceAreaModule).Assembly);
        try
        {
            var engineAsset=candidate.Manifest.Modules.Single(m=>(m.Files??[]).Any(f=>f.Kind=="engine"));
            var operation=isolatedModules.Load(Manifest(engineAsset,candidate.Manifest.Api),candidate.DirectoryPath);
            if(!operation.Success)throw new InvalidOperationException(engineAsset.Id+": "+operation.Message);
            loadedModuleIds.Add(engineAsset.Id);moduleLeases.Add(isolatedModules.Acquire(engineAsset.Id));
            var engineModule=moduleLeases.Single().Module as IWorkspaceDocumentModule??throw new InvalidOperationException("문서 엔진 계약이 올바르지 않습니다.");
            var resource=(engineAsset.Files??[]).Single(f=>f.Kind=="engine");
            documentEngine=engineModule.CreateHost(Path.Combine(candidate.DirectoryPath,ModuleDistributionService.ResourceFileName(engineAsset,resource)));
            sharedDocument=new ShellDocumentHost(this,documentEngine);
            documentEngine.DocumentChanged+=ModularDocumentChanged;
            areaCoordinator=new(sharedDocument,new AreaSwapHost(this),(module,host)=> {
                var area=module as IWorkspaceAreaModule??throw new InvalidOperationException("영역 모듈 계약이 올바르지 않습니다.");
                return new(area.Region,area.CreateView(host));
            },typeof(IWorkspaceAreaModule).Assembly);
            foreach(var region in new[]{WorkspaceRegions.Ribbon,WorkspaceRegions.Sidebar,WorkspaceRegions.Workspace})
            {
                var asset=candidate.Manifest.Modules.Single(m=>m.Region==region);
                var started=areaCoordinator.Start(region,Manifest(asset,candidate.Manifest.Api),candidate.DirectoryPath);
                if(!started.Success)throw new InvalidOperationException(started.Message);
            }
            if(!modularCommandsBound)
            {
            modularCommandsBound=true;
            foreach (var button in TopTabs.Children.OfType<Button>())
            {
                var category = button.Content?.ToString() ?? "";
                button.IsEnabled = category is "파일" or "홈" or "문서" or "보기" or "도움말";
                ApplyRealTabAppearance(button, category == "홈");
                button.Click += async (_,_) => {
                    RibbonPanel.Visibility = Visibility.Visible;
                    foreach(var tab in TopTabs.Children.OfType<Button>()) ApplyRealTabAppearance(tab, Equals(tab.Content, category));
                    if (RibbonScroller.Content is IWorkspaceRibbonView ribbon) ribbon.SelectCategory(category);
                    await Task.CompletedTask;
                };
            }
            void Bind(Key key, ModifierKeys mods, string command) => InputBindings.Add(new KeyBinding(new UiCommand(async()=>await sharedDocument.DispatchAsync(new(command))),key,mods));
            Bind(Key.O,ModifierKeys.Control,"document.open"); Bind(Key.S,ModifierKeys.Control,"document.save"); Bind(Key.S,ModifierKeys.Control|ModifierKeys.Shift,"document.saveAs"); Bind(Key.W,ModifierKeys.Control,"document.close"); Bind(Key.Z,ModifierKeys.Control,"document.undo"); Bind(Key.Y,ModifierKeys.Control,"document.redo"); Bind(Key.U,ModifierKeys.Control|ModifierKeys.Shift,"app.checkUpdates");
            Bind(Key.D0,ModifierKeys.Control,"view.fitPage"); Bind(Key.D2,ModifierKeys.Control,"view.fitWidth"); Bind(Key.Add,ModifierKeys.Control,"view.zoomIn"); Bind(Key.Subtract,ModifierKeys.Control,"view.zoomOut");
            }
            StatusText.Text = "";
        }
        catch { await DisposeModularAsync(); throw; }
    }
    private void ModularDocumentChanged(object? sender, DocumentChangedEventArgs e) => Dispatcher.InvokeAsync(()=> { Title=ProductBrand.DocumentTitle(e.Document); if(!modularBusy) RefreshDocumentStatus(); SchedulePendingUpdateWhenIdle(); });
    private async Task ShowStartupOpenAsync() { var path = new StartupOpenWindow(this,recentFiles){Title=ProductBrand.DialogTitle("PDF 열기")}.ShowModal(); if(path != null && sharedDocument != null) await sharedDocument.DispatchAsync(new("document.open",Argument:path)); }
    private async void GuardModularClosing(CancelEventArgs e)
    {
        if(modularExitApproved) return;
        e.Cancel = true;
        if(confirmingExit || modularBusy || quiescenceDepth>0) return;
        confirmingExit = true; modularBusy=true;
        try { if(sharedDocument != null && !(await sharedDocument.CloseAsync()).Success) return; await DisposeModularAsync(); modularExitApproved=true; _ = Dispatcher.BeginInvoke(Close); }
        finally { confirmingExit=false; modularBusy=false; }
    }
    internal static string DefaultExtractFileName(string? displayName, IReadOnlyList<int>? pages)
    {
        var baseName = Path.GetFileNameWithoutExtension(displayName);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "문서";
        var ordered = (pages ?? Array.Empty<int>()).Where(p => p >= 1).Distinct().Order().ToArray();
        if (ordered.Length == 0) return $"{baseName}_추출.pdf";
        if (ordered.Length == 1) return $"{baseName}_{ordered[0]}페이지.pdf";
        if (ordered[^1] - ordered[0] + 1 == ordered.Length)
            return $"{baseName}_{ordered[0]}-{ordered[^1]}페이지.pdf";
        if (ordered.Length <= 5)
            return $"{baseName}_{string.Join(",", ordered)}페이지.pdf";
        return $"{baseName}_{ordered[0]}-{ordered[^1]}({ordered.Length}쪽)페이지.pdf";
    }
    internal Task DisposeForChecksAsync()=>DisposeModularAsync();
    private async Task DisposeModularAsync()
    {
        StopBackgroundUpdates();
        areaCoordinator?.Dispose();areaCoordinator=null;areaViews.Clear();runningCandidate=null;
        RibbonScroller.Content=null;
        foreach(var child in modularBody.Children.Cast<UIElement>().Where(c=>c is not GridSplitter).ToArray()) modularBody.Children.Remove(child);
        if(documentEngine != null) { documentEngine.DocumentChanged -= ModularDocumentChanged; if(documentEngine is IAsyncDisposable d) await d.DisposeAsync(); }
        documentEngine=null; sharedDocument=null;
        foreach(var lease in moduleLeases) lease.Dispose(); moduleLeases.Clear();
        foreach(var id in loadedModuleIds.AsEnumerable().Reverse()) isolatedModules?.Unload(id, applicationShutdown:true);
        loadedModuleIds.Clear(); isolatedModules=null;
    }
    private sealed class ShellDocumentHost(MainWindow owner, IWorkspaceHost engine) : IWorkspaceHost
    {
        public DocumentSnapshot Document => engine.Document;
        public event EventHandler<DocumentChangedEventArgs>? DocumentChanged { add => engine.DocumentChanged += value; remove => engine.DocumentChanged -= value; }
        public Task<byte[]> RenderPageAsync(Guid id,long version,int page,int dpi,CancellationToken ct=default)=>engine.RenderPageAsync(id,version,page,dpi,ct);
        private WorkspaceIntent Current(string command,string? argument=null)=>new(command,Document.DocumentId,Document.Version,Argument:argument);
        internal async Task<CommandResult> CloseAsync()
        {
            if(Document.DocumentId == null) return new(true,"");
            if(Document.IsDirty)
            {
                var decision=MessageBox.Show(owner,"변경한 내용을 저장하시겠습니까?",ProductBrand.Name,MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
                if(decision==MessageBoxResult.Cancel) return new(false,"취소했습니다.");
                if(decision==MessageBoxResult.Yes) { var saved=await SaveAsync(false,null,default); if(!saved.Success)return saved; }
                else return await engine.DispatchAsync(Current("document.close.discard"));
            }
            return await engine.DispatchAsync(Current("document.close"));
        }
        private async Task<CommandResult> SaveAsync(bool saveAs,string? path,CancellationToken ct)
        {
            if(saveAs && path == null) { var picker=new SaveFileDialog { Filter="PDF 문서 (*.pdf)|*.pdf",DefaultExt=".pdf",FileName=Document.DisplayName,OverwritePrompt=false }; if(picker.ShowDialog(owner)!=true)return new(false,"취소했습니다.");path=picker.FileName; }
            if(saveAs && !File.Exists(path)) return await engine.DispatchAsync(Current("document.saveAs",path),ct);
            var prepared=await engine.DispatchAsync(Current("document.save.prepare",path),ct); if(!prepared.Success)return prepared;
            using var json=JsonDocument.Parse(prepared.Message);
            if(MessageBox.Show(owner,$"다음 파일을 저장한 내용으로 바꾸시겠습니까?\n{json.RootElement.GetProperty("Destination").GetString()}","PDF 저장",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return new(false,"취소했습니다.");
            return await engine.DispatchAsync(Current("document.save.confirmed",json.RootElement.GetProperty("Token").GetString()),ct);
        }
        public async Task<CommandResult> DispatchAsync(WorkspaceIntent intent,CancellationToken ct=default)
        {
            if(!owner.Dispatcher.CheckAccess()) return await owner.Dispatcher.InvokeAsync(()=>DispatchAsync(intent,ct)).Task.Unwrap();
            if(owner.modularBusy || owner.quiescenceDepth>0)return new(false,"다른 작업이 진행 중입니다.");
            if(intent.CommandId=="app.checkUpdates")return owner.ScheduleModuleUpdate();
            owner.modularBusy=true; owner.StatusText.Text=OperationCaption(intent.CommandId);
            try
            {
                CommandResult result;
                switch(intent.CommandId)
                {
                    case "ui.ribbon": if(owner.RibbonScroller.Content is IWorkspaceRibbonView ribbon) ribbon.SelectCategory(intent.Argument ?? "홈"); return new(true,"");
                    case var viewCommand when viewCommand.StartsWith("view.",StringComparison.Ordinal):
                        var viewer=owner.modularBody.Children.OfType<IWorkspaceViewerView>().SingleOrDefault();
                        return new(viewer?.ApplyViewCommand(viewCommand)==true,"보기 명령: "+viewCommand);
                    case "document.open":
                        var path=intent.Argument ?? new StartupOpenWindow(owner,owner.recentFiles){Title=ProductBrand.DialogTitle("PDF 열기")}.ShowModal();
                        if(path==null)return new(false,"취소했습니다.");
                        if(!(await CloseAsync()).Success)return new(false,"취소했습니다.");
                        result=await engine.DispatchAsync(new("document.open",Argument:path),ct);
                        if(result.Success) { try { owner.recentFiles.RecordOpened(path); } catch(Exception ex) { owner.StatusText.Text="파일은 열렸으나 최근 파일 기록을 저장하지 못했습니다: "+ex.Message; } }
                        break;
                    case "document.save": result=await SaveAsync(false,intent.Argument,ct); break;
                    case "document.saveAs": result=await SaveAsync(true,intent.Argument,ct); break;
                    case "document.close": result=await CloseAsync(); break;
                    case "pages.extract" when intent.Argument==null:
                        var extractFileName=DefaultExtractFileName(Document.DisplayName, intent.Pages ?? Document.SelectedPages);
                        var output=new SaveFileDialog { Filter="PDF 문서 (*.pdf)|*.pdf",DefaultExt=".pdf",FileName=extractFileName,OverwritePrompt=true };
                        if(output.ShowDialog(owner)!=true)return new(false,"취소했습니다.");
                        result=await engine.DispatchAsync(Current("pages.extract",output.FileName) with { Pages=intent.Pages },ct); break;
                    case "document.merge" when intent.Argument==null:
                        var merge=new OpenFileDialog { Filter="PDF 문서 (*.pdf)|*.pdf",Multiselect=true,CheckFileExists=true };
                        if(merge.ShowDialog(owner)!=true)return new(false,"취소했습니다.");
                        result=await engine.DispatchAsync(Current("document.merge",JsonSerializer.Serialize(merge.FileNames)),ct);break;
                    default: result=await engine.DispatchAsync(intent.DocumentId==null ? intent with { DocumentId=Document.DocumentId,ExpectedVersion=Document.Version } : intent,ct);break;
                }
                if(!result.Success)owner.StatusText.Text=result.Message;
                return result;
            }
            catch(Exception ex) { owner.StatusText.Text=ex.Message;return new(false,ex.Message); }
            finally { owner.modularBusy=false; if(owner.StatusText.Text==OperationCaption(intent.CommandId))owner.RefreshDocumentStatus();owner.SchedulePendingUpdateWhenIdle(); }
        }
    }
}










