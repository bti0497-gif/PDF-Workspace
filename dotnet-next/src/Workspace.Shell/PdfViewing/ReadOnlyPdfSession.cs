using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workspace.Shell.PdfViewing;

// No WPF or sample-document dependency. Only a private copy is passed to the CLI.
internal sealed class ReadOnlyPdfSession : IAsyncDisposable
{
    internal const string EnvironmentKey = "PDFWORKSPACE_PDFCRAFT_CLI";
    internal const string PinnedHash = "32C94DF8EABB995DE8B24B078BF4D51F84A2CEE8B42C868B660017F7E5BBF8D0";
    internal const long MaxInputBytes = 200L * 1024 * 1024;
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DeojonPdfReadOnly", Guid.NewGuid().ToString("N"));
    private string? executable;
    private bool disposed;
    public int PageCount { get; private set; }
    public string DisplayName { get; private set; } = "";
    public string Notice { get; private set; } = "";
    internal string TemporaryDirectory => directory;

    internal static string DependencyPath() => Environment.GetEnvironmentVariable(EnvironmentKey)
        ?? Path.Combine(AppContext.BaseDirectory, "Dependencies", "PdfCraft", "0.21.1", "pdfcraft-cli.exe");

    internal static string ValidateLocalPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.IndexOf(':', 2) >= 0)
            throw new IOException("로컬 일반 파일만 지원합니다. UNC·장치·대체 스트림은 열지 않습니다.");
        if (new DriveInfo(Path.GetPathRoot(full)!).DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new IOException("시험판은 로컬 고정·이동식 디스크만 지원합니다.");
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("링크 또는 재분석 경로는 시험판에서 지원하지 않습니다.");
        return full;
    }

    public async Task OpenAsync(string path, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await sessionGate.WaitAsync(linked.Token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (PageCount != 0) throw new InvalidOperationException("새 문서는 새 세션으로 여십시오.");
            executable = await Task.Run(async () => {
                var candidate = ValidateLocalPath(DependencyPath());
                await using var file = File.OpenRead(candidate);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, linked.Token));
                if (hash != PinnedHash) throw new IOException("PdfCraft 0.21.1 검증 실행파일과 해시가 다릅니다.");
                return candidate;
            }, linked.Token);
            Directory.CreateDirectory(directory);
            await Task.Run(async () => {
                var sourcePath = ValidateLocalPath(path);
                if (!string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("PDF 파일만 열 수 있습니다.");
                await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                if (source.Length is <= 0 or > MaxInputBytes) throw new IOException("파일 크기 제한: 1 byte–200 MB.");
                var header = new byte[5];
                await source.ReadExactlyAsync(header, linked.Token);
                if (Encoding.ASCII.GetString(header) != "%PDF-") throw new IOException("PDF 헤더를 확인할 수 없습니다.");
                source.Position = 0;
                await using var copy = new FileStream(Path.Combine(directory, "input.pdf"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(copy, linked.Token);
                DisplayName = Path.GetFileName(sourcePath);
            }, linked.Token);
            var json = await RunAsync(["info", Path.Combine(directory, "input.pdf")], linked.Token);
            PageCount = ParsePageCount(json);
            using var metadata = JsonDocument.Parse(json);
            Notice = metadata.RootElement.TryGetProperty("warnings", out var warnings) && warnings.GetArrayLength() > 0
                ? "엔진 경고가 있습니다. 원본과 표시를 별도로 확인하십시오." : "표시 충실도·한글 글꼴은 문서별 확인이 필요합니다.";
        }
        finally { sessionGate.Release(); }
    }

    internal static int ParsePageCount(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("encrypted", out var encrypted) && encrypted.GetBoolean())
            throw new IOException("암호화 PDF는 이 시험판에서 지원하지 않습니다.");
        var pages = doc.RootElement.GetProperty("pages").GetInt32();
        if (pages is < 1 or > 5000) throw new IOException("시험판 페이지 제한: 1–5000쪽.");
        return pages;
    }

    internal static ProcessStartInfo CreateStartInfo(string exe, string cwd, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        // Any conventional runtime temporary files remain within this disposable session.
        info.Environment["TEMP"] = cwd; info.Environment["TMP"] = cwd;
        return info;
    }

    private async Task<string> RunAsync(string[] args, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await ProcessGate.WaitAsync(deadline.Token);
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(executable!, directory, args) };
            process.Start();
            try
            {
                var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
                var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
                if (process.ExitCode != 0) throw new IOException("PDF 엔진이 파일을 처리하지 못했습니다. 손상·암호·미지원 파일일 수 있습니다.");
                return await stdout;
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("PDF 처리 제한 시간(30초)을 초과했습니다."); }
        finally { ProcessGate.Release(); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (text.Length + read > 1024 * 1024) throw new IOException("PDF 엔진 응답 크기 제한 초과.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    public async Task<byte[]> RenderAsync(int page, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await sessionGate.WaitAsync(linked.Token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (page < 1 || page > PageCount) throw new ArgumentOutOfRangeException(nameof(page));
            var output = Path.Combine(directory, "page.png");
            File.Delete(output);
            try
            {
                await RunAsync(["render", Path.Combine(directory, "input.pdf"), "--page", page.ToString(CultureInfo.InvariantCulture), "--dpi", "96", "--out", output], linked.Token);
                if (new FileInfo(output).Length > 64 * 1024 * 1024) throw new IOException("페이지 이미지 크기 제한 초과.");
                return await File.ReadAllBytesAsync(output, linked.Token);
            }
            finally { File.Delete(output); }
        }
        finally { sessionGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await sessionGate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        finally { sessionGate.Release(); }
    }
}
