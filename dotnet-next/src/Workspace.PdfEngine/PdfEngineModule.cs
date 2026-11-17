using Workspace.Contracts;

namespace Workspace.PdfEngine;

/// <summary>Parameterless entry point for the isolated, explicit module loader.
/// The shell supplies the locally verified dependency path; every session also enforces the CLI hash pin.</summary>
public sealed class PdfEngineModule : IWorkspaceDocumentModule
{
    public ModuleDescriptor Descriptor { get; } = new("pdfengine", "PDF 문서 엔진", new Version(1, 0, 1), ModuleApi.Version);
    public IReadOnlyList<IWorkspaceCommand> Commands { get; } = Array.Empty<IWorkspaceCommand>();
    public IWorkspaceHost CreateHost(string verifiedEnginePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedEnginePath);
        return new PdfDocumentService(verifiedEnginePath);
    }
}
