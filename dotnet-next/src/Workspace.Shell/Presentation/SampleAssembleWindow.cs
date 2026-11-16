using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Workspace.Shell.Presentation;
internal sealed class SampleAssembleWindow:Window
{
 internal Grid Panel {get;}=new(){Margin=new Thickness(20),Background=new SolidColorBrush(Color.FromRgb(245,247,249))};
 internal ListBox Available {get;}=new(){MinHeight=70};
 internal ListBox Ordered {get;}=new(){MinHeight=70};
 internal TextBox Ranges {get;}=new(){MinHeight=32,VerticalContentAlignment=VerticalAlignment.Center,MaxLength=4096};
 internal TextBlock Message {get;}=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 internal Button Accept {get;}=new(){Content="새 샘플 탭 만들기",MinWidth=140,Height=34,IsDefault=true};
 internal Button Cancel {get;}=new(){Content="취소",MinWidth=80,Height=34,IsCancel=true,Margin=new Thickness(8,0,0,0)};
 internal ObservableCollection<AssembleSource> Order {get;}=new();
 internal Func<bool?> ShowModal {get;}
 internal SampleAssembleWindow(AssemblePlan plan)
 {
  Title=plan.Split?"샘플 분할 · 메모리 전용":"샘플 병합 · 메모리 전용";
  Width=760;Height=560;MinWidth=560;MinHeight=420;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  foreach(var list in new[]{Available,Ordered}) { list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);list.HorizontalContentAlignment=HorizontalAlignment.Stretch;var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(TextBlock.MarginProperty,new Thickness(4));list.ItemTemplate=new DataTemplate{VisualTree=text}; }
  FontFamily=new FontFamily("Malgun Gothic");FontSize=13;Background=new SolidColorBrush(Color.FromRgb(245,247,249));Content=Panel;ShowModal=ShowDialog;
  Panel.RowDefinitions.Add(new(){Height=GridLength.Auto});Panel.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Panel.RowDefinitions.Add(new(){Height=GridLength.Auto});Panel.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var header=new StackPanel();header.Children.Add(new TextBlock{Text=Title,FontSize=21,FontWeight=FontWeights.SemiBold});header.Children.Add(new TextBlock{Text="원본·선택·기록 유지 · 결과는 독립된 미저장 탭\n실제 PDF/파일 없음 · 이번 실행 메모리만 사용 · 앱 종료 시 사라짐",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});Panel.Children.Add(header);
  if(plan.Split)
  {
   var body=new StackPanel();body.Children.Add(new TextBlock{Text=plan.Sources[0].ToString(),TextWrapping=TextWrapping.Wrap});body.Children.Add(new TextBlock{Text="출력 탭별 범위: 쉼표는 쪽, 세미콜론은 새 탭\n예: 1,3; 2 · 각 쪽 정확히 한 번 · 각 탭 내부는 원본 쪽 순서",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,8)});body.Children.Add(Ranges);Ranges.Text=plan.InitialRanges;Grid.SetRow(body,1);Panel.Children.Add(body);
  }
  else
  {
   var body=new Grid();body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new(){Width=GridLength.Auto});body.ColumnDefinitions.Add(new());
   var left=new DockPanel();var label=new TextBlock{Text="열린 샘플 · 선택 후 추가",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(label,Dock.Top);left.Children.Add(label);Available.ItemsSource=plan.Sources;left.Children.Add(Available);body.Children.Add(left);
   var actions=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8)};Grid.SetColumn(actions,1);body.Children.Add(actions);
   void Add(string text,Action action,Func<bool> can){var b=new Button{Content=text,MinWidth=60,Height=30,Margin=new Thickness(0,3,0,3),Command=new UiCommand(()=>{action();Validate();},can)};actions.Children.Add(b);}
   Add("추가 →",()=>{if(Available.SelectedItem is AssembleSource s)Order.Add(s);},()=>Available.SelectedItem is AssembleSource s&&!Order.Contains(s));
   Add("제거",()=>{if(Ordered.SelectedItem is AssembleSource s)Order.Remove(s);},()=>Ordered.SelectedItem!=null);
   Add("위로 ↑",()=>Move(-1),()=>Ordered.SelectedIndex>0);Add("아래로 ↓",()=>Move(1),()=>Ordered.SelectedIndex>=0&&Ordered.SelectedIndex<Order.Count-1);
   var right=new DockPanel();var orderLabel=new TextBlock{Text="출력 순서: 위 → 아래\n각 원본 내부: 1쪽 → 마지막 쪽",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(orderLabel,Dock.Top);right.Children.Add(orderLabel);Ordered.ItemsSource=Order;right.Children.Add(Ordered);Grid.SetColumn(right,2);body.Children.Add(right);Grid.SetRow(body,1);Panel.Children.Add(body);
   void Refresh(){foreach(var b in actions.Children.OfType<Button>())((UiCommand)b.Command).Refresh();Validate();}
   Available.SelectionChanged+=(_,_)=>Refresh();Ordered.SelectionChanged+=(_,_)=>Refresh();Order.CollectionChanged+=(_,_)=>Refresh();
   Ordered.PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Alt&&e.Key is Key.Up or Key.Down){Move(e.Key==Key.Up?-1:1);e.Handled=true;}};
  }
  Grid.SetRow(Message,2);Panel.Children.Add(Message);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(Accept);buttons.Children.Add(Cancel);Grid.SetRow(buttons,3);Panel.Children.Add(buttons);
  void Validate(){var valid=plan.Split?PreviewWorkspace.TryPartition(Ranges.Text,plan.Sources[0].Pages,out _,out _):Order.Count>=2;Accept.IsEnabled=valid;Message.Text=valid?(plan.Split?"모든 쪽을 한 번씩 배정합니다. 입력된 그룹 순서로 새 탭 생성":"출력 "+Order.Sum(s=>s.Pages)+"쪽 · 위/아래 버튼 또는 Alt+↑/↓로 순서 변경"):(plan.Split?"2개 이상의 그룹에 모든 쪽을 중복·누락 없이 배정하세요.":"서로 다른 원본을 2개 이상 추가하세요.");}
  Ranges.TextChanged+=(_,_)=>Validate();Accept.Click+=(_,_)=>{Validate();if(!Accept.IsEnabled)return;if(!plan.Commit(Order.Select(s=>s.Key).ToArray(),Ranges.Text)){Message.Text="원본 변경 또는 열린 탭 한도(32개)로 생성하지 못했습니다. 취소 후 다시 여세요.";Accept.IsEnabled=false;return;}Close();};
  Cancel.Click+=(_,_)=>Close();Closed+=(_,_)=>plan.Cancel();KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);Validate();
 }
 internal void Move(int delta){var i=Ordered.SelectedIndex;var target=i+delta;if(i<0||target<0||target>=Order.Count)return;Order.Move(i,target);Ordered.SelectedIndex=target;}
}
