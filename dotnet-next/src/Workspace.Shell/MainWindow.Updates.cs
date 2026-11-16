using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Workspace.Shell.Updates;
namespace Workspace.Shell;
public partial class MainWindow
{
    private WpfUpdateClient? updateClient;
    private readonly CancellationTokenSource updateLifetime = new();
    private DispatcherTimer? updateTimer;
    private DownloadedUpdate? pendingUpdate;
    private bool updateChecking, updateInstalling;
    private string updateStatus = "WPF 미리보기 업데이트 대기";
    private Button? updateButton;
    // Prototype only: normal startup does not call this while the update design is on hold.
    internal void StartUpdates()
    {
        updateClient = new WpfUpdateClient();
        updateButton = new Button { Content = "업데이트", ToolTip = updateStatus, Padding = new Thickness(8,2,8,2), Margin = new Thickness(4,0,4,0) };
        DockPanel.SetDock(updateButton, Dock.Right); HeaderBar.Children.Insert(0, updateButton);
        updateButton.Click += async (_,_) => await ShowUpdatesAsync();
        Closed += (_,_) => { updateLifetime.Cancel(); updateTimer?.Stop(); updateClient.Dispose(); };
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        updateTimer.Tick += async (_,_) => await CheckUpdatesAsync(); updateTimer.Start();
        _ = CheckUpdatesAsync();
    }
    private void UpdateStatus(string message)
    {
        updateStatus = message;
        if (updateButton != null) { updateButton.ToolTip = message; updateButton.Content = pendingUpdate != null ? "업데이트 준비됨" : "업데이트"; }
    }
    private async Task CheckUpdatesAsync()
    {
        if (updateChecking || updateInstalling || updateClient == null || updateLifetime.IsCancellationRequested || pendingUpdate != null) return;
        updateChecking = true;
        try
        {
            UpdateStatus("WPF 미리보기 새 버전 확인 중…");
            var offer = await updateClient.CheckAsync(WpfUpdateClient.CurrentVersion, updateLifetime.Token);
            if (offer == null) { UpdateStatus($"WPF {WpfUpdateClient.CurrentVersion}: 새 미리보기 업데이트 없음"); return; }
            UpdateStatus($"WPF {offer.Version} 다운로드 중… (최대 10분)");
            pendingUpdate = await updateClient.DownloadAsync(offer, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeojonPdfDotNetTest3", "UpdateCache"), updateLifetime.Token);
            UpdateStatus($"WPF {offer.Version} 다운로드/무결성 확인 완료. 설치는 사용자 선택 시에만 실행합니다.");
        }
        catch (OperationCanceledException) { UpdateStatus("업데이트 확인/다운로드 시간 초과 또는 취소. 문서에는 영향이 없습니다."); }
        catch (Exception error) { UpdateStatus("업데이트 실패: " + SafeUpdateError(error)); }
        finally { updateChecking = false; }
    }
    private static string SafeUpdateError(Exception error) => error switch
    {
        System.Net.Http.HttpRequestException => "서버 연결/응답 오류. 나중에 다시 확인하십시오.",
        InvalidDataException => "업데이트 메타데이터 또는 파일 검증 실패. 설치하지 않습니다.",
        IOException => "업데이트 파일 읽기/쓰기 실패. 저장 공간과 권한을 확인하십시오.",
        UnauthorizedAccessException => "업데이트 저장 폴더 접근 불가.",
        _ => "업데이트를 안전하게 처리할 수 없습니다. 설치하지 않습니다."
    };
    private bool UpdateWorkspaceSafe() => !confirmingExit && !realExitApproved && !Model.IsBusy && !Model.IsAssembling && !Model.IsFileDialog && !Model.IsPrinting &&
        realWorkspace != null && !realWorkspace.IsBusy && realWorkspace.LastAction.IsCompleted && realWorkspace.Session?.IsDirty != true;
    private async Task ShowUpdatesAsync()
    {
        if (updateInstalling) return;
        if (pendingUpdate == null)
        {
            if (MessageBox.Show(this, updateStatus + "\n\n확인/다운로드만 자동 실행합니다. 일반 종료 시 설치하지 않습니다.\n지금 다시 확인하시겠습니까?", "WPF 업데이트", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                await CheckUpdatesAsync();
            return;
        }
        if (!UpdateWorkspaceSafe()) { MessageBox.Show(this, "작업이 끝난 뒤 변경한 PDF를 먼저 저장하십시오. 업데이트 때문에 변경 내용을 버리지 않습니다.", "업데이트 대기"); return; }
        var selected = pendingUpdate;
        if (MessageBox.Show(this, $"WPF {selected.Offer.Version} 설치를 위해 앱을 종료하시겠습니까?\n\nSHA-256은 파일 일치만 확인하며 게시자 서명/진위를 보증하지 않습니다. GitHub 저장소를 신뢰하는 경우에만 진행하십시오.\n설치 프로그램이 표시되며 완료 후 앱은 직접 실행하십시오.\n취소하면 문서와 앱은 그대로 유지됩니다.", "앱 종료 후 업데이트 설치", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        if (!UpdateWorkspaceSafe()) { UpdateStatus("문서/작업 상태가 바뀌어 설치를 취소했습니다."); return; }
        updateInstalling = true; IsEnabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WpfUpdateClient.VerifyAsync(selected, timeout.Token);
            if (!UpdateWorkspaceSafe()) throw new InvalidOperationException("Workspace changed.");
            // Installer must wait for this PID AND acquire TEST3's lifetime mutex before replacement.
            var start = new ProcessStartInfo(selected.Path) { UseShellExecute = true, Arguments = $"/UPDATE /WAITPID={Environment.ProcessId}" };
            using var installer = Process.Start(start) ?? throw new IOException("Installer did not start.");
            // No discard/save dialog during this path: only a freshly verified clean, idle session is accepted.
            await realWorkspace.DisposeAsync(); realExitApproved = true; Close();
        }
        catch (Exception error) { UpdateStatus("설치 시작 실패: " + SafeUpdateError(error)); MessageBox.Show(this, updateStatus + "\n앱은 종료하지 않았습니다.", "WPF 업데이트"); }
        finally { updateInstalling = false; IsEnabled = true; }
    }
}
