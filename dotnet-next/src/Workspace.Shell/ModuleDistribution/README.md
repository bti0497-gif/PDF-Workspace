# WPF module distribution backend (no shell/load integration)

New files only. Local tests: `./Run-Checks.ps1`. The script builds a disposable independent net9.0 fixture/harness using cleared NuGet sources and an in-memory HTTP handler. It never launches the WPF shell or downloaded programs and removes its own generated harness.

## API

Namespace `Workspace.Shell.ModuleDistribution`:

- `new ModuleDistributionService(HttpClient, filesystemRoot, trustedResourceHashes?, verifyEngineSignature?, manifestSignaturePolicy: new ManifestSignaturePolicy(productEmbeddedPublicSpkiPem))`. The final parameter defaults to null for source compatibility but null **fails closed**; an explicitly supplied fixed product trust key is mandatory.
- `CreateDownloadClient()` supplies a cookie-free client with automatic redirects/decompression disabled. Own/dispose client externally. Injection is for trusted host configuration and deterministic tests, not an untrusted HTTP client.
- `PrepareAsync(new ReleaseSource("1.0.1"), IProgress<DistributionProgress>?, CancellationToken)` returns `DistributionResult(Status, Candidate, Detail)`. Status is `Ready`, `NoChange`, or `Offline`. It does **not** activate or load anything.
- `GetActiveAsync(ct)` revalidates all active files before returning a candidate.
- `ActivateAsync(candidate,ct)` revalidates, checks downgrade/reused-version constraints, atomically replaces the active pointer, and retains previous/highest identities.
- `RestoreActiveAsync(expectedCurrentCandidate, previousCandidateOrNull, ct)` compensates a failed runtime switch with guarded exact pointer comparison. It can restore no active modules after failed first-install activation, verifies an existing predecessor, preserves the release high-water mark, and refuses a stale caller overwriting another activation. If activation failed before commit and the prior pointer is intact, it is a no-op. The host must save the previous candidate before activation and use a fresh non-cancelled recovery token. This changes disk state only, not loaded runtime objects.
- `RollbackAsync(ct)` explicitly revalidates and swaps to the previous directory, retaining the highest accepted release to reject network downgrades.
- Candidate: `DirectoryName`, `Manifest`, `DirectoryPath`. `ModuleFileName(module)` and `ResourceFileName(module,resource)` give generated filenames. Load only modules listed in the verified manifest, never enumerate unknown DLLs.
- Progress retains `Stage`, `ModuleId`, `Bytes`, `TotalBytes`, `Detail`, with optional `PlannedVersion`, `InstalledVersion`, `AssetName`, `AreaLabel`, `ChangeKind`, `ModuleCount`, `VerifiedModuleCount`. Original five-argument construction and five-value deconstruction remain supported. `Plan` is emitted for every validated module before any module payload download, including no-change plans. ChangeKind is Install/Update/Reuse, or Cached offline. PlannedVersion comes only from the accepted manifest; InstalledVersion comes only from the reverified active cache and remains the pre-activation version throughout preparation. Offline planned version is unknown/null. AreaLabel is the manifest Region or logical ID, not an invented translated feature name. AssetName identifies the DLL/resource, never infers its independent version. Details describe the observable payload operation, not an unprovided release changelog.
- Byte counts are **whole-candidate cumulative payload bytes**, including cache-copy/reuse bytes, not per-module bytes, actual network traffic, or lifecycle completion percentage. `ModuleCount` describes the accepted plan; `VerifiedModuleCount` advances only after a module DLL and all its resources verify. NoChange/Offline report reverified cached counts. Checking before a plan has unknown/null counts. Ready means staged, not activated or loaded. The shell owns runtime-attachment/activation phases and must not present preparation bytes as complete lifecycle percentage. UI should provide its own exception/cancellation presentation; asynchronous progress may be delivered after the method returns.

## Manifest v1

Strict camelCase JSON, no unknown or duplicate fields; no archives or filesystem paths:

```json
{
  "product": "PDF-Workspace-WPF",
  "version": "1.0.1",
  "api": 1,
  "modules": [{
    "id": "viewer",
    "version": "1.0.0",
    "url": "https://github.com/bti0497-gif/PDF-Workspace/releases/download/wpf-v1.0.1/Workspace.ViewerModule-1.0.0.dll",
    "size": 12345,
    "sha256": "REPLACE_WITH_64_HEX_DIGITS",
    "assemblyName": "Workspace.ViewerModule",
    "region": "Viewer",
    "type": "Workspace.ViewerModule.ViewerModule",
    "files": []
  }]
}
```

The entry type in this example is illustrative, not a verified real type. `assemblyName`, `region`, `type`, `files` are optional for the basic backend; the dynamic loader must require the fields it needs and independently validate API/interface/region/type compatibility before construction. Without assemblyName, ID is the assembly simple name. Logical IDs and assembly names are distinct when explicitly supplied. DLL assembly identity and x.y.z.0 version must match. Entry type existence and actual module API implementation are the loader's responsibility.

The manifest is always `https://github.com/bti0497-gif/PDF-Workspace/releases/download/wpf-v{productVersion}/wpf-modules.json`. Its mandatory detached signature is the same URL plus `.sig`: raw 384-byte RSA-3072/PSS/SHA-256 signature over the exact manifest bytes. `ManifestSignaturePolicy` accepts only an independently supplied product-embedded public SPKI PEM, never a key carried by the feed. Verification happens before JSON parsing or payload requests. Cached `manifest.json` preserves the original signed bytes, with `manifest.json.sig` alongside; every cache read/activation/rollback revalidates the signature. Missing or invalid signatures fail closed, including offline caches created by older unsigned code. Tests use temporary independent RSA keys; no private signing key is part of this subtree. Signing proves possession of the configured trust key, not harmlessness, legal publisher identity, key custody, or release immutability. Key rotation/revocation requires a separate host policy. DLL asset name is `{assemblyName}-{moduleVersion}.dll`. No Electron `v*` tag, latest API, user-supplied host, local file URL, query, or arbitrary path is accepted. Only the GitHub release-assets CDN can be reached by manual HTTPS redirect. Tag naming is an immutable-release convention, not proof GitHub administrators cannot mutate it. Existing accepted release versions cannot change locally. `ReleaseDiscovery` separately selects the highest numeric published stable WPF release through the exact repository API with bounded validated pagination. The release-list response is HTTPS metadata, not signed metadata; selected module manifests still require the embedded-key signature. The host composes discovery with preparation.

## Native engine/resources

Optional `files` entries have exactly `kind`, `name`, `url`, `size`, `sha256`. Allowed pairs:

- engine / `pdfcraft-cli.exe`
- notice / `LICENSE.pdfcraft`
- notice / `NOTICE.pdfcraft`

URL is exactly the same WPF release prefix plus `{moduleId}-{moduleVersion}-{name}`. Installed filename is `{moduleId}-{name}` in the versioned directory. Engine requires both notice assets. Notices may combine upstream license texts without changing their meaning.

Engine packages fail closed unless the host supplies a hash independently pinned to the official CLI under dictionary key `pdfcraft-cli.exe`, and a trusted asynchronous verifier which checks Authenticode chain/trust AND the expected publisher, returning true only on success. The backend checks pinned SHA, actual size/hash, x64 PE executable format, then calls the verifier while retaining a read-sharing handle. It never executes the engine. No built-in fake signature success and no claim of a real official engine test. Parent integration must use the independently verified official pin and real OS signature validation. Missing policy is an explicit block.

## Safety and failure boundaries

- 128 KiB manifest, 64 modules, 256 MiB per artifact, 1 GiB total, 1 MiB per notice. All metadata is validated before creating module/staging paths or downloading module resources. Manifest reads and downloads are bounded while streaming, even without Content-Length.
- Size/SHA alone prove byte consistency, **not** authentication or harmlessness. The mandatory RSA-PSS signature authenticates the exact manifest against the independent embedded product key, binding its payload hashes to that trust anchor. HTTPS source checks and managed PE validation remain separate; none sandbox code or prove key custody.
- New stage directories become versioned directories by same-volume rename; active pointer uses `File.Replace` or first-time `File.Move`. No live DLL is overwritten. Unknown files are never deleted. Incomplete stages and unreferenced completed candidates intentionally remain inert for later explicit maintenance.
- Exclusive root lock serializes backend operations. Paths and ancestors reject reparse points/junctions/symlinks before access. Use an application-owned root with restrictive ACLs. This is not a defense against a hostile same-user process racing filesystem substitutions, local pointer/manifest edits, hard links, or a privileged attacker. Manifest contents are signed and reverified, but the active pointer/high-water index is not a tamper-proof anti-rollback store. A hostile same-user actor can still interfere with availability or choose older legitimately signed state; restrictive root ACLs and trusted-host lifecycle remain required.
- Corrupt active cache fails closed rather than silently using it. Network/HTTP failure during manifest check returns a fully reverified active cache, or a clear first-install error. Mid-download failure/cancel preserves activation state; caller must surface the error and can explicitly continue with GetActiveAsync. Cancellation is honored before commit; the tiny atomic commit itself is not interruptible.
- No crash/power-loss filesystem durability proof, OS ACL sandbox, signature authority implementation, auto-GC, installer, registry integration, executable launch, or true hot reload is included.
- Shared contract/runtime libraries stay with the shell. Additional managed/native private dependencies, architectures, dependency resolution, module unloadability and native engine process lifetime need a separate packaging/loader policy. Raw independent DLL delivery does not prove those modules are runnable. This code never automatically downloads arbitrary dependency names.
- The host must quiesce affected modules/document work before activation and only switch the live runtime after safe loading/verification; on failure it can explicitly roll back. A verified disk pointer is not proof the runtime switched successfully.
