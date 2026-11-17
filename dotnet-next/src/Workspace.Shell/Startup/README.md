# Startup open modal

New files only in `Workspace.Shell/Startup`; no App or MainWindow changes are included.

## Integration

```csharp
var recents = new JsonRecentFilesStore();
// The main window must already have its native handle / be shown.
var path = new StartupOpenWindow(mainWindow, recents).ShowModal();
if (path is not null)
{
    // Hand path to the real document-open command, using the usual error handling.
    // ONLY after successful actual opening/decoding:
    recents.RecordOpened(path);
}
```

Show the modal once after startup checks have completed. `Owner` is required, `ShowModal()` returns the selected fully qualified readable PDF path, or `null` for Cancel, Escape, or window close. Selection does not decode a PDF, claim document success, or update history. Record every successful actual open (including subsequent Ctrl+O) through the same store. Catch storage errors around `RecordOpened`: a history-write failure must not undo a successfully opened document.

Constructor:
`StartupOpenWindow(Window owner, IRecentFilesStore? recents = null, IStartupFilePicker? filePicker = null, IStartupFileProbe? fileProbe = null)`

Injectable seams:
- `IRecentFilesStore`: `Read()`, `RecordOpened(string path)`, `Remove(string path)`.
- `IStartupFilePicker.PickPdf(Window owner)`: returns selected path or null. Native default picker is owned by the startup modal.
- `IStartupFileProbe.Check(string path)`: `FileAccessResult` with accessibility and contextual error.
- `StartupOpenModel`: window-independent refresh, selection, removal and message state.

Default storage: `%LOCALAPPDATA%\Deojon\PdfWorkspace.Wpf\Startup\recent-files.json`. No legacy migration or fabricated entries. A constructor storage path override supports isolated tests or separate product variants. Max 20 entries, case-insensitive deduplication, most recent first. A sibling exclusive writer lock protects read-modify-write. Writes flush a same-directory unique temporary file, then atomically replace/move. Missing history is an empty first run. Corrupt, oversized, or unknown-version history is preserved and blocks changes rather than being overwritten. It can be manually archived/repaired later; ordinary PDF selection remains available. No automatic document deletion, quarantine or migration.

UI: approved existing simple WPF chrome/font/colors; resizable 850x540, minimum 660x430, left file picker and right actual recent history; single-click entry selection, keyboard Enter/Delete in the list, Ctrl+O, initial browse focus, cyclic Tab and Cancel/Escape. Missing/unreadable files stay in history until explicitly removed; error is contextual. No persistent security/experimental notices. Tooltips distinguish history-only removal. Caller must still handle access changes after path selection.

## Isolated checks

Run from the repository root; all generated output stays in the specified temporary folder (do not run with default output directories, to avoid nested generated sources under Shell):

```powershell
$build = '<absolute-temporary-directory>'
dotnet build dotnet-next/src/Workspace.Shell/Startup/Startup.Checks.csproj --configfile dotnet-next/NuGet.Config -p:BaseIntermediateOutputPath="$build\obj\" -p:OutputPath="$build\bin\"
& "$build\bin\Startup.Checks.exe" "$build\results"
```

33 checks passed, isolated build 0 warnings/errors. Coverage: real temporary files; readable/missing/directory/non-PDF/locked paths; persisted restart; deduplication/capacity; history-only deletion; corrupt/unknown schema preservation; concurrent writer protection; atomic temporary cleanup; default location; modal-owner/picker/focus/cancel configuration; actual entry button routed event; no false record-on-selection; and three offscreen layout renders (644x391, 834x501, 1184x721 client DIPs).

These checks create WPF handles and render content offscreen, without showing the normal desktop. They do not verify normal native modal interaction, actual file picker interaction, physical DPI, install/startup integration or PDF decoding. Test `.pdf` files are deliberate probe fixtures, not validated PDFs. The entire check code is behind `STARTUP_CHECKS`; the isolated test project does not add an application entry point to the normal shell build.
