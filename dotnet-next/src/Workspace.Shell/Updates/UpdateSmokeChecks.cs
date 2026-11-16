using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Workspace.Shell.Updates;
internal static class UpdateSmokeChecks
{
    internal static async Task RunAsync(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) throw new ArgumentException("Existing absolute output directory required.");
        var passed = new List<string>(); var payload = Encoding.UTF8.GetBytes("mock installer; never execute");
        var version = new Version(0,4,0); var hash = Convert.ToHexString(SHA256.HashData(payload));
        byte[] Manifest(string? asset = null, string? digest = null) => JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion=1, channel=WpfUpdateClient.Channel, repository=WpfUpdateClient.Repository, tag="wpf-v0.4.0", version="0.4.0", asset=asset??WpfUpdateClient.AssetName(version), size=payload.Length, sha256=digest??hash });
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed.Add(name); }
        async Task Reject(Func<Task> run, string name) { try { await run(); } catch { passed.Add(name); return; } throw new Exception("Accepted: "+name); }
        Check(WpfUpdateClient.ParseVersion("0.10.0") > WpfUpdateClient.ParseVersion("0.9.0"), "numeric version ordering");
        foreach(var bad in new[]{"0.4.0/../../x","01.4.0","1.2.3.4","1.0.0-beta","99999.0.0"}) await Reject(()=>Task.FromResult(WpfUpdateClient.ParseVersion(bad)), "reject version "+bad);
        await Reject(()=>Task.FromResult(WpfUpdateClient.ParseManifest(Manifest("../../evil.exe"),version)),"reject asset path traversal");
        await Reject(()=>Task.FromResult(WpfUpdateClient.ParseManifest(Manifest(digest:"bad"),version)),"reject invalid checksum");
        var duplicate=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Manifest()).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"));
        await Reject(()=>Task.FromResult(WpfUpdateClient.ParseManifest(duplicate,version)),"reject duplicate manifest keys");
        foreach(var uri in new[]{"http://github.com/x","https://evil.example/x","https://github.com.evil.example/x","https://github.com:444/x","https://user@github.com/x"}) await Reject(()=>{WpfUpdateClient.CheckUri(new(uri));return Task.CompletedTask;},"reject destination "+uri);
        object Release(string tag, bool pre, bool draft=false) => new {tag_name=tag, prerelease=pre,draft,assets=new[]{new {name="wpf-update.json", browser_download_url=WpfUpdateClient.AssetUri("wpf-v0.4.0","wpf-update.json").AbsoluteUri,size=100},new{name=WpfUpdateClient.AssetName(version),browser_download_url=WpfUpdateClient.AssetUri("wpf-v0.4.0",WpfUpdateClient.AssetName(version)).AbsoluteUri,size=payload.Length}}};
        var releases=JsonSerializer.SerializeToUtf8Bytes(new[]{Release("v99.0.0",false),Release("wpf-v0.4.0",true),Release("wpf-v9.0.0",true,true)});
        using var client=new WpfUpdateClient(new MockHandler(uri => new HttpResponseMessage(HttpStatusCode.OK) {Content=new ByteArrayContent(uri.Host=="api.github.com"?releases:uri.AbsolutePath.EndsWith(".json",StringComparison.Ordinal)?Manifest():payload)}));
        var offer=await client.CheckAsync(new Version(0,3,0),CancellationToken.None);
        Check(offer?.Version==version,"ignore Electron stable and draft; select WPF prerelease");
        Check(await client.CheckAsync(version,CancellationToken.None)==null,"no reinstall or downgrade");
        var download=await client.DownloadAsync(offer!,Path.Combine(directory,"cache"),CancellationToken.None);
        Check(File.ReadAllBytes(download.Path).SequenceEqual(payload),"bounded mock download and SHA256 verification");
        await File.WriteAllTextAsync(download.Path,"tampered"); await Reject(()=>WpfUpdateClient.VerifyAsync(download,CancellationToken.None),"reject cache tampering before execution");
        using var redirect=new WpfUpdateClient(new MockHandler(_ => {var r=new HttpResponseMessage(HttpStatusCode.Redirect);r.Headers.Location=new Uri("https://evil.example/payload");return r;}));
        await Reject(()=>redirect.CheckAsync(new Version(0,3,0),CancellationToken.None),"reject hostile redirect");
        using var truncated=new WpfUpdateClient(new MockHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([1,2])}));
        await Reject(()=>truncated.DownloadAsync(offer!,Path.Combine(directory,"partial"),CancellationToken.None),"reject incomplete download");
        Check(!Directory.EnumerateFiles(Path.Combine(directory,"partial"),"*",SearchOption.AllDirectories).Any(),"failed payload cleanup");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Reject(()=>client.CheckAsync(new Version(0,3,0),cancelled.Token),"cancellation respected");
        await File.WriteAllTextAsync(Path.Combine(directory,"update-checks.json"),JsonSerializer.Serialize(new{Passed=true,Count=passed.Count,Checks=passed,AssemblyVersion=WpfUpdateClient.CurrentVersion.ToString(),Scope="Offline mock HTTP/backend only; no network, installer launch or GUI acceptance"},new JsonSerializerOptions{WriteIndented=true}));
    }
    private sealed class MockHandler(Func<Uri,HttpResponseMessage> response):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(response(request.RequestUri!));} }
}
