using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Workspace.Shell.PdfViewing;
namespace Workspace.Shell;
internal static partial class ShellSmokeChecks
{
    private static async Task CheckReadOnlyPdfAsync(Action<bool, string> check)
    {
        check(ReadOnlyPdfSession.ParsePageCount("{\"pages\":3,\"encrypted\":false}") == 3, "PDF metadata page count");
        foreach (var json in new[] { "{\"pages\":0}", "{\"pages\":5001}", "{\"pages\":1,\"encrypted\":true}", "not JSON" }) {
            var rejected = false; try { ReadOnlyPdfSession.ParsePageCount(json); } catch { rejected = true; }
            check(rejected, "PDF invalid/encrypted metadata rejected: " + json);
        }
        var odd = "C:\\한글 폴더\\a & --out strange.pdf";
        var start = ReadOnlyPdfSession.CreateStartInfo("engine.exe", Path.GetTempPath(), ["info", odd]);
        check(!start.UseShellExecute && start.ArgumentList.Count == 2 && start.ArgumentList[1] == odd && start.Arguments == "", "PDF arbitrary filename stays one ArgumentList token");
        check(start.CreateNoWindow && start.RedirectStandardError && start.RedirectStandardOutput, "PDF headless redirected CLI boundary");
        check(start.Environment["TEMP"] == Path.GetTempPath(), "PDF CLI temporary output confined by environment");
        foreach (var path in new[] { @"\\server\share\x.pdf", @"\\?\C:\x.pdf", @"C:\x.pdf:stream" }) {
            var rejected = false; try { ReadOnlyPdfSession.ValidateLocalPath(path); } catch { rejected = true; }
            check(rejected, "PDF remote/device/stream path rejected");
        }
        var badPng = false; try { ReadOnlyPdfWindow.Decode(new byte[24]); } catch { badPng = true; }
        check(badPng, "PDF malformed render rejected before bitmap decode");
        var viewer = new ReadOnlyPdfWindow();
        try {
            check(viewer.Title.Contains("실제 PDF") && viewer.Title.Contains("페이지 작업") && viewer.DataContext == null,
                "Real PDF page-operation window is separate from the synthetic sample model");
            var content = (DockPanel)viewer.Content;
            var buttons = content.Children.OfType<WrapPanel>().SelectMany(panel => panel.Children.OfType<Button>()).ToArray();
            var notices = content.Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
            check(notices.Any(text => text.Contains("원본 변경 없음") && text.Contains("각 작업은 새 PDF 저장") && text.Contains("합성 샘플과 분리")),
                "PDF page-operation UI explicitly promises separate new-file outputs, not source mutation");
            check(!Descendants<FrameworkElement>(content).Any(element => element.DataContext is Presentation.ShellViewModel || element is Presentation.PreviewWorkspace),
                "Real PDF content contains no synthetic workspace or shell model binding");
            foreach (var caption in new[] { "현재 쪽 90° 회전", "현재 쪽 180° 회전", "현재 쪽 270° 회전", "현재 쪽 삭제…", "쪽 추출…", "쪽 순서 변경…", "PDF 병합…" }) {
                var button = buttons.SingleOrDefault(candidate => candidate.Content?.ToString() == caption);
                check(button != null && !button.IsEnabled, "PDF operation exists and requires an opened source: " + caption);
            }
            check(buttons.Single(button => button.Content?.ToString() == "PDF 열기").IsEnabled,
                "Real PDF source open is available before a document is loaded");
            check(!buttons.Single(button => button.Content?.ToString() == "결과 열기").IsEnabled
                && !buttons.Single(button => button.Content?.ToString() == "작업 취소").IsEnabled,
                "PDF result-open and cancellation require a result or an active operation");
            check(!buttons.Any(button => new[] { "저장", "편집", "인쇄", "실행 취소", "다시 실행" }.Contains(button.Content?.ToString())),
                "Real PDF UI exposes no in-place save, content editing, printing, or synthetic undo controls");

            // UI preflight checks complement, never replace, the service's publish-time guards.
            var guardRoot = Path.Combine(Path.GetTempPath(), "PdfViewerOutputGuards-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(guardRoot);
            try {
                var original = Path.Combine(guardRoot, "original.pdf");
                var existing = Path.Combine(guardRoot, "existing.pdf");
                var directory = Path.Combine(guardRoot, "directory.pdf");
                await File.WriteAllTextAsync(original, "UI guard fixture, not a PDF processing fixture");
                await File.WriteAllTextAsync(existing, "existing output must remain intact");
                Directory.CreateDirectory(directory);
                var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(original));
                check(ReadOnlyPdfWindow.ValidateOutputPath(original.ToUpperInvariant(), [original]) != null,
                    "PDF output UI rejects the input path case-insensitively");
                check(ReadOnlyPdfWindow.ValidateOutputPath(existing, [original]) != null,
                    "PDF output UI rejects unrelated existing files");
                check(ReadOnlyPdfWindow.ValidateOutputPath(directory, [original]) != null,
                    "PDF output UI rejects existing directories");
                check(ReadOnlyPdfWindow.ValidateOutputPath(Path.Combine(guardRoot, "new.txt"), [original]) != null
                    && ReadOnlyPdfWindow.ValidateOutputPath("relative.pdf", [original]) != null,
                    "PDF output UI requires an absolute PDF filename");
                var fresh = Path.Combine(guardRoot, "fresh.pdf");
                check(ReadOnlyPdfWindow.ValidateOutputPath(fresh, [original]) == null && !File.Exists(fresh),
                    "PDF output preflight accepts a fresh filename without writing a file");
                check(originalHash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(original)))
                    && await File.ReadAllTextAsync(existing) == "existing output must remain intact",
                    "PDF output preflight leaves source and existing output unchanged");
            } finally { Directory.Delete(guardRoot, true); }
        } finally { viewer.Close(); }
        var sample = Environment.GetEnvironmentVariable("PDFWORKSPACE_TEST_PDF");
        if (sample == null) return; // Optional local corpus test, never download fixtures/dependencies.
        var before = SHA256.HashData(await File.ReadAllBytesAsync(sample));
        var session = new ReadOnlyPdfSession(); var temp = session.TemporaryDirectory;
        try {
            await session.OpenAsync(sample, CancellationToken.None);
            check(session.PageCount > 0, "PDF real pinned CLI opens local fixture");
            var bitmap = ReadOnlyPdfWindow.Decode(await session.RenderAsync(1, CancellationToken.None));
            check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 && bitmap.IsFrozen, "PDF real CLI renders decodable immutable first page");
            var invalidPage = false; try { await session.RenderAsync(0, CancellationToken.None); } catch (ArgumentOutOfRangeException) { invalidPage = true; }
            check(invalidPage, "PDF invalid render page rejected");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var stopped = false; try { await session.RenderAsync(1, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            check(stopped, "PDF cancelled render does not execute");
        } finally { await session.DisposeAsync(); }
        check(!Directory.Exists(temp), "PDF session removes private copies and render files");
        check(before.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(sample))), "PDF original SHA256 unchanged after actual CLI rendering");
    }
}
