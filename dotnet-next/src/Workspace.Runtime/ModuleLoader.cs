using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Workspace.Contracts;
namespace Workspace.Runtime;

public sealed record ModuleEntry(string Id, string Version, int ApiVersion, string Assembly, string Type);
public sealed record ModuleCatalog(int SchemaVersion, ModuleEntry[] Modules);
public sealed record ModuleLoadReport(IReadOnlyList<string> Loaded, IReadOnlyList<string> Errors);
public static class ModuleLoader
{
    // Only explicit local catalog entries. No folder scanning, network downloads or live replacement.
    public static ModuleLoadReport Load(string catalogPath, string moduleFolder, ModuleRegistry registry)
    {
        var loaded = new List<string>(); var errors = new List<string>();
        ModuleCatalog catalog;
        try {
            catalog = JsonSerializer.Deserialize<ModuleCatalog>(File.ReadAllText(catalogPath))
                ?? throw new InvalidOperationException("빈 모듈 목록");
            if (catalog.SchemaVersion != 1 || catalog.Modules is null) throw new InvalidOperationException("지원하지 않는 목록 형식");
        } catch(Exception ex) { return new(loaded,new[]{"모듈 목록: " + ex.Message}); }
        foreach(var entry in catalog.Modules) {
            try {
                if(entry is null || entry.ApiVersion != ModuleApi.Version) throw new InvalidOperationException("호환되지 않는 모듈 API");
                if(string.IsNullOrWhiteSpace(entry.Assembly) || Path.GetFileName(entry.Assembly) != entry.Assembly ||
                    entry.Assembly.IndexOfAny(new[]{'/', (char)92, ':'}) >= 0 || !entry.Assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("모듈 DLL은 Modules 폴더의 파일명이어야 합니다.");
                var file = Path.GetFullPath(Path.Combine(moduleFolder,entry.Assembly));
                if(!File.Exists(file)) throw new FileNotFoundException("모듈 DLL이 없습니다.",entry.Assembly);
                if((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                    (File.GetAttributes(moduleFolder) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("연결된 모듈 경로는 허용하지 않습니다.");
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
                var type = assembly.GetType(entry.Type,true)!;
                if(!typeof(IWorkspaceModule).IsAssignableFrom(type) || type.IsAbstract) throw new InvalidOperationException("모듈 계약을 구현하지 않은 형식");
                var module = (IWorkspaceModule)Activator.CreateInstance(type)!;
                if(module.Descriptor.Id != entry.Id || module.Descriptor.Version.ToString() != entry.Version ||
                    module.Descriptor.ApiVersion != entry.ApiVersion) throw new InvalidOperationException("목록과 DLL 모듈 정보가 다릅니다.");
                registry.Register(module); loaded.Add(entry.Id);
            } catch(Exception ex) { errors.Add((entry?.Id ?? "unknown") + ": " + (ex is TargetInvocationException ? ex.InnerException?.Message : ex.Message)); }
        }
        return new(loaded,errors);
    }
}
