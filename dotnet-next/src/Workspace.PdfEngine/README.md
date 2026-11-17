# Workspace.PdfEngine

UI-independent .NET 9 service extracted from the existing Shell PDF sessions and PdfCraft operations adapter. References only `Workspace.Contracts`; no WPF/Shell dependency. Binary runtime is deliberately Windows-local-path only. No CLI download/discovery/network behavior.

`PdfDocumentService(string? executablePath = null, Action<DocumentSnapshot>? publishState = null)` is the sole active-document owner and implements `IWorkspaceHost` and `IAsyncDisposable`. Default dependency: environment `PDFWORKSPACE_PDFCRAFT_CLI`, otherwise `Dependencies/PdfCraft/0.21.1/pdfcraft-cli.exe`. Actual CLI SHA256 must equal `32C94DF8EABB995DE8B24B078BF4D51F84A2CEE8B42C868B660017F7E5BBF8D0`.

## State and rendering

All operations serialize through one gate. Snapshot lists are read-only copies. Version increments on state changes; ContentVersion only changes on open/edit/undo/redo/close. RenderPageAsync(Guid documentId,long expectedContentVersion,int oneBasedPage,int dpi,CancellationToken) returns actual PNG bytes. Direct overload RenderPageAsync(int oneBasedPage,int dpi=96,CancellationToken) also available. DPI bounds 36..600; output limit 64 MiB. Never assume physical PDF fidelity from successful rendering.

DocumentChanged and optional publish callback run synchronously on the operation thread. UI must marshal to its dispatcher, must not synchronously wait on another service operation from a callback, and should reject stale asynchronous results. Callback errors are traced, not reported as failed committed operations.

## Exact intent IDs

Except document.open, intents require matching DocumentId and ExpectedVersion. Open rejects supplied stale identity/version and refuses dirty-document replacement. Missing page lists default to selection.

- document.select: Pages selection; optional Argument current-page decimal. Empty selection allowed.
- document.navigate: Pages one target page. Does not change selection.
- pages.rotate: Pages; optional Argument degrees (default 90).
- pages.delete: Pages, cannot delete all pages.
- pages.reorder: no Argument => Pages full permutation. With decimal Argument => Pages moved selection; Argument is zero-based insertion slot in original ordering before removal (0 first; PageCount last). Selected pages retain document order.
- pages.extract: Pages and Argument new absolute PDF destination. Does not modify the active document; existing destination rejected.
- document.merge: Argument JSON array of absolute PDF paths appended to current document.
- document.open: Argument absolute PDF path.
- document.undo, document.redo: fail if no history exists.
- document.save: existing source requires separate approval API.
- document.saveAs: Argument destination; existing file requires separate approval API.
- document.close: refuses unsaved changes.

Dispatch returns CommandResult(false,message) for rejected/unsupported operations; cancellation propagates. Direct typed APIs throw on rejection. No success is fabricated for unavailable dialogs or commands.

## Explicit overwrite consent

SaveAsync and SaveAsAsync never overwrite existing files. UI prepares a candidate with PrepareReplacementAsync(destination), displays candidate.Destination to the user, obtains affirmative replacement consent, and only then calls SaveWithReplacementAsync(candidate,true,token). Preparation is NOT approval. Candidate binds service, document identity, state version, path and content fingerprint. External source changes and target changes after preparation are rejected. Save uses reopened validation, flushed same-directory stage, final fingerprint recheck and File.Replace. This is not an OS sandbox or protection from every hostile same-user filesystem race.

For user close call CloseAsync() first; after an explicit discard choice call CloseAsync(true). DisposeAsync is final owner teardown and discards private revisions. Open never implicitly discards dirty work.

## Validation

Isolated harness outside repository: session tmp/pdfengine-boundary-check/EngineCheck.csproj. Actual pinned CLI + a copy of font-render-diagnostic.pdf: 27 checks passed (open, selection/content version, stale version, PNG render 96dpi, rotate, undo/redo, dirty close, overwrite consent, SaveAs, approved replace, close, reopen, merge, reorder, extract, delete, undo delete, external modification rejection, original SHA256 unchanged, cancellation, notifications, shared-host preparation/no-write/confirmed save, forged/replayed consent token rejection). Build 0 warnings/errors. This does not establish GUI integration, text/font preservation across every PDF, installer acceptance, or hostile-process sandboxing.

## Isolated module loading and shared-host consent

`PdfEngineModule` has a parameterless constructor, implements `IWorkspaceDocumentModule`, descriptor ID `pdfengine`, version `1.0.1`, and `CreateHost(string verifiedEnginePath)` returns the shared host. The shell does not need an engine-project reference or downcast. The configured CLI path is still hash-checked on use.

Shared `IWorkspaceHost` overwrite flow:
1. Dispatch `document.save.prepare` with normal identity/version and optional Argument destination (omitted = current source), or `document.saveAs.prepare` with destination. This requires an existing PDF target. For a new target, ordinary `document.saveAs` succeeds without replacement approval.
2. A successful preparation returns `CommandResult.Message` JSON `{ "Token": "opaque value", "Destination": "absolute path" }`. This is preparation only, never evidence of saving.
3. Display Destination to the user. Only after explicit affirmative consent dispatch `document.save.confirmed` (or `document.saveAs.confirmed`) with Argument equal to Token and the same document identity/version.
4. Tokens are service-held, fingerprint-bound, one-use, invalidated on any state publication. Do not auto-confirm based on preparation success. Failure/cancellation requires a fresh preparation and confirmation.

`document.close.discard` is the shared-host explicit-discard path, only after a discard choice. Ordinary close still refuses dirty state. Loader/owner teardown may dispose the host through `IAsyncDisposable`.

