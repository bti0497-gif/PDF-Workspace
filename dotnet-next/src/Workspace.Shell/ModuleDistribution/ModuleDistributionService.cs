using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace.Shell.ModuleDistribution;

public sealed record ModuleAsset(string Id, string Version, string Url, long Size, string Sha256, string? AssemblyName = null, string? Region = null, string? Type = null, ModuleResource[]? Files = null);
public sealed record ModuleResource(string Kind, string Name, string Url, long Size, string Sha256);
public sealed record DistributionManifest(string Product, string Version, int Api, ModuleAsset[] Modules);
/// <summary>Byte counts are cumulative across the candidate DLLs/resources (including copied cache bytes),
/// not per asset or network-only. Versions describe the validated plan and pre-activation verified cache;
/// Verified/Ready do not mean activation or runtime loading has completed.</summary>
public sealed record DistributionProgress(string Stage, string? ModuleId, long Bytes, long TotalBytes, string Detail,
    string? PlannedVersion = null, string? InstalledVersion = null, string? AssetName = null,
    string? AreaLabel = null, string? ChangeKind = null, int? ModuleCount = null, int? VerifiedModuleCount = null)
{
    // Preserve callers using the original five-value positional deconstruction.
    public void Deconstruct(out string stage, out string? moduleId, out long bytes, out long totalBytes, out string detail)
        => (stage, moduleId, bytes, totalBytes, detail) = (Stage, ModuleId, Bytes, TotalBytes, Detail);
}
public sealed record DistributionCandidate(string DirectoryName, DistributionManifest Manifest, string DirectoryPath);
public sealed record DistributionResult(string Status, DistributionCandidate? Candidate, string Detail);
/// <summary>Version must come from trusted shell release selection, not a downloaded document.</summary>
public sealed record ReleaseSource(string Version)
{
    public const string Repository = "https://github.com/bti0497-gif/PDF-Workspace/releases/download/";
    public string AssetPrefix => Repository + "wpf-v" + Version + "/";
    public Uri ManifestUri => new(AssetPrefix + "wpf-modules.json");
    public Uri ManifestSignatureUri => new(AssetPrefix + "wpf-modules.json.sig");
}
public sealed class DistributionException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Stages inert bytes only. Never loads code, launches processes, or overwrites live DLLs.</summary>
public sealed partial class ModuleDistributionService
{
    public const string Product = "PDF-Workspace-WPF";
    public const string ShellVersion = "1.0.1";
    public const int SupportedApi = 1;
    private const long MaxAsset = 256L * 1024 * 1024, MaxTotal = 1024L * 1024 * 1024;
    private const int MaxManifest = 128 * 1024;
    private readonly HttpClient http;
    private readonly string root;
    private readonly ManifestSignaturePolicy manifestSignaturePolicy;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record State(string? Active, string? Previous, string HighestVersion, string HighestDirectory);
    public ModuleDistributionService(HttpClient httpClient, string filesystemRoot, IReadOnlyDictionary<string, string>? trustedResourceHashes = null, Func<string, CancellationToken, Task<bool>>? verifyEngineSignature = null, ManifestSignaturePolicy? manifestSignaturePolicy = null)
    {
        http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.manifestSignaturePolicy = manifestSignaturePolicy ?? throw new DistributionException("Signed module distribution requires a product-embedded manifest verification key.");
        this.trustedResourceHashes = trustedResourceHashes is null ? new Dictionary<string, string>() : trustedResourceHashes.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        this.verifyEngineSignature = verifyEngineSignature;
        root = Path.GetFullPath(filesystemRoot);
        Guard(root); Directory.CreateDirectory(root); Guard(root);
    }
    public static HttpClient CreateDownloadClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false
    }) { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<DistributionResult> PrepareAsync(ReleaseSource source,
        IProgress<DistributionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ParseVersion(source.Version);
        using var gate = Lock();
        var state = ReadState();
        DistributionCandidate? active = state?.Active is { } name ? await ReadCandidate(name, cancellationToken) : null;
        DistributionManifest manifest;
        byte[] exactManifestBytes;
        byte[] manifestSignature;
        var plannedModules = new Dictionary<string, ModuleAsset>(StringComparer.Ordinal);
        int verifiedModules = 0;
        Report("Checking", null, 0, 0, "Checking the selected WPF release manifest.");
        try
        {
            exactManifestBytes = await Download(source.ManifestUri, MaxManifest, cancellationToken);
            try { manifestSignature = await Download(source.ManifestSignatureUri, ManifestSignaturePolicy.SignatureBytes, cancellationToken); }
            catch (HttpRequestException ex) when (ex.StatusCode is not null)
            { throw new DistributionException("Required signed manifest asset is missing or unavailable; unsigned releases are forbidden.", ex); }
            manifestSignaturePolicy.Verify(exactManifestBytes, manifestSignature);
            manifest = ParseManifest(exactManifestBytes); Validate(manifest, source.Version);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            if (active is not null)
            {
                foreach (var cachedModule in active.Manifest.Modules)
                    Report("Offline", cachedModule.Id, 0, 0, "Using installed module " + cachedModule.Version + "; online planned version is unknown.");
                Report("Offline", null, 0, 0, "Network unavailable; using reverified active cache.");
                return new("Offline", active, "Network unavailable: " + ex.Message);
            }
            throw new DistributionException("First-run modules unavailable. Connect to the trusted release source and retry.", ex);
        }
        if (state is not null && ParseVersion(manifest.Version) < ParseVersion(state.HighestVersion))
            throw new DistributionException("Release downgrade rejected.");
        var baseline = state is null ? active : await ReadCandidate(state.HighestDirectory, cancellationToken);
        if (baseline is not null && manifest.Version == baseline.Manifest.Version && !Equivalent(manifest, baseline.Manifest))
            throw new DistributionException("Previously activated release was mutated.");
        if (baseline is not null)
        {
            foreach (var old in baseline.Manifest.Modules)
            {
                var next = manifest.Modules.SingleOrDefault(m => m.Id == old.Id);
                if (next is null) throw new DistributionException("Removing modules requires an approved migration.");
                if (ParseVersion(next.Version) < ParseVersion(old.Version)) throw new DistributionException("Module downgrade rejected.");
                if (next.Version == old.Version && (!SamePayload(next, old) || !SameResources(next, old))) throw new DistributionException("Module version reused with different bytes.");
            }
        }
        bool noChange = active is not null && manifest.Version == active.Manifest.Version;
        if (noChange && !Equivalent(manifest, active!.Manifest)) throw new DistributionException("Release version reused with different manifest.");
        plannedModules = manifest.Modules.ToDictionary(m => m.Id, StringComparer.Ordinal);
        long total = manifest.Modules.Sum(m => m.Size + (m.Files?.Sum(f => f.Size) ?? 0)), done = 0;
        // Publish every validated module plan before creating stage files or fetching any module bytes.
        foreach (var module in manifest.Modules)
        {
            var installedModule = active?.Manifest.Modules.SingleOrDefault(m => m.Id == module.Id);
            string kind = Change(module, installedModule);
            string detail = kind == "Install" ? "Install module " + module.Version + "; no installed version."
                : kind == "Reuse" ? "Retain module " + installedModule!.Version + "; unchanged verified payload."
                : "Update module " + installedModule!.Version + " to " + module.Version + "; download changed payloads.";
            Report("Plan", module.Id, 0, total, detail);
        }
        if (noChange)
        {
            verifiedModules = manifest.Modules.Length;
            foreach (var module in manifest.Modules)
                Report("NoChange", module.Id, 0, 0, "Installed module " + module.Version + " reverified; no download required.");
            Report("NoChange", null, 0, 0, "Installed modules reverified; no downloads required.");
            return new("NoChange", active, "Already current; no module downloads.");
        }
        var directoryName = "v" + manifest.Version + "-" + Guid.NewGuid().ToString("N");
        var stagingName = "stage-" + Guid.NewGuid().ToString("N");
        var staging = Safe(stagingName); Directory.CreateDirectory(staging);
        foreach (var module in manifest.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Safe(stagingName, AssemblyName(module) + ".dll");
            var previous = active?.Manifest.Modules.SingleOrDefault(m => m.Id == module.Id && SamePayload(m, module));
            if (previous is not null)
            {
                Report("Reusing", module.Id, done, total, "Copying verified unchanged DLL.");
                File.Copy(Safe(active!.DirectoryName, AssemblyName(module) + ".dll"), target, false);
            }
            else
            {
                Report("Downloading", module.Id, done, total, "Downloading managed module " + module.Version + ".");
                await DownloadTo(new Uri(module.Url), target, module.Size,
                    n => Report("Downloading", module.Id, done + n, total, module.Id), cancellationToken);
            }
            await VerifyFile(target, module, cancellationToken);
            done += module.Size;
            foreach (var resource in module.Files ?? [])
            {
                string resourceTarget = Safe(stagingName, module.Id + "-" + resource.Name);
                var oldResource = active?.Manifest.Modules.SingleOrDefault(m => m.Id == module.Id)?.Files?.SingleOrDefault(f => f.Name == resource.Name && f.Sha256 == resource.Sha256 && f.Size == resource.Size);
                if (oldResource is not null)
                {
                    Report("Reusing", module.Id, done, total, "Copying unchanged resource " + resource.Name + ".", resource.Name);
                    File.Copy(Safe(active!.DirectoryName, module.Id + "-" + resource.Name), resourceTarget, false);
                }
                else
                {
                    Report("Downloading", module.Id, done, total, "Downloading resource " + resource.Name + ".", resource.Name);
                    await DownloadTo(new Uri(resource.Url), resourceTarget, resource.Size,
                        n => Report("Downloading", module.Id, done + n, total, "Downloading resource " + resource.Name + ".", resource.Name), cancellationToken);
                }
                await VerifyResource(resourceTarget, resource, cancellationToken);
                done += resource.Size;
            }
            verifiedModules++;
            Report("Verified", module.Id, done, total, "Signed manifest, size, SHA-256 and managed identity verified; runtime loading is still pending.");
        }
        // Preserve exact signed bytes; serialization or whitespace normalization would invalidate authenticity.
        await WriteNew(Safe(stagingName, "manifest.json"), exactManifestBytes, cancellationToken);
        await WriteNew(Safe(stagingName, "manifest.json.sig"), manifestSignature, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested(); Guard(staging);
        var final = Safe(directoryName); Directory.Move(staging, final);
        var candidate = new DistributionCandidate(directoryName, manifest, final);
        Report("Ready", null, done, total, "Staged only. Activation requires safe module lifecycle handling.");
        return new("Ready", candidate, "Verified candidate staged; active state unchanged.");
        static string Change(ModuleAsset planned, ModuleAsset? installed) => installed is null ? "Install"
            : SamePayload(planned, installed) && SameResources(planned, installed) ? "Reuse" : "Update";
        void Report(string stage, string? id, long bytes, long count, string detail, string? assetName = null)
        {
            var planned = id is not null && plannedModules.TryGetValue(id, out var entry) ? entry : null;
            var installed = id is null ? null : active?.Manifest.Modules.SingleOrDefault(m => m.Id == id);
            var knownModule = planned ?? installed;
            progress?.Report(new(stage, id, bytes, count, detail, planned?.Version, installed?.Version,
                assetName ?? (knownModule is null ? null : ModuleFileName(knownModule)),
                knownModule?.Region ?? knownModule?.Id,
                planned is null ? installed is null ? null : "Cached" : Change(planned, installed),
                plannedModules.Count > 0 ? plannedModules.Count : stage == "Offline" ? active?.Manifest.Modules.Length : null,
                plannedModules.Count > 0 ? verifiedModules : stage == "Offline" ? active?.Manifest.Modules.Length : null));
        }
    }
    public async Task<DistributionCandidate?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        using var gate = Lock();
        return ReadState()?.Active is { } name ? await ReadCandidate(name, cancellationToken) : null;
    }
    public async Task ActivateAsync(DistributionCandidate candidate, CancellationToken cancellationToken = default)
    {
        using var gate = Lock();
        var verified = await ReadCandidate(candidate.DirectoryName, cancellationToken);
        if (!Equivalent(verified.Manifest, candidate.Manifest) || verified.DirectoryPath != candidate.DirectoryPath)
            throw new DistributionException("Candidate identity mismatch.");
        var state = ReadState();
        if (state is not null && ParseVersion(verified.Manifest.Version) < ParseVersion(state.HighestVersion))
            throw new DistributionException("Activation downgrade rejected; use explicit rollback.");
        if (state?.Active == candidate.DirectoryName) return;
        if (state is not null)
        {
            var old = await ReadCandidate(state.HighestDirectory, cancellationToken);
            if (old.Manifest.Version == verified.Manifest.Version && !Equivalent(old.Manifest, verified.Manifest))
                throw new DistributionException("Conflicting same-version activation rejected.");
            foreach (var asset in old.Manifest.Modules)
            {
                var next = verified.Manifest.Modules.SingleOrDefault(m => m.Id == asset.Id);
                if (next is null || ParseVersion(next.Version) < ParseVersion(asset.Version) || (next.Version == asset.Version && (!SamePayload(asset, next) || !SameResources(asset, next))))
                    throw new DistributionException("Stale candidate module downgrade or mutation rejected.");
            }
        }
        await WriteState(new(candidate.DirectoryName, state?.Active, verified.Manifest.Version, candidate.DirectoryName), cancellationToken);
    }
    public async Task<DistributionCandidate> RollbackAsync(CancellationToken cancellationToken = default)
    {
        using var gate = Lock();
        var state = ReadState() ?? throw new DistributionException("No active state.");
        var previous = state.Previous ?? throw new DistributionException("No rollback candidate.");
        var verified = await ReadCandidate(previous, cancellationToken);
        await WriteState(new(previous, state.Active, state.HighestVersion, state.HighestDirectory), cancellationToken);
        return verified;
    }
    /// <summary>Compensates a failed runtime switch, including first-install restoration to no active modules.
    /// Compare-and-restore only: never toggles or replaces a different concurrent activation.</summary>
    public async Task RestoreActiveAsync(DistributionCandidate expectedCurrentCandidate,
        DistributionCandidate? previousCandidate, CancellationToken cancellationToken = default)
    {
        using var gate = Lock();
        var state = ReadState();
        var previousName = previousCandidate?.DirectoryName;
        // Activation may have failed before its atomic commit. The caller's previous state is already intact.
        if (state?.Active == previousName)
        {
            if (previousCandidate is not null)
            {
                var intact = await ReadCandidate(previousName!, cancellationToken);
                if (!Equivalent(intact.Manifest, previousCandidate.Manifest) || intact.DirectoryPath != previousCandidate.DirectoryPath)
                    throw new DistributionException("Previous candidate identity mismatch.");
            }
            return;
        }
        if (state is null || state.Active != expectedCurrentCandidate.DirectoryName || state.Previous != previousName)
            throw new DistributionException("Active state changed; refusing stale activation compensation.");
        // The failed candidate may be corrupt, which must not block restoration of a verified predecessor.
        // Its generated directory identity is compared, not loaded or trusted as executable bytes here.
        if (previousCandidate is not null)
        {
            var verified = await ReadCandidate(previousName!, cancellationToken);
            if (!Equivalent(verified.Manifest, previousCandidate.Manifest) || verified.DirectoryPath != previousCandidate.DirectoryPath)
                throw new DistributionException("Rollback candidate identity mismatch.");
        }
        await WriteState(new(previousName, null, state.HighestVersion, state.HighestDirectory), cancellationToken);
    }
    private async Task<DistributionCandidate> ReadCandidate(string name, CancellationToken ct)
    {
        if (!Regex.IsMatch(name, @"^v[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{32}$")) throw new DistributionException("Invalid cache directory identity.");
        var exactBytes = await ReadBounded(Safe(name, "manifest.json"), MaxManifest, ct);
        byte[] signature;
        try { signature = await ReadBounded(Safe(name, "manifest.json.sig"), ManifestSignaturePolicy.SignatureBytes, ct); }
        catch (FileNotFoundException ex) { throw new DistributionException("Cached module manifest has no mandatory signature.", ex); }
        manifestSignaturePolicy.Verify(exactBytes, signature);
        var manifest = ParseManifest(exactBytes);
        Validate(manifest, manifest.Version);
        if (!name.StartsWith("v" + manifest.Version + "-", StringComparison.Ordinal)) throw new DistributionException("Cache version mismatch.");
        foreach (var module in manifest.Modules)
        {
            await VerifyFile(Safe(name, AssemblyName(module) + ".dll"), module, ct);
            foreach (var resource in module.Files ?? []) await VerifyResource(Safe(name, module.Id + "-" + resource.Name), resource, ct);
        }
        return new(name, manifest, Safe(name));
    }
    private State? ReadState()
    {
        var p = Safe("activation.json"); if (!File.Exists(p)) return null;
        if (new FileInfo(p).Length > 8192) throw new DistributionException("Oversized activation state.");
        var state = JsonSerializer.Deserialize<State>(File.ReadAllBytes(p), Json) ?? throw new DistributionException("Invalid activation state.");
        ParseVersion(state.HighestVersion); return state;
    }
    private async Task WriteState(State state, CancellationToken ct)
    {
        var temp = Safe("activation-" + Guid.NewGuid().ToString("N") + ".tmp");
        await WriteNew(temp, JsonSerializer.SerializeToUtf8Bytes(state, Json), ct);
        ct.ThrowIfCancellationRequested(); var destination = Safe("activation.json");
        if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
    }
    private FileStream Lock()
    {
        try { return new FileStream(Safe("distribution.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new DistributionException("Distribution busy, or cache unavailable.", ex); }
    }
    private string Safe(params string[] parts)
    {
        foreach (var part in parts)
            if (string.IsNullOrEmpty(part) || part is "." or ".." || part.IndexOfAny(['/', '\\', ':']) >= 0)
                throw new DistributionException("Invalid cache path component.");
        var full = Path.Combine(new[] { root }.Concat(parts).ToArray()); Guard(full); return full;
    }
    private static void Guard(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
        {
            try { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new DistributionException("Symlink/junction cache paths forbidden."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
    }
    private static Version ParseVersion(string value)
    {
        if (value is null || !Regex.IsMatch(value, @"^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$"))
            throw new DistributionException("Canonical three-part version required.");
        var version = Version.Parse(value);
        if (version.Major > 65534 || version.Minor > 65534 || version.Build > 65534) throw new DistributionException("Version exceeds assembly range.");
        return version;
    }
    private void Validate(DistributionManifest m, string expectedVersion)
    {
        if (m.Product != Product || m.Api != SupportedApi || m.Version != expectedVersion || ParseVersion(m.Version) < ParseVersion(ShellVersion))
            throw new DistributionException("Incompatible product, release version, or contract API.");
        if (m.Modules is null || m.Modules.Length is < 1 or > 64) throw new DistributionException("Manifest must contain 1..64 modules.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var a in m.Modules)
        {
            if (a is null || a.Id is null || !Regex.IsMatch(a.Id, @"^[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*$") || a.Id.Length > 100 || !ids.Add(a.Id) ||
                Regex.IsMatch(a.Id, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                throw new DistributionException("Invalid, reserved, or duplicate module ID.");
            ParseVersion(a.Version);
            if (a.Size is <= 0 or > MaxAsset || (total += a.Size) > MaxTotal) throw new DistributionException("Package size limit exceeded.");
            if (a.Sha256 is null || !Regex.IsMatch(a.Sha256, "^[a-fA-F0-9]{64}$")) throw new DistributionException("Invalid SHA-256.");
            ValidateExtras(a, m.Version, ref total);
            if (!assemblyNames.Add(AssemblyName(a))) throw new DistributionException("Duplicate assembly filename.");
            var expected = new ReleaseSource(m.Version).AssetPrefix + AssemblyName(a) + "-" + a.Version + ".dll";
            if (a.Url != expected) throw new DistributionException("Module URL outside exact WPF release asset allowlist.");
        }
    }
    private static DistributionManifest ParseManifest(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 }); Unique(doc.RootElement);
            return JsonSerializer.Deserialize<DistributionManifest>(bytes, Json) ?? throw new DistributionException("Empty manifest.");
        }
        catch (JsonException ex) { throw new DistributionException("Invalid manifest JSON.", ex); }
        static void Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new DistributionException("Duplicate JSON property."); Unique(p.Value); }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var e in element.EnumerateArray()) Unique(e);
        }
    }
    private static bool SamePayload(ModuleAsset a, ModuleAsset b) => a.Id == b.Id && a.Version == b.Version && a.Size == b.Size && string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase) && a.AssemblyName == b.AssemblyName && a.Region == b.Region && a.Type == b.Type;
    private static bool SameResources(ModuleAsset a, ModuleAsset b) => (a.Files?.Length ?? 0) == (b.Files?.Length ?? 0) && (a.Files ?? []).All(x => (b.Files ?? []).Any(y => x.Name == y.Name && x.Kind == y.Kind && x.Size == y.Size && x.Sha256.Equals(y.Sha256, StringComparison.OrdinalIgnoreCase)));
    private static bool Equivalent(DistributionManifest a, DistributionManifest b) => a.Product == b.Product && a.Version == b.Version && a.Api == b.Api && a.Modules.Length == b.Modules.Length && a.Modules.All(x => b.Modules.Any(y => SamePayload(x, y) && x.Url == y.Url && JsonSerializer.Serialize(x.Files, Json) == JsonSerializer.Serialize(y.Files, Json)));
    private async Task<HttpResponseMessage> Get(Uri uri, CancellationToken ct)
    {
        for (int redirects = 0; ; redirects++)
        {
            var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            var actual = response.RequestMessage?.RequestUri;
            if (actual is not null && actual != uri) { response.Dispose(); throw new DistributionException("Injected client must disable automatic redirects."); }
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null || redirects >= 3) { response.Dispose(); throw new DistributionException("Invalid release redirect."); }
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location); response.Dispose();
                if (next.Scheme != "https" || next.Host != "release-assets.githubusercontent.com" || !next.IsDefaultPort || next.UserInfo.Length != 0 || next.Fragment.Length != 0)
                    throw new DistributionException("Untrusted release redirect.");
                uri = next; continue;
            }
            if (!response.IsSuccessStatusCode) { var code = response.StatusCode; response.Dispose(); throw new HttpRequestException("Release request failed: " + code, null, code); }
            return response;
        }
    }
    private async Task<byte[]> Download(Uri uri, long limit, CancellationToken ct)
    {
        using var response = await Get(uri, ct); using var target = new MemoryStream();
        await Copy(response, target, limit, null, ct); return target.ToArray();
    }
    private async Task DownloadTo(Uri uri, string path, long size, Action<long> progress, CancellationToken ct)
    {
        using var response = await Get(uri, ct); Guard(path);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await Copy(response, target, size, progress, ct); await target.FlushAsync(ct); target.Flush(true);
    }
    private static async Task Copy(HttpResponseMessage response, Stream target, long limit, Action<long>? progress, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } length && length > limit) throw new DistributionException("Response exceeds declared limit.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[65536]; long total = 0;
        while (true)
        {
            int n = await input.ReadAsync(buffer.AsMemory(), ct); if (n == 0) break;
            total += n; if (total > limit) throw new DistributionException("Response exceeded streaming limit.");
            await target.WriteAsync(buffer.AsMemory(0, n), ct); progress?.Invoke(total);
        }
    }
    private static async Task VerifyFile(string path, ModuleAsset asset, CancellationToken ct)
    {
        Guard(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length != asset.Size) throw new DistributionException("Module size mismatch: " + asset.Id);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)) throw new DistributionException("Module SHA-256 mismatch: " + asset.Id);
        stream.Position = 0;
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata || !pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll)) throw new DistributionException("Expected managed DLL: " + asset.Id);
            var reader = pe.GetMetadataReader(); var assembly = reader.GetAssemblyDefinition(); var version = ParseVersion(asset.Version);
            if (reader.GetString(assembly.Name) != AssemblyName(asset) || assembly.Version.Major != version.Major || assembly.Version.Minor != version.Minor || assembly.Version.Build != version.Build || assembly.Version.Revision != 0)
                throw new DistributionException("Assembly identity/version mismatch: " + asset.Id);
        }
        catch (BadImageFormatException ex) { throw new DistributionException("Invalid managed DLL: " + asset.Id, ex); }
    }
    private static async Task<byte[]> ReadBounded(string path, int max, CancellationToken ct)
    {
        Guard(path); await using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (s.Length > max) throw new DistributionException("Cache manifest exceeds limit.");
        using var memory = new MemoryStream(); var b = new byte[8192]; int n;
        while ((n = await s.ReadAsync(b, ct)) != 0) { if (memory.Length + n > max) throw new DistributionException("Cache manifest exceeds limit."); await memory.WriteAsync(b.AsMemory(0, n), ct); }
        return memory.ToArray();
    }
    private static async Task WriteNew(string path, byte[] bytes, CancellationToken ct)
    {
        Guard(path); await using var s = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await s.WriteAsync(bytes, ct); await s.FlushAsync(ct); s.Flush(true);
    }
}
