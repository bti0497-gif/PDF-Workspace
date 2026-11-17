using System.Globalization;
using System.Text.Json;
using Workspace.Contracts;

namespace Workspace.PdfEngine;

/// <summary>Single document owner. All operations serialize. Subscribers run on the operation thread;
/// UI hosts must marshal publication to their dispatcher. No original reaches the CLI.</summary>
public sealed class PdfDocumentService : IWorkspaceHost, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly string? executable;
    private readonly Action<DocumentSnapshot>? publish;
    private readonly Dictionary<string, ReplacementApproval> pendingReplacements = new(StringComparer.Ordinal);
    private PdfDocumentSession? session;
    private bool disposed;
    private DocumentSnapshot document = DocumentSnapshot.Empty;
    public DocumentSnapshot Document => Volatile.Read(ref document);
    public string? SourcePath => session?.SourcePath;
    public bool CanUndo => session?.CanUndo == true;
    public bool CanRedo => session?.CanRedo == true;
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
    public PdfDocumentService(string? executablePath = null, Action<DocumentSnapshot>? publishState = null)
    { executable = executablePath; publish = publishState; }

    private void Publish(bool contentChanged = false, int? current = null, IReadOnlyList<int>? selection = null, Guid? identity = null)
    {
        var old = document;
        var count = session?.PageCount ?? 0;
        var page = count == 0 ? 0 : Math.Clamp(current ?? old.CurrentPage, 1, count);
        var selected = count == 0 ? Array.Empty<int>() : (selection ?? old.SelectedPages).Where(p => p >= 1 && p <= count).Distinct().ToArray();
        var next = new DocumentSnapshot(session == null ? null : identity ?? old.DocumentId,
            session == null ? null : Path.GetFileName(session.SourcePath), count, page,
            Array.AsReadOnly(selected), session?.IsDirty ?? false, old.Version + 1,
            old.ContentVersion + (contentChanged ? 1 : 0),
            session?.CanUndo ?? false, session?.CanRedo ?? false);
        Volatile.Write(ref document, next);
        pendingReplacements.Clear();
        // Notification failure cannot turn a committed file/state change into a failed operation.
        try { publish?.Invoke(next); } catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
        if (DocumentChanged != null)
            foreach (EventHandler<DocumentChangedEventArgs> handler in DocumentChanged.GetInvocationList())
                try { handler(this, new(next)); } catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
    }
    private async Task<T> Locked<T>(Func<CancellationToken, Task<T>> work, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(disposed, this); return await work(linked.Token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private Task Locked(Func<CancellationToken, Task> work, CancellationToken token) => Locked(async ct => { await work(ct).ConfigureAwait(false); return true; }, token);
    private PdfDocumentSession Active() => session ?? throw new InvalidOperationException("No document is open.");
    private int[] Pages(IReadOnlyList<int> pages, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var copy = pages.ToArray(); var count = Active().PageCount;
        if ((!allowEmpty && copy.Length == 0) || copy.Any(p => p < 1 || p > count) || copy.Distinct().Count() != copy.Length)
            throw new ArgumentException("Use distinct one-based pages within the document.");
        return copy;
    }
    private async Task OpenCore(string path, CancellationToken ct)
    {
        if (session?.IsDirty == true) throw new InvalidOperationException("Save or explicitly discard the current document first.");
        var next = await PdfDocumentSession.OpenAsync(path, ct, executable).ConfigureAwait(false);
        var old = session; session = next;
        Publish(true, 1, [1], Guid.NewGuid());
        if (old != null) await old.DisposeAsync();
    }
    public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Locked(ct => OpenCore(path, ct), cancellationToken);
    private void SelectCore(IReadOnlyList<int> pages, int? current)
    {
        var selection = Pages(pages, true);
        if (current != null) Pages([current.Value]);
        Publish(current: current, selection: selection);
    }
    public Task SelectAsync(IReadOnlyList<int> pages, int? currentPage = null, CancellationToken cancellationToken = default) => Locked(_ => { SelectCore(pages, currentPage); return Task.CompletedTask; }, cancellationToken);
    public Task NavigateAsync(int page, CancellationToken cancellationToken = default) => Locked(_ => { Pages([page]); Publish(current: page); return Task.CompletedTask; }, cancellationToken);
    public Task<byte[]> RenderPageAsync(Guid documentId, long contentVersion, int oneBasedPage, int dpi, CancellationToken cancellationToken = default) => Locked(ct =>
    {
        if (Document.DocumentId != documentId || Document.ContentVersion != contentVersion) throw new InvalidOperationException("Stale document render request.");
        return Active().Viewer.RenderAsync(oneBasedPage, ct, dpi);
    }, cancellationToken);
    public Task<byte[]> RenderPageAsync(int oneBasedPage, int dpi = 96, CancellationToken cancellationToken = default) => Locked(ct => Active().Viewer.RenderAsync(oneBasedPage, ct, dpi), cancellationToken);
    private async Task ModifyCore(PdfOperationKind kind, IReadOnlyList<int>? pages, string[]? additions, int degrees, CancellationToken ct)
    {
        var selected = pages == null ? null : Pages(pages);
        await Active().ModifyAsync(kind, selected, additions, ct, degrees).ConfigureAwait(false);
        Publish(true, selection: kind is PdfOperationKind.Delete or PdfOperationKind.Reorder ? Array.Empty<int>() : selected);
    }
    public Task RotateAsync(IReadOnlyList<int> pages, int degrees = 90, CancellationToken cancellationToken = default) => Locked(ct => ModifyCore(PdfOperationKind.Rotate, pages, null, degrees, ct), cancellationToken);
    public Task DeleteAsync(IReadOnlyList<int> pages, CancellationToken cancellationToken = default) => Locked(ct => ModifyCore(PdfOperationKind.Delete, pages, null, 90, ct), cancellationToken);
    public Task ReorderAsync(IReadOnlyList<int> permutation, CancellationToken cancellationToken = default) => Locked(ct => ModifyCore(PdfOperationKind.Reorder, permutation, null, 90, ct), cancellationToken);
    public Task MergeAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) => Locked(ct => ModifyCore(PdfOperationKind.Merge, null, paths.ToArray(), 90, ct), cancellationToken);
    private Task<PdfOperationResult> ExtractCore(IReadOnlyList<int> pages, string destination, CancellationToken ct) => new PdfCraftOperationsService(executable ?? ReadOnlyPdfSession.DependencyPath()).ExecuteAsync(new(PdfOperationKind.Extract, [Active().CurrentPath], destination, Pages(pages)), ct);
    public Task<PdfOperationResult> ExtractAsync(IReadOnlyList<int> pages, string destination, CancellationToken cancellationToken = default) => Locked(ct => ExtractCore(pages, destination, ct), cancellationToken);
    private async Task UndoCore(bool redo, CancellationToken ct)
    {
        var active = Active(); if (redo ? !active.CanRedo : !active.CanUndo) throw new InvalidOperationException(redo ? "Nothing to redo." : "Nothing to undo.");
        await active.UndoAsync(redo, ct).ConfigureAwait(false); Publish(true);
    }
    public Task UndoAsync(CancellationToken cancellationToken = default) => Locked(ct => UndoCore(false, ct), cancellationToken);
    public Task RedoAsync(CancellationToken cancellationToken = default) => Locked(ct => UndoCore(true, ct), cancellationToken);
    private async Task SaveCore(string path, bool approved, string? fingerprint, CancellationToken ct)
    { await Active().SaveAsync(path, approved, ct, fingerprint).ConfigureAwait(false); Publish(); }
    public Task SaveAsync(CancellationToken cancellationToken = default) => Locked(ct => SaveCore(Active().SourcePath, false, null, ct), cancellationToken);
    public Task SaveAsAsync(string destination, CancellationToken cancellationToken = default) => Locked(ct => SaveCore(destination, false, null, ct), cancellationToken);

    // Preparation is NOT consent. UI must show Destination, obtain explicit affirmative consent,
    // then pass the SAME opaque candidate. Token binds identity/version and target content hash.
    public sealed class ReplacementApproval
    {
        internal Guid DocumentId { get; }
        internal long Version { get; }
        internal string Fingerprint { get; }
        internal PdfDocumentService Owner { get; }
        public string Destination { get; }
        internal ReplacementApproval(PdfDocumentService owner, Guid id, long version, string destination, string fingerprint)
        { Owner = owner; DocumentId = id; Version = version; Destination = destination; Fingerprint = fingerprint; }
    }
    public Task<ReplacementApproval> PrepareReplacementAsync(string destination, CancellationToken cancellationToken = default) => Locked(ct => PrepareReplacementCore(destination, ct), cancellationToken);
    private async Task<ReplacementApproval> PrepareReplacementCore(string destination, CancellationToken ct)
    {
        Active(); var path = ReadOnlyPdfSession.ValidateLocalPath(destination);
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A PDF filename is required.");
        return new ReplacementApproval(this, Document.DocumentId!.Value, Document.Version, path, await PdfDocumentSession.FingerprintAsync(path, ct));
    }
    public Task SaveWithReplacementAsync(ReplacementApproval approval, bool replacementApproved, CancellationToken cancellationToken = default) => Locked(ct =>
    {
        ArgumentNullException.ThrowIfNull(approval);
        if (!replacementApproved) throw new InvalidOperationException("Explicit replacement consent is required.");
        if (approval.Owner != this || approval.DocumentId != Document.DocumentId || approval.Version != Document.Version) throw new InvalidOperationException("Replacement approval is stale.");
        return SaveCore(approval.Destination, true, approval.Fingerprint, ct);
    }, cancellationToken);
    private async Task CloseCore(bool discard)
    {
        if (session == null) return;
        if (session.IsDirty && !discard) throw new InvalidOperationException("Unsaved changes require save or explicit discard.");
        var old = session; session = null; Publish(true); await old.DisposeAsync();
    }
    public Task CloseAsync(bool discardChanges = false, CancellationToken cancellationToken = default) => Locked(_ => CloseCore(discardChanges), cancellationToken);

    public async Task<CommandResult> DispatchAsync(WorkspaceIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        try { return await Locked(async ct =>
        {
            if (intent.CommandId != "document.open" && (intent.DocumentId == null || intent.DocumentId != Document.DocumentId || intent.ExpectedVersion != Document.Version))
                throw new InvalidOperationException("Stale or missing document identity/version.");
            if (intent.CommandId == "document.open" && ((intent.DocumentId != null && intent.DocumentId != Document.DocumentId) || (intent.ExpectedVersion != null && intent.ExpectedVersion != Document.Version)))
                throw new InvalidOperationException("Stale open request.");
            var pages = intent.Pages ?? Document.SelectedPages;
            string Argument() => !string.IsNullOrWhiteSpace(intent.Argument) ? intent.Argument : throw new ArgumentException("This command requires a path/argument supplied by the host.");
            switch (intent.CommandId)
            {
                case "document.open": await OpenCore(Argument(), ct); break;
                case "document.select": SelectCore(pages, intent.Argument == null ? null : int.Parse(intent.Argument, CultureInfo.InvariantCulture)); break;
                case "document.navigate": if (pages.Count != 1) throw new ArgumentException("Navigation needs one page."); Pages(pages); Publish(current: pages[0]); break;
                case "pages.rotate": await ModifyCore(PdfOperationKind.Rotate, pages, null, intent.Argument == null ? 90 : int.Parse(intent.Argument, CultureInfo.InvariantCulture), ct); break;
                case "pages.delete": await ModifyCore(PdfOperationKind.Delete, pages, null, 90, ct); break;
                case "pages.reorder":
                    if (intent.Argument != null)
                    {
                        var selected = Pages(pages).ToHashSet(); var slot = int.Parse(intent.Argument, CultureInfo.InvariantCulture);
                        if (slot < 0 || slot > Active().PageCount) throw new ArgumentOutOfRangeException(nameof(intent.Argument));
                        var order = Enumerable.Range(1, Active().PageCount).ToList(); var moving = order.Where(selected.Contains).ToArray();
                        var adjusted = order.Take(slot).Count(p => !selected.Contains(p)); order.RemoveAll(selected.Contains); order.InsertRange(adjusted, moving); pages = order;
                    }
                    await ModifyCore(PdfOperationKind.Reorder, pages, null, 90, ct); break;
                case "pages.extract": await ExtractCore(pages, Argument(), ct); break;
                case "document.merge": await ModifyCore(PdfOperationKind.Merge, null, JsonSerializer.Deserialize<string[]>(Argument()) ?? throw new ArgumentException("Merge paths required."), 90, ct); break;
                case "document.undo": await UndoCore(false, ct); break;
                case "document.redo": await UndoCore(true, ct); break;
                case "document.save": await SaveCore(Active().SourcePath, false, null, ct); break;
                case "document.saveAs": await SaveCore(Argument(), false, null, ct); break;
                case "document.save.prepare":
                case "document.saveAs.prepare":
                    var candidate = await PrepareReplacementCore(intent.Argument ?? Active().SourcePath, ct);
                    if (pendingReplacements.Count >= 8) pendingReplacements.Clear();
                    var candidateToken = Guid.NewGuid().ToString("N"); pendingReplacements.Add(candidateToken, candidate);
                    return new CommandResult(true, JsonSerializer.Serialize(new { Token = candidateToken, candidate.Destination }));
                case "document.save.confirmed":
                case "document.saveAs.confirmed":
                    if (!pendingReplacements.Remove(Argument(), out var approvedCandidate)
                        || approvedCandidate.DocumentId != Document.DocumentId || approvedCandidate.Version != Document.Version)
                        throw new InvalidOperationException("Replacement consent token is missing, expired or stale.");
                    await SaveCore(approvedCandidate.Destination, true, approvedCandidate.Fingerprint, ct); break;
                case "document.close": await CloseCore(false); break;
                case "document.close.discard": await CloseCore(true); break;
                default: return new CommandResult(false, "Unsupported command: " + intent.CommandId);
            }
            return new CommandResult(true, "Completed: " + intent.CommandId);
        }, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or PdfOperationException or TimeoutException or UnauthorizedAccessException or JsonException or FormatException or OverflowException)
        { return new CommandResult(false, ex.Message); }
    }
    /// <summary>Final owner teardown discards private revisions. For user close use CloseAsync first.</summary>
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); await gate.WaitAsync().ConfigureAwait(false);
        try { if (disposed) return; disposed = true; await CloseCore(true); }
        finally { gate.Release(); }
    }
}
