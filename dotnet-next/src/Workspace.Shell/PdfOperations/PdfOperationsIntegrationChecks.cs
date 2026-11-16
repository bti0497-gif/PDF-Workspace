using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workspace.Shell.PdfOperations;

/// <summary>Explicit opt-in real CLI backend tests, not WPF UI acceptance. Never called automatically.</summary>
public static class PdfOperationsIntegrationChecks
{
    /// <summary>Pass a trusted three-page fixture and an existing local output directory. Only fresh copies are changed.</summary>
    public static async Task<IReadOnlyList<string>> RunAsync(string executable, string threePageFixture,
        string outputDirectory, CancellationToken token = default)
    {
        var run = Path.Combine(outputDirectory, "pdfops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var source = Path.Combine(run, "원본 한글 # & 복사.pdf");
        File.Copy(threePageFixture, source, false);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(source, token));
        var service = new PdfCraftOperationsService(executable);
        var checks = new List<string>();
        void Assert(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Failed: " + name); checks.Add(name); }
        string Out(string name) => Path.Combine(run, name + ".pdf");
        async Task<PdfOperationResult> Operation(PdfOperationKind kind, string name, int count, int[]? pages = null, int degrees = 90, string[]? inputs = null)
        {
            var result = await service.ExecuteAsync(new(kind, inputs ?? [source], Out(name), pages, degrees), token);
            Assert(result.PageCount == count && File.Exists(result.OutputPath) && result.Bytes > 5, name);
            return result;
        }
        async Task Reject<T>(PdfOperationRequest request, string name) where T : Exception
        {
            try { await service.ExecuteAsync(request, token); }
            catch (T) { checks.Add(name); return; }
            throw new InvalidOperationException("Expected rejection: " + name);
        }
        var save = await Operation(PdfOperationKind.SaveAs, "다른 이름 저장", 3);
        var rotate = await Operation(PdfOperationKind.Rotate, "회전", 3, [1], 90);
        var delete = await Operation(PdfOperationKind.Delete, "삭제", 2, [2]);
        var extract = await Operation(PdfOperationKind.Extract, "추출 3-1", 2, [3, 1]);
        var reorder = await Operation(PdfOperationKind.Reorder, "순서 3-1-2", 3, [3, 1, 2]);
        await Operation(PdfOperationKind.Merge, "병합", 5, inputs: [extract.OutputPath, reorder.OutputPath]);
        await Operation(PdfOperationKind.SaveAs, "편집 결과 저장", 2, inputs: [delete.OutputPath]);
        await Reject<IOException>(new(PdfOperationKind.SaveAs, [source], source), "original overwrite rejected");
        await Reject<IOException>(new(PdfOperationKind.SaveAs, [source], save.OutputPath), "existing output rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.Delete, [source], Out("invalid-all"), [1, 2, 3]), "delete every page rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.Extract, [source], Out("invalid-range"), [4]), "out of range rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.Reorder, [source], Out("invalid-order"), [3, 1]), "incomplete reorder rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.Rotate, [source], Out("invalid-angle"), [1], 45), "invalid angle rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.Extract, [source], Out("invalid-duplicate"), [1, 1]), "duplicate pages rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.SaveAs, [source], Out("stream") + ":other.pdf"), "alternate stream rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.SaveAs, [source], @"\\localhost\share\bad.pdf"), "UNC output rejected");
        await Reject<ArgumentException>(new(PdfOperationKind.SaveAs, [source], "relative.pdf"), "relative output rejected");
        var fake = Out("not-a-pdf");
        await File.WriteAllTextAsync(fake, "not a PDF", token);
        await Reject<PdfOperationException>(new(PdfOperationKind.SaveAs, [fake], Out("invalid-header")), "invalid header rejected");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await service.ExecuteAsync(new(PdfOperationKind.SaveAs, [source], Out("canceled")), canceled.Token); }
            catch (OperationCanceledException) { checks.Add("pre-cancellation honored"); }
            Assert(!File.Exists(Out("canceled")), "cancellation no final output");
        }
        try
        {
            await new PdfCraftOperationsService(executable, TimeSpan.FromMilliseconds(1))
                .ExecuteAsync(new(PdfOperationKind.SaveAs, [source], Out("timeout")), token);
            throw new InvalidOperationException("Expected timeout.");
        }
        catch (TimeoutException) { checks.Add("timeout enforced"); }
        Assert(!File.Exists(Out("timeout")), "timeout no final output");
        var untrustedExecutable = Path.Combine(run, "untrusted.exe");
        await File.WriteAllTextAsync(untrustedExecutable, "not executable", token);
        try
        {
            await new PdfCraftOperationsService(untrustedExecutable)
                .ExecuteAsync(new(PdfOperationKind.SaveAs, [source], Out("untrusted")), token);
            throw new InvalidOperationException("Expected binary hash rejection.");
        }
        catch (PdfOperationException ex) when (ex.Message.Contains("SHA-256", StringComparison.Ordinal))
        { checks.Add("injected executable hash mismatch rejected before launch"); }
        Assert(!File.Exists(Out("untrusted")), "untrusted binary no final output");
        // Content ordering checks use the pinned CLI, not an independent PDF engine.
        async Task<string> Cli(params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            foreach (var arg in arguments) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI not started.");
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            var text = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
            return text.Trim();
        }
        var p1 = await Cli("text", source, "--page", "1");
        var p3 = await Cli("text", source, "--page", "3");
        Assert(await Cli("text", extract.OutputPath, "--page", "1") == p3, "extract first page is source page 3");
        Assert(await Cli("text", extract.OutputPath, "--page", "2") == p1, "extract second page is source page 1");
        Assert(await Cli("text", reorder.OutputPath, "--page", "1") == p3, "reorder first page is source page 3");
        Assert(await Cli("text", delete.OutputPath, "--page", "2") == p3, "delete remaining page is source page 3");
        using var originalInfo = JsonDocument.Parse(await Cli("info", source));
        using var rotatedInfo = JsonDocument.Parse(await Cli("info", rotate.OutputPath));
        var sizeBefore = originalInfo.RootElement.GetProperty("first_page_pt");
        var sizeAfter = rotatedInfo.RootElement.GetProperty("first_page_pt");
        Assert(Math.Abs(sizeBefore[0].GetDouble() - sizeAfter[1].GetDouble()) < 0.1
            && Math.Abs(sizeBefore[1].GetDouble() - sizeAfter[0].GetDouble()) < 0.1, "rotation swaps displayed dimensions");
        Assert(before.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(source, token))), "source SHA-256 unchanged");
        Assert(!Directory.EnumerateFiles(run, ".pdf-save-*.tmp").Any(), "no sibling publication staging remains");
        await File.WriteAllTextAsync(Path.Combine(run, "checks.json"), JsonSerializer.Serialize(new { run, checks }), token);
        return checks;
    }
}
