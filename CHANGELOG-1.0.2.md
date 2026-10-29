# PDF Workspace 1.0.2

2026-10-29 local revision. No source repository push or release publication was performed.

## Implemented

- Fixed Electron ESM/CommonJS autoUpdater import that prevented 1.0.1 startup.
- Automatic update download/install disabled; update restart no longer discards dirty documents.
- Added a large page-organizer workspace with source file/page labels, Ctrl/Shift selection, drag reorder, insertion, rotation, extraction and deletion.
- Replaced numeric-only crop dialog with actual rendered preview, pointer-drag rectangle, four corner handles, mm/pt values and current/selected/all page scope. Existing CropBox origin and absolute rotations are respected.
- Added actual export-preview rendering for cropped/resized pages.
- Replaced merge list with file thumbnails, expand/select/exclude pages, file-level order, cross-file page ordering, result preview and separate save. Opening the result is opt-in and checks unsaved work.
- Added bookmark rename, target change, hierarchy/order controls and Unicode PDF outline export. Stable page IDs preserve destinations during page reorder/delete/undo.
- Exposed image insertion separately from the stamp default.
- Added conservative text-run editing: no automatic neighboring-run merges, exact bundled-font width measurement, preserved whitespace/deletion, IME commit guard, alignment anchors, overflow rejection.
- Added a browser-compatible derivative of the existing NanumGothic font without changing the original asset. Browser and exported text share this font.
- Preserved source bytes when PDF.js transfers data to its worker. Loaded real per-page CropBox sizes/rotation, including inserted PDFs.
- Replacing a saved PDF now writes a same-directory temporary file and makes a timestamped .bak of an existing destination before replacement.

## Important limits

This is an initial NesPDF-inspired workflow revision, not full feature parity or a copy of its proprietary interface/assets.

Text editing is NOT arbitrary native PDF text-stream/font editing. A user must explicitly consent to a safe-copy mode: only pages containing text replacements become image-backed, the replacement text remains searchable, but other original text on those edited pages is no longer searchable/copyable and existing links/forms on those pages are flattened. Other pages are copied without that rasterization. Saving text edits always opens Save As with a new suggested name. The user can still intentionally choose an existing path; a .bak is created in desktop mode.

Only conservatively separable, horizontal text on white backgrounds is supported. Rotated/resized pages, uncertain overlapping text and complex whitespace runs are excluded. There is no automatic font-size reduction or neighboring-content reflow. New text larger than the original box must be shortened or explicitly resized.

Cropped/resized pages display the actual output preview. Further direct text/annotation editing on those transformed pages requires saving and reopening first.

Bookmark export retains titles/hierarchy/page targets using Fit destinations, not arbitrary original external actions or XYZ zoom settings. Merge is page-content based and does not promise preservation of outlines, document metadata, digital signatures, complex form semantics or every link relationship. Generic link editing and full imposition workflows were not added in this revision.

## Verification performed

- TypeScript noEmit check and production Electron/Vite build.
- text-edit-regression.cjs: saved/reopened edited text, removed source text stream on edited page, unedited page text retained, consent/overflow rejection, deletion, absolute 90-degree rotation, existing CropBox origin.
- test-bookmarks.mjs: stable destination binding, reorder/delete/undo, hierarchy, Unicode outline write/reload.
- merge-plan-check.cjs: page order/count/exclusion/rotation, immutable inputs, invalid plans rejected.
- Actual browser UI: page drag reorder; crop mouse-drag and synced numeric margins; crop apply/save; Korean replacement (서울휴게소 → 부산휴게소); overflow disables Apply; actual save and reopen; merge two 3-page PDFs, reorder file groups, exclude one page and save 5-page result; bookmark add/rename/save/reopen.
- UI-saved outputs were independently parsed: expected CropBoxes/rotations/page count, edited Korean text present, old first-page searchable text absent, second/third-page searchable text preserved, Korean outline present.
- Pixel comparison outside the edited region across browser-rasterized and Node-rendered outputs had 0.0523% pixels differing by more than 20 channel levels. This is a bounded rendering comparison, not pixel-perfect identity across engines or arbitrary-document validation.
- Desktop development-mode executable: window visible, renderer loaded, preload/IPC registered.
- Packaged executable (dist/win-unpacked) launched successfully and opened the 3-page mixed-size/rotation test PDF; startup/window visibility/renderer/preload verified. The pre-font-fix smoke log contained zero renderer, preload-load or critical errors. After the font fix, the final package also opened the saved 3-page PDF successfully; Electron emitted PDF-font OTS/cmap load warnings, so this is not a warning-free run. This is direct packaged-executable testing, NOT an in-place installer upgrade test.
- Added serialized canvas rendering after detecting overlapping render calls during the earlier smoke test; final packaged retest passed.
- Final installer SHA256: 75D62B9D00D935D63D1C29E01D8A494362CB0EA74E3F010E07EAB88797C3AF8C. Artifact copy hash matched. Packaged main/preload/JS/CSS matched the final build byte-for-byte.

## Build

`npm run build:win` prepares the compatible font, builds and packages with `--publish never`.

The 1.0.1 installers remain in dist. New installer: `dist/Douzone-PDF-Editor-Setup-1.0.2-x64.exe`.

- Final font correction: fully embed the Korean TTF. A late visual review found that subset embedding could omit glyph outlines even while text extraction passed. Added Korean glyph pixel-comparison regression; passed. Re-tested Korean UI edit/save/reopen and independently rendered the saved PDF; adjacent cell/line content remained visible and the edited text displayed correctly.

- [blocked] Final native visual font-fallback check was not performed because Windows-MCP reported ongoing USER_CONTROL. The test app was left running instead of interrupting user input. Browser save/reopen visuals and independent PDF rendering passed; native font-load warnings remain documented. An idle desktop is needed for the remaining native visual check.

## Unpackaged UI revisions after the installer build

These changes are in source and the local `out` build only. No new installer was generated, per user instruction. The earlier 1.0.2 installer does NOT contain these later revisions.

- Splash card: company mark reduced; title, initialization label and progress bar contained within the green card. Preserved original source graphic; dynamic progress still uses engine warmup.
- Sidebar: explicit 1/2/3 columns, default 2 for new preferences, 72px small size, responsive cell sizing, persisted column choice, Ctrl/Shift selection and grid-aware drop targeting. Scoped CSS specificity ensures app-wide list styles cannot override the chosen columns; control rows wrap on narrow panels.
- Document close: visible workspace-bar button, File ribbon and Ctrl+W share Save / Discard / Cancel flow. A clean file closes directly. Save cancellation or failure leaves the document open. Close resets document/search/history/tool state and PDF caches, returning to the welcome screen and Home ribbon without rerunning app initialization.
- Save operation now returns a real success boolean, checks false write results, coalesces concurrent saves, and preserves edits made during asynchronous save. It will not update a different replacement document.
- Verification: direct TypeScript check and local electron-vite build passed. `scripts/document-close-regression.cjs` covers success, cancellation, failures, concurrent edits/saves, replacement-document protection and reset, with IPC/encoding deliberately mocked. Browser preview with an 18-page PDF verified 1/2/3 columns without horizontal overflow, Ctrl/Shift selection, clean close, Cancel, Ctrl+W prompt, Discard, Save-and-close download, reopening and Home ribbon return. Downloaded PDF independently parsed as 18 pages with the saved first-page 90-degree rotation. These are browser UI/local logic checks, not a new installer/native OS save-dialog validation.

### Centralized display options (unpackaged)
- Added always-available Options dialog for theme, selection color/custom color, thumbnail size/columns and sidebar width. Changes are staged until Save; Cancel/Escape leaves saved preferences unchanged. Defaults reset the draft only.
- Sidebar controls reduced to size/column quick buttons and one page-selection menu/count. Removed its color palette, color input and precise size slider.
- Moved column preference to shared store, preserving the existing localStorage key/value and synchronizing quick controls with Options. Display settings never set PDF dirty state.
- TypeScript/build, options state persistence regression and existing close regression passed. Browser preview checked Cancel, Save/reload persistence, saved options updating a live 18-page document, 2/3-column synchronization, even-page selection (9/18), absence of PDF modified state, and restored effective original display preferences after testing.
- Installer not generated. PDF association configuration was inspected only; Windows defaults/registry were not changed. Association registration at installation does not itself guarantee Windows default-app selection.

### Removed masking tool (unpackaged)
- Removed both masking ribbon buttons, the shared annotation/tool kind, status/list labels, drag preview, overlay rendering and both PDF save branches. Other annotation tools remain available.
- Limited drag creation to actual drawing tools. Unsupported legacy annotation payloads now stop saving instead of silently disappearing; already-baked PDF graphics/text are not removed.
- TypeScript and production build passed. New annotation-tools-regression.cjs verified unchanged original PDF content streams (including an existing black rectangle), saving all five remaining annotation kinds in both save paths, and rejection of removed-kind payloads. Options and document-close regressions passed.
- Browser preview verified both ribbons have no masking entry and a highlight drag still creates an annotation. Installer not generated or distributed. Native font compatibility and final installer/file-association checks remain separate release gates.
