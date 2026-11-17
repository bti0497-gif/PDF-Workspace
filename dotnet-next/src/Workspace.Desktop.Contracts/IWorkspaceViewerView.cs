namespace Workspace.Desktop.Contracts;

/// <summary>View-only commands delivered by the shell to the workspace region.</summary>
public interface IWorkspaceViewerView
{
    bool ApplyViewCommand(string commandId);
}
