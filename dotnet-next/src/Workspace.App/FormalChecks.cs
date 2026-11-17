using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Workspace.Contracts;
using Workspace.Shell.ModuleDistribution;
using Workspace.Shell.Startup;
namespace Workspace.Shell;

internal static class FormalChecks
{
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private static string Output(string output)
    {
        if(!Path.IsPathFullyQualified(output))throw new ArgumentException("Use an absolute verification output directory.");
        Directory.CreateDirectory(output);return output;
    }
    internal static async Task RunSmokeAsync(string output)
    {
        output=Output(output);
        var lifecycle=await LifecycleChecks.RunAsync();
        var setupChecks=SetupProgressChecks.Run();
        var leaseChecks=InstalledCoreLeaseChecks.Run(output);
        var backgroundChecks=await BackgroundUpdateChecks.RunAsync();
#if DEBUG
        var localUiReviewCompiled=true;
#else
        var localUiReviewCompiled=false;
        Require(typeof(FormalChecks).GetMethod("CreateLocalUiReviewPreparation",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)==null,"Local UI fixture review must be compiled out of Release.");
#endif
        var window=new MainWindow(new JsonRecentFilesStore(Path.Combine(output,"recent-files.json")));
        try
        {
            Require(window.ModularHost==null,"A blank formal shell must not construct an embedded engine.");
            Require(window.Title==ProductBrand.Name&&window.Title=="더죤 PDF Workspace","Formal window title must use exact user-selected brand.");
            Require(ProductBrand.DocumentTitle(DocumentSnapshot.Empty)==ProductBrand.Name,"Empty-document title must retain exact brand.");
            var titleDocument=new DocumentSnapshot(Guid.NewGuid(),"문서.pdf",1,1,[1],true,1);
            Require(ProductBrand.DocumentTitle(titleDocument)=="문서.pdf * · 더죤 PDF Workspace","Open-document title must preserve filename/dirty prefix and exact brand.");
            var names=Directory.GetFiles(AppContext.BaseDirectory,"*",SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Require(!names.Any(n=>n!=null&&(n.Contains("PdfEngine",StringComparison.OrdinalIgnoreCase)||n.Contains("SidebarModule",StringComparison.OrdinalIgnoreCase)||n.Contains("ViewerModule",StringComparison.OrdinalIgnoreCase)||n.Contains("RibbonModule",StringComparison.OrdinalIgnoreCase)||n.Contains("FoundationModule",StringComparison.OrdinalIgnoreCase)||n.Contains("pdfcraft",StringComparison.OrdinalIgnoreCase))),"Feature/native payload leaked into base shell output.");
            Capture(window,Path.Combine(output,"formal-shell.png"));
            var types=typeof(App).Assembly.GetTypes().Select(t=>t.FullName).ToArray();
            Require(!types.Any(t=>t!=null&&(t.Contains("PdfViewing")||t.Contains("PdfOperations")||t.Contains("PreviewWorkspace")||t.Contains("UpdateController"))),"Legacy engine/sample/updater code compiled into formal app.");
            await File.WriteAllTextAsync(Path.Combine(output,"formal-smoke.json"),JsonSerializer.Serialize(new{Passed=true,Assembly=typeof(App).Assembly.Location,Files=names,NetworkRequests=0,LifecycleChecks=lifecycle,SetupProgressChecks=setupChecks,InstalledCoreLeaseChecks=leaseChecks,BackgroundUpdateChecks=backgroundChecks,LocalUiReviewCompiled=localUiReviewCompiled,ProductTitle=window.Title,BrandChecks=3,Scope="Offscreen formal empty shell and feature-isolation audit; not installed/live UI acceptance"},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally { await window.DisposeForChecksAsync(); }
    }
#if DEBUG
    internal static Func<IProgress<DistributionProgress>,CancellationToken,Task<PreparedModuleUpdate>> CreateLocalUiReviewPreparation(string fixtureRoot,string freshRoot)
    {
        if(!Path.IsPathFullyQualified(fixtureRoot)||!Path.IsPathFullyQualified(freshRoot))throw new ArgumentException("Local UI review paths must be absolute.");
        var cache=Path.Combine(freshRoot,"module-cache");
        ModuleDistributionService Service(HttpClient client)=>new(client,cache,ModulePreparation.TrustedResourceHashes,EngineSignatureVerifier.VerifyAsync,new ManifestSignaturePolicy(ModuleSigningPublicKey.Pem));
        async Task WithService(Func<ModuleDistributionService,Task> action)
        {
            using var handler=new FixtureHandler(fixtureRoot,null);using var client=new HttpClient(handler);await action(Service(client));
        }
        return async (progress,ct)=>
        {
            using var handler=new FixtureHandler(fixtureRoot,null);using var client=new HttpClient(handler);var service=Service(client);
            var previous=await service.GetActiveAsync(ct);
            var result=await service.PrepareAsync(new("1.0.1"),progress,ct);
            var candidate=result.Candidate??throw new InvalidOperationException("No signed local candidate.");
            return new PreparedModuleUpdate(candidate,previous,
                token=>WithService(async current=>{await current.ActivateAsync(candidate,token);if((await current.GetActiveAsync(token))?.DirectoryName!=candidate.DirectoryName)throw new InvalidOperationException("Local activation mismatch.");}),
                token=>WithService(current=>current.RestoreActiveAsync(candidate,previous,token)));
        };
    }
#endif
    internal static Task RunReleaseAsync(string fixtureRoot,string pdf,string output,string officialEngineHash)=>RunReleaseCoreAsync(fixtureRoot,pdf,output,officialEngineHash,false);
    internal static Task RunReleaseTrustedAsync(string fixtureRoot,string pdf,string output)=>RunReleaseCoreAsync(fixtureRoot,pdf,output,ModulePreparation.TrustedResourceHashes["pdfcraft-cli.exe"],true);
    private static async Task RunReleaseCoreAsync(string fixtureRoot,string pdf,string output,string officialEngineHash,bool trusted)
    {
        output=Output(output);
        Require(Path.IsPathFullyQualified(fixtureRoot)&&Path.IsPathFullyQualified(pdf),"Fixture/PDF paths must be absolute.");
        Require(officialEngineHash.Length==64&&officialEngineHash.All(Uri.IsHexDigit),"Supply an independently verified native engine SHA-256.");
        using var testSigningKey=trusted?null:RSA.Create(3072);
        using var handler=new FixtureHandler(fixtureRoot,testSigningKey);
        using var client=new HttpClient(handler);
        var policy=new ManifestSignaturePolicy(trusted?ModuleSigningPublicKey.Pem:testSigningKey!.ExportSubjectPublicKeyInfoPem());
        var trustedNegativeChecks=0;
        if(trusted)
        {
            var exact=await File.ReadAllBytesAsync(Path.Combine(fixtureRoot,"wpf-modules.json"));
            var signature=await File.ReadAllBytesAsync(Path.Combine(fixtureRoot,"wpf-modules.json.sig"));
            policy.Verify(exact,signature);
            var tampered=(byte[])exact.Clone();tampered[0]^=1;
            try{policy.Verify(tampered,signature);}catch(DistributionException){trustedNegativeChecks++;}
            using var wrongKey=RSA.Create(3072);
            var wrongSignature=wrongKey.SignData(exact,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
            try{policy.Verify(exact,wrongSignature);}catch(DistributionException){trustedNegativeChecks++;}
            Require(trustedNegativeChecks==2,"Product manifest trust failed to reject tampering or wrong signer.");
        }
        var realNativeSignatureChecks=0;
        async Task<bool> NativeSignature(string path,CancellationToken ct)
        {
            if(!trusted)return true;
            var valid=await EngineSignatureVerifier.VerifyAsync(path,ct);
            if(valid)Interlocked.Increment(ref realNativeSignatureChecks);
            return valid;
        }
        var service=new ModuleDistributionService(client,Path.Combine(output,"module-cache"),new Dictionary<string,string>{{"pdfcraft-cli.exe",officialEngineHash}},NativeSignature,manifestSignaturePolicy:policy);
        // Trusted mode uses the compiled product public key and real Windows Authenticode verifier.
        // Ordinary fixture mode is explicitly test-only and does not establish production signer policy.
        var previous=await service.GetActiveAsync();
        var setupModel=new ModuleSetupProgress();
        var setupTrace=new List<ModuleSetupSnapshot>();
        var preparation=await service.PrepareAsync(new("1.0.1"),new InlineProgress<DistributionProgress>(p=>setupTrace.Add(setupModel.Apply(p))));
        var candidate=preparation.Candidate??throw new InvalidOperationException("No staged candidate.");
        Require((await service.GetActiveAsync())?.DirectoryName==previous?.DirectoryName,"Preparation activated bytes before runtime verification.");
        var update=new PreparedModuleUpdate(candidate,previous,ct=>service.ActivateAsync(candidate,ct),ct=>service.RestoreActiveAsync(candidate,previous,ct));
        // Actual DLL/backend failure paths, not only a fake activation store.
        var badAsset=candidate.Manifest.Modules.First(m=>m.Region=="ribbon");
        var badCandidate=candidate with { Manifest=candidate.Manifest with { Modules=candidate.Manifest.Modules.Select(m=>m.Id==badAsset.Id?m with { Type="Invalid.MissingEntryPoint" }:m).ToArray() } };
        var rejectedWindow=new MainWindow(new JsonRecentFilesStore(Path.Combine(output,"failure-recents.json")));
        var badCommitCalls=0;
        try
        {
            var invalid=new PreparedModuleUpdate(badCandidate,previous,async ct=>{badCommitCalls++;await service.ActivateAsync(badCandidate,ct);},ct=>service.RestoreActiveAsync(badCandidate,previous,ct));
            var failed=false;try{await rejectedWindow.ActivatePreparedModulesAsync(invalid);}catch{failed=true;}
            Require(failed&&badCommitCalls==0&&rejectedWindow.ModularHost==null,"Real DLL entrypoint failure did not remain uncommitted/cleaned.");
            Require((await service.GetActiveAsync())?.DirectoryName==previous?.DirectoryName,"Actual load failure changed disk activation.");
        }
        finally {await rejectedWindow.DisposeForChecksAsync();}
        var rollbackWindow=new MainWindow(new JsonRecentFilesStore(Path.Combine(output,"rollback-recents.json")));
        try
        {
            var partialCommit=new PreparedModuleUpdate(candidate,previous,async ct=>{await service.ActivateAsync(candidate,ct);throw new IOException("Injected failure after activation rename");},ct=>service.RestoreActiveAsync(candidate,previous,ct));
            var failed=false;try{await rollbackWindow.ActivatePreparedModulesAsync(partialCommit);}catch{failed=true;}
            Require(failed&&rollbackWindow.ModularHost==null,"Post-activation failure left runtime attached.");
            Require((await service.GetActiveAsync())?.DirectoryName==previous?.DirectoryName,"Actual activation compensation did not restore the prior/null pointer.");
        }
        finally {await rollbackWindow.DisposeForChecksAsync();}
        var recents=new JsonRecentFilesStore(Path.Combine(output,"recent-files.json"));
        var window=new MainWindow(recents);
        try
        {
            setupModel.SetCandidatePlan(candidate,previous);setupTrace.Add(setupModel.Attaching());
            await window.ActivatePreparedModulesAsync(update,runtimeAttached:()=>setupTrace.Add(setupModel.Attached()));
            setupTrace.Add(setupModel.Completed());
            await File.WriteAllTextAsync(Path.Combine(output,"setup-progress-trace.json"),JsonSerializer.Serialize(setupTrace,new JsonSerializerOptions{WriteIndented=true}));
            Require((await service.GetActiveAsync())?.DirectoryName==candidate.DirectoryName,"Successful runtime attachment did not activate candidate.");
            var host=window.ModularHost??throw new InvalidOperationException("No common document host.");
            var opened=await host.DispatchAsync(new("document.open",Argument:pdf));Require(opened.Success,opened.Message);
            Require(host.Document.PageCount>0,"Real PDF did not open.");
            Require(recents.Read().Entries.Any(e=>Path.GetFullPath(e.Path)==Path.GetFullPath(pdf)),"Successful open not recorded.");
            var first=host.Document;
            var render=await host.RenderPageAsync(first.DocumentId!.Value,first.ContentVersion,1,72);Require(render.Length>0,"Native page render returned no bytes.");
            await File.WriteAllBytesAsync(Path.Combine(output,"real-page.png"),render);
            var rotate=await host.DispatchAsync(new("pages.rotate",first.DocumentId,first.Version,[1],"90"));Require(rotate.Success,rotate.Message);Require(host.Document.IsDirty,"Rotation did not mark shared document dirty.");
            var undo=await host.DispatchAsync(new("document.undo"));Require(undo.Success,undo.Message);
            var savePath=Path.Combine(output,"rehearsal-saved-"+Guid.NewGuid().ToString("N")+".pdf");
            var save=await host.DispatchAsync(new("document.saveAs",Argument:savePath));Require(save.Success&&File.Exists(savePath),save.Message);
            var reopened=await host.DispatchAsync(new("document.open",Argument:savePath));Require(reopened.Success,reopened.Message);
            Require(host.Document.PageCount==first.PageCount,"Saved PDF reopened with a different page count.");
            var savedRender=await host.RenderPageAsync(host.Document.DocumentId!.Value,host.Document.ContentVersion,1,72);
            Require(savedRender.Length>0,"Saved PDF did not render after reopen.");
            await File.WriteAllBytesAsync(Path.Combine(output,"saved-page.png"),savedRender);
            var visualReadiness=await window.WaitForRenderedAreasForChecksAsync();
            Require(window.Title==ProductBrand.DocumentTitle(host.Document),"Actual open-document window title does not match exact product brand.");
            Capture(window,Path.Combine(output,"formal-real-document.png"));
            Require(!trusted||realNativeSignatureChecks>0,"Trusted rehearsal did not execute the real native signer policy.");
            await File.WriteAllTextAsync(Path.Combine(output,"release-rehearsal.json"),JsonSerializer.Serialize(new{Passed=true,Pages=host.Document.PageCount,DocumentId=host.Document.DocumentId,Candidate=candidate.DirectoryName,Saved=savePath,SavedPdfReopenedAndRendered=true,VisualReadiness=visualReadiness,ActualBackendLoadFailurePreservedPointer=true,ActualBackendPartialCommitRestoredPointer=true,ProductTitle=window.Title,ProductionTrustMode=trusted,RejectedBadManifestSignatures=trustedNegativeChecks,RealNativeSignatureChecks=realNativeSignatureChecks,Requests=handler.Requests,RealNetworkRequests=0,Scope=trusted?"Product RSA-PSS manifest trust plus real pinned Authenticode signer; local injected transport; real PDF open/render/rotate/undo/save/reopen and rollback; offscreen, not installer/live desktop acceptance":"Local exact-URL injected feed, active revalidation, dynamic regions, real native PDF open/render/rotate/undo/save; offscreen only; signature callback is test-only, not production authenticode proof"},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally { await window.DisposeForChecksAsync(); }
    }
    private static void Capture(MainWindow window,string path)
    {
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1280,780));root.Arrange(new Rect(0,0,1280,780));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1280,780,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    private sealed class FixtureHandler(string root,RSA? key):HttpMessageHandler
    {
        public List<string> Requests {get;}=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var uri=request.RequestUri??throw new InvalidOperationException("Missing fixture URI.");
            var prefix=new ReleaseSource("1.0.1").AssetPrefix;
            Require(uri.AbsoluteUri.StartsWith(prefix,StringComparison.Ordinal),"Unexpected release URL in injected transport.");
            var filename=Uri.UnescapeDataString(uri.AbsoluteUri[prefix.Length..]);
            Require(Path.GetFileName(filename)==filename&&!filename.Contains('\\')&&!filename.Contains('/'),"Nested fixture path rejected.");
            Requests.Add(uri.AbsoluteUri);
            if(filename=="wpf-modules.json.sig"&&key!=null)
            {
                var exact=await File.ReadAllBytesAsync(Path.Combine(root,"wpf-modules.json"),cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(key.SignData(exact,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))};
            }
            var path=Path.Combine(root,filename);
            if(!File.Exists(path))return new HttpResponseMessage(HttpStatusCode.NotFound){RequestMessage=request};
            return new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(await File.ReadAllBytesAsync(path,cancellationToken))};
        }
    }
}



