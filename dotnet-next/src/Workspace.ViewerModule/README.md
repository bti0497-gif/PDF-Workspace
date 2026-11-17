# Workspace.ViewerModule

Independent WPF workspace area module. Entry type: Workspace.ViewerModule.ViewerModule; module ID: viewer; version: 0.1.0; region: WorkspaceRegions.Workspace.

References Desktop.Contracts only (and its transitive platform-neutral contracts), not Shell or the engine. Host owns document state; viewer sends document.navigate with one-based Pages and expected state Version. Rendering uses DocumentId + ContentVersion, never the navigation Version. Module disposal unsubscribes host events, cancels rendering, releases page controls/bitmaps. Loader registration and solution changes belong to the composing host.

## Behavior

- Real ScrollViewer with continuous vertical extent for every document page; single-page mode also available.
- Fit page, fit width, 10–400% manual zoom, Ctrl+wheel, Ctrl+Home/End, keyboard scroll, page-number Enter navigation. Small-window toolbar wraps rather than clipping controls.
- Geometry starts as a cheap 816×1056 placeholder. Completed 96-DPI renderer images supply true width/height including rotation. Geometry is retained separately from the downsampled bitmap and offsets are anchored during correction.
- At most 12 page controls and 12 cached images; offscreen images are released. Each decoded image has longest side at most 1024 pixels (nominal <=48 MiB total BGRA pixels), with one encoded render/decode in flight. Actual managed/native peak memory also includes the renderer, encoded bytes, WPF resources and GC retention; no process-RSS bound is claimed.
- One serialized async render pump, no per-page task fanout. View/content generations cancel and reject stale results. Navigation echoes do not reset a user scroll position. Empty document has no fabricated pages.
- Actual errors are shown contextually. No PDF editing or synthetic production content.

## Checks

Run locally, no desktop window:

    dotnet run --project src/Workspace.ViewerModule/Tests/ViewerChecks.csproj -- <temporary-output-directory>

Fake host has 129 pages and asynchronous portrait/rotated PNG fixtures. Tests use the real ScrollViewer and WPF dispatcher offscreen: boundary jumps 2/3/24/25/64/128/129/1, navigation echo count, external host navigation, single and continuous, fit/zoom, actual geometry, stale cancellation, one render at a time, bounded realization/cache, 1000×760 / 360×420 / 1280×800 layout, and disposal. Generated screenshot is a fixture-based offscreen layout proof, not a real-PDF or user acceptance result.

Latest checks pass with 8 navigation dispatches, 28 completed renders, 4 canceled renders, maximum concurrency 1, final cache 2 pages / 6,476,708 estimated pixel bytes. Timing counters can vary.

## Remaining boundaries

Rendering is currently fixed at 96 DPI and downsampled to 1024px longest side; zoom enlarges cached imagery, so high-zoom text sharpness is limited. A future bounded quality-aware rendering policy can improve it without changing the host API. Metadata for unseen pages is unknown until rendered, so initial document extent is approximate and adjusts as pages become visible. Fake-host checks do not establish real engine latency, native input, physical DPI, font fidelity, or full shell/sidebar integration. The module does not launch windows, install, download, release, or register default applications.

## Shell view-command bridge

The returned PdfViewer root implements shared IWorkspaceViewerView. ApplyViewCommand handles view.fitPage, view.fitWidth, view.singlePage, view.continuous, view.zoomIn, view.zoomOut, and view.actualSize using the existing viewer methods. Recognized commands return true; unknown commands or calls after disposal return false. These are view-only commands and never dispatch document mutations. Offscreen checks exercise all seven commands, their layout/zoom effects, unknown-command stability, and rejection after disposal.
