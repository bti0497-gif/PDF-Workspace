using System.Windows;
using System.Windows.Controls;
using Workspace.Contracts;
namespace Workspace.FoundationModule;
public partial class FoundationView : UserControl
{
    private readonly Func<string,Task<CommandResult>> execute;
    public FoundationView(Func<string,Task<CommandResult>> execute) { InitializeComponent(); this.execute = execute; }
    private async void RunCheck(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        try { ResultText.Text = (await execute("foundation.check")).Message; }
        finally { RunButton.IsEnabled = true; }
    }
}
