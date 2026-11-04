using System.Windows.Input;
namespace Workspace.Shell.Presentation;

public sealed class UiCommand(Action run,Func<bool>? canRun=null) : ICommand
{
    public bool CanExecute(object? parameter) => canRun?.Invoke() ?? true;
    public void Execute(object? parameter) { if(CanExecute(parameter)) run(); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
