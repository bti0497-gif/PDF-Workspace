using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Workspace.Shell;
public partial class MainWindow : Window
{
    public MainWindow(Workspace.Shell.Startup.IRecentFilesStore? recents = null)
    {
        recentFiles=recents ?? new Workspace.Shell.Startup.JsonRecentFilesStore();
        InitializeComponent(); InitializeModularChrome(); Closing += (_,e)=>GuardModularClosing(e);
        SizeChanged += (_,e)=>RibbonScroller.MaxHeight=e.NewSize.Height<600?100:174;
        InputBindings.Add(new KeyBinding(new UiCommand(()=>{ RibbonPanel.Visibility=RibbonPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible; }),Key.F1,ModifierKeys.Control));
    }
    private static void ApplyRealTabAppearance(Button button,bool selected)
    {
        button.Background=selected?new SolidColorBrush(Color.FromRgb(246,246,246)):Brushes.Transparent;
        button.Foreground=selected?new SolidColorBrush(Color.FromRgb(156,71,46)):Brushes.White;
    }
    private sealed class UiCommand(Action run):ICommand
    {
        public bool CanExecute(object? parameter)=>true;
        public void Execute(object? parameter)=>run();
        public event EventHandler? CanExecuteChanged {add{} remove{}}
    }
}


