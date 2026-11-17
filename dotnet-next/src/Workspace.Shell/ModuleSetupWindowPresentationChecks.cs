using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Workspace.Shell.Startup;

namespace Workspace.Shell;

/// <summary>STA, offscreen presentation checks. No network, installation, startup integration, or physical-input claims.</summary>
internal static class ModuleSetupWindowPresentationChecks
{
    internal static IReadOnlyList<string> Run(string? screenshotPath = null)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        var owner = new Window();
        _ = new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        var item = new ModuleSetupItem("pdf.engine", "PDF 문서 엔진", "1.0.1", InstalledVersion: "1.0.0", PatchDetails: "1.0.0 → 1.0.1\n페이지 회전과 저장 처리 개선");
        var initial = new ModuleSetupSnapshot(new[] { item }, "필요한 기능을 확인합니다.");
        var window = new ModuleSetupWindow(owner, initial);
        var actions = new List<ModuleSetupAction>();
        window.ActionRequested += actions.Add;
        Check(ReferenceEquals(window.Owner, owner) && !window.ShowInTaskbar, "owner-bound dialog");
        Check(window.OverallProgress.IsIndeterminate && window.OverallProgress.Value == 0, "unknown progress is indeterminate, not invented");
        Check(System.Windows.Input.KeyboardNavigation.GetTabNavigation(window) == System.Windows.Input.KeyboardNavigationMode.Cycle && window.CancelButton.Focusable, "keyboard focus cycles within dialog");
        var mutable = new List<ModuleSetupItem> { item };
        window.Report(initial with { Items = mutable }); mutable.Clear();
        Check(window.Current.Items.Count == 1, "snapshot list defensively copied");
        var thread = new System.Threading.Thread(() => window.Report(initial with { Status = "worker progress" }));
        thread.Start(); thread.Join();
        var frame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(window.StatusText.Text == "worker progress", "worker-thread progress dispatched to UI");
        Check(window.CancelButton.IsEnabled && !window.RetryButton.IsEnabled && !window.ExitButton.IsEnabled && !window.ContinueButton.IsEnabled, "running action enablement");
        Check(window.DetailsText.Text.Contains("페이지 회전") && window.DetailsText.Text.Contains("1.0.1"), "selected patch and versions exposed");
        foreach (var stage in Enum.GetValues<ModuleSetupStage>())
        {
            window.Report(initial with { Items = new[] { item with { Stage = stage } } });
            Check(window.Current.Items[0].Stage == stage && !string.IsNullOrWhiteSpace(window.Current.Items[0].StageText), $"stage {stage}");
        }
        window.Report(initial with { Items = new[] { item with { Stage = ModuleSetupStage.Downloading, BytesReceived = 256, TotalBytes = 1024 } }, OverallPercent = 25, BytesReceived = 256, TotalBytes = 1024 });
        Check(!window.OverallProgress.IsIndeterminate && window.OverallProgress.Value == 25 && window.CountsText.Text.Contains("256"), "backend percent and bytes shown exactly");
        Check(!window.Request(ModuleSetupAction.Continue) && !window.Request(ModuleSetupAction.Exit), "active work cannot continue or exit");
        Check(window.Request(ModuleSetupAction.Cancel) && window.CancellationPending && actions.SequenceEqual(new[] { ModuleSetupAction.Cancel }), "cancel emits request only");
        Check(!window.Request(ModuleSetupAction.Cancel) && !window.CancelButton.IsEnabled && window.Result is null, "cancel waits for backend acknowledgment without closing");
        window.Report(initial with { Outcome = ModuleSetupOutcome.Cancelled, CanRetry = true, Status = "다운로드 중단" });
        Check(!window.CancellationPending && window.RetryButton.IsEnabled && window.ExitButton.IsEnabled && !window.CancelButton.IsEnabled, "acknowledged cancellation enables retry and exit");
        Check(window.Request(ModuleSetupAction.Retry) && !window.RetryButton.IsEnabled && !window.ExitButton.IsEnabled && !window.Request(ModuleSetupAction.Retry), "retry duplicate and exit guarded until response");
        window.Report(initial with { Outcome = ModuleSetupOutcome.Failed, CanRetry = true, Error = "다운로드 연결이 끊어졌습니다." });
        Check(window.Result is null && window.ErrorText.Text.Contains("끊어") && window.RetryButton.IsEnabled && !window.ContinueButton.IsEnabled, "failure remains open and preserves error");
        window.Report(initial with { Outcome = ModuleSetupOutcome.Completed, OverallPercent = 100 });
        Check(window.Current.Outcome == ModuleSetupOutcome.Failed && !window.ContinueButton.IsEnabled, "false completion rejected when required module is not ready");
        window.Report(initial with { Items = Array.Empty<ModuleSetupItem>(), Outcome = ModuleSetupOutcome.Completed });
        Check(window.Current.Outcome == ModuleSetupOutcome.Failed, "empty manifest cannot signal success");
        window.Report(initial with { Items = new[] { item with { Stage = ModuleSetupStage.Ready }, new ModuleSetupItem("pdf.tools", "페이지 편집", "1.0.1", ModuleSetupStage.Installing, PatchDetails: "페이지 순서 및 추출 처리") }, OverallPercent = 85, BytesReceived = 1024, TotalBytes = 1024 });
        Check(!window.ContinueButton.IsEnabled && window.CountsText.Text.Contains("1 / 2"), "download finished is not install finished; item count accurate");
        foreach (var size in new[] { new Size(516, 436), new Size(796, 596) })
        {
            window.Panel.Measure(size); window.Panel.Arrange(new Rect(size)); window.Panel.UpdateLayout();
            Check(window.ModuleList.ActualHeight >= 80 && window.ContinueButton.ActualWidth >= 95 && window.DetailsText.ActualWidth <= size.Width, $"offscreen layout {size.Width}x{size.Height}");
        }
        if (screenshotPath is not null)
        {
            var bitmap = new RenderTargetBitmap(840, 640, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window.Panel);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(screenshotPath); png.Save(stream);
        }
        window.Report(initial with { Items = new[] { item with { Stage = ModuleSetupStage.Ready } }, Outcome = ModuleSetupOutcome.Completed, OverallPercent = 100, Status = "필수 모듈 1개 검증 및 설치 완료" });
        Check(window.ContinueButton.IsEnabled && window.ExitButton.IsEnabled && !window.RetryButton.IsEnabled && !window.CancelButton.IsEnabled && !window.OverallProgress.IsIndeterminate, "verified completion action state");
        Check(window.ErrorText.Visibility == Visibility.Collapsed, "previous failure cleared by new backend report");
        Check(window.Request(ModuleSetupAction.Continue) && window.Result == ModuleSetupAction.Continue, "explicit completion produces continue result");
        window.Report(initial);
        Check(window.Current.Outcome == ModuleSetupOutcome.Completed, "late report after close ignored");
        var throwing = new ModuleSetupWindow(owner, initial);
        throwing.ActionRequested += _ => throw new InvalidOperationException("callback test failure");
        Check(!throwing.Request(ModuleSetupAction.Cancel) && throwing.CancelButton.IsEnabled && throwing.ErrorText.Text.Contains("callback test failure"), "callback failure remains recoverable and visible");
        throwing.Report(initial with { Outcome = ModuleSetupOutcome.Failed });
        Check(throwing.Request(ModuleSetupAction.Exit) && throwing.Result == ModuleSetupAction.Exit, "exit closes only dialog and returns result");
        var invalidRejected = false;
        try { _ = new ModuleSetupWindow(owner, initial with { OverallPercent = double.NaN }); } catch (ArgumentOutOfRangeException) { invalidRejected = true; }
        Check(invalidRejected, "invalid progress rejected rather than displayed");
        var duplicateRejected = false;
        try { _ = new ModuleSetupWindow(owner, initial with { Items = new[] { item, item } }); } catch (ArgumentException) { duplicateRejected = true; }
        Check(duplicateRejected, "duplicate module identity rejected");
        var success = initial with { Items = new[] { item with { Stage = ModuleSetupStage.Ready } }, Outcome = ModuleSetupOutcome.Completed };
        var automatic = new ModuleSetupWindow(owner, initial);
        Check(!automatic.AutoContinueOnSuccess, "auto continuation defaults off for manual updates");
        automatic.AutoContinueOnSuccess = true;
        automatic.Report(success);
        void Drain()
        {
            var drain = new System.Windows.Threading.DispatcherFrame();
            automatic.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => drain.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(drain);
        }
        Drain();
        Check(automatic.Result is null && automatic.ContinueButton.IsEnabled, "successful hidden nonmodal window never auto closes");
        Check(ModuleSetupWindow.IsValidatedSuccess(success), "validated ready completed report permits auto continuation");
        foreach (var rejected in new[]
        {
            initial, success with { Outcome = ModuleSetupOutcome.Cancelled }, success with { Outcome = ModuleSetupOutcome.Failed },
            success with { Error = "verification error" }, success with { Items = Array.Empty<ModuleSetupItem>() },
            success with { Items = new[] { item with { Stage = ModuleSetupStage.Installing } } },
            success with { Items = new[] { item with { Stage = ModuleSetupStage.Ready }, new ModuleSetupItem("optional", "선택 기능", "1", ModuleSetupStage.Cancelled, Required: false) } }
        }) Check(!ModuleSetupWindow.IsValidatedSuccess(rejected), $"auto continuation rejects {rejected.Outcome}/{rejected.Error}/{rejected.Items.Count}");
        var eligible = true; var continues = 0;
        var scheduled = new ModuleSetupAutoContinue(automatic.Dispatcher, () => eligible, () => continues++);
        scheduled.Refresh();
        Check(continues == 0, "auto continuation deferred rather than closing during report");
        Drain(); Check(continues == 1, "eligible continuation executes at dispatcher background");
        scheduled.Refresh(); eligible = false;
        Drain(); Check(continues == 1, "callback rechecks opt-out, visibility or cancellation before closing");
        eligible = true; scheduled.Refresh(); eligible = false; scheduled.Refresh(); eligible = true;
        Drain(); Check(continues == 1, "new report invalidates stale queued success");
        scheduled.Refresh(); scheduled.Refresh();
        Drain(); Check(continues == 2, "duplicate completion queues execute only latest callback once");
        automatic.Request(ModuleSetupAction.Exit);
        owner.Close();
        return checks;
    }
}
