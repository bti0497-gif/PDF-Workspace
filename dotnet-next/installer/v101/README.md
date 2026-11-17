# WPF formal 1.0.1 packaging boundary

New implementation. Old WpfPreview/TEST packaging is reference only. The formal installer asset filename is exactly `더죤 PDF Workspace.exe`, and visible installer/product/Apps display name is `더죤 PDF Workspace`. Internal WPF identifiers remain unchanged and distinct from Electron. Product is `PDF-Workspace-WPF`; per-user installation is `%LOCALAPPDATA%\Programs\DeojonPdfWpf`, HKCU uninstall identity `DeojonPdfWpf`, Desktop and Start Menu shortcut `더죤 PDF Workspace`. No Electron/TEST2 registry reads, migration, PDF association, elevation, downloads, auto-launch, commit or upload. Release tag is **wpf-v1.0.1**, never Electron `v1.0.1` and never Electron latest/update YAML. Parent chooses formal release status; use non-latest on GitHub even if marked stable.

## Branding and icons

Build uses the approved `dotnet-next/src/Workspace.App/Assets/deojon-pdf-workspace.ico` by default, or the explicit `-AppIconPath` override. It validates ICO structure, passes the same asset as NSIS installer/uninstaller icon, and records its SHA256. The icon file itself is never edited by packaging. The parent must publish the apphost rebuilt with that same icon embedded.

Initial installation creates Desktop and Start Menu `더죤 PDF Workspace.lnk` shortcuts, both targeting installed `Workspace.App.exe` with explicit icon source `Workspace.App.exe,0`. Fresh-install collisions with existing same-named shortcuts fail before deployment rather than overwriting an Electron/other-app shortcut. Existing WPF upgrade shortcut entries are preserved. The internal install directory and HKCU key are still `DeojonPdfWpf`; the product manifest remains `PDF-Workspace-WPF` and tag remains `wpf-v1.0.1`.

The isolated native fixture verifies actual Unicode PE product metadata and both `.lnk` target/icon properties via Windows Script Host COM, without writing real Desktop/Start Menu entries. Pass `-AppIconPath` to `Test-Fixture.ps1` to compile with the approved icon once it exists. Shortcut icon-field correctness is distinct from visually accepting the rendered icon artwork.

## Build contract

`Build.ps1 -PublishDirectory <verified publish> -InputManifest <json> -VerificationJson <json> -OfficialCliZip <official pinned zip> -OutputDirectory <NEW directory> -MakeNsis <cached makensis.exe> -ProtectedSigningKeyPath <private DPAPI file> -ExpectedPublicKeySha256 <embedded SPKI SHA256>`

Build consumes artifacts, never runs dotnet publish. Source global.json pins SDK9.0.308; this machine currently only has SDK9. Formal release gate requires `net10.0` **self-contained Windows Desktop** output and exact runtime include entries. .NET10 SDK/runtime installation and publishing are parent's responsibility. `-DevelopmentNet9` supports current development inputs, emits DEVELOPMENT-net9 name and `formalReleaseEligible:false`; it is not a supported-LTS release. No quiet fallback to an absent runtime or unsupported runtime release.

Input JSON (all paths absolute except shellFiles paths; fill hashes from the exact tested bytes):

```json
{
  "product":"PDF-Workspace-WPF", "version":"1.0.1", "api":1,
  "shellFiles":[{"path":"Workspace.App.exe","sha256":"64hex"}],
  "modules":[{
    "id":"pdfengine", "version":"1.0.1",
    "path":"C:\\verified\\Workspace.PdfEngine.dll", "sha256":"64hex",
    "assemblyName":"Workspace.PdfEngine", "region":null,
    "type":"Workspace.PdfEngine.PdfEngineModule"
  }]
}
```

The example is illustrative, not a usable manifest. Include all four shell/shared DLLs, apphost, deps/runtimeconfig and every self-contained runtime file actually tested. Startup dialogs are owned by shell and compiled in it. No feature DLL, Modules folder, PdfCraft or PDB in shell. Managed shell/shared assembly versions must be 1.0.1.0. Modules need their exact type/region and managed identity/version x.y.z.0. Include viewer/sidebar modules too, with actual verified types. Input manifest hash binds every DLL/runtime artifact hash to QA receipt.

QA receipt required:

```json
{"passed":true,"inputManifestSha256":"64hex","checks":{"shellOnly":true,"startupOwnedModals":true,"moduleIntegration":true,"backend":true,"offscreen":true,"coreUpdateLease":true}}
```

Receipt is a QA attestation, not a signature or substitute for tests. Parent must create it only from actual tests on the exact published payload. This packager does not assert full GUI/fidelity or update safety simply from the boolean labels.

## Separate distribution assets

Backend contract coordinated with ModuleDistribution sibling. `assets/wpf-modules.json` uses camelCase product/version/api/modules. Each DLL gets `<assemblyName>-<moduleVersion>.dll`; resources get `<moduleId>-<moduleVersion>-<name>`. All URLs pinned to `https://github.com/bti0497-gif/PDF-Workspace/releases/download/wpf-v1.0.1/`. No ZIP is downloaded/extracted by shell. First-run downloading belongs entirely to shell.

Official input ZIP SHA256: `DCBCC2DDC019D85CCA90A8242D5E4DC380F1FF6BFB6B951A89152D1DBAE14CB4`.
Native CLI SHA256: `32C94DF8EABB995DE8B24B078BF4D51F84A2CEE8B42C868B660017F7E5BBF8D0`.
CLI must have valid Authenticode signature identifying Learning Machines Inc. The build rechecks the official archive SHA256 on the same read-locked handle used to consume it. PdfCraft 0.21.1 raw executable belongs to pdfengine, not installer; only that executable is extracted, to a fixed output filename.

`PortableLegal.ps1` discovers **every** file whose basename matches `LICENSE*` or `OFL-*.txt` anywhere in the archive (case-insensitive) and includes it in `LICENSE.pdfcraft`. Every `NOTICE*` and `README*` file is included in `NOTICE.pdfcraft`. There is no closed font-name allowlist. Original full archive paths become section headers; original entry bytes are copied unchanged, preserving full text, BOM/encoding, whitespace and line endings. Entries are ordered using ordinal comparison. Legal files are read directly from the ZIP, never extracted to archive-controlled paths. Invalid paths, traversal, backslashes, case-insensitive duplicate entries, empty legal files and combined notices over the backend's 1 MiB limit fail closed.

`build/legal-inputs.json` records the official archive hash and every selected entry's original path, byte length, SHA256, target bundle and content offset, plus each bundle's hash. The same provenance is embedded in the build receipt. Original legal bytes can therefore be verified directly against their exact archive entries. The official pinned ZIP test found **18 legal entries: 3 LICENSE files, 13 OFL font licenses, NOTICE and README.md**. Both bundled outputs remain below 1 MiB. Publish all generated assets and notices together; rebuild any older bundle that contained only the Nanum font license.

SHA256 is integrity, not publisher authentication. Output receipt records actual installer signature status. The tool does not sign the installer; expect NotSigned unless parent independently signs and recomputes post-sign asset hashes. Do not claim a signed first-party product. Do not upload build intermediates, receipts containing local paths, or DEVELOPMENT artifacts as formal release assets.

## Installer and removal

`CoreDeployment.ps1` now implements guarded same-volume directory deployment, replacing the temporary existing-install rejection policy. NSIS invokes this Windows PowerShell 5.1-compatible helper for recovery before staging and commit after complete extraction. No live files are replaced individually.

The installer and formal shell must share adjacent `<installDir>.update.lock`: updater acquires ReadWrite/FileShare.None before journal/recovery/mutation and holds it through completion; shell holds ReadWrite/FileShare.Read for its full lifetime and refuses both `.update-journal.json` and `.update-journal.json.new`. The app integration is provided separately in `Workspace.App/InstalledCoreLease.cs`. Build's exact-artifact QA receipt requires `checks.coreUpdateLease:true`; do not attest it until that compiled shell behavior is verified. NSIS also checks the apphost lock before work.

A candidate must match its exact ownership/hash manifest and contain no unexpected payload. Existing owned files must still match their prior manifest; changed/missing owned files fail closed. Unknown files and directories are copied with hash verification; any collision with new owned files blocks. Reparse trees are rejected. Snapshot read handles deny concurrent writes during preparation; they are released before directory rename because Windows disallows renaming while descendants remain open. The application lifetime gate stays exclusive throughout. This is not protection against a hostile same-user writer racing filesystem operations; backups are always retained and any unexpected recovery hash fails closed.

Durable journal writes use a flushed temporary FileStream, then atomic move/replace. States are Prepared, OldMoved, NewMoved, Committed and RollingBack. Old installation is renamed to a unique backup, candidate is renamed to the stable install directory, all candidate bytes are reverified, then Committed is recorded. Ordinary pre-commit failures restore the original tree. After interruption the next installer deterministically rolls back unfinished states or finishes an already-Committed state. Recovery also handles a durable `.new` journal. Unexpected trees or changed backups block rather than overwrite anything. Journals, old backups, failed candidates and orphan staging are retained under unique adjacent names, never recursively deleted.

Upgrade registration and shortcut entries are left untouched; existing stable paths still address the newly switched tree. First-install registration happens only after core commit and is not part of an atomic filesystem/registry transaction. A failure between first core commit and initial registration can leave a complete but unregistered installation; this acceptance case is not proven resolved. Uninstall remains exact-owned only and preserves unknown documents. No downloads, process killing, reboot replacement, or auto-launch.

## Reproducible identifiers, not reproducible PE bytes

Product, version, tag, file names and sorted includes are deterministic. NSIS PE timestamps/compression may vary; no bit-for-bit reproducible build claim. Retained build/includes/receipt make package membership auditable.

## Tests executed

`Test-CoreDeployment.ps1` passed under both PowerShell 7 and actual Windows PowerShell 5.1: successful initial deployment/upgrade plus injected ordinary failure and simulated interruption at Prepared, OldMoved, NewMoved and Committed. Pre-commit cases restore old content; Committed cases retain verified new content. Every case preserves PDF/unknown files. Lifetime-lock refusal and changed-owned-file refusal pass. Backup directories are never deleted.

`Test-Fixture.ps1` compiled and executed actual NSIS installers in an isolated temporary root. Fresh installation, apphost lock refusal, upgrade after release, first-candidate-file failure, orphan-stage recovery/retry, exact-owned removal, PDF/unknown preservation and unchanged real registry/shortcut snapshots pass. Fixture disables registry/shortcut writes, so actual registration UX is not established by this test. No actual user application was installed or launched.

These are real filesystem/process fixtures with simulated transition interruption, not a physical power-loss test or a complete formal WPF end-to-end upgrade acceptance. Directory metadata/power-loss durability, initial registration interruption and actual compiled shell lifetime-lease integration remain independent acceptance checks. Run both fixture scripts with new isolated temporary roots. No GUI or publication occurs.

## Detached manifest signing

Build requires `-ProtectedSigningKeyPath` and `-ExpectedPublicKeySha256`. The key path is supplied by the parent, never hardcoded, and must be outside source/publish/output trees. File format is Windows DPAPI CurrentUser-protected raw RSA-3072 PKCS#8 DER, using additional entropy UTF8 `Deojon.PdfWorkspace.WPF.ReleaseSigning.v1`. The unprotected DER byte array is zeroed immediately after import and on every failure path. Keys are never copied into output, included in receipts, or printed.

The builder derives SPKI DER and requires its SHA256 to equal the supplied expected fingerprint, which must match the public SPKI PEM embedded in the compiled shell. It signs the **exact bytes** of `assets/wpf-modules.json` using RSA-PSS with SHA256 and writes a raw binary detached `wpf-modules.json.sig`. It verifies the signature before writing and includes only public fingerprint/signature metadata in the receipt. Publish JSON and signature together without reformatting the JSON. This authenticates the module manifest; it is not installer Authenticode signing. The installer can still report `NotSigned`.

`Test-ManifestSigning.ps1 -TestRoot <new temp directory>` generates a temporary RSA3072 key, protects it with the real DPAPI/entropy contract, verifies with a public-only RSA instance, rejects modified manifest bytes and a wrong fingerprint, checks public outputs for private material, then removes the fixture protected key. The real release private key is not accessed by this test. Signing tests passed; no publishing occurred.

## Legal packaging regression

Run `Test-PortableLegal.ps1 -TestRoot <NEW temp directory> -OfficialCliZip <official pinned ZIP>`. Tests read the real official archive under the SHA256 pin and independently compare every matched original entry's bytes and original-name header with its bundled slice. Synthetic fixtures cover previously unseen and nested font licenses, nested LICENSE/NOTICE/README files, CRLF/LF and Korean text. Malicious traversal, absolute, backslash and duplicate-entry archives must be rejected. This test performs no publishing or installation.

Verified official bundle hashes after the all-font correction:

- `LICENSE.pdfcraft`: 73,159 bytes, SHA256 `D229F45E1D077B59AC060A91424278B8E921B8F84942A5E9A0CA51D5874B7BE8`.
- `NOTICE.pdfcraft`: 52,127 bytes, SHA256 `8115BAC8114EC13229FAEEAB5A3BD28F948167FB8251FC04FB35AB9A122D3AC2`.

These hashes bind this exact pinned ZIP and current ordinal full-path header format; they do not replace the archive and CLI pin/signature gates.




