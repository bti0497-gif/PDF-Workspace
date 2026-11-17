using System.Reflection;
using System.Runtime.Loader;
using Workspace.Contracts;
namespace Workspace.Runtime;

public sealed record PreparedAreaView<TView>(string Region, TView View) where TView : class;
public sealed record AreaSwapResult(bool Success, bool RestartRequired, string Message);
public sealed record AreaSwapChange(string Region, IsolatedModuleManifest Manifest, string VersionFolder);
public sealed record RetiredAreaGeneration(string Region, string VersionFolder, WeakReference Context,
    bool DisposalSucceeded, string Detail);

/// <summary>
/// UI-thread-only host boundary. TryEnterQuiescence must freeze input/commands and
/// reject unsafe dirty/modal/background states until its token is disposed.
/// TryExchangeView is an atomic compare-and-exchange: false/exception MUST leave
/// the expected view attached. True means replacement is attached, old detached.
/// Never expose staging candidates to user input. Do not delete generation folders.
/// </summary>
public interface IWpfAreaSwapHost<TView> where TView : class
{
    void VerifyAccess();
    IDisposable? TryEnterQuiescence(string region, DocumentSnapshot expected);
    TView? GetAttachedView(string region);
    bool TryExchangeView(string region, TView? expected, TView? replacement);
}

/// <summary>
/// Explicit opt-in view-generation replacement, NOT guaranteed WPF assembly unload.
/// Default IsolatedModuleManager WPF restart policy is unchanged. A separate manager
/// owns each generation; no file is overwritten. Retired directories are retained for
/// the entire process lifetime, even after GC. Budget includes failed preparations;
/// restart is required when exhausted. Module Dispose owns its view disposal.
/// Host must also hold its document-owner lease for this coordinator's lifetime.
/// </summary>
public sealed class WpfAreaSwapCoordinator<TView> : IDisposable where TView : class
{
    private readonly IWorkspaceHost document;
    private readonly IWpfAreaSwapHost<TView> ui;
    private readonly Func<IWorkspaceModule, IWorkspaceHost, PreparedAreaView<TView>> factory;
    private readonly Assembly[] shared;
    private readonly int maximumRetiredGenerations;
    private readonly Dictionary<string, Generation> active = new(StringComparer.Ordinal);
    private readonly List<RetiredAreaGeneration> retired = [];
    private readonly List<Generation> quarantine = [];
    private readonly HashSet<string> usedFolders = new(StringComparer.OrdinalIgnoreCase);
    private int replacementAttempts;
    private bool blocked, disposed, operating;
    public WpfAreaSwapCoordinator(IWorkspaceHost document, IWpfAreaSwapHost<TView> ui,
        Func<IWorkspaceModule, IWorkspaceHost, PreparedAreaView<TView>> factory,
        Assembly desktopContracts, int maximumRetiredGenerations = 4)
    {
        if (maximumRetiredGenerations < 1 || maximumRetiredGenerations > 32)
            throw new ArgumentOutOfRangeException(nameof(maximumRetiredGenerations));
        this.document = document; this.ui = ui; this.factory = factory;
        shared = [desktopContracts]; this.maximumRetiredGenerations = maximumRetiredGenerations;
    }
    public IReadOnlyList<RetiredAreaGeneration> RetiredGenerations => retired.ToArray();
    public bool RestartRequired => blocked || replacementAttempts >= maximumRetiredGenerations;
    public int RemainingReplacementAttempts => Math.Max(0, maximumRetiredGenerations - replacementAttempts);

    public AreaSwapResult Start(string region, IsolatedModuleManifest manifest, string versionFolder)
        => Change(region, manifest, versionFolder, initial: true);
    public AreaSwapResult Swap(string region, IsolatedModuleManifest manifest, string versionFolder)
        => Change(region, manifest, versionFolder, initial: false);

    private AreaSwapResult Change(string region, IsolatedModuleManifest manifest, string folder, bool initial)
    {
        ui.VerifyAccess();
        if (disposed || blocked || operating) return new(false, true, "Coordinator unavailable; restart required or operation already active.");
        active.TryGetValue(region, out var old);
        if (initial != (old is null)) return new(false, false, "Region start/swap does not match active state.");
        if (old is not null && old.Id != manifest.Id) return new(false, false, "Stable module ID cannot change during an area patch.");
        if (old is not null && replacementAttempts >= maximumRetiredGenerations)
            return new(false, true, "Generation budget exhausted. Restart before another patch.");
        if (old is not null && old.Proxy.Pending != 0) return new(false, false, "Area commands or renders are still active.");
        var fullFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (usedFolders.Contains(FolderKey(region, fullFolder))) return new(false, false, "Each area generation requires a new immutable version folder.");
        var snapshot = document.Document;
        using var guard = ui.TryEnterQuiescence(region, snapshot);
        if (guard is null) return new(false, false, "Host rejected quiescence/dirty-state check; current area retained.");
        if (!ReferenceEquals(ui.GetAttachedView(region), old?.View)) return new(false, false, "Host area ownership changed.");
        operating = true;
        Generation? candidate = null;
        try
        {
            if (old is not null) replacementAttempts++;
            usedFolders.Add(FolderKey(region, fullFolder)); // shared package folder is allowed across distinct regions
            var manager = new IsolatedModuleManager(shared);
            var loaded = manager.Load(manifest, fullFolder);
            if (!loaded.Success) return new(false, RestartRequired, "Candidate rejected; current area retained: " + loaded.Message);
            var lease = manager.Acquire(manifest.Id);
            var proxy = new StagedHost(document);
            var context = AssemblyLoadContext.GetLoadContext(lease.Module.GetType().Assembly)!;
            candidate = new(manager, lease, proxy, manifest.Id, region, fullFolder, new(context));
            var prepared = factory(lease.Module, proxy);
            candidate.View = prepared.View;
            if (prepared.Region != region || candidate.View is null)
                throw new InvalidOperationException("Candidate region/view does not match the requested area.");
            var now = document.Document;
            if (now.DocumentId != snapshot.DocumentId || now.Version != snapshot.Version || now.IsDirty != snapshot.IsDirty ||
                old?.Proxy.Pending > 0)
                throw new InvalidOperationException("Document or area changed during preparation.");
            if (!ui.TryExchangeView(region, old?.View, candidate.View))
                throw new InvalidOperationException("Host rejected atomic view exchange.");
            if (!ReferenceEquals(ui.GetAttachedView(region), candidate.View))
                throw new InvalidOperationException("Host did not confirm candidate attachment.");
            // Commit: document identity/owner and all other region views are unchanged.
            old?.Proxy.Deactivate();
            candidate.Proxy.Activate();
            active[region] = candidate;
            candidate = null;
            if (old is not null) Retire(old);
            return new(true, RestartRequired, blocked
                ? "New area attached, but old cleanup failed. Restart required; old generation retained."
                : "Area replaced without application exit. Retired generation folders retained; collection is not assumed.");
        }
        catch (Exception ex)
        {
            if (candidate is not null)
            {
                // Defend against a broken host callback which changed the tree then threw.
                var attached = ui.GetAttachedView(region);
                if (!ReferenceEquals(attached, old?.View))
                {
                    var restored = false;
                    try { restored = ui.TryExchangeView(region, attached, old?.View) && ReferenceEquals(ui.GetAttachedView(region), old?.View); }
                    catch { }
                    if (!restored)
                    {
                        blocked = true; candidate.Proxy.Deactivate(); quarantine.Add(candidate); candidate = null;
                        return new(false, true, "Host atomicity/rollback failed. Both generations retained; restart required: " + ex.Message);
                    }
                }
                Retire(candidate);
            }
            return new(false, RestartRequired, "Candidate failed; old area retained: " + ex.Message);
        }
        finally { operating = false; }
    }
    private static string FolderKey(string region, string folder) => region + "\n" + folder;

    /// <summary>
    /// Transactional multi-area patch. Call on the UI synchronization context.
    /// Per-region quiescence guards must support nesting and collectively freeze ALL
    /// document input through asynchronous commit/rollback. commit persists activation;
    /// restoreDisk must compare-and-restore that activation if commit started (including
    /// a partial commit which threw). Old generations remain alive until commit succeeds.
    /// Neither callback may mutate documents or pump user input past the guard.
    /// </summary>
    public async Task<AreaSwapResult> SwapBatchAsync(IReadOnlyList<AreaSwapChange> changes,
        Func<CancellationToken, Task> commit, Func<Task> restoreDisk, CancellationToken cancellationToken = default)
    {
        ui.VerifyAccess();
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(restoreDisk);
        if (SynchronizationContext.Current is null)
            return new(false, false, "A UI synchronization context is required for asynchronous area transactions.");
        if (disposed || blocked || operating) return new(false, true, "Coordinator unavailable or another operation is active.");
        if (changes.Count == 0) return new(false, false, "No changed areas were supplied.");
        if (changes.Count > RemainingReplacementAttempts) return new(false, true, "Insufficient generation budget; restart before applying this batch.");
        var requested = changes.ToArray();
        var regions = new HashSet<string>(StringComparer.Ordinal);
        var previous = new Dictionary<string, Generation>(StringComparer.Ordinal);
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var change in requested)
        {
            if (!regions.Add(change.Region) || !active.TryGetValue(change.Region, out var old) || old.Id != change.Manifest.Id)
                return new(false, false, "Duplicate, unknown or mismatched area identity in patch batch.");
            if (old.Proxy.Pending != 0) return new(false, false, "An affected area still has active commands/renders.");
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(change.VersionFolder));
            if (usedFolders.Contains(FolderKey(change.Region, folder)))
                return new(false, false, "An area generation cannot reuse an attempted version folder.");
            folders.Add(change.Region, folder); previous.Add(change.Region, old);
        }
        var snapshot = document.Document;
        var guards = new List<IDisposable>();
        var candidates = new List<Generation>();
        var commitEntered = false;
        var suspended = false;
        operating = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var change in requested)
            {
                var guard = ui.TryEnterQuiescence(change.Region, snapshot)
                    ?? throw new InvalidOperationException("Host rejected batch quiescence/dirty-state check.");
                guards.Add(guard);
                if (!ReferenceEquals(ui.GetAttachedView(change.Region), previous[change.Region].View))
                    throw new InvalidOperationException("Host area ownership changed before preparation.");
            }
            foreach (var old in previous.Values) old.Proxy.Deactivate();
            suspended = true;
            foreach (var change in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                replacementAttempts++;
                var folder = folders[change.Region];
                usedFolders.Add(FolderKey(change.Region, folder));
                var manager = new IsolatedModuleManager(shared);
                var load = manager.Load(change.Manifest, folder);
                if (!load.Success) throw new InvalidOperationException("Candidate rejected: " + load.Message);
                var lease = manager.Acquire(change.Manifest.Id);
                var proxy = new StagedHost(document);
                var context = AssemblyLoadContext.GetLoadContext(lease.Module.GetType().Assembly)!;
                var candidate = new Generation(manager, lease, proxy, change.Manifest.Id, change.Region, folder, new(context));
                candidates.Add(candidate);
                var view = factory(lease.Module, proxy);
                candidate.View = view.View;
                if (view.Region != change.Region || view.View is null)
                    throw new InvalidOperationException("Candidate region/view mismatch.");
            }
            void VerifyDocument()
            {
                var now = document.Document;
                if (now.DocumentId != snapshot.DocumentId || now.Version != snapshot.Version ||
                    now.ContentVersion != snapshot.ContentVersion || now.IsDirty != snapshot.IsDirty ||
                    previous.Values.Any(g => g.Proxy.Pending != 0))
                    throw new InvalidOperationException("Document or affected area changed inside the quiescence guard.");
            }
            VerifyDocument();
            foreach (var candidate in candidates)
            {
                if (!ui.TryExchangeView(candidate.Region, previous[candidate.Region].View, candidate.View) ||
                    !ReferenceEquals(ui.GetAttachedView(candidate.Region), candidate.View))
                    throw new InvalidOperationException("Host rejected an atomic area exchange.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            commitEntered = true;
            await commit(cancellationToken); // capture UI context; old views still undisposed
            ui.VerifyAccess();
            cancellationToken.ThrowIfCancellationRequested();
            VerifyDocument();
            if (candidates.Any(c => !ReferenceEquals(ui.GetAttachedView(c.Region), c.View)))
                throw new InvalidOperationException("Area ownership changed during activation commit.");
            foreach (var candidate in candidates) { active[candidate.Region] = candidate; candidate.Proxy.Activate(); }
            candidates.Clear(); // ownership moved to active; subsequent cleanup cannot roll back committed activation
            foreach (var old in previous.Values) Retire(old);
            return new(true, RestartRequired, blocked
                ? "Batch committed; old-generation cleanup requires restart. Retired folders retained."
                : "All area views and activation committed; old generations retired without assuming collection.");
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            foreach (var candidate in candidates.AsEnumerable().Reverse())
            {
                try
                {
                    var oldView = previous[candidate.Region].View;
                    var attached = ui.GetAttachedView(candidate.Region);
                    if (!ReferenceEquals(attached, oldView) &&
                        (!ui.TryExchangeView(candidate.Region, attached, oldView) || !ReferenceEquals(ui.GetAttachedView(candidate.Region), oldView)))
                        rollbackErrors.Add("view " + candidate.Region);
                }
                catch (Exception rollback) { rollbackErrors.Add("view " + candidate.Region + ": " + rollback.Message); }
            }
            if (commitEntered)
            {
                try { await restoreDisk(); ui.VerifyAccess(); }
                catch (Exception rollback) { rollbackErrors.Add("activation: " + rollback.Message); }
            }
            if (rollbackErrors.Count != 0)
            {
                blocked = true;
                foreach (var candidate in candidates) { candidate.Proxy.Deactivate(); quarantine.Add(candidate); }
                return new(false, true, "Batch rollback failed; both generations retained and commands paused. Restart required. " + string.Join("; ", rollbackErrors));
            }
            foreach (var candidate in candidates) Retire(candidate);
            if (suspended && !blocked) foreach (var old in previous.Values) old.Proxy.Activate();
            return new(false, RestartRequired, "Batch failed; all old views and activation retained/restored: " + ex.Message);
        }
        finally
        {
            for (var i = guards.Count - 1; i >= 0; i--)
                try { guards[i].Dispose(); } catch { blocked = true; }
            operating = false;
        }
    }

    private void Retire(Generation generation)
    {
        generation.Proxy.Deactivate();
        string? cancellationFailure = null;
        try { generation.Proxy.Dispose(); }
        catch (Exception ex) { cancellationFailure = ex.Message; blocked = true; }
        generation.View = null;
        generation.Lease.Dispose();
        // This manager contains only the detached generation. Explicit shutdown is
        // scoped to that generation, not a relaxation of the default loader policy.
        var result = generation.Manager.Unload(generation.Id, applicationShutdown: true);
        var cleaned = result.Success && cancellationFailure is null;
        retired.Add(new(generation.Region, generation.Folder, generation.Context, cleaned,
            cancellationFailure is null ? result.Message : "Cancellation cleanup failed: " + cancellationFailure + "; " + result.Message));
        if (!cleaned) { blocked = true; quarantine.Add(generation); }
    }
    public void Dispose()
    {
        ui.VerifyAccess();
        if (disposed) return;
        if (operating) throw new InvalidOperationException("Cannot dispose during an area exchange.");
        foreach (var generation in active.Values)
        {
            if (!ui.TryExchangeView(generation.Region, generation.View, null))
            { blocked = true; throw new InvalidOperationException("Host could not detach area during shutdown."); }
            Retire(generation);
        }
        active.Clear(); disposed = true;
        // Quarantined managers and directories intentionally remain until process exit.
    }
    private sealed class Generation(IsolatedModuleManager manager, ModuleLease lease, StagedHost proxy,
        string id, string region, string folder, WeakReference context)
    {
        public IsolatedModuleManager Manager { get; } = manager;
        public ModuleLease Lease { get; } = lease;
        public StagedHost Proxy { get; } = proxy;
        public string Id { get; } = id;
        public string Region { get; } = region;
        public string Folder { get; } = folder;
        public WeakReference Context { get; } = context;
        public TView? View { get; set; }
    }
    private sealed class StagedHost : IWorkspaceHost, IDisposable
    {
        private readonly IWorkspaceHost owner;
        private readonly CancellationTokenSource lifetime = new();
        private int pending;
        private volatile bool active, disposed;
        public StagedHost(IWorkspaceHost owner) { this.owner = owner; owner.DocumentChanged += Changed; }
        public DocumentSnapshot Document => owner.Document;
        public int Pending => Volatile.Read(ref pending);
        public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
        private void Changed(object? sender, DocumentChangedEventArgs e) { if (!disposed) DocumentChanged?.Invoke(this, e); }
        public void Activate() => active = true;
        public void Deactivate() => active = false;
        public async Task<CommandResult> DispatchAsync(WorkspaceIntent intent, CancellationToken cancellationToken = default)
        {
            if (!active || disposed) return new(false, "Area is staging or retired.");
            Interlocked.Increment(ref pending);
            try { using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token); return await owner.DispatchAsync(intent, linked.Token); }
            finally { Interlocked.Decrement(ref pending); }
        }
        public async Task<byte[]> RenderPageAsync(Guid id, long version, int page, int dpi, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Interlocked.Increment(ref pending);
            try { using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token); return await owner.RenderPageAsync(id, version, page, dpi, linked.Token); }
            finally { Interlocked.Decrement(ref pending); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; active = false; owner.DocumentChanged -= Changed; DocumentChanged = null;
            lifetime.Cancel(); // outstanding requests own linked tokens until completion
        }
    }
}
