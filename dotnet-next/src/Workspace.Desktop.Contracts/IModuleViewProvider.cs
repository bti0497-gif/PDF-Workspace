using System.Windows;
using Workspace.Contracts;
namespace Workspace.Desktop.Contracts;
// Presentation boundary: document and module runtime projects do not reference WPF.
public interface IModuleViewProvider
{
    FrameworkElement CreateView(Func<string,Task<CommandResult>> execute);
}
