using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Workspace.Shell.Presentation;
namespace Workspace.Shell.PdfViewing;
internal sealed partial class PdfWorkspaceView
{
 private readonly Dictionary<string,Button> ribbonButtons=new();
 private readonly List<(Border Container,string[] Categories)> ribbonGroups=new();
 internal string RibbonCategory {get;private set;}="홈";
 internal IReadOnlyDictionary<string,Button> RibbonButtons=>ribbonButtons;
 internal static bool SupportsRibbonCategory(string category)=>category is "파일" or "홈" or "문서" or "보기";
 internal void SelectRibbonCategory(string category)
 {
  if(!SupportsRibbonCategory(category))return;
  RibbonCategory=category;
  foreach(var group in ribbonGroups)group.Container.Visibility=group.Categories.Contains(category)?Visibility.Visible:Visibility.Collapsed;
 }
 private void BuildRibbon()
 {
  foreach(var button in Ribbon.Children.OfType<Button>())ribbonButtons.Add((string)button.Content,button);
  Ribbon.Children.Clear();Ribbon.Margin=new Thickness(0);
  void Group(string name,string[] categories,params (string Caption,Geometry Icon)[] entries)
  {
   var buttons=new WrapPanel();
   foreach(var entry in entries){var button=ribbonButtons[entry.Caption];button.SetResourceReference(StyleProperty,"SpecialRibbonIconButton");button.ClearValue(Control.PaddingProperty);button.Margin=new Thickness(1,0,1,0);
    AutomationProperties.SetName(button,entry.Caption);AutomationProperties.SetAutomationId(button,"RealPdf."+entry.Caption);
    if(button.ToolTip==null)button.ToolTip=entry.Caption+" · 실제 PDF";ToolTipService.SetShowOnDisabled(button,true);
    var content=new FrameworkElementFactory(typeof(StackPanel));
    var icon=new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));icon.SetValue(System.Windows.Shapes.Path.DataProperty,entry.Icon);icon.SetValue(WidthProperty,28d);icon.SetValue(HeightProperty,30d);icon.SetValue(System.Windows.Shapes.Shape.StretchProperty,Stretch.Uniform);icon.SetValue(System.Windows.Shapes.Shape.StrokeProperty,new SolidColorBrush(Color.FromRgb(82,109,136)));icon.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty,1.8d);icon.SetValue(MarginProperty,new Thickness(0,0,0,5));content.AppendChild(icon);
    var label=new FrameworkElementFactory(typeof(TextBlock));if(entry.Caption=="다른 이름으로 저장"){label.SetValue(TextBlock.TextProperty,"다른 이름 저장");button.Width=104;}else label.SetBinding(TextBlock.TextProperty,new Binding());label.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Center);label.SetValue(TextBlock.TextWrappingProperty,entry.Caption=="다른 이름으로 저장"?TextWrapping.NoWrap:TextWrapping.Wrap);label.SetValue(TextBlock.FontSizeProperty,12d);content.AppendChild(label);
    button.ContentTemplate=new DataTemplate{VisualTree=content};buttons.Children.Add(button);
   }
   var caption=new TextBlock{Text=name};caption.SetResourceReference(StyleProperty,"GroupCaption");var stack=new StackPanel();stack.Children.Add(buttons);stack.Children.Add(caption);var border=new Border{Child=stack};border.SetResourceReference(StyleProperty,"GroupBorder");Ribbon.Children.Add(border);ribbonGroups.Add((border,categories));
  }
  Group("파일",["홈","파일"],("파일 열기",RibbonLayout.Folder),("저장",RibbonLayout.Save),("다른 이름으로 저장",RibbonLayout.Save),("문서 닫기",RibbonLayout.Exit));
  Group("실행 기록",["홈","문서"],("실행 취소",RibbonLayout.Undo),("다시 실행",RibbonLayout.Redo));
  Group("페이지 관리",["홈","문서"],("90° 회전",RibbonLayout.Rotate),("삭제",RibbonLayout.DeletePage),("앞으로 이동",RibbonLayout.Reorder),("뒤로 이동",RibbonLayout.Reorder));
  Group("문서 조합",["문서"],("선택 쪽 추출",RibbonLayout.Extract),("PDF 병합",RibbonLayout.Merge));
  Group("확대/축소",["보기"],("축소",RibbonLayout.Search),("실제 크기",RibbonLayout.SinglePage),("확대",RibbonLayout.Search),("너비 맞춤",RibbonLayout.FitWidth));
  Group("미지원 편집",["문서"],("텍스트 편집",RibbonLayout.TextEdit),("자르기",RibbonLayout.Crop),("주석",RibbonLayout.Comment));
  Group("인쇄 · 미지원",["파일"],("인쇄",RibbonLayout.Print));
  Group("현재 작업",["홈","파일","문서","보기"],("작업 취소",RibbonLayout.Exit));
  SelectRibbonCategory("홈");
 }
}
