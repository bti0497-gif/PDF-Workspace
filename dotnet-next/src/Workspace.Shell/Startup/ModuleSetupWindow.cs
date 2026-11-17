using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Workspace.Shell.Startup;

internal enum ModuleSetupStage { Planned, Checking, Downloading, Verifying, Installing, Ready, Failed, Cancelled }
internal enum ModuleSetupOutcome { Running, Completed, Failed, Cancelled }
internal enum ModuleSetupAction { Retry, Cancel, Exit, Continue }

/// <summary>Presentation contract only. Ready means the backend has verified installation, not just downloaded bytes.</summary>
internal sealed record ModuleSetupItem(string Id, string Area, string PlannedVersion,
    ModuleSetupStage Stage = ModuleSetupStage.Planned, string? InstalledVersion = null,
    bool Required = true, long? BytesReceived = null, long? TotalBytes = null,
    string? Detail = null, string? PatchDetails = null)
{
    public string StageText => Stage switch
    {
        ModuleSetupStage.Planned => "대기", ModuleSetupStage.Checking => "확인 중",
        ModuleSetupStage.Downloading => "다운로드 중", ModuleSetupStage.Verifying => "무결성 검증 중",
        ModuleSetupStage.Installing => "설치 중", ModuleSetupStage.Ready => "준비 완료",
        ModuleSetupStage.Failed => "실패", ModuleSetupStage.Cancelled => "취소됨", _ => "알 수 없음"
    };
}

/// <summary>Full snapshot, not a delta. Percent is backend-reported end-to-end work (0..100), never a timer or download-only percentage.</summary>
internal sealed record ModuleSetupSnapshot(IReadOnlyList<ModuleSetupItem> Items, string Status,
    ModuleSetupOutcome Outcome = ModuleSetupOutcome.Running, double? OverallPercent = null,
    long? BytesReceived = null, long? TotalBytes = null, string? Error = null,
    bool CanCancel = true, bool CanRetry = false);

/// <summary>
/// Owner-bound presentation only: call ShowModal(), report immutable snapshots via IProgress, and handle ActionRequested.
/// The handler must schedule work asynchronously; this class performs no I/O or installation.
/// Cancellation/close during work only requests cancellation and waits for a terminal backend snapshot.
/// Failure never closes this window. Caller controls app shutdown after an explicit Exit result.
/// </summary>
internal sealed class ModuleSetupWindow : Window, IProgress<ModuleSetupSnapshot>
{
    internal Grid Panel { get; } = new() { Margin = new Thickness(22) };
    internal ListBox ModuleList { get; } = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 80 };
    internal ProgressBar OverallProgress { get; } = new() { Minimum = 0, Maximum = 100, Height = 16, Margin = new Thickness(0, 8, 0, 5) };
    internal TextBlock StatusText { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.SemiBold };
    internal TextBlock CountsText { get; } = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
    internal TextBox DetailsText { get; } = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 55, MaxHeight = 105, Padding = new Thickness(8) };
    internal TextBlock ErrorText { get; } = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 8) };
    internal Button RetryButton { get; } = ActionButton("다시 시도 (_R)");
    internal Button CancelButton { get; } = ActionButton("취소 (_C)");
    internal Button ExitButton { get; } = ActionButton("종료 (_X)");
    internal Button ContinueButton { get; } = ActionButton("완료 (_D)");
    internal ModuleSetupSnapshot Current { get; private set; }
    internal ModuleSetupAction? Result { get; private set; }
    internal bool CancellationPending { get; private set; }
    /// <summary>Opt in only for first-run startup. Manual module updates retain the completion button.</summary>
    internal bool AutoContinueOnSuccess { get; set; }
    internal event Action<ModuleSetupAction>? ActionRequested;
    private readonly Dictionary<string, (ListBoxItem Row, TextBlock Label, TextBlock State, TextBlock Detail)> rows = new(StringComparer.Ordinal);
    private bool retryPending, allowClose, closed, showingModal, cancellationRequested;
    private readonly ModuleSetupAutoContinue autoContinue;

    internal ModuleSetupWindow(Window owner, ModuleSetupSnapshot initial)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner; Current = Freeze(initial);
        autoContinue = new ModuleSetupAutoContinue(Dispatcher,
            () => AutoContinueOnSuccess && showingModal && IsVisible && !closed && Result is null &&
                !cancellationRequested && !CancellationPending && !retryPending && IsValidatedSuccess(Current),
            () => Request(ModuleSetupAction.Continue));
        Title = "필수 기능 설치 · 더죤 PDF Workspace";
        Width = 840; Height = 680; MinWidth = 560; MinHeight = 520;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 249));
        Panel.Background = Background;
        Content = Panel;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
            Panel.RowDefinitions.Add(new RowDefinition { Height = height });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "필요한 기능 준비", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "필수 모듈을 확인하고 필요한 항목만 설치합니다. 검증과 설치가 끝나야 사용할 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Place(header, 0); Place(ModuleList, 1);
        ScrollViewer.SetHorizontalScrollBarVisibility(ModuleList, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(ModuleList, ScrollBarVisibility.Auto);
        var progress = new StackPanel { Margin = new Thickness(0, 14, 0, 10) };
        progress.Children.Add(StatusText); progress.Children.Add(OverallProgress); progress.Children.Add(CountsText); Place(progress, 2);
        var details = new StackPanel();
        details.Children.Add(new TextBlock { Text = "선택한 기능 · 변경 / 패치 내역", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        details.Children.Add(DetailsText); Place(details, 3); Place(ErrorText, 4);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in new[] { RetryButton, CancelButton, ExitButton, ContinueButton }) buttons.Children.Add(button);
        Place(buttons, 5);
        AutomationProperties.SetName(ModuleList, "설치할 기능, 버전 및 진행 상태");
        AutomationProperties.SetName(OverallProgress, "전체 설치 진행률");
        AutomationProperties.SetName(DetailsText, "선택한 모듈 변경 및 패치 내역");
        AutomationProperties.SetLiveSetting(StatusText, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(ErrorText, AutomationLiveSetting.Assertive);
        RetryButton.Click += (_, _) => Request(ModuleSetupAction.Retry);
        CancelButton.Click += (_, _) => Request(ModuleSetupAction.Cancel);
        ExitButton.Click += (_, _) => Request(ModuleSetupAction.Exit);
        ContinueButton.Click += (_, _) => Request(ModuleSetupAction.Continue);
        ModuleList.SelectionChanged += (_, _) => RefreshDetails();
        ContentRendered += (_, _) => { FocusAction(); autoContinue.Refresh(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (CancelButton.IsEnabled) Request(ModuleSetupAction.Cancel);
            else if (ExitButton.IsEnabled) Request(ModuleSetupAction.Exit);
            e.Handled = true;
        };
        Closing += OnClosing;
        Closed += (_, _) => { closed = true; autoContinue.Refresh(); };
        Apply(Current);
    }

    internal ModuleSetupAction? ShowModal()
    {
        showingModal = true;
        try { ShowDialog(); return Result; }
        finally { showingModal = false; autoContinue.Refresh(); }
    }

    /// <summary>Safe from worker threads; copies the item list before queuing. Reports must be ordered by one backend coordinator.</summary>
    public void Report(ModuleSetupSnapshot value)
    {
        var copy = Freeze(value);
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (Dispatcher.CheckAccess()) { if (!closed) Apply(copy); }
        else Dispatcher.BeginInvoke(new Action(() => { if (!closed) Apply(copy); }));
    }

    private static ModuleSetupSnapshot Freeze(ModuleSetupSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(value.Items);
        var items = value.Items.ToArray();
        if (items.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id)) || items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Module IDs must be nonempty and unique.", nameof(value));
        if (value.OverallPercent is { } percent && (!double.IsFinite(percent) || percent < 0 || percent > 100))
            throw new ArgumentOutOfRangeException(nameof(value), "Overall progress must be between zero and 100, or unknown.");
        return value with { Items = items };
    }

    private void Apply(ModuleSetupSnapshot snapshot)
    {
        var oldOutcome = Current.Outcome;
        var focusWasAction = new[] { RetryButton, CancelButton, ExitButton, ContinueButton }.Any(b => b.IsKeyboardFocusWithin);
        // A completion flag alone is not evidence that the required modules are installed.
        if (snapshot.Outcome == ModuleSetupOutcome.Completed &&
            (snapshot.Items.Count == 0 || snapshot.Items.Any(x => x.Required && x.Stage != ModuleSetupStage.Ready) || snapshot.Items.Any(x => x.Stage == ModuleSetupStage.Failed)))
            snapshot = snapshot with { Outcome = ModuleSetupOutcome.Failed, CanRetry = false, Error = "완료 보고가 필수 모듈 상태와 일치하지 않습니다. 설치 결과를 확인해 주세요." };
        Current = snapshot;
        retryPending = false;
        if (snapshot.Outcome != ModuleSetupOutcome.Running) CancellationPending = false;
        var ids = snapshot.Items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in rows.Keys.Where(id => !ids.Contains(id)).ToArray()) { ModuleList.Items.Remove(rows[stale].Row); rows.Remove(stale); }
        foreach (var item in snapshot.Items)
        {
            if (!rows.TryGetValue(item.Id, out var view))
            {
                var stack = new StackPanel { Margin = new Thickness(8, 7, 8, 7) };
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
                var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
                var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
                stack.Children.Add(label); stack.Children.Add(state); stack.Children.Add(detail);
                var row = new ListBoxItem { Content = stack, Tag = item.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                view = (row, label, state, detail); rows.Add(item.Id, view); ModuleList.Items.Add(row);
            }
            view.Label.Text = $"{item.Area}  ·  {(item.Required ? "필수" : "선택")}  ·  목표 {item.PlannedVersion}";
            view.State.Text = $"{item.StageText}  |  현재 {item.InstalledVersion ?? "미확인"}  |  {Bytes(item.BytesReceived, item.TotalBytes)}";
            view.State.Foreground = item.Stage == ModuleSetupStage.Failed ? Brushes.Firebrick : item.Stage == ModuleSetupStage.Ready ? Brushes.DarkGreen : Brushes.DarkSlateBlue;
            view.Detail.Text = item.Detail ?? ""; view.Detail.Visibility = string.IsNullOrEmpty(item.Detail) ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetName(view.Row, $"{view.Label.Text}. {view.State.Text}. {item.Detail}");
        }
        if (ModuleList.SelectedIndex < 0 && ModuleList.Items.Count > 0) ModuleList.SelectedIndex = 0;
        OverallProgress.IsIndeterminate = snapshot.Outcome == ModuleSetupOutcome.Running && snapshot.OverallPercent is null;
        OverallProgress.Value = snapshot.OverallPercent ?? 0;
        var ready = snapshot.Items.Count(x => x.Stage == ModuleSetupStage.Ready);
        CountsText.Text = $"준비 완료 {ready} / {snapshot.Items.Count}개  ·  {Bytes(snapshot.BytesReceived, snapshot.TotalBytes)}  ·  {(snapshot.OverallPercent is { } p ? $"전체 {p:0.#}%" : "전체 진행률 미확정")}";
        StatusText.Text = CancellationPending ? "취소 요청 중입니다. 안전하게 중단될 때까지 기다려 주세요." : snapshot.Outcome switch
        {
            ModuleSetupOutcome.Completed => AutoContinueOnSuccess && !cancellationRequested && IsValidatedSuccess(snapshot)
                ? "필요한 기능 설치가 완료되었습니다. 시작 화면으로 이동합니다."
                : "필요한 기능 설치가 완료되었습니다. 완료를 눌러 계속하세요.",
            ModuleSetupOutcome.Failed => "설치하지 못했습니다. 오류를 확인한 후 다시 시도하거나 종료하세요.",
            ModuleSetupOutcome.Cancelled => "설치가 취소되었습니다.", _ => snapshot.Status
        };
        if (snapshot.Outcome != ModuleSetupOutcome.Running && !string.IsNullOrWhiteSpace(snapshot.Status)) StatusText.Text += "\n" + snapshot.Status;
        ErrorText.Text = snapshot.Error ?? "";
        ErrorText.Visibility = string.IsNullOrWhiteSpace(ErrorText.Text) ? Visibility.Collapsed : Visibility.Visible;
        RefreshActions(); RefreshDetails();
        if (IsVisible && (focusWasAction || oldOutcome != snapshot.Outcome)) FocusAction();
        autoContinue.Refresh();
    }

    internal static bool IsValidatedSuccess(ModuleSetupSnapshot snapshot) =>
        snapshot.Outcome == ModuleSetupOutcome.Completed && snapshot.Items.Count > 0 &&
        string.IsNullOrWhiteSpace(snapshot.Error) &&
        snapshot.Items.All(x => x.Stage == ModuleSetupStage.Ready);

    private void RefreshActions()
    {
        var terminal = Current.Outcome != ModuleSetupOutcome.Running;
        RetryButton.IsEnabled = !retryPending && Current.CanRetry && Current.Outcome is ModuleSetupOutcome.Failed or ModuleSetupOutcome.Cancelled;
        CancelButton.IsEnabled = !terminal && Current.CanCancel && !CancellationPending && !retryPending;
        ExitButton.IsEnabled = terminal && !retryPending;
        ContinueButton.IsEnabled = Current.Outcome == ModuleSetupOutcome.Completed && !retryPending;
        ContinueButton.IsDefault = ContinueButton.IsEnabled;
        RetryButton.IsDefault = RetryButton.IsEnabled;
    }

    internal bool Request(ModuleSetupAction action)
    {
        var enabled = action switch { ModuleSetupAction.Retry => RetryButton.IsEnabled, ModuleSetupAction.Cancel => CancelButton.IsEnabled, ModuleSetupAction.Exit => ExitButton.IsEnabled, ModuleSetupAction.Continue => ContinueButton.IsEnabled, _ => false };
        if (!enabled || closed) return false;
        if (action is ModuleSetupAction.Cancel or ModuleSetupAction.Retry)
        {
            if (ActionRequested is null) { ErrorText.Text = "설치 작업 연결이 없습니다. 작업 담당자에게 문의하세요."; ErrorText.Visibility = Visibility.Visible; return false; }
            if (action == ModuleSetupAction.Cancel) { CancellationPending = true; cancellationRequested = true; }
            else { retryPending = true; cancellationRequested = false; }
            autoContinue.Refresh();
            StatusText.Text = action == ModuleSetupAction.Cancel ? "취소 요청 중입니다. 안전하게 중단될 때까지 기다려 주세요." : "다시 시도 요청 중입니다. 작업 응답을 기다립니다.";
            RefreshActions();
            try { ActionRequested.Invoke(action); }
            catch (Exception ex)
            {
                CancellationPending = false; retryPending = false;
                ErrorText.Text = $"요청을 처리하지 못했습니다: {ex.Message}"; ErrorText.Visibility = Visibility.Visible; RefreshActions();
                return false;
            }
            return true;
        }
        Result = action;
        autoContinue.Refresh();
        // Exit is a dialog result, never Environment.Exit / Application.Shutdown.
        allowClose = true;
        if (showingModal) DialogResult = action == ModuleSetupAction.Continue;
        else Close();
        return true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (Current.Outcome == ModuleSetupOutcome.Running || retryPending)
        { e.Cancel = true; Request(ModuleSetupAction.Cancel); return; }
        Result = ModuleSetupAction.Exit;
    }
    private void RefreshDetails()
    {
        var id = (ModuleList.SelectedItem as ListBoxItem)?.Tag as string;
        var item = Current.Items.FirstOrDefault(x => x.Id == id);
        DetailsText.Text = item is null ? "기능 목록을 기다리고 있습니다." : $"{item.Area}\n현재: {item.InstalledVersion ?? "미확인"} → 목표: {item.PlannedVersion}\n{item.PatchDetails ?? "전달된 변경 / 패치 내역이 없습니다."}";
    }
    private void FocusAction()
    {
        if (ContinueButton.IsEnabled) ContinueButton.Focus();
        else if (RetryButton.IsEnabled) RetryButton.Focus();
        else if (CancelButton.IsEnabled) CancelButton.Focus();
        else if (ExitButton.IsEnabled) ExitButton.Focus();
        else ModuleList.Focus();
    }
    private void Place(UIElement element, int row) { Grid.SetRow(element, row); Panel.Children.Add(element); }
    private static Button ActionButton(string caption) => new() { Content = caption, MinWidth = 95, MinHeight = 36, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(6, 0, 0, 4) };
    private static string Bytes(long? received, long? total)
    {
        static string Format(long bytes) => bytes.ToString("N0", CultureInfo.CurrentCulture) + " B";
        if (received is null || received < 0) return total is >= 0 ? $"수신량 미확인 / {Format(total.Value)}" : "용량 미확정";
        return total is >= 0 ? $"{Format(received.Value)} / {Format(total.Value)}" : $"{Format(received.Value)} / 전체 용량 미확정";
    }
}

/// <summary>Schedules after render/input; rechecks current eligibility and invalidates stale queued snapshots.</summary>
internal sealed class ModuleSetupAutoContinue(Dispatcher dispatcher, Func<bool> canContinue, Action continueAction)
{
    private long revision;
    internal void Refresh()
    {
        dispatcher.VerifyAccess();
        var scheduledRevision = ++revision;
        if (!canContinue() || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (scheduledRevision != revision || !canContinue()) return;
            ++revision; // Retire this callback before the close action can reenter the dispatcher.
            continueAction();
        }));
    }
}
