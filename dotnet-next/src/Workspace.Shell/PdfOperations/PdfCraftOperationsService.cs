using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workspace.Shell.PdfOperations;

public enum PdfOperationKind { SaveAs, Rotate, Delete, Extract, Reorder, Merge }
/// <summary>Pages are one-based. Reorder requires a complete permutation. Absolute local paths only.</summary>
public sealed record PdfOperationRequest(PdfOperationKind Kind, IReadOnlyList<string> InputPaths,
    string OutputPath, IReadOnlyList<int>? Pages = null, int Degrees = 90);
public sealed record PdfOperationResult(string OutputPath, int PageCount, long Bytes, IReadOnlyList<string> Warnings);
public interface IPdfOperationsService
{
    Task<PdfOperationResult> ExecuteAsync(PdfOperationRequest request, CancellationToken cancellationToken = default);
}
public sealed class PdfOperationException : Exception
{
    public PdfOperationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// UI-free adapter for trusted PdfCraft 0.21.1. The child sees only copies in a fresh job root.
/// No caller scripts or roots. NOT an OS sandbox against hostile PDFs or same-user filesystem races.
/// Existing outputs are rejected. Success means reopened/page-count validated, not full PDF fidelity.
/// </summary>
public sealed class PdfCraftOperationsService : IPdfOperationsService
{
    private readonly string executable;
    private readonly TimeSpan timeout;
    // Official v0.21.1 Windows-x64 portable CLI, from the separately verified release package.
    public const string ExpectedCliSha256 = "32C94DF8EABB995DE8B24B078BF4D51F84A2CEE8B42C868B660017F7E5BBF8D0";
    private const long MaxInputBytes = 512L * 1024 * 1024;
    private const int MaxPages = 10000;
    private const int MaxCaptureChars = 2 * 1024 * 1024;

    public PdfCraftOperationsService(string? executablePath = null, TimeSpan? operationTimeout = null)
    {
        executable = LocalPath(executablePath ?? Path.Combine(AppContext.BaseDirectory,
            "Dependencies", "PdfCraft", "0.21.1", "pdfcraft-cli.exe"));
        timeout = operationTimeout ?? TimeSpan.FromMinutes(2);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
    }

    public async Task<PdfOperationResult> ExecuteAsync(PdfOperationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(request.Kind)) throw new ArgumentException("Unknown PDF operation.");
        var inputs = request.InputPaths?.Select(LocalPath).ToArray() ?? throw new ArgumentException("Inputs are required.");
        var pages = request.Pages?.ToArray();
        var output = LocalPath(request.OutputPath);
        if (inputs.Length is < 1 or > 64 || (request.Kind != PdfOperationKind.Merge && inputs.Length != 1)
            || (request.Kind == PdfOperationKind.Merge && inputs.Length < 2))
            throw new ArgumentException("Provide one input, or 2–64 inputs for merge.");
        if (inputs.Contains(output, StringComparer.OrdinalIgnoreCase)) throw new IOException("Original overwrite is forbidden.");
        RequirePdf(output);
        EnsureNewOutput(output);
        CheckNoLinks(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("Pinned PdfCraft CLI is not installed.", executable);
        var temporaryBase = LocalPath(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        CheckNoLinks(temporaryBase);
        var job = Path.Combine(temporaryBase, "DeojonPdfOps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        var locks = new List<FileStream>();
        var warnings = new List<string>();
        string? publishStage = null;
        using var timer = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        var token = linked.Token;
        try
        {
            // Hold a non-write/non-delete share until all native calls have finished, also for injected test paths.
            var executableLock = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            locks.Add(executableLock);
            var executableHash = Convert.ToHexString(await SHA256.HashDataAsync(executableLock, token));
            if (!executableHash.Equals(ExpectedCliSha256, StringComparison.Ordinal))
                throw new PdfOperationException("PdfCraft executable SHA-256 does not match the approved 0.21.1 binary.");
            var version = await RunAsync(job, ["--version"], token);
            var versionLine = version.Split('\n', 2)[0].Trim();
            if (!versionLine.Equals("0.21.1", StringComparison.Ordinal)
                && !versionLine.Equals("pdfcraft-cli 0.21.1", StringComparison.Ordinal))
                throw new PdfOperationException("Expected PdfCraft CLI 0.21.1; a different dependency was found.");
            var counts = new List<int>();
            var copyNames = new List<string>();
            long total = 0;
            for (var i = 0; i < inputs.Length; i++)
            {
                RequirePdf(inputs[i]);
                CheckNoLinks(inputs[i]);
                var source = new FileStream(inputs[i], FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                locks.Add(source);
                total = checked(total + source.Length);
                if (source.Length < 5 || total > MaxInputBytes) throw new PdfOperationException("Input size is unsupported (total maximum 512 MiB).");
                var name = $"input-{i:D3}.pdf";
                var copy = Path.Combine(job, name);
                await using (var target = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await source.CopyToAsync(target, token);
                await ValidateHeaderAsync(copy, token);
                counts.Add(await InspectAsync(job, name, warnings, token));
                copyNames.Add(name);
            }
            var expected = ValidateSelection(request.Kind, pages, request.Degrees, counts);
            var steps = new List<object>();
            static object Step(string tool, object args) => new { tool, args };
            if (request.Kind == PdfOperationKind.Merge)
                steps.Add(Step("doc_combine", new { paths = copyNames, @out = "output.pdf", open = false, field_conflicts = "rename" }));
            else
            {
                steps.Add(Step("doc_open", new { path = copyNames[0] }));
                if (request.Kind == PdfOperationKind.Rotate)
                    steps.Add(Step("page_rotate", new { doc = 1, pages, degrees = request.Degrees }));
                if (request.Kind == PdfOperationKind.Delete)
                    steps.Add(Step("page_delete", new { doc = 1, pages }));
                if (request.Kind is PdfOperationKind.Extract or PdfOperationKind.Reorder)
                    steps.Add(Step("page_extract", new { doc = 1, pages, @out = "output.pdf", open = false, delete = false }));
                else
                    steps.Add(Step("doc_save", new { doc = 1, path = "output.pdf", full = true, flatten_fill_sign = false }));
                steps.Add(Step("doc_close", new { doc = 1 }));
            }
            var script = Path.Combine(job, "steps.json");
            await File.WriteAllTextAsync(script, JsonSerializer.Serialize(steps), new UTF8Encoding(false), token);
            var report = await RunAsync(job, ["run", "--script", script, "--root", job, "--report"], token);
            using (var document = JsonDocument.Parse(report))
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
                    || !root.TryGetProperty("steps", out var results) || results.GetArrayLength() != steps.Count
                    || results.EnumerateArray().Any(x => !x.TryGetProperty("status", out var status) || status.GetString() != "ok"))
                    throw new PdfOperationException("PdfCraft did not confirm every operation; no output was published.");
                CollectWarnings(root, warnings);
            }
            var resultFile = Path.Combine(job, "output.pdf");
            CheckNoLinks(resultFile);
            await ValidateHeaderAsync(resultFile, token);
            var actual = await InspectAsync(job, "output.pdf", warnings, token);
            if (actual != expected) throw new PdfOperationException($"Output page count mismatch: expected {expected}, got {actual}.");
            EnsureNewOutput(output);
            publishStage = Path.Combine(Path.GetDirectoryName(output)!, ".pdf-save-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var from = new FileStream(resultFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var to = new FileStream(publishStage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await from.CopyToAsync(to, token);
                await to.FlushAsync(token);
                to.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            EnsureNewOutput(output);
            var bytes = new FileInfo(publishStage).Length;
            File.Move(publishStage, output, false);
            publishStage = null;
            return new PdfOperationResult(output, actual, bytes, warnings.Distinct().ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timer.IsCancellationRequested)
        { throw new TimeoutException("PDF operation timed out; the original was not modified."); }
        catch (JsonException ex) { throw new PdfOperationException("PdfCraft returned an invalid report; no output was published.", ex); }
        finally
        {
            foreach (var file in locks) file.Dispose();
            if (publishStage != null) { try { File.Delete(publishStage); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            try { Directory.Delete(job, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static int ValidateSelection(PdfOperationKind kind, int[]? pages, int degrees, List<int> counts)
    {
        if (kind is PdfOperationKind.Merge or PdfOperationKind.SaveAs)
        {
            if (pages != null) throw new ArgumentException("Pages must be omitted for merge and Save As.");
            var sum = counts.Sum();
            if (sum > MaxPages) throw new ArgumentException("Output exceeds 10,000 pages.");
            return sum;
        }
        if (pages == null || pages.Length == 0 || pages.Length > MaxPages || pages.Any(p => p < 1 || p > counts[0])
            || pages.Distinct().Count() != pages.Length) throw new ArgumentException("Select distinct, valid one-based pages.");
        if (kind == PdfOperationKind.Rotate && degrees is not (90 or 180 or 270 or -90 or -180 or -270))
            throw new ArgumentException("Rotation must be ±90, ±180 or ±270 degrees.");
        if (kind == PdfOperationKind.Delete && pages.Length >= counts[0]) throw new ArgumentException("Cannot delete every page.");
        if (kind == PdfOperationKind.Reorder && pages.Length != counts[0]) throw new ArgumentException("Reorder needs a complete page permutation.");
        return kind switch { PdfOperationKind.Delete => counts[0] - pages.Length, PdfOperationKind.Extract => pages.Length, _ => counts[0] };
    }

    private async Task<int> InspectAsync(string job, string name, List<string> warnings, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await RunAsync(job, ["info", name], token));
        var root = doc.RootElement;
        if (root.TryGetProperty("encrypted", out var encrypted) && encrypted.ValueKind == JsonValueKind.True)
            throw new PdfOperationException("Encrypted PDFs are not supported in this test version.");
        if (!root.TryGetProperty("pages", out var pages) || !pages.TryGetInt32(out var count) || count is < 1 or > MaxPages)
            throw new PdfOperationException("PDF page count is invalid or exceeds 10,000.");
        CollectWarnings(root, warnings);
        return count;
    }
    private static void CollectWarnings(JsonElement value, List<string> warnings)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if ((property.Name is "warnings" or "repairs") && property.Value.ValueKind == JsonValueKind.Array)
                    foreach (var warning in property.Value.EnumerateArray()) warnings.Add(warning.ToString());
                else CollectWarnings(property.Value, warnings);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CollectWarnings(item, warnings);
    }
    private async Task<string> RunAsync(string job, string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = job, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new PdfOperationException("PdfCraft could not be started.");
        var stdout = ReadBoundedAsync(process.StandardOutput, token);
        var stderr = ReadBoundedAsync(process.StandardError, token);
        try
        {
            await process.WaitForExitAsync(token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new PdfOperationException($"PdfCraft failed (exit {process.ExitCode}). {error.Text}");
            if (output.Overflow || error.Overflow) throw new PdfOperationException("PdfCraft output exceeded the safety limit.");
            return output.Text;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            throw;
        }
    }
    private static async Task<(string Text, bool Overflow)> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        var overflow = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            var take = Math.Min(count, MaxCaptureChars - text.Length);
            text.Append(buffer, 0, take);
            overflow |= take < count;
        }
        return (text.ToString(), overflow);
    }
    private static async Task ValidateHeaderAsync(string file, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var header = new byte[5];
        if (stream.Length < 5 || stream.Length > MaxInputBytes) throw new PdfOperationException("PDF size is invalid or exceeds 512 MiB.");
        await stream.ReadExactlyAsync(header, token);
        if (!header.AsSpan().SequenceEqual("%PDF-"u8)) throw new PdfOperationException("File does not start with a PDF header.");
    }
    private static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.Contains('\0')) throw new ArgumentException("An absolute local drive path is required; UNC/device paths are not supported.");
        var full = Path.GetFullPath(path);
        if (full.Length < 3 || full[1] != ':' || full[2] != '\\' || full.AsSpan(2).Contains(':'))
            throw new ArgumentException("Only local drive paths without alternate streams are supported.");
        foreach (var segment in full[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Ambiguous Windows path component.");
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3])))
                throw new ArgumentException("Windows device filenames are not allowed.");
        }
        return full;
    }
    private static void RequirePdf(string file)
    {
        if (!Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only .pdf files are supported.");
    }
    private static void CheckNoLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse points/links are not accepted."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static void EnsureNewOutput(string path)
    {
        CheckNoLinks(path);
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("Output directory must already exist.");
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Save As requires a new filename; replacement is disabled.");
    }
}
