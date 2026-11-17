using Workspace.Contracts;
namespace Workspace.Runtime;

// Owned by the shell/domain adapter. Notifications occur on the publishing thread;
// UI adapters publish on their dispatcher. Publication is not a PDF mutation.
public sealed class WorkspaceState(Func<WorkspaceIntent, CancellationToken, Task<CommandResult>> dispatch,
    Func<Guid, long, int, int, CancellationToken, Task<byte[]>>? render = null) : IWorkspaceHost
{
    private DocumentSnapshot document = DocumentSnapshot.Empty;
    private readonly SemaphoreSlim execution = new(1, 1);
    public DocumentSnapshot Document => Volatile.Read(ref document);
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
    public event EventHandler<string>? NotificationFailed;
    public void Publish(DocumentSnapshot next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.Version <= Document.Version || next.PageCount < 0 || next.Version < 0 ||
            (next.DocumentId is null && (next.PageCount != 0 || next.CurrentPage != 0 || next.IsDirty)) ||
            (next.PageCount == 0 ? next.CurrentPage != 0 : next.CurrentPage < 1 || next.CurrentPage > next.PageCount) ||
            next.SelectedPages.Any(p => p < 1 || p > next.PageCount))
            throw new ArgumentException("Invalid or non-monotonic document snapshot.", nameof(next));
        var safe = next with { SelectedPages = Array.AsReadOnly(next.SelectedPages.Distinct().ToArray()) };
        Volatile.Write(ref document, safe);
        foreach (var subscriber in DocumentChanged?.GetInvocationList() ?? [])
            try { ((EventHandler<DocumentChangedEventArgs>)subscriber)(this, new(safe)); }
            catch (Exception ex) { try { NotificationFailed?.Invoke(this, ex.Message); } catch { /* reporting cannot roll back publication */ } }
    }
    public async Task<byte[]> RenderPageAsync(Guid documentId, long expectedContentVersion, int oneBasedPage, int dpi, CancellationToken cancellationToken = default)
    {
        void Validate()
        {
            var current = Document;
            if (current.DocumentId != documentId || current.ContentVersion != expectedContentVersion)
                throw new InvalidOperationException("Stale document rendering request.");
            if (oneBasedPage < 1 || oneBasedPage > current.PageCount || dpi < 24 || dpi > 300)
                throw new ArgumentOutOfRangeException(nameof(oneBasedPage), "Page or DPI outside allowed range (24..300).");
        }
        Validate();
        var renderer = render ?? throw new NotSupportedException("No domain renderer is connected.");
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await renderer(documentId, expectedContentVersion, oneBasedPage, dpi, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Validate();
        return bytes;
    }
    public async Task<CommandResult> DispatchAsync(WorkspaceIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.CommandId)) return new(false, "A command ID is required.");
        try
        {
            if (!await execution.WaitAsync(0, cancellationToken)) return new(false, "Another document command is active.");
        }
        catch (OperationCanceledException) { return new(false, "Cancelled."); }
        try
        {
            var current = Document;
            if ((intent.DocumentId is not null && intent.DocumentId != current.DocumentId) ||
                (intent.ExpectedVersion is not null && intent.ExpectedVersion != current.Version))
                return new(false, "The document changed. Refresh before retrying.");
            var safe = intent with { Pages = intent.Pages is null ? null : Array.AsReadOnly(intent.Pages.ToArray()) };
            return await dispatch(safe, cancellationToken);
        }
        catch (OperationCanceledException) { return new(false, "Cancelled."); }
        catch (Exception ex) { return new(false, "Command failed: " + ex.Message); }
        finally { execution.Release(); }
    }
}
