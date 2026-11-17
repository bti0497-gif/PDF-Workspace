using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace.Shell.ModuleDistribution;

public sealed partial class ModuleDistributionService
{
    private readonly IReadOnlyDictionary<string, string> trustedResourceHashes;
    private readonly Func<string, CancellationToken, Task<bool>>? verifyEngineSignature;
    public static string AssemblyName(ModuleAsset module) => module.AssemblyName ?? module.Id;
    public static string ModuleFileName(ModuleAsset module) => AssemblyName(module) + ".dll";
    public static string ResourceFileName(ModuleAsset module, ModuleResource resource) => module.Id + "-" + resource.Name;
    private void ValidateExtras(ModuleAsset module, string release, ref long total)
    {
        var name = AssemblyName(module);
        if (name.Length > 100 || !Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*$") ||
            Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
            throw new DistributionException("Invalid assembly name.");
        if (module.Region is not null && !Regex.IsMatch(module.Region, @"^[A-Za-z][A-Za-z0-9]{0,39}$")) throw new DistributionException("Invalid module region.");
        if (module.Type is not null && (module.Type.Length > 200 || !Regex.IsMatch(module.Type, @"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$"))) throw new DistributionException("Invalid entry type name.");
        if (module.Files is { Length: > 3 }) throw new DistributionException("Too many resources.");
        if (module.Files?.Any(f => f?.Kind == "engine") == true && !(module.Files.Any(f => f?.Name == "LICENSE.pdfcraft") && module.Files.Any(f => f?.Name == "NOTICE.pdfcraft")))
            throw new DistributionException("Engine requires license and notice resources.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in module.Files ?? [])
        {
            if (resource is null || !names.Add(resource.Name) || !((resource.Kind == "engine" && resource.Name == "pdfcraft-cli.exe") ||
                (resource.Kind == "notice" && resource.Name is "LICENSE.pdfcraft" or "NOTICE.pdfcraft")))
                throw new DistributionException("Resource kind/name not allowlisted.");
            if (resource.Size <= 0 || resource.Size > MaxAsset || (total += resource.Size) > MaxTotal || (resource.Kind == "notice" && resource.Size > 1024 * 1024))
                throw new DistributionException("Resource exceeds size limit.");
            if (resource.Sha256 is null || !Regex.IsMatch(resource.Sha256, "^[a-fA-F0-9]{64}$")) throw new DistributionException("Invalid resource SHA-256.");
            if (resource.Url != new ReleaseSource(release).AssetPrefix + module.Id + "-" + module.Version + "-" + resource.Name)
                throw new DistributionException("Resource URL not an exact WPF release asset.");
            if (resource.Kind == "engine")
            {
                if (!trustedResourceHashes.TryGetValue(resource.Name, out var hash) || !hash.Equals(resource.Sha256, StringComparison.OrdinalIgnoreCase) || verifyEngineSignature is null)
                    throw new DistributionException("Engine requires independent official SHA pin and trusted publisher-signature verifier.");
            }
        }
    }
    private async Task VerifyResource(string path, ModuleResource resource, CancellationToken ct)
    {
        Guard(path);
        await using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
        {
            if (s.Length != resource.Size || !Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).Equals(resource.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new DistributionException("Resource size/hash mismatch: " + resource.Name);
            if (resource.Kind == "engine")
            {
                s.Position = 0;
                try
                {
                    using var pe = new PEReader(s, PEStreamOptions.LeaveOpen);
                    if (pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll) || pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                        throw new DistributionException("Engine must be an x64 PE executable.");
                }
                catch (BadImageFormatException ex) { throw new DistributionException("Invalid engine PE.", ex); }
                // Keep a read-only sharing handle open throughout signature verification.
                if (verifyEngineSignature is null || !await verifyEngineSignature(path, ct))
                    throw new DistributionException("Engine publisher signature validation failed.");
            }
        }
    }
}
