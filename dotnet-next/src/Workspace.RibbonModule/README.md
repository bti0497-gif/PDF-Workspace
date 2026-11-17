# Ribbon area module

Independent WPF DLL. References only Workspace.Desktop.Contracts (and transitive Workspace.Contracts), not Shell or PDF engine. No installation/release changes.

## Integration API

- Entry point: `Workspace.RibbonModule.RibbonModule : IWorkspaceAreaModule` (public parameterless constructor).
- Descriptor: `ribbon`, version `1.0.0`, API `ModuleApi.Version`; Region `WorkspaceRegions.Ribbon`.
- `CreateView(IWorkspaceHost host)` returns `RibbonView : UserControl, IWorkspaceRibbonView, IDisposable`.
- Shell casts returned root to shared `IWorkspaceRibbonView` and calls `SelectCategory("file" | "home" | "document" | "view" | "help")`. Korean aliases 파일/홈/기본/문서/보기/도움말 accepted. Default file.
- Module owns only grouped icon ribbon. Orange tab bar, keyboard shortcuts and all pickers/confirmation remain Shell responsibilities.
- `RibbonView.Actions` exposes genuine ICommand actions; `ExecuteAsync(id)` returns exact host result. `LastResult` and `CommandCompleted` expose completion without adding unsolicited banners or fake success notices.
- `Dispose()` cancels in-flight work, unsubscribes host events, disables actions; creating a replacement view disposes the old view. Host detaches view before module disposal.

## Host intents

`document.open`, `document.save`, `document.saveAs`, `document.close`, `document.undo`, `document.redo`, `pages.rotate`, `pages.delete`, `pages.extract`, `document.merge`, `view.fitPage`, `view.fitWidth`, `view.singlePage`, `view.continuous`, `app.checkUpdates`.

Every dispatch captures current document identity/version. Page actions copy current shared selection. Rotate argument is `90`; all picker-dependent commands pass null arguments. The host MUST be the shell bridge, which gathers paths, obtains save/close approval and forwards valid requests, not the raw engine host. No local file writes, approval tokens or fabricated successful PDF operation occur in this module.

No document enables Open and parent-approved update check only; category selection remains available. History availability follows authoritative DocumentSnapshot.CanUndo/CanRedo. Empty selection disables selected-page actions, and selecting every page disables Delete. In-flight actions disable duplicate dispatch. DocumentChanged marshals availability refresh to UI dispatcher. No unimplemented tools or warning/initial guidance labels are shown.

Approved MainWindow ribbon dimensions (78x70/88/112 cards, 28x30 icons, #F6F6F6 surface, #526D88 strokes, group separators/captions) and independently authored RibbonLayout paths are retained. Explicit button templates never fall back to Windows disabled white/gray rectangles. Optional resource `/Workspace.RibbonModule;component/RibbonStyles.xaml` key `RibbonCategoryButton` is available to shell but does not inject tabs; disabled-tab continuity tests apply to this explicit style, not an uninspected shell template.

## Verification

```
dotnet run --project dotnet-next/src/Workspace.RibbonModule/tests/Ribbon.Checks.csproj -- <output-directory>
```

Tests use fake shared host and real offscreen WPF rendering: identity/version/selection/arguments, no-file/history gating, actual bound ICommand, duplicate protection, host failure preservation, async completion UI-thread handoff, view replacement/disposal, card/icon geometry, all five category renders, no opaque-white pixels in disabled ribbon, orange continuity in disabled tab template. Generated PNG evidence is not source. Does not establish native mouse/keyboard, monitor DPI, real PDF mutation, picker UI or full-shell integration acceptance.

Help update check delegates entirely to the shell host; it is disabled while the document is dirty or a ribbon action is busy. Shell must revalidate modal/busy/document state before committing any update. No network, timer, startup scheduling or update installation is implemented inside this presentation DLL.
