using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Workspace.Shell;

/// <summary>Uses Windows Authenticode policy, independent of release manifest data.</summary>
internal static class EngineSignatureVerifier
{
    private const string Script = "$ErrorActionPreference='Stop';$s=Get-AuthenticodeSignature -LiteralPath $env:PDF_WORKSPACE_SIGNATURE_FILE;[pscustomobject]@{status=$s.Status.ToString();subject=if($s.SignerCertificate){$s.SignerCertificate.Subject}else{''}}|ConvertTo-Json -Compress";
    internal static async Task<bool> VerifyAsync(string file, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(file)) return false;
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable)) return false;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoLogo"); start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
        start.Environment["PDF_WORKSPACE_SIGNATURE_FILE"] = file;
        // Do not inherit PowerShell 7 module paths into Windows PowerShell 5.1.
        start.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "Modules");
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            if (!process.Start()) return false;
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout; _ = await stderr;
            if (process.ExitCode != 0 || output.Length > 16384) return false;
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            return root.TryGetProperty("status", out var status) && status.GetString() == "Valid"
                && root.TryGetProperty("subject", out var subject)
                && (subject.GetString() ?? "").Contains("O=Learning Machines Inc", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException) { return false; }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
