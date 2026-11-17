using System.Windows;
using Workspace.Contracts;
namespace Workspace.Desktop.Contracts;

public static class WorkspaceRegions
{
    public const string Ribbon = "ribbon";
    public const string Sidebar = "sidebar";
    public const string Workspace = "workspace";
}
// Create/dispose on the host UI thread. Host detaches the view BEFORE Dispose.
// Dispose must unsubscribe host events and stop timers/background work.
// WPF views are restart-only until a concrete module proves collectibility.
public interface IWorkspaceAreaModule : IWorkspaceModule, IDisposable
{
    string Region { get; }
    FrameworkElement CreateView(IWorkspaceHost host);
}
