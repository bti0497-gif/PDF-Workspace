# Area and document API (additive to legacy API 1)

`IWorkspaceAreaModule : IWorkspaceModule, IDisposable` supplies `Region` (ribbon/sidebar/workspace) and `CreateView(IWorkspaceHost)`. Construct/create/dispose WPF areas on the UI dispatcher. Detach views, cancel pending work, release image resources, unsubscribe host events, and drop view/command references before releasing the manager lease. Dispose is synchronous; modules must not leave background work using their types after it returns.

`IWorkspaceHost` supplies an immutable published `DocumentSnapshot`, `DocumentChanged`, `DispatchAsync(WorkspaceIntent, CancellationToken)` and `RenderPageAsync(Guid documentId, long expectedContentVersion, int oneBasedPage, int dpi, CancellationToken)` returning PNG bytes from the actual domain renderer. Render requests validate identity/content before and after await, bounds and DPI 24..300. The actual owner remains responsible for render resource limits/serialization and actual document operations. No PDF operations are invented by Runtime.

`DocumentSnapshot(Guid? DocumentId,string? DisplayName,int PageCount,int CurrentPage,IReadOnlyList<int> SelectedPages,bool IsDirty,long Version,long ContentVersion=0)` uses one-based pages. State `Version` is globally increasing; `ContentVersion` changes on document content edits/open/undo/redo, not current-page/selection navigation. Modules must use document identity plus content version for image caches, and identity plus state version for optimistic command intents. `WorkspaceIntent(string CommandId,Guid? DocumentId=null,long? ExpectedVersion=null,IReadOnlyList<int>? Pages=null,string? Argument=null)` forwards to the owner without interpreting operations.

The owner creates `WorkspaceState(dispatch, render)` and alone calls `Publish`. Publish on the UI dispatcher for UI consumers; notifications run on the publishing thread. Selection arrays are defensively copied, listener exceptions are isolated, and `NotificationFailed` reports them. Dispatch is async, single-flight, and rejects stale supplied identity/version. Domain owners must publish resulting state, not modules. A missing render adapter throws NotSupportedException rather than fabricating content.

## Isolated loading

`IsolatedModuleManifest(Id,Version,ApiVersion,Assembly,Type,ReloadPolicy=RestartRequired)` uses exact stable identity/version/API and an explicit DLL filename. Keep each version in a separate immutable version folder, including its private dependencies and deps.json. Runtime does not download, write catalogs, rename installed files, verify signatures/hashes or activate installed packages.

`IsolatedModuleManager(params Assembly[] additionalSharedContracts)` always shares Workspace.Contracts from the default context. A WPF host must also pass `typeof(IWorkspaceAreaModule).Assembly`. Use Load(manifest,versionFolder), Acquire(id), Replace(manifest,versionFolder), Unload(id,applicationShutdown:false). Acquire returns a ModuleLease. Every view, async command and retained module reference must remain covered by a lease. No command/module object may outlive it. Contract versions and tokens must match; private dependencies resolve inside the version folder, not via accidental default-context module assemblies.

Each module has its own collectible ALC. Replace first validates the candidate, and leaves the current module untouched when candidate loading/validation fails. Active leases block replacement/unload. Disposal failure quarantines the old module and requires restart; this is not reported as successful rollback/unload. Disposal is not transactional, so arbitrary side effects from trusted constructors/Dispose cannot be rolled back. Framework/unmanaged resources and untracked external references can still root an ALC. `UnloadedContext` is a WeakReference, not a promise that collection occurred; verify collection before deleting retired version folders. Do not force GC in normal user operations. Non-collecting contexts should be retained for deferred cleanup after process exit.

## WPF limitation / verified deferred strategy

WPF DependencyProperty/XAML/BAML/type caches can retain collectible assemblies. Direct WPF/Desktop.Contracts references FORCE RestartRequired even if a manifest requests Collectible. The default is restart-only for all modules; any indirect WPF dependency must retain that default. This implementation does NOT promise live WPF view unload, instantiated-view GC, or lossless hot replacement of any actual feature area.

For WPF, stage an independently versioned candidate, retain the current version while running, wait for clean shutdown, then select the validated candidate at the next launch. Candidate catalog persistence, package integrity checks and startup fallback belong to bootstrap/update, not this runtime. On shutdown detach/dispose/release leases before Unload(id,true). The real WPF DLL tests verify contract identity and forced deferral, not physical UI rendering/GC. Actual non-WPF DLL tests prove old and replacement ALC WeakReferences become dead after GC. Async test temporaries can retain module references; checks use no-inline helpers to distinguish test-root retention from loader retention.

Legacy ModuleLoader/ModuleRegistry and IModuleViewProvider are unchanged and remain restart-only/default-context.

## Verification

Run `src/Workspace.Checks/ModuleIsolation/Verify.ps1` from any working directory. It builds real module versions and same-name private dependency variants, plus a WPF DLL, then runs 30 checks. Run existing Workspace.Checks separately: 17 legacy checks. No desktop windows, installer, network, deployment or release involved.
