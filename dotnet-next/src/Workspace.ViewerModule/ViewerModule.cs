using System.Windows;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
namespace Workspace.ViewerModule;

public sealed class ViewerModule : IWorkspaceAreaModule
{
    private PdfViewer? viewer;
    public ModuleDescriptor Descriptor { get; } = new("viewer", "PDF 보기", new Version(0,1,0), ModuleApi.Version);
    public IReadOnlyList<IWorkspaceCommand> Commands { get; } = Array.Empty<IWorkspaceCommand>();
    public string Region => WorkspaceRegions.Workspace;
    public FrameworkElement CreateView(IWorkspaceHost host) { viewer?.Dispose(); return viewer = new PdfViewer(host); }
    public void Dispose() { viewer?.Dispose(); viewer = null; }
}
