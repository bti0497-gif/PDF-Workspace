# Formal 1.0.1 base application

`Workspace.App.csproj` is the formal base shell. `Workspace.Shell` remains the prior development/reference project; do not publish that project as the modular base.

The base compiles its own App/MainWindow, and source-links only the Startup modal/history files and ModuleDistribution backend. Its only project references are Runtime and Desktop.Contracts (transitively Contracts). No PdfViewing, PdfOperations, PreviewWorkspace, old updater, FoundationModule, native CLI, or feature DLL is compiled/staged into its output.

The approved orange chrome styles and tab layout are retained. All actual menu, sidebar, viewer and document engine implementations come from reverified active module candidates via IsolatedModuleManager. The WPF area coordinator supports opt-in transactional area-generation replacement while retaining the shared document engine. Only changed ribbon/sidebar/viewer areas can swap. Dirty documents, active modal/operations/renders, engine changes, and exhausted generation budgets block live application. Old generations/folders remain retained; successful view replacement is not proof of WPF assembly collection.

`ModulePreparation.cs` is the production trust configuration point. It uses the pinned PdfCraft executable SHA-256 and the Windows Authenticode publisher verifier; `ModuleSigningPublicKey.cs` embeds the public key used to verify detached manifest signatures. An empty trust configuration fails closed. These checks do not imply that the installer itself is Authenticode-signed.

## Verification

- `--formal-smoke <absolute-new-output-dir>` is offline and does not show a window. It audits base output for feature/native payloads, audits compiled types for legacy implementations, and renders formal-shell.png.
- `--release-rehearsal <absolute-release-fixture-dir> <absolute-real-pdf> <absolute-new-output-dir> <independently-verified-engine-sha256>` injects an HTTP handler mapping only exact 1.0.1 release HTTPS URLs to local fixture basenames. It performs no network. It prepares/activates/revalidates a candidate in the test output directory, dynamically loads actual modules, opens and renders the real PDF, rotates/undoes/saves a separate new file, verifies history and writes JSON and offscreen captures. Its signature callback is explicitly test-only: this test does NOT establish production Authenticode verification.
- Fixture directory contains `wpf-modules.json` and the exact asset basenames appearing in that manifest's URLs. No arbitrary file or local URL loader exists in normal startup.
- Keep outputs under fresh `.test-results/` directories. Offscreen evidence is not live desktop interaction, installation, or release acceptance.

Normal startup stages verified bytes, creates the engine and all region views, and only then activates the candidate. Failed attachment leaves the old pointer unchanged; partial activation failure compare-restores the exact previous pointer or null first-install state. Nine deterministic lifecycle failure/order checks are included in --formal-smoke; --release-rehearsal additionally exercises actual DLL entrypoint rejection and actual backend post-rename compensation. Help → 업데이트 확인 (Ctrl+Shift+U) starts a real staged update and transactional batch swap. Its ribbon command returns before updating so the requesting area has no pending command lease during its own replacement. Normal startup shows owner-bound module setup, then the file/recent-open modal. Canceling the open modal leaves the shell running. Save prompts consume the engine's fingerprint-bound single-use token; owner close serializes operations and requires save/discard/cancel for a dirty document.


## Installer progress contract

The shell consumes validated per-module Plan events. It displays actual module versions, pre-install version → selected version, and install/update/reuse actions using human area names. Offline mode identifies the verified local version and explicitly leaves the online target unknown. It never substitutes product version 1.0.1 for a module version.

Overall percent is completed work stages, not a time estimate or download percentage: each module's complete DLL/resource verification is one stage, actual runtime attachment is one, persisted activation is one. For four modules these are six stages: all files verified 4/6; runtime attached 5/6; activation complete 6/6. Cumulative processed bytes (including reused cache bytes) are kept at overall level; no fabricated per-module byte totals are shown. Raw backend URLs, paths, and generic authentication caveats are not rendered as progress copy.

Formal smoke includes 13 progress mapping checks. Local release rehearsal signs exact fixture JSON bytes in memory with a newly generated RSA-3072/PSS/SHA-256 test key; the injected handler serves its detached .sig and the service receives only its public key. No private test key is persisted, and production public-key trust is not replaced. Native Authenticode verification remains a separate production policy boundary from this fixture's test-only native-signature callback.

## Installed core replacement coordination

Only normal execution from the exact normalized `%LOCALAPPDATA%\Programs\DeojonPdfWpf` directory acquires its adjacent `.update.lock` using OpenOrCreate / ReadWrite / FileShare.Read. It is held before any normal window/module work until App.OnExit. Either `.update-journal.json` or `.update-journal.json.new` blocks launch pending installer recovery. Development and explicit offscreen verification branches never acquire the production installation lease. The updater takes FileShare.None before any journal/core mutation. Thirteen real FileShare/journal checks run against isolated temporary directories.

## Running-app update scheduling

A DispatcherTimer at ApplicationIdle checks every six hours. Preparation downloads inert verified bytes without disabling ShellRoot or blocking foreground document commands/exit. A candidate downloaded while dirty/busy/modal is retained and retried on subsequent clean idle, without redownload; only atomic area swap quiescence freezes input. Timers and in-flight preparation are cancelled on disposal. Engine/core changes remain restart-only. Nine deterministic async preparation/defer/cancellation/payload-comparison checks exercise the scheduling boundary; live changed-area replacement has separate actual-DLL integration evidence.

## Trusted rehearsal and DEBUG human review

`--trusted-release-rehearsal <signed-fixture-dir> <real-pdf> <fresh-output-dir>` uses the compiled product RSA public key and actual Windows native Authenticode verifier, with an exact-URL local transport. It rejects tampered/wrong-key manifests and exercises actual attach/native work/rollback. No private signing key is read or generated for the accepted product signature.

DEBUG builds only: `--local-ui-review <signed-fixture-dir> <new-nonexistent-review-root>` displays the actual setup progress, automatically proceeds to the actual open modal, then lets the user operate real modules. Cache and recents remain isolated in the supplied review root. The fixture must carry a product-key signature and the native signer check is real. This command and its preparation entry point are compiled out of Release; formal Release smoke asserts their absence. Agents must not launch this interactive mode without the parent/user's desktop handoff.

The status-bar product version begins at 1.0.1 and changes to the committed active manifest version only after successful startup or live patch activation. A failed patch/rollback keeps the previously displayed version; base assembly version is unchanged.
