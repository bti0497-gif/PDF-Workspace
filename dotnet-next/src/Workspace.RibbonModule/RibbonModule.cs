using System.Windows;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
namespace Workspace.RibbonModule;
public sealed class RibbonModule : IWorkspaceAreaModule
{
    private RibbonView? view;
    public ModuleDescriptor Descriptor { get; } = new("ribbon", "리본", new Version(1, 0, 2), ModuleApi.Version);
    public string Region => WorkspaceRegions.Ribbon;
    public IReadOnlyList<IWorkspaceCommand> Commands => Array.Empty<IWorkspaceCommand>();
    public FrameworkElement CreateView(IWorkspaceHost host) { view?.Dispose(); return view = new RibbonView(host); }
    public void Dispose() { view?.Dispose(); view=null; }
}
