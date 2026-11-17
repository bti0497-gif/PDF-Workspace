# Sidebar module

Independent WPF DLL. Entry type `Workspace.SidebarModule.SidebarModule`, module ID `sidebar`, version `1.0.0`, region `WorkspaceRegions.Sidebar`. References only Desktop.Contracts (and its Contracts dependency), never Shell or a PDF engine. No solution or manifest changes are included here.

`CreateView(IWorkspaceHost)` creates the sidebar; detach its visual before `Dispose`. Disposal removes the host subscription, stops gesture timers, cancels queued/in-flight previews, and guards queued dispatcher callbacks. Runtime replacement/collectibility is not claimed.

## Host API

- `DocumentSnapshot`: DocumentId, PageCount, CurrentPage, SelectedPages, Version, **ContentVersion**.
- `DocumentChanged`: authoritative selection/content state, marshalled onto the view dispatcher.
- `DispatchAsync`: `document.select` (Pages plus optional current-page Argument); `pages.rotate` (degrees), `pages.delete`, `pages.extract`, `pages.reorder` (original-order zero-based insertion slot before removal).
- Every intent includes current DocumentId and ExpectedVersion; the sidebar does not edit a synthetic document or invent successful operations.
- `RenderPageAsync(documentId, ContentVersion, page, dpi:40, token)` must support background callers and cancellation. The return must be an encoded WPF-readable bitmap (PNG is tested). Returned pages are decoded to 220px wide frozen images off the UI thread. The current document/content-version cache persists across selection/navigation; two active requests maximum. Visible pages are promoted ahead of unrendered background pages; all pages are eventually requested, with no 24-page cutoff. No document bytes/file preload occurs in the sidebar.

## Interaction

Card-only highlight, Ctrl toggle, Shift range, Ctrl+Shift union, navigation only on plain click; 64–220 preferred size and responsive multi-column wrap. Actual page-image aspect ratio is respected when available. Right-click preserves a selected group or selects the clicked card. Card drag uses the hovered card's left/right edge as its red insertion marker, including wrapped rows. Scroll-edge timer supports dragging and blank-space marquee. Escape cancels gesture, Ctrl+A selects all, Delete dispatches removal. Rendering failure has a per-page retry button/context action.

## Compatibility boundaries

- `pages.extract` emits no destination; the application host must intercept it and show its owned destination chooser before dispatching to the engine. Without this, the engine's truthful error is displayed.
- The view updates definitive selection from host events, not optimistic document mutation. Host must publish accepted selection events; stale-version rejection is shown rather than retried against another document.
- Native pointer/capture/auto-scroll and real PDF engine behavior need integration acceptance. Tests call the same selection/geometry/intent methods and render actual WPF offscreen, not physical mouse input.
- Card visuals/images are retained for every page in the current document. Two renderer calls and 220px decoded previews bound per-request cost, but this is not a virtualized/memory-budgeted infinite-document list.

## Checks

`dotnet run --project dotnet-next/src/Workspace.SidebarModule/tests/Sidebar.Checks.csproj`

No test packages/network. Fake-host checks cover selection/visual highlight, size clamps, wrapping, card-relative insertion, operation intents, 129 previews, bounded concurrency, visible priority, navigation cache retention, stale-document cancellation, visible failure/retry, and unsubscription. The executable writes `sidebar-offscreen.png` beside its generated binary. No window is shown and no installer/release/desktop settings are touched.

## Actual 129-page report check · 2026-11-17

A unique temporary STA/WPF dispatcher harness used the real `SidebarModule` and real `PdfDocumentService` with the engine's exact SHA-pinned PdfCraft 0.21.1 CLI. The report copy's before/after SHA-256 matched the supplied original digest. No window, report screenshot, extracted report text, desktop action, or explicit process kill was used. Harness exit code: 0.

- 129/129 actual previews, zero failures, exactly 129 per-page render requests.
- Open: 529 ms; view creation plus offscreen layout: 531 ms; first preview: 739 ms; page 25: 7,181 ms; page 129: 36,214 ms; all previews: 36,231 ms. Timings are this local run, not a performance guarantee.
- Pages 25 and 129 each had a frozen, non-flat 220×311 bitmap. All 129 bitmaps were frozen and 220 pixels wide.
- At most two outstanding sidebar host render calls. The engine itself serializes real CLI operations.
- Largest returned PNG: 133,263 bytes. Current-document decoded bitmap payload: 35,304,720 bytes (about 33.67 MiB). The 129 cache entries and 129 image controls shared the same bitmap instances, rather than duplicating decoded payloads. Harness working set rose from 44,249,088 bytes to a sampled peak of 139,849,728 bytes; this excludes CLI child-process memory and is not a hard process-memory cap.
- Selecting pages 25/129 and changing preferred size to 220 caused no additional render request.
- No-kill cancellation check: two genuine CLI renders completed, their results were held at the instrumented host boundary, and module disposal cancelled both waiting completions in under 1 ms with zero previews published. This verifies module cancellation/late-result suppression, **not cancellation of a running CLI**. The engine's existing per-operation deadline/cancellation policy was not modified.
- Memory remains proportional to current-document page count; the module is not virtualized and has no document-size-independent cache cap.

Private evidence: session temporary `sidebar-real-1794905935820/run.log` and `bin/Debug/net9.0-windows/result.json`; the harness source is in that same unique directory. Logs contain counts, timings, memory measurements and hash checks, not report content.
