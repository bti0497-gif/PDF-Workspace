using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Workspace.Shell.Updates;

// Repository metadata is untrusted data. SHA-256 detects corruption, NOT publisher authenticity.
internal sealed record UpdateOffer(Version Version, string Tag, string Asset, long Size, string Sha256);
internal sealed record DownloadedUpdate(UpdateOffer Offer, string Path);
internal sealed class WpfUpdateClient : IDisposable
{
    internal const string Repository = "bti0497-gif/PDF-Workspace";
    internal const string Channel = "wpf-preview";
    internal const long MaximumInstallerBytes = 512L * 1024 * 1024;
    internal const string ReleasesUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=100";
    private readonly HttpClient http;
    internal WpfUpdateClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PDF-Workspace-WPF-Updater/1.0");
    }
    internal static Version CurrentVersion
    {
        get { var v = typeof(WpfUpdateClient).Assembly.GetName().Version!; return new(v.Major, v.Minor, v.Build); }
    }
    internal static Version ParseVersion(string text)
    {
        if (!Regex.IsMatch(text, @"^(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})$")) throw new InvalidDataException("Invalid WPF version.");
        return Version.Parse(text);
    }
    internal static string AssetName(Version version) => $"PDF-Workspace-WPF-{version}-win-x64-setup.exe";
    internal static Uri AssetUri(string tag, string asset) => new($"https://github.com/{Repository}/releases/download/{tag}/{asset}");
    internal static void CheckUri(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
            throw new InvalidDataException("Unapproved update destination.");
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var i = 0; i < 5; i++)
        {
            CheckUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var next = response.Headers.Location; response.Dispose();
                if (next == null) throw new InvalidDataException("Missing redirect target.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("Too many update redirects.");
    }
    private async Task<byte[]> ReadBoundedAsync(Uri uri, int maximum, CancellationToken token)
    {
        using var response = await GetAsync(uri, token);
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Update metadata is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int n;
        while ((n = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length + n > maximum) throw new InvalidDataException("Update metadata is too large."); await output.WriteAsync(buffer.AsMemory(0,n), token); }
        return output.ToArray();
    }
    internal async Task<UpdateOffer?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        using var releases = JsonDocument.Parse(await ReadBoundedAsync(new Uri(ReleasesUrl), 2 * 1024 * 1024, token), new JsonDocumentOptions { MaxDepth = 24 });
        if (releases.RootElement.ValueKind != JsonValueKind.Array || releases.RootElement.GetArrayLength() > 100) throw new InvalidDataException("Invalid release list.");
        Version? best = null; JsonElement selected = default;
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || !release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith("wpf-v", StringComparison.Ordinal)) continue;
            Version version; try { version = ParseVersion(tag[5..]); } catch (InvalidDataException) { continue; }
            if (version > current && (best == null || version > best)) { best = version; selected = release; }
        }
        if (best == null) return null;
        var expectedTag = "wpf-v" + best; var assetName = AssetName(best);
        var assets = selected.GetProperty("assets").EnumerateArray().ToArray();
        foreach (var name in new[] { "wpf-update.json", assetName })
        {
            var matching = assets.Where(x => x.GetProperty("name").GetString() == name).ToArray();
            if (matching.Length != 1 || matching[0].GetProperty("browser_download_url").GetString() != AssetUri(expectedTag, name).AbsoluteUri)
                throw new InvalidDataException("Missing or unexpected WPF release asset.");
        }
        var offer = ParseManifest(await ReadBoundedAsync(AssetUri(expectedTag, "wpf-update.json"), 16384, token), best);
        if (assets.Single(x => x.GetProperty("name").GetString() == assetName).GetProperty("size").GetInt64() != offer.Size)
            throw new InvalidDataException("Installer size differs from manifest.");
        return offer;
    }
    internal static UpdateOffer ParseManifest(byte[] bytes, Version expected)
    {
        if (bytes.Length > 16384) throw new InvalidDataException("Manifest too large.");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 }); var root = json.RootElement;
        string[] keys = ["schemaVersion", "channel", "repository", "tag", "version", "asset", "size", "sha256"];
        var fields = root.EnumerateObject().Select(x => x.Name).ToArray();
        if (fields.Length != keys.Length || !fields.Order().SequenceEqual(keys.Order())) throw new InvalidDataException("Unknown, duplicate or missing manifest fields.");
        var version = ParseVersion(root.GetProperty("version").GetString() ?? "");
        var tag = root.GetProperty("tag").GetString(); var asset = root.GetProperty("asset").GetString();
        var size = root.GetProperty("size").GetInt64(); var hash = root.GetProperty("sha256").GetString() ?? "";
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("channel").GetString() != Channel ||
            root.GetProperty("repository").GetString() != Repository || version != expected || tag != "wpf-v" + version ||
            asset != AssetName(version) || size <= 0 || size > MaximumInstallerBytes || !Regex.IsMatch(hash, "^[A-Fa-f0-9]{64}$"))
            throw new InvalidDataException("Invalid WPF update manifest.");
        return new(version, tag, asset, size, hash);
    }
    internal async Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer, string cacheRoot, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(10)); var token = timeout.Token;
        // Random directory + CreateNew, never use a server-supplied path or overwrite an existing payload.
        Directory.CreateDirectory(cacheRoot);
        if ((File.GetAttributes(cacheRoot) & FileAttributes.ReparsePoint) != 0) throw new IOException("Update cache cannot be a link.");
        var directory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "setup.exe");
        try
        {
            using var response = await GetAsync(AssetUri(offer.Tag, offer.Asset), token);
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != offer.Size) throw new InvalidDataException("Installer length mismatch.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; long total = 0; int n;
                while ((n = await input.ReadAsync(buffer, token)) > 0)
                { total += n; if (total > offer.Size || total > MaximumInstallerBytes) throw new InvalidDataException("Installer exceeds size limit."); await output.WriteAsync(buffer.AsMemory(0,n), token); }
                if (total != offer.Size) throw new InvalidDataException("Incomplete installer.");
            }
            var download = new DownloadedUpdate(offer, target); await VerifyAsync(download, token); return download;
        }
        catch { try { Directory.Delete(directory, true); } catch (IOException) { } throw; }
    }
    internal static async Task VerifyAsync(DownloadedUpdate download, CancellationToken token)
    {
        await using var file = new FileStream(download.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (file.Length != download.Offer.Size || file.Length > MaximumInstallerBytes) throw new InvalidDataException("Cached installer length mismatch.");
        var hash = await SHA256.HashDataAsync(file, token);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(download.Offer.Sha256))) throw new InvalidDataException("Installer SHA-256 mismatch.");
    }
    public void Dispose() => http.Dispose();
}
