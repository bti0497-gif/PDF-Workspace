using System.Windows;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
namespace Workspace.SidebarModule;
public sealed class SidebarModule : IWorkspaceAreaModule
{
    private SidebarView? view;
    public ModuleDescriptor Descriptor { get; } = new("sidebar", "페이지 축소판", new Version(1,0,0), ModuleApi.Version);
    public IReadOnlyList<IWorkspaceCommand> Commands => Array.Empty<IWorkspaceCommand>();
    public string Region => WorkspaceRegions.Sidebar;
    public FrameworkElement CreateView(IWorkspaceHost host) { view?.Dispose(); return view = new SidebarView(host); }
    public void Dispose() { view?.Dispose(); view = null; }
}
