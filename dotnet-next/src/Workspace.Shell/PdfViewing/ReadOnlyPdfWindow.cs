using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Workspace.Shell.PdfOperations;

namespace Workspace.Shell.PdfViewing;

// Independent of synthetic state: every operation creates a new file, never an edit buffer.
internal sealed class ReadOnlyPdfWindow : Window
{
    private readonly IPdfOperationsService operations;
    private readonly Image image = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock status = new() { Text = "로컬 PDF를 여십시오.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextBlock sourceLabel = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 8, 2) };
    private readonly TextBlock pageLabel = new() { Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock zoomLabel = new() { Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(Button Button, Func<bool> Available)> actions = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Button cancel = new() { Content = "작업 취소", Margin = new Thickness(3), Padding = new Thickness(8), IsEnabled = false };
    private ReadOnlyPdfSession? session;
    private string? originalPath;
    private string? resultPath;
    private CancellationTokenSource? operationCancellation;
    private TaskCompletionSource? idle;
    private int currentPage;
    private double zoom = 1;
    private bool busy;
    private bool closed;
    private bool closeReady;
    private const string ReviewNotice = "서명·양식·책갈피·주석·읽기 순서·한글·배치 보존은 미검증입니다. 결과를 원본과 비교하십시오.";

    public ReadOnlyPdfWindow(IPdfOperationsService? operations = null)
    {
        this.operations = operations ?? new PdfCraftOperationsService();
        Title = "실제 PDF · 페이지 작업 TEST";
        Width = 1100; Height = 820; MinWidth = 600; MinHeight = 480;
        FontFamily = new FontFamily("Malgun Gothic");
        var root = new DockPanel(); Content = root;
        var label = new TextBlock { Text = "실제 PDF 시험판 | 원본 변경 없음 · 각 작업은 새 PDF 저장 · 합성 샘플과 분리", Background = Brushes.LightGoldenrodYellow, Padding = new Thickness(10), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(label, Dock.Top); root.Children.Add(label);
        DockPanel.SetDock(sourceLabel, Dock.Top); root.Children.Add(sourceLabel);
        var bar = new WrapPanel(); DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        Add(bar, "PDF 열기", async () => { var dialog = new OpenFileDialog { Filter = "PDF 문서 (*.pdf)|*.pdf", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) await OpenPdfAsync(dialog.FileName); });
        Add(bar, "이전 쪽", () => ShowPageAsync(currentPage - 1), () => currentPage > 1);
        Add(bar, "다음 쪽", () => ShowPageAsync(currentPage + 1), () => session != null && currentPage < session.PageCount);
        bar.Children.Add(pageLabel);
        Add(bar, "−", () => { SetZoom(zoom / 1.25); return Task.CompletedTask; }, HasDocument);
        Add(bar, "+", () => { SetZoom(zoom * 1.25); return Task.CompletedTask; }, HasDocument);
        Add(bar, "100%", () => { SetZoom(1); return Task.CompletedTask; }, HasDocument); bar.Children.Add(zoomLabel);
        Add(bar, "문서 닫기", async () => { SetBusy(true); try { await ClearAsync(); } finally { SetBusy(false); } }, HasDocument);
        var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        foreach (var degrees in new[] { 90, 180, 270 })
            Add(tools, $"현재 쪽 {degrees}° 회전", () => RunOperationAsync(PdfOperationKind.Rotate, [currentPage], degrees), HasDocument);
        Add(tools, "현재 쪽 삭제…", async () => {
            if (session == null || session.PageCount <= 1) return;
            if (MessageBox.Show(this, $"새 파일에서 {currentPage}쪽을 제외합니다. 원본은 그대로 유지됩니다. 계속하시겠습니까?", "현재 쪽 삭제", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                await RunOperationAsync(PdfOperationKind.Delete, [currentPage]);
        }, () => HasDocument() && session!.PageCount > 1);
        Add(tools, "쪽 추출…", () => SelectPagesAsync(false), HasDocument);
        Add(tools, "쪽 순서 변경…", () => SelectPagesAsync(true), HasDocument);
        Add(tools, "PDF 병합…", MergeAsync, HasDocument);
        Add(tools, "결과 열기", async () => { if (resultPath != null) await OpenPdfAsync(resultPath); }, () => resultPath != null);
        Add(tools, "제한 / 도움말", () => {
            MessageBox.Show(this, "모든 작업은 새 파일 저장이며 원본 덮어쓰기와 기존 파일 교체는 금지됩니다. 저장되지 않은 편집 상태나 실행 취소는 없습니다. 연속 작업은 ‘결과 열기’ 후 진행하십시오.\n\n회전·삭제는 현재 쪽에만 적용됩니다. 추출·쪽 순서 변경은 입력한 순서대로 처리하며 중복 쪽은 지원하지 않습니다. 병합은 현재 문서가 첫 번째이며 추가 문서 순서는 확인 창에서 바꿀 수 있습니다.\n\n로컬 일반 PDF만 지원합니다. 암호화·링크/네트워크 경로는 미지원입니다. 보기 제한: 200 MB / 5,000쪽. 작업 제한: 합계 512 MiB / 10,000쪽, 병합 최대 64파일. 큰 결과는 저장되어도 여기서 열리지 않을 수 있습니다.\n텍스트 편집·자르기·OCR·주석·인쇄는 이 창에서 지원하지 않습니다. " + ReviewNotice + "\n\n작업은 디스크 원본에서 읽습니다. 외부에서 원본을 바꿨다면 다시 여십시오. 취소는 처리 종료까지 기다립니다. 저장 직후 취소한 경우 완료 결과가 표시됩니다.", "시험판 사용 범위", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        });
        cancel.Click += (_, _) => { operationCancellation?.Cancel(); cancel.IsEnabled = false; status.Text = "취소 요청됨 · 처리 종료 대기 중…"; }; tools.Children.Add(cancel);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.DimGray });
        Closing += async (_, e) => {
            if (closeReady) return;
            e.Cancel = true;
            if (closed) return;
            closed = true; lifetime.Cancel(); operationCancellation?.Cancel(); RefreshActions();
            try { if (idle != null) await idle.Task; await ClearAsync(); }
            finally { closeReady = true; await Dispatcher.InvokeAsync(new Action(Close)); }
        };
        SetZoom(1); RefreshActions();
    }
    private bool HasDocument() => session != null && originalPath != null && currentPage > 0;
    private void Add(Panel panel, string text, Func<Task> action, Func<bool>? available = null)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(3) };
        button.Click += async (_, _) => { if (busy || closed) return; try { await action(); } catch (Exception ex) { status.Text = "실패 · 원본 변경 없음: " + ex.Message; } };
        actions.Add((button, available ?? (() => true))); panel.Children.Add(button);
    }
    public async Task OpenPdfAsync(string path)
    {
        if (busy || closed) return;
        SetBusy(true);
        try
        {
            await ClearAsync();
            session = new ReadOnlyPdfSession(); status.Text = "PDF 임시 복사본 여는 중…";
            var fullPath = Path.GetFullPath(path);
            await session.OpenAsync(fullPath, lifetime.Token);
            originalPath = fullPath; sourceLabel.Text = "현재 입력 원본: " + fullPath;
            Title = session.DisplayName + " · 원본 보호 / 새 파일 작업 TEST";
            await RenderPageAsync(1);
        }
        catch (OperationCanceledException) { if (!closed) status.Text = "열기 취소됨"; await ClearFailedAsync(); }
        catch (Exception ex) { status.Text = "열기 실패: " + ex.Message; await ClearFailedAsync(); }
        finally { SetBusy(false); }
    }
    private async Task ShowPageAsync(int page)
    {
        if (busy || session == null || closed || page < 1 || page > session.PageCount) return;
        SetBusy(true);
        try { await RenderPageAsync(page); }
        catch (OperationCanceledException) { if (!closed) status.Text = "취소됨"; }
        catch (Exception ex) { image.Source = null; currentPage = 0; pageLabel.Text = "표시 실패 · 다시 열기"; status.Text = "페이지 표시 실패: " + ex.Message; }
        finally { SetBusy(false); }
    }
    private async Task RenderPageAsync(int page)
    {
        image.Source = null; status.Text = $"{page}쪽 렌더링 중…";
        var bytes = await session!.RenderAsync(page, lifetime.Token);
        var bitmap = await Task.Run(() => Decode(bytes), lifetime.Token);
        if (closed) return;
        image.Source = bitmap; currentPage = page;
        pageLabel.Text = $"{currentPage} / {session.PageCount}쪽";
        SetZoom(zoom); status.Text = "원본 보기 · 96 DPI · " + session.Notice;
    }
    private async Task SelectPagesAsync(bool reorder)
    {
        if (!HasDocument()) return;
        var dialog = new PdfPageOrderDialog(session!.PageCount, currentPage, reorder) { Owner = this };
        if (dialog.ShowDialog() == true) await RunOperationAsync(reorder ? PdfOperationKind.Reorder : PdfOperationKind.Extract, dialog.Pages);
    }
    private async Task MergeAsync()
    {
        if (!HasDocument()) return;
        var chooser = new OpenFileDialog { Title = "현재 문서 뒤에 추가할 PDF 선택", Filter = "PDF 문서 (*.pdf)|*.pdf", CheckFileExists = true, Multiselect = true };
        if (chooser.ShowDialog(this) != true) return;
        if (chooser.FileNames.Length > 63) { status.Text = "병합은 현재 문서 포함 최대 64파일입니다."; return; }
        var order = new PdfMergeOrderDialog(originalPath!, chooser.FileNames) { Owner = this };
        if (order.ShowDialog() == true) await RunOperationAsync(PdfOperationKind.Merge, inputs: order.InputPaths);
    }
    private async Task RunOperationAsync(PdfOperationKind kind, IReadOnlyList<int>? pages = null, int degrees = 90, IReadOnlyList<string>? inputs = null)
    {
        if (busy || closed || !HasDocument()) return;
        var sourcePaths = inputs?.ToArray() ?? [originalPath!];
        var output = ChooseNewOutput(sourcePaths, kind);
        if (output == null) { status.Text = "저장 선택 취소 · 원본 변경 없음"; return; }
        SetBusy(true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operationCancellation = cancellation; resultPath = null; cancel.IsEnabled = true;
        status.Text = "새 PDF 생성 중… 원본은 변경하지 않습니다.";
        try
        {
            var result = await operations.ExecuteAsync(new PdfOperationRequest(kind, sourcePaths, output, pages?.ToArray(), degrees), cancellation.Token);
            resultPath = result.OutputPath;
            status.Text = $"새 파일 저장 완료: {result.OutputPath}\n{result.PageCount}쪽 / {result.Bytes:N0} bytes · 현재 화면은 입력 원본입니다. ‘결과 열기’로 확인하십시오.\n" + ReviewNotice;
            if (result.Warnings.Count > 0) status.Text += "\n엔진 경고: " + string.Join(" / ", result.Warnings);
        }
        catch (OperationCanceledException) { if (!closed) status.Text = "작업 취소됨 · 새 결과 저장 완료 없음 · 원본 변경 없음"; }
        catch (Exception ex) { if (!closed) status.Text = "작업 실패 · 원본 변경 없음: " + ex.Message; }
        finally { operationCancellation = null; cancel.IsEnabled = false; SetBusy(false); }
    }
    private string? ChooseNewOutput(IReadOnlyList<string> inputs, PdfOperationKind kind)
    {
        var dialog = new SaveFileDialog { Title = "새 PDF 이름 선택 · 기존 파일 덮어쓰기 불가", Filter = "PDF 문서 (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true, CheckPathExists = true, OverwritePrompt = false,
            InitialDirectory = Path.GetDirectoryName(originalPath!), FileName = Path.GetFileNameWithoutExtension(originalPath!) + "_" + kind.ToString().ToLowerInvariant() + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pdf" };
        while (dialog.ShowDialog(this) == true)
        {
            var error = ValidateOutputPath(dialog.FileName, inputs);
            if (error == null) return Path.GetFullPath(dialog.FileName);
            MessageBox.Show(this, error, "다른 새 파일 이름을 선택하십시오", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return null;
    }
    internal static string? ValidateOutputPath(string path, IReadOnlyList<string> inputs)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) return "로컬 절대 경로의 .pdf 파일 이름을 지정하십시오.";
        var full = Path.GetFullPath(path);
        if (inputs.Any(input => string.Equals(Path.GetFullPath(input), full, StringComparison.OrdinalIgnoreCase))) return "입력 원본 경로로 저장할 수 없습니다.";
        if (File.Exists(full) || Directory.Exists(full)) return "이미 존재하는 파일/폴더입니다. 사용하지 않은 이름을 지정하십시오.";
        return null; // Service checks links, path rules and existence again at publish time.
    }
    internal static BitmapSource Decode(byte[] bytes)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new IOException("잘못된 PNG 출력.");
        var w = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var h = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (w == 0 || h == 0 || w > 10000 || h > 10000 || (ulong)w * h > 25000000) throw new IOException("페이지 픽셀 제한 초과.");
        using var stream = new MemoryStream(bytes, false);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    private void SetZoom(double value) { zoom = Math.Clamp(value, .25, 4); if (image.Source is BitmapSource bitmap) { image.Width = bitmap.PixelWidth * zoom; image.Height = bitmap.PixelHeight * zoom; } zoomLabel.Text = $"{zoom:P0}"; }
    private void RefreshActions() { foreach (var item in actions) item.Button.IsEnabled = !busy && !closed && item.Available(); }
    private void SetBusy(bool value) { busy = value; if (value) idle = new(TaskCreationOptions.RunContinuationsAsynchronously); else idle?.TrySetResult(); RefreshActions(); }
    private async Task ClearFailedAsync() { originalPath = null; sourceLabel.Text = ""; image.Source = null; currentPage = 0; pageLabel.Text = ""; try { var old = session; session = null; if (old != null) await old.DisposeAsync(); } catch (Exception ex) { status.Text += " 임시 파일 정리 실패: " + ex.Message; } }
    private async Task ClearAsync() { await ClearFailedAsync(); if (!closed) { Title = "실제 PDF · 페이지 작업 TEST"; status.Text = "문서 닫힘 · 원본 변경 없음. PDF 열기를 선택하십시오."; } RefreshActions(); }
}

internal sealed class PdfPageOrderDialog : Window
{
    internal IReadOnlyList<int> Pages { get; private set; } = [];
    internal PdfPageOrderDialog(int count, int current, bool reorder)
    {
        Title = reorder ? "쪽 순서 변경 · 새 파일" : "쪽 추출 · 새 파일";
        Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = $"총 {count}쪽. 입력한 순서대로 새 PDF를 만듭니다.\n예: 3,1-2 또는 4-2,1 (역순 범위 가능). 중복 불가.\n" + (reorder ? "모든 쪽을 정확히 한 번씩 입력하십시오." : "추출할 쪽만 입력하십시오. 원본에서 삭제하지 않습니다."), TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { Text = reorder ? $"1-{count}" : current.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(input);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "새 파일 위치 선택…", IsDefault = true, Margin = new Thickness(4), Padding = new Thickness(8) };
        ok.Click += (_, _) => { try { Pages = ParsePages(input.Text, count, reorder); DialogResult = true; } catch (ArgumentException ex) { error.Text = ex.Message; } };
        buttons.Children.Add(ok); buttons.Children.Add(new Button { Content = "취소", IsCancel = true, Margin = new Thickness(4), Padding = new Thickness(8) }); panel.Children.Add(buttons);
    }
    internal static int[] ParsePages(string text, int count, bool reorder)
    {
        if (count < 1 || count > 10000 || string.IsNullOrWhiteSpace(text) || text.Length > 100000) throw new ArgumentException("유효한 쪽 번호를 입력하십시오.");
        var pages = new List<int>(); var seen = new HashSet<int>();
        foreach (var part in text.Split(','))
        {
            var ends = part.Trim().Split('-');
            if (ends.Length is < 1 or > 2 || !int.TryParse(ends[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var first)) throw new ArgumentException("쉼표와 하이픈으로 쪽 번호를 입력하십시오. 예: 3,1-2");
            var last = first;
            if (ends.Length == 2 && !int.TryParse(ends[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out last)) throw new ArgumentException("범위 끝 쪽 번호가 잘못되었습니다.");
            if (first < 1 || last < 1 || first > count || last > count) throw new ArgumentException($"쪽 번호는 1~{count}이어야 합니다.");
            var step = first <= last ? 1 : -1;
            for (var page = first; ; page += step) { if (!seen.Add(page)) throw new ArgumentException("같은 쪽을 두 번 지정할 수 없습니다."); pages.Add(page); if (page == last) break; }
        }
        if (reorder && pages.Count != count) throw new ArgumentException("순서 변경은 모든 쪽을 정확히 한 번씩 지정해야 합니다.");
        return pages.ToArray();
    }
}

internal sealed class PdfMergeOrderDialog : Window
{
    internal IReadOnlyList<string> InputPaths { get; private set; } = [];
    internal PdfMergeOrderDialog(string original, IReadOnlyList<string> additions)
    {
        Title = "병합 순서 확인 · 위에서 아래로"; Width = 680; Height = 460; MinWidth = 450; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        var info = new TextBlock { Text = "1. 현재 문서 (첫 번째 고정):\n" + original + "\n\n아래 추가 문서를 위에서 아래 순서로 병합합니다. 선택 후 위/아래로 조정하십시오.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);
        var files = additions.ToList(); var list = new ListBox { ItemsSource = files, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar);
        void Move(int offset) { var index = list.SelectedIndex; var target = index + offset; if (index < 0 || target < 0 || target >= files.Count) return; (files[index], files[target]) = (files[target], files[index]); list.Items.Refresh(); list.SelectedIndex = target; }
        var up = new Button { Content = "위로", Margin = new Thickness(3), Padding = new Thickness(8) }; up.Click += (_, _) => Move(-1); bar.Children.Add(up);
        var down = new Button { Content = "아래로", Margin = new Thickness(3), Padding = new Thickness(8) }; down.Click += (_, _) => Move(1); bar.Children.Add(down);
        var ok = new Button { Content = "이 순서로 새 파일 저장…", IsDefault = true, Margin = new Thickness(3), Padding = new Thickness(8) }; ok.Click += (_, _) => { InputPaths = new[] { original }.Concat(files).ToArray(); DialogResult = true; }; bar.Children.Add(ok);
        bar.Children.Add(new Button { Content = "취소", IsCancel = true, Margin = new Thickness(3), Padding = new Thickness(8) }); root.Children.Add(list);
    }
}
