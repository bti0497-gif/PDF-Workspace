using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Workspace.Shell.Presentation;
internal sealed class SampleFileWindow : Window
{
 internal Grid Panel { get; } = new(){Margin=new Thickness(22)};
 internal ListBox Catalog {get;} = new(){Margin=new Thickness(0,12,0,12)};
 internal TextBox NameInput {get;} = new(){Height=32,VerticalContentAlignment=VerticalAlignment.Center,MaxLength=80};
 internal TextBlock Error {get;} = new(){Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 internal Button Accept {get;} = new(){MinWidth=100,Height=34,Margin=new Thickness(8,0,0,0),IsDefault=true};
 internal Button Cancel {get;} = new(){Content="취소",MinWidth=90,Height=34,IsCancel=true};
 internal string? Result {get;private set;}
 internal Func<bool?> ShowModal {get;}
 internal SampleFileWindow(IReadOnlyList<VirtualSampleEntry> entries,string? suggested=null,IReadOnlyList<string>? occupied=null)
 {
  var saving=suggested!=null;Title=saving?"다른 이름으로 샘플 저장 · 메모리 전용":"샘플 열기 · 가상 목록";
  Width=720;Height=500;MinWidth=560;MinHeight=400;WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  FontFamily=new FontFamily("Malgun Gothic");FontSize=13;Background=new SolidColorBrush(Color.FromRgb(245,247,249));Content=Panel;ShowModal=ShowDialog;
  Panel.RowDefinitions.Add(new(){Height=GridLength.Auto});Panel.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Panel.RowDefinitions.Add(new(){Height=GridLength.Auto});Panel.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var header=new StackPanel();header.Children.Add(new TextBlock{Text=saving?"이름을 정해 샘플 저장":"열 샘플을 선택하세요",FontSize=21,FontWeight=FontWeights.SemiBold});header.Children.Add(new TextBlock{Text="실제 파일/폴더가 아닙니다. 이번 실행 메모리만 사용하며 앱 종료 시 사라집니다.\nPDF 열기·디스크 저장은 연결하지 않았습니다. 저장본 한도: 32개",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)});Panel.Children.Add(header);
  Catalog.ItemsSource=entries;Grid.SetRow(Catalog,1);Panel.Children.Add(Catalog);
  var fields=new StackPanel();Grid.SetRow(fields,2);Panel.Children.Add(fields);
  if(saving){fields.Children.Add(new TextBlock{Text="가상 샘플 이름 (경로 아님, 최대 80자)"});fields.Children.Add(NameInput);NameInput.Text=suggested+" 복사";}
  fields.Children.Add(Error);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(buttons,3);buttons.Children.Add(Cancel);buttons.Children.Add(Accept);Panel.Children.Add(buttons);Accept.Content=saving?"메모리에 저장":"샘플 열기";
  void Validate(){Error.Text=saving?PreviewWorkspace.ValidateSampleName(NameInput.Text,occupied??Array.Empty<string>())??"":"";Accept.IsEnabled=saving?Error.Text.Length==0:Catalog.SelectedItem is VirtualSampleEntry;}
  NameInput.TextChanged+=(_,_)=>Validate();Catalog.SelectionChanged+=(_,_)=>Validate();
  Accept.Click+=(_,_)=>{Validate();if(!Accept.IsEnabled)return;Result=saving?NameInput.Text.Trim():((VirtualSampleEntry)Catalog.SelectedItem).Key;Close();};Cancel.Click+=(_,_)=>Close();
  Catalog.MouseDoubleClick+=(_,_)=>{if(!saving&&Accept.IsEnabled)Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
  ContentRendered+=(_,_)=>{if(saving){NameInput.Focus();NameInput.SelectAll();}else Catalog.Focus();};
  KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);Validate();
 }
}
