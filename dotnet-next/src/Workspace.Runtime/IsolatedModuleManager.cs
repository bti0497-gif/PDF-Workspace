using System.Reflection;
using System.Runtime.Loader;
using Workspace.Contracts;
namespace Workspace.Runtime;

public enum ModuleReloadPolicy { Collectible, RestartRequired }
public sealed record IsolatedModuleManifest(string Id, string Version, int ApiVersion,
    string Assembly, string Type, ModuleReloadPolicy ReloadPolicy = ModuleReloadPolicy.RestartRequired);
public sealed record ModuleOperation(bool Success, string Message, WeakReference? UnloadedContext = null);

// Trusted local modules only. ALC is dependency isolation, NOT a security boundary.
public sealed class IsolatedModuleManager
{
    private readonly object gate = new();
    private readonly Dictionary<string, Slot> slots = new(StringComparer.Ordinal);
    private readonly Assembly[] shared;
    public IsolatedModuleManager(params Assembly[] additionalSharedContracts)
    {
        shared = new[] { typeof(IWorkspaceModule).Assembly }.Concat(additionalSharedContracts).Distinct().ToArray();
        if (shared.Any(a => AssemblyLoadContext.GetLoadContext(a) != AssemblyLoadContext.Default))
            throw new ArgumentException("Shared contracts must come from the default context.");
    }
    public IReadOnlyList<ModuleDescriptor> Modules { get { lock (gate) return slots.Values.Select(s => s.Module!.Descriptor).ToArray(); } }
    public ModuleLease Acquire(string id)
    {
        lock (gate)
        {
            var slot = slots[id];
            if (slot.Faulted) throw new InvalidOperationException("Module requires restart after disposal failure.");
            slot.References++;
            return new ModuleLease(slot.Module!, () => { lock (gate) slot.References--; });
        }
    }
    public ModuleOperation Load(IsolatedModuleManifest manifest, string versionFolder)
    {
        lock (gate)
        {
            if (slots.ContainsKey(manifest.Id)) return new(false, "Module already loaded.");
            try { slots.Add(manifest.Id, Prepare(manifest, versionFolder)); return new(true, "Loaded."); }
            catch (Exception ex) { return new(false, ex.Message); }
        }
    }
    public ModuleOperation Replace(IsolatedModuleManifest manifest, string versionFolder)
    {
        lock (gate)
        {
            if (!slots.TryGetValue(manifest.Id, out var old)) return new(false, "Module is not loaded.");
            if (old.References != 0) return new(false, "Active module leases block replacement.");
            if (old.Policy == ModuleReloadPolicy.RestartRequired || old.Faulted)
                return new(false, "Restart required. Keep current version active and stage candidate for next launch.");
            Slot candidate;
            try { candidate = Prepare(manifest, versionFolder); }
            catch (Exception ex) { return new(false, "Candidate rejected; current module retained: " + ex.Message); }
            if (candidate.Policy == ModuleReloadPolicy.RestartRequired)
            {
                Release(candidate);
                return new(false, "Candidate requires restart; current module retained.");
            }
            var released = Release(old);
            if (!released.Success)
            {
                Release(candidate);
                old.Faulted = true;
                return released;
            }
            slots[manifest.Id] = candidate;
            return new(true, "Replaced. Verify old context collection before deleting its version folder.", released.UnloadedContext);
        }
    }
    public ModuleOperation Unload(string id, bool applicationShutdown = false)
    {
        lock (gate)
        {
            if (!slots.TryGetValue(id, out var slot)) return new(false, "Module is not loaded.");
            if (slot.References != 0) return new(false, "Active module leases block unload.");
            if (!applicationShutdown && slot.Policy == ModuleReloadPolicy.RestartRequired)
                return new(false, "Restart required for this module.");
            var result = Release(slot);
            if (result.Success) slots.Remove(id); else slot.Faulted = true;
            return result;
        }
    }
    private Slot Prepare(IsolatedModuleManifest manifest, string folder)
    {
        if (manifest.ApiVersion != ModuleApi.Version || !Version.TryParse(manifest.Version, out _))
            throw new InvalidOperationException("Incompatible manifest API/version.");
        if (string.IsNullOrWhiteSpace(manifest.Assembly) || Path.GetFileName(manifest.Assembly) != manifest.Assembly ||
            manifest.Assembly.IndexOfAny(['/', '\\', ':']) >= 0 || !manifest.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An explicit local DLL filename is required.");
        var root = Path.GetFullPath(folder);
        var file = Path.Combine(root, manifest.Assembly);
        if (!File.Exists(file)) throw new FileNotFoundException("Module DLL not found.", file);
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked module directories unsupported.");
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked module DLL unsupported.");
        var context = new ModuleContext(file, shared);
        IWorkspaceModule? module = null;
        try
        {
            var assembly = context.LoadFromAssemblyPath(file);
            var type = assembly.GetType(manifest.Type, true)!;
            if (!typeof(IWorkspaceModule).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidOperationException("Module does not implement the shared contract.");
            module = (IWorkspaceModule)Activator.CreateInstance(type)!;
            var d = module.Descriptor;
            if (d.Id != manifest.Id || d.Version.ToString() != manifest.Version || d.ApiVersion != manifest.ApiVersion)
                throw new InvalidOperationException("Manifest and module identity/version differ.");
            new ModuleRegistry().Register(module);
            // WPF DP/XAML/type caches can root assemblies. No manifest override allowed.
            var wpf = assembly.GetReferencedAssemblies().Any(a => a.Name is "Workspace.Desktop.Contracts" or "PresentationFramework" or "PresentationCore" or "WindowsBase");
            if (manifest.ReloadPolicy == ModuleReloadPolicy.Collectible && module is not IDisposable)
                throw new InvalidOperationException("Collectible modules must implement IDisposable.");
            return new(context, module, wpf ? ModuleReloadPolicy.RestartRequired : manifest.ReloadPolicy);
        }
        catch
        {
            try { (module as IDisposable)?.Dispose(); } catch { }
            context.Unload();
            throw;
        }
    }
    private static ModuleOperation Release(Slot slot)
    {
        try { (slot.Module as IDisposable)?.Dispose(); }
        catch (Exception ex) { return new(false, "Disposal failed; restart required: " + ex.Message); }
        var context = slot.Context!;
        slot.Module = null; slot.Context = null;
        var weak = new WeakReference(context, false);
        context.Unload();
        return new(true, "Unload requested, not yet proof of collection.", weak);
    }
    private sealed class Slot(ModuleContext context, IWorkspaceModule module, ModuleReloadPolicy policy)
    {
        public ModuleContext? Context = context;
        public IWorkspaceModule? Module = module;
        public ModuleReloadPolicy Policy = policy;
        public int References;
        public bool Faulted;
    }
    private sealed class ModuleContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;
        private readonly Dictionary<string, Assembly> contracts;
        private readonly string root;
        private static readonly HashSet<string> FrameworkNames = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator).Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p).StartsWith("Microsoft.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll" or "WindowsBase.dll" or "PresentationFramework.dll" or "PresentationCore.dll")
            .Select(p => Path.GetFileNameWithoutExtension(p)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        public ModuleContext(string file, Assembly[] shared) : base("module-" + Guid.NewGuid(), isCollectible: true)
        {
            root = Path.GetDirectoryName(file)!;
            resolver = new(file);
            contracts = shared.ToDictionary(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase);
        }
        protected override Assembly? Load(AssemblyName name)
        {
            if (contracts.TryGetValue(name.Name!, out var contract))
            {
                if (name.Version != contract.GetName().Version ||
                    !((name.GetPublicKeyToken() ?? []).SequenceEqual(contract.GetName().GetPublicKeyToken() ?? [])))
                    throw new FileLoadException("Shared contract version mismatch: " + name);
                return contract;
            }
            if (name.Name!.StartsWith("Workspace.Contracts", StringComparison.Ordinal) || name.Name == "Workspace.Desktop.Contracts")
                throw new FileLoadException("Host must explicitly share the contract: " + name.Name);
            if (FrameworkNames.Contains(name.Name)) return null;
            var resolved = resolver.ResolveAssemblyToPath(name);
            var local = Path.Combine(root, name.Name + ".dll");
            if (resolved is null && File.Exists(local)) resolved = local;
            if (resolved is null) throw new FileNotFoundException("Unresolved private dependency: " + name.Name);
            return LoadFromAssemblyPath(ValidatePrivatePath(resolved));
        }
        protected override IntPtr LoadUnmanagedDll(string name)
        {
            var file = resolver.ResolveUnmanagedDllToPath(name);
            if (file is null) throw new DllNotFoundException("Unresolved private native dependency: " + name);
            return LoadUnmanagedDllFromPath(ValidatePrivatePath(file));
        }
        private string ValidatePrivatePath(string file)
        {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new FileLoadException("Dependency outside module version folder.");
            return full;
        }
    }
}

public sealed class ModuleLease : IDisposable
{
    private IWorkspaceModule? module;
    private Action? release;
    internal ModuleLease(IWorkspaceModule module, Action release) { this.module = module; this.release = release; }
    // No module/view/command reference may outlive its lease.
    public IWorkspaceModule Module => module ?? throw new ObjectDisposedException(nameof(ModuleLease));
    public void Dispose() { module = null; Interlocked.Exchange(ref release, null)?.Invoke(); }
}
