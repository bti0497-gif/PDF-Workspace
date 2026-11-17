namespace Workspace.Contracts;

// A separate trusted domain-owner DLL supplies the shared host. The bootstrap
// verifies the CLI path before passing it; the implementation must also validate it.
// The shell owns and awaits IAsyncDisposable when the returned host implements it.
public interface IWorkspaceDocumentModule : IWorkspaceModule
{
    IWorkspaceHost CreateHost(string verifiedEnginePath);
}
