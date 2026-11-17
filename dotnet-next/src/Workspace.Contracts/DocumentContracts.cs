namespace Workspace.Contracts;

// Pages are one-based. An empty document has null identity, page/current counts zero.
public sealed record DocumentSnapshot(Guid? DocumentId, string? DisplayName, int PageCount,
    int CurrentPage, IReadOnlyList<int> SelectedPages, bool IsDirty, long Version, long ContentVersion = 0,
    bool CanUndo = false, bool CanRedo = false)
{
    public static DocumentSnapshot Empty { get; } = new(null, null, 0, 0, Array.Empty<int>(), false, 0);
}
public sealed class DocumentChangedEventArgs(DocumentSnapshot document) : EventArgs
{
    public DocumentSnapshot Document { get; } = document;
}
public sealed record WorkspaceIntent(string CommandId, Guid? DocumentId = null,
    long? ExpectedVersion = null, IReadOnlyList<int>? Pages = null, string? Argument = null);

// Views receive this read/intent boundary, never a document writer or PDF engine.
public interface IWorkspaceHost
{
    DocumentSnapshot Document { get; }
    event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
    Task<CommandResult> DispatchAsync(WorkspaceIntent intent, CancellationToken cancellationToken = default);
    Task<byte[]> RenderPageAsync(Guid documentId, long expectedContentVersion, int oneBasedPage, int dpi, CancellationToken cancellationToken = default);
}
