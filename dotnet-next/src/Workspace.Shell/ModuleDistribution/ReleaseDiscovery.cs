using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace.Shell.ModuleDistribution;

public sealed record ReleaseDiscoveryResult(string Version, bool IsOfflineFallback, string Detail, int PagesRead);

/// <summary>
/// Discovers only this product's published stable WPF releases. HTTPS repository metadata is not
/// signed metadata or proof of release immutability. ModulePreparation must still verify the manifest
/// and payloads. Inject only a trusted client with automatic redirects disabled.
/// </summary>
public sealed class ReleaseDiscovery
{
    public const string Endpoint = "https://api.github.com/repos/bti0497-gif/PDF-Workspace/releases";
    public const int PageSize = 100;
    public const int MaximumPages = 10;
    public const int MaximumPageBytes = 1024 * 1024;
    public const int MaximumTotalBytes = 5 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly TimeSpan timeout;

    public ReleaseDiscovery(HttpClient client, TimeSpan? timeout = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (this.timeout < TimeSpan.FromMilliseconds(10) || this.timeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Discovery timeout must be 10ms..60s.");
    }

    public static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false
    }) { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// The installed version is the host's persisted product version, not an untrusted URL or tag.
    /// On transport/HTTP failure only, cache.GetActiveAsync rechecks actual files before permitting
    /// fallback. No cache, cancellation, invalid metadata, bounds violation, or no matching release
    /// ever silently falls back to 1.0.1. A rollback cache below installedProductVersion is rejected.
    /// </summary>
    public async Task<ReleaseDiscoveryResult> DiscoverAsync(string installedProductVersion,
        ModuleDistributionService? cache = null, CancellationToken cancellationToken = default)
    {
        var installed = ParseVersion(installedProductVersion)
            ?? throw new DistributionException("Installed product version is not canonical x.y.z.");
        var floor = new Version(1, 0, 1);
        if (installed < floor) throw new DistributionException("WPF discovery supports installed product 1.0.1 or newer.");
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        int pagesRead = 0;
        try
        {
            Version? selected = null;
            int totalBytes = 0;
            Uri? next = PageUri(1);
            while (next is not null)
            {
                if (pagesRead >= MaximumPages) throw new DistributionException("Release discovery pagination limit exceeded; no partial selection accepted.");
                int pageNumber = pagesRead + 1;
                using var request = new HttpRequestMessage(HttpMethod.Get, next);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.UserAgent.ParseAdd("PDF-Workspace-WPF/1.0.1");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.RequestMessage?.RequestUri is { } actual && actual != next)
                    throw new DistributionException("Discovery client followed a redirect; configure AllowAutoRedirect=false.");
                if ((int)response.StatusCode is >= 300 and < 400)
                    throw new DistributionException("Release discovery redirects are forbidden; repository endpoint must remain exact.");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("GitHub WPF release discovery failed: " + response.StatusCode, null, response.StatusCode);
                if (response.Content.Headers.ContentEncoding.Count != 0)
                    throw new DistributionException("Encoded release metadata is not accepted.");
                var bytes = await ReadBounded(response.Content, deadline.Token);
                totalBytes += bytes.Length;
                if (totalBytes > MaximumTotalBytes) throw new DistributionException("Release discovery total byte limit exceeded.");
                using var document = ParseDocument(bytes);
                var array = document.RootElement;
                if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > PageSize)
                    throw new DistributionException("Expected bounded GitHub releases array.");
                foreach (var release in array.EnumerateArray())
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (release.ValueKind != JsonValueKind.Object) throw new DistributionException("Invalid release entry.");
                    RejectDuplicateProperties(release);
                    if (!release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
                        !release.TryGetProperty("draft", out var draft) || draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                        !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new DistributionException("Release identity/status fields missing or malformed.");
                    if (draft.GetBoolean() || prerelease.GetBoolean()) continue;
                    if (!release.TryGetProperty("published_at", out var published) || published.ValueKind != JsonValueKind.String ||
                        !DateTimeOffset.TryParseExact(published.GetString(), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _)) continue;
                    string text = tag.GetString()!;
                    if (!text.StartsWith("wpf-v", StringComparison.Ordinal)) continue;
                    var version = ParseVersion(text[5..]);
                    if (version is null || version < installed || version < floor) continue;
                    if (selected is null || version > selected) selected = version;
                }
                pagesRead++;
                next = ReadNext(response, pageNumber);
            }
            if (selected is null) throw new DistributionException("No published stable WPF release at or above installed version was found; Electron tags and draft/prerelease tags are ignored.");
            return new(selected.ToString(3), false, "Selected highest numeric published WPF release across all bounded pages; manifest/payload verification still required.", pagesRead);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = cache is null ? null : await cache.GetActiveAsync(cancellationToken);
            var activeVersion = active is null ? null : ParseVersion(active.Manifest.Version);
            if (activeVersion is null || activeVersion < installed)
                throw new DistributionException("WPF release discovery unavailable and no reverified active cache at the installed version or newer exists. First-run installation requires a working official endpoint.", ex);
            return new(active!.Manifest.Version, true,
                "Release discovery unavailable; retained fully reverified active cache, not a newly authenticated release. " + ex.Message, pagesRead);
        }
        catch (JsonException ex) { throw new DistributionException("Malformed GitHub release metadata.", ex); }
    }

    private static Uri PageUri(int page) => new(Endpoint + "?per_page=" + PageSize + "&page=" + page.ToString(CultureInfo.InvariantCulture));
    private static Version? ParseVersion(string? value)
    {
        if (value is null || !Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z")) return null;
        if (!Version.TryParse(value, out var version) || version.Major > 65534 || version.Minor > 65534 || version.Build > 65534) return null;
        return version;
    }
    private static JsonDocument ParseDocument(byte[] bytes) => JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    private static void RejectDuplicateProperties(JsonElement release)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in release.EnumerateObject())
            if (!seen.Add(property.Name)) throw new DistributionException("Duplicate release metadata property.");
    }
    private static Uri? ReadNext(HttpResponseMessage response, int currentPage)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        string header = string.Join(",", values);
        if (header.Length > 8192) throw new DistributionException("Pagination header exceeds limit.");
        Uri? next = null;
        var relations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in header.Split(','))
        {
            var match = Regex.Match(item.Trim(), "\\A<([^>]+)>;\\s*rel=\"(next|prev|first|last)\"\\z");
            if (!match.Success || !relations.Add(match.Groups[2].Value)) throw new DistributionException("Invalid or duplicate pagination relation.");
            string raw = match.Groups[1].Value;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "api.github.com" ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/repos/bti0497-gif/PDF-Workspace/releases")
                throw new DistributionException("Pagination URL outside the exact official repository.");
            var query = Regex.Match(uri.Query, @"\A\?(?:per_page=100&page=([1-9][0-9]{0,5})|page=([1-9][0-9]{0,5})&per_page=100)\z");
            if (!query.Success) throw new DistributionException("Unsupported pagination cursor.");
            int number = int.Parse(query.Groups[1].Success ? query.Groups[1].Value : query.Groups[2].Value, CultureInfo.InvariantCulture);
            // Only these canonical encodings are accepted; no userinfo, encoded path, alternate port or hidden parameter.
            if (raw != Endpoint + "?per_page=100&page=" + number && raw != Endpoint + "?page=" + number + "&per_page=100")
                throw new DistributionException("Noncanonical pagination URL.");
            if (number > MaximumPages) throw new DistributionException("Repository pagination exceeds discovery limit; no partial result accepted.");
            if (match.Groups[2].Value == "next")
            {
                if (number != currentPage + 1) throw new DistributionException("Nonsequential or looping pagination cursor.");
                next = PageUri(number);
            }
        }
        return next;
    }
    private static async Task<byte[]> ReadBounded(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } size && size > MaximumPageBytes) throw new DistributionException("Release page exceeds byte limit.");
        await using var source = await content.ReadAsStreamAsync(ct);
        using var target = new MemoryStream(); var buffer = new byte[16384];
        while (true)
        {
            int n = await source.ReadAsync(buffer.AsMemory(), ct); if (n == 0) break;
            if (target.Length + n > MaximumPageBytes) throw new DistributionException("Release page exceeds streamed byte limit.");
            await target.WriteAsync(buffer.AsMemory(0, n), ct);
        }
        return target.ToArray();
    }
}
