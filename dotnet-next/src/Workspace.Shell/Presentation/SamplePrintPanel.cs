using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Workspace.Shell.Presentation;
// Synthetic-only content hosted by SamplePrintWindow. No printer, PDF engine or filesystem.
public sealed class SamplePrintPanel : Grid
{
 internal readonly ComboBox Scope = new() { ItemsSource=new[]{"현재 보는 쪽","모든 쪽","쪽 범위"},SelectedIndex=0 };
 internal readonly TextBox Range=new(){Text="1"}, Copies=new(){Text="1"};
 internal readonly ComboBox Pages=new();
 internal readonly TextBlock Validation=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick}, Summary=new(){TextWrapping=TextWrapping.Wrap};
 internal readonly Viewbox PaperView=new(){Stretch=Stretch.Uniform,Margin=new Thickness(18)};
 internal readonly Button CloseButton=new(){Content="닫기 (Esc)",MinHeight=32,Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,0)}, CancelButton=new(){Content="취소",MinHeight=32,Padding=new Thickness(12,5,12,5)};
 private readonly PreviewWorkspace workspace; private readonly int current,total; private bool updating;
 public event Action? CloseRequested;
 internal int[] ChosenPages {get;private set;}=Array.Empty<int>();
 internal bool IsValid {get;private set;}
 internal Border? Paper {get;private set;}
 public SamplePrintPanel(PreviewWorkspace source)
 {
  workspace=source;current=source.PageNumber;total=source.PageCount;
  Background=new SolidColorBrush(Color.FromRgb(245,247,249));Margin=new Thickness(12);
  KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
  AutomationProperties.SetName(this,"샘플 전용 인쇄 미리보기 · 실제 인쇄 미연결");
  RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());RowDefinitions.Add(new(){Height=GridLength.Auto});
  Children.Add(new TextBlock{Text="인쇄 미리보기 · 합성 샘플 전용\n실제 프린터·인쇄 미연결 · 파일 출력 없음",FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(10),TextWrapping=TextWrapping.Wrap});
  var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(220)});body.ColumnDefinitions.Add(new());Grid.SetRow(body,1);Children.Add(body);
  var settings=new StackPanel{Margin=new Thickness(10)};
  void Field(string label,Control control,string id){settings.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,10,0,4),TextWrapping=TextWrapping.Wrap});control.MinHeight=28;control.FontSize=12;control.ToolTip=label;AutomationProperties.SetName(control,label);AutomationProperties.SetAutomationId(control,id);settings.Children.Add(control);}
  Field("대상 쪽 · 현재 문서만",Scope,"PrintScope");Field($"범위 (1-{total}) · 예: 1,3-5",Range,"PrintRange");Field("부수 (1-999) · 수량 계산만",Copies,"PrintCopies");Field("미리 볼 쪽 · 선택 범위 안에서",Pages,"PrintPage");
  settings.Children.Add(new TextBlock{Text="방향: 샘플 회전 상태 유지\n배율: 화면 맞춤 (인쇄 크기 아님)\n용지·여백·양면·프린터 설정 미지원",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,10),Foreground=Brushes.DimGray});settings.Children.Add(Validation);settings.Children.Add(Summary);
  body.Children.Add(new ScrollViewer{Content=settings,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
  var preview=new Border{Background=new SolidColorBrush(Color.FromRgb(116,123,131)),Child=PaperView,Margin=new Thickness(6)};Grid.SetColumn(preview,1);body.Children.Add(preview);
  var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(10)};Grid.SetRow(footer,2);Children.Add(footer);
  var print=new Button{Content="인쇄 · 미연결",IsEnabled=false,ToolTip="실제 프린터 또는 파일로 출력하지 않습니다.",Margin=new Thickness(0,0,10,0)};ToolTipService.SetShowOnDisabled(print,true);AutomationProperties.SetName(print,"실제 인쇄 미연결");footer.Children.Add(print);footer.Children.Add(CloseButton);footer.Children.Add(CancelButton);
  AutomationProperties.SetName(CloseButton,"샘플 인쇄 미리보기 닫기");AutomationProperties.SetName(CancelButton,"샘플 인쇄 미리보기 취소");CloseButton.Click+=(_,_)=>CloseRequested?.Invoke();CancelButton.Click+=(_,_)=>CloseRequested?.Invoke();
  Scope.SelectionChanged+=(_,_)=>Refresh();Range.TextChanged+=(_,_)=>Refresh();Copies.TextChanged+=(_,_)=>Refresh();Pages.SelectionChanged+=(_,_)=>ShowPage();PreviewKeyDown+=(_,e)=>{if(HandleKey(e.Key))e.Handled=true;};Refresh();
 }
 internal bool HandleKey(Key key){if(key!=Key.Escape)return false;CloseRequested?.Invoke();return true;}
 internal static bool TryRange(string text,int total,out int[] pages)
 {
  pages=Array.Empty<int>();if(string.IsNullOrWhiteSpace(text)||text.Length>4096)return false;var selected=new SortedSet<int>();
  foreach(var token in text.Split(',')){var ends=token.Trim().Split('-');bool Number(string s,out int n)=>int.TryParse(s.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out n)&&n>=1&&n<=total;if(ends.Length>2||!Number(ends[0],out var first))return false;var last=first;if(ends.Length==2&&(!Number(ends[1],out last)||last<first))return false;for(var n=first;n<=last;n++)selected.Add(n);}
  pages=selected.ToArray();return pages.Length>0;
 }
 private void Refresh()
 {
  if(updating)return;updating=true;
  try{Range.IsEnabled=Scope.SelectedIndex==2;int[] selected=Array.Empty<int>();var valid=Scope.SelectedIndex switch{0=>(selected=new[]{current}).Length>0,1=>(selected=Enumerable.Range(1,total).ToArray()).Length>0,2=>TryRange(Range.Text,total,out selected),_=>false};var copiesValid=int.TryParse(Copies.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var count)&&count>=1&&count<=999;IsValid=valid&&copiesValid;ChosenPages=IsValid?selected:Array.Empty<int>();Validation.Text=!valid?$"1-{total} 사이 쪽을 입력하세요. 역순 범위·빈 항목은 허용하지 않습니다.":!copiesValid?"부수는 1-999의 정수여야 합니다.":"";Summary.Text=IsValid?$"선택 {selected.Length}쪽 × {count}부 = {(long)selected.Length*count}쪽 분량\n중복 제거 · 문서 순서로 표시\n출력 작업은 생성되지 않습니다.":"입력을 수정하면 미리보기가 표시됩니다.";var old=Pages.SelectedItem;Pages.ItemsSource=ChosenPages;Pages.SelectedItem=old is int n&&ChosenPages.Contains(n)?n:ChosenPages.Cast<object>().FirstOrDefault();}finally{updating=false;}ShowPage();
 }
 private void ShowPage(){if(updating)return;Paper=IsValid&&Pages.SelectedItem is int n?workspace.CreatePrintPaper(n):null;PaperView.Child=Paper;}
}
public sealed partial class PreviewWorkspace
{
 internal Border CreatePrintPaper(int number){var d=Current??throw new InvalidOperationException("No active sample.");var paper=BuildPaper(d,number-1);paper.LayoutTransform=PaperTransform(d.Pages[number-1],1);AutomationProperties.SetName(paper,$"합성 샘플 {number}쪽 · 자르기 및 회전 반영");return paper;}
}
