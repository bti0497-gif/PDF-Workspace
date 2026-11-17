using Microsoft.Win32;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Workspace.Shell.Startup;

public interface IStartupFilePicker { string? PickPdf(Window owner); }
public sealed class StartupFilePicker : IStartupFilePicker
{
    public string? PickPdf(Window owner)
    {
        var dialog = new OpenFileDialog { Title = "PDF 파일 불러오기", Filter = "PDF 문서 (*.pdf)|*.pdf", DefaultExt = ".pdf", CheckFileExists = true, CheckPathExists = true, Multiselect = false };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}

/// <summary>Call after startup checks, with the visible main window as owner. No document decoding or synthetic sample state.</summary>
public sealed class StartupOpenWindow : Window
{
    public StartupOpenModel Model { get; }
    public string? SelectedPath => Model.SelectedPath;
    private readonly IStartupFilePicker picker;
    internal Grid Panel { get; } = new() { Margin = new Thickness(24) };
    internal Button BrowseButton { get; } = new() { Content = "파일 불러오기 (_O)", MinHeight = 42, Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
    internal ListBox RecentList { get; } = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    internal Button CancelButton { get; } = new() { Content = "취소", IsCancel = true, MinWidth = 92, MinHeight = 34, Padding = new Thickness(16, 6, 16, 6) };
    internal TextBlock Status { get; } = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock empty = new() { Text = "최근에 연 PDF가 없습니다.\n왼쪽에서 파일을 불러와 시작하세요.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(95, 106, 120)), Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center };
    private bool showingModal;

    public StartupOpenWindow(Window owner, IRecentFilesStore? recents = null, IStartupFilePicker? filePicker = null, IStartupFileProbe? fileProbe = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
        Model = new(recents ?? new JsonRecentFilesStore(), fileProbe ?? new StartupFileProbe());
        picker = filePicker ?? new StartupFilePicker();
        Title = "PDF 열기 · 더존 PDF";
        Width = 850; Height = 540; MinWidth = 660; MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; ResizeMode = ResizeMode.CanResize; WindowStyle = WindowStyle.SingleBorderWindow;
        FontFamily = new FontFamily("Malgun Gothic"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 249));
        Content = Panel;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(new TextBlock { Text = "PDF 파일 열기", FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(34, 48, 66)) });
        Panel.Children.Add(heading);
        var columns = new Grid(); Grid.SetRow(columns, 1); Panel.Children.Add(columns);
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(0.85, GridUnitType.Star), MinWidth = 210 });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 300 });
        var left = new StackPanel { Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = "내 PC의 PDF", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        left.Children.Add(BrowseButton);
        var leftCard = Card(left); columns.Children.Add(leftCard);
        var right = new Grid(); right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        right.Children.Add(new TextBlock { Text = "최근 파일", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(16, 14, 16, 10) });
        var listArea = new Grid(); Grid.SetRow(listArea, 1); right.Children.Add(listArea); listArea.Children.Add(RecentList); listArea.Children.Add(empty);
        var rightCard = Card(right); Grid.SetColumn(rightCard, 2); columns.Children.Add(rightCard);
        Grid.SetRow(Status, 2); Panel.Children.Add(Status);
        AutomationProperties.SetLiveSetting(Status, System.Windows.Automation.AutomationLiveSetting.Polite);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(CancelButton); Grid.SetRow(footer, 3); Panel.Children.Add(footer);
        AutomationProperties.SetName(BrowseButton, "PDF 파일 불러오기"); AutomationProperties.SetName(RecentList, "최근에 연 실제 PDF 파일");
        ScrollViewer.SetHorizontalScrollBarVisibility(RecentList, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(RecentList, ScrollBarVisibility.Auto);
        BrowseButton.Click += (_, _) => Browse();
        CancelButton.Click += (_, _) => { if (showingModal) DialogResult = false; else Close(); };
        RecentList.PreviewKeyDown += (_, e) =>
        {
            if (RecentList.SelectedItem is not ListBoxItem { Tag: RecentFileEntry entry }) return;
            if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button) { SelectPath(entry.Path); e.Handled = true; }
            else if (e.Key == Key.Delete) { RemoveEntry(entry.Path); e.Handled = true; }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { Browse(); e.Handled = true; } };
        ContentRendered += (_, _) => BrowseButton.Focus();
        RefreshList();
    }
    private static Border Card(UIElement child) => new() { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(218, 225, 232)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = child };
    public string? ShowModal()
    {
        showingModal = true;
        try { return ShowDialog() == true ? SelectedPath : null; }
        finally { showingModal = false; }
    }
    internal void Browse()
    {
        try { var path = picker.PickPdf(this); if (path is not null) SelectPath(path); }
        catch (Exception ex) when (JsonRecentFilesStore.IsStorageFailure(ex)) { Status.Text = "파일 선택 창을 열지 못했습니다. 다시 시도해 주세요."; }
    }
    internal bool SelectPath(string path)
    {
        var success = Model.TrySelect(path);
        Status.Text = Model.Message ?? "";
        if (success && showingModal) DialogResult = true;
        return success;
    }
    internal void RemoveEntry(string path)
    {
        var index = RecentList.SelectedIndex;
        Model.Remove(path); RefreshList();
        if (RecentList.Items.Count > 0) { RecentList.SelectedIndex = Math.Clamp(index, 0, RecentList.Items.Count - 1); ((ListBoxItem)RecentList.SelectedItem).Focus(); }
        else BrowseButton.Focus();
    }
    internal void RefreshList()
    {
        RecentList.Items.Clear();
        foreach (var entry in Model.Snapshot.Entries)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(entry.Path), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock { Text = entry.Path, FontSize = 11, Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            label.Children.Add(new TextBlock { Text = entry.LastOpenedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), FontSize = 11, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0) });
            var open = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10), Margin = new Thickness(2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = entry.Path };
            AutomationProperties.SetName(open, $"최근 파일 열기: {entry.Path}");
            var remove = new Button { Content = "제거", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = "최근 목록에서만 제거 (실제 파일 유지)" };
            AutomationProperties.SetName(remove, $"최근 목록에서 제거: {entry.Path}. 실제 파일은 유지됩니다.");
            open.Click += (_, _) => SelectPath(entry.Path); remove.Click += (_, _) => RemoveEntry(entry.Path);
            row.Children.Add(open); Grid.SetColumn(remove, 1); row.Children.Add(remove);
            RecentList.Items.Add(new ListBoxItem { Content = row, Tag = entry, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Margin = new Thickness(0, 1, 0, 1) });
        }
        empty.Visibility = RecentList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = Model.Message ?? "";
    }
}


