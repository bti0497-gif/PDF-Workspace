using Workspace.Contracts;
namespace Workspace.Shell;
internal static class ProductBrand
{
    internal const string Name="더죤 PDF Workspace";
    internal static string DialogTitle(string context)=>$"{context} · {Name}";
    internal static string DocumentTitle(DocumentSnapshot document)=>document.DocumentId==null?Name:$"{document.DisplayName}{(document.IsDirty?" *":"")} · {Name}";
}
