using System.IO;
using Workspace.Shell.ModuleDistribution;
namespace Workspace.Shell;

internal static class ModulePreparation
{
    // Production trust configuration is intentionally separate from the formal shell composition.
    // Populate from the independently verified publisher release, never from downloaded manifest values.
    internal static IReadOnlyDictionary<string,string> TrustedResourceHashes { get; set; } = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["pdfcraft-cli.exe"] = "32C94DF8EABB995DE8B24B078BF4D51F84A2CEE8B42C868B660017F7E5BBF8D0"
    };
    internal static Func<string,CancellationToken,Task<bool>>? VerifyEngineSignature { get; set; } = EngineSignatureVerifier.VerifyAsync;
    internal static Func<IProgress<DistributionProgress>,CancellationToken,Task<PreparedModuleUpdate>> CreateDefault()
    {
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Deojon","PdfWorkspace.Wpf","Modules");
        return async (progress,ct)=>
        {
            using var client=ModuleDistributionService.CreateDownloadClient();
            var service=new ModuleDistributionService(client,root,TrustedResourceHashes,VerifyEngineSignature,manifestSignaturePolicy:null);
            var previous=await service.GetActiveAsync(ct);
            using var discoveryClient=ReleaseDiscovery.CreateClient();
            var discovery=await new ReleaseDiscovery(discoveryClient).DiscoverAsync("1.0.1",service,ct);
            var prepared=await service.PrepareAsync(new(discovery.Version),progress,ct);
            var candidate=prepared.Candidate??throw new InvalidOperationException("설치 후보가 없습니다.");
            return new PreparedModuleUpdate(candidate,previous,
                async token=>{using var ownedClient=ModuleDistributionService.CreateDownloadClient();var current=new ModuleDistributionService(ownedClient,root,TrustedResourceHashes,VerifyEngineSignature,manifestSignaturePolicy:null);await current.ActivateAsync(candidate,token);var active=await current.GetActiveAsync(token);if(active?.DirectoryName!=candidate.DirectoryName)throw new InvalidOperationException("활성 모듈을 확인할 수 없습니다.");},
                async token=>{using var ownedClient=ModuleDistributionService.CreateDownloadClient();var current=new ModuleDistributionService(ownedClient,root,TrustedResourceHashes,VerifyEngineSignature,manifestSignaturePolicy:null);await current.RestoreActiveAsync(candidate,previous,token);});
        };
    }
    internal static async Task<DistributionCandidate> PrepareActiveAsync(ModuleDistributionService service,IProgress<DistributionProgress>? progress,CancellationToken ct)
    {
        var result=await service.PrepareAsync(new("1.0.1"),progress,ct);
        await service.ActivateAsync(result.Candidate??throw new InvalidOperationException("설치 후보가 없습니다."),ct);
        return await service.GetActiveAsync(ct)??throw new InvalidOperationException("활성 모듈을 확인할 수 없습니다.");
    }
}
