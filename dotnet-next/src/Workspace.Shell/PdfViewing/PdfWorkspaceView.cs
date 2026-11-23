using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Workspace.Shell.PdfOperations;
namespace Workspace.Shell.PdfViewing;
internal sealed partial class PdfWorkspaceView : Grid
{
 internal PdfDocumentSession? Session {get;private set;}
 private readonly ListBox thumbnails=new(){SelectionMode=SelectionMode.Extended,Background=Brushes.WhiteSmoke};
 private readonly Image image=new(){Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(20)};
 private readonly ScrollViewer canvas=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=new SolidColorBrush(Color.FromRgb(209,213,218))};
 private readonly TextBlock status=new(){Text="PDF 파일을 열어 작업을 시작하십시오. Ctrl+O",Margin=new Thickness(8),TextWrapping=TextWrapping.Wrap};
 private readonly TextBox pageNumber=new(){Width=48,Text="1",Margin=new Thickness(4)};
 private readonly TextBlock count=new(){VerticalAlignment=VerticalAlignment.Center};
 private readonly List<Button> tools=new();private CancellationTokenSource? operation;private int currentPage=1;private double zoom=1;private bool refreshing;
 internal Task LastAction {get;private set;}=Task.CompletedTask;
 internal Task TrackAsync(Func<Task> action){LastAction=action();return LastAction;}
 internal Func<string?>? PickOpenFile {get;set;}
 internal Func<string,string?>? PickSaveFile {get;set;}
 internal Func<string,bool>? AskReplace {get;set;}
 internal Func<int>? AskClose {get;set;}
 internal Action<Exception>? ReportError {get;set;}
 internal Image PageImage=>image;
 internal ListBox ThumbnailList=>thumbnails;
 internal string StatusMessage=>status.Text;
 public bool IsBusy=>operation!=null;
 public WrapPanel Ribbon {get;}=new(){Margin=new Thickness(4)};
 public event Action? StateChanged;
 public PdfWorkspaceView()
 {
  RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());RowDefinitions.Add(new(){Height=GridLength.Auto});
  var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(220)});body.ColumnDefinitions.Add(new(){Width=new GridLength(5)});body.ColumnDefinitions.Add(new());
  body.Children.Add(thumbnails);var splitter=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Stretch};SetColumn(splitter,1);body.Children.Add(splitter);
  canvas.Content=image;SetColumn(canvas,2);body.Children.Add(canvas);SetRow(body,1);Children.Add(body);Children.Add(status);
  var bottom=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
  Add(bottom,"이전",()=>NavigateAsync(currentPage-1));bottom.Children.Add(pageNumber);bottom.Children.Add(count);Add(bottom,"이동",()=>NavigateAsync(int.TryParse(pageNumber.Text,out var n)?n:currentPage));Add(bottom,"다음",()=>NavigateAsync(currentPage+1));
  Add(bottom,"−",()=>ZoomAsync(zoom/1.2));Add(bottom,"100%",()=>ZoomAsync(1));Add(bottom,"+",()=>ZoomAsync(zoom*1.2));Add(bottom,"너비 맞춤",()=>ZoomAsync(Math.Max(0.1,(canvas.ActualWidth-48)/(image.Source?.Width??600))));SetRow(bottom,2);Children.Add(bottom);
  Add(Ribbon,"파일 열기",OpenDialogAsync,false);Add(Ribbon,"저장",()=>SaveAsync(false));Add(Ribbon,"다른 이름으로 저장",()=>SaveAsync(true));Add(Ribbon,"실행 취소",()=>UndoAsync(false));Add(Ribbon,"다시 실행",()=>UndoAsync(true));
  Add(Ribbon,"90° 회전",()=>ModifyAsync(PdfOperationKind.Rotate));Add(Ribbon,"삭제",()=>ModifyAsync(PdfOperationKind.Delete));Add(Ribbon,"앞으로 이동",()=>MoveAsync(-1));Add(Ribbon,"뒤로 이동",()=>MoveAsync(1));Add(Ribbon,"선택 쪽 추출",ExtractAsync);Add(Ribbon,"PDF 병합",MergeAsync);Add(Ribbon,"문서 닫기",CloseDocumentAsync);
  var cancel=new Button{Content="작업 취소",Margin=new Thickness(3),Padding=new Thickness(8)};cancel.Click+=(_,_)=>operation?.Cancel();Ribbon.Children.Add(cancel);
  foreach(var label in new[]{"텍스트 편집","자르기","주석","인쇄"})Ribbon.Children.Add(new Button{Content=label,IsEnabled=false,ToolTip="실제 PDF 기능 미지원",Margin=new Thickness(3)});
  Add(Ribbon,"축소",()=>ZoomAsync(zoom/1.2));Add(Ribbon,"실제 크기",()=>ZoomAsync(1));Add(Ribbon,"확대",()=>ZoomAsync(zoom*1.2));Add(Ribbon,"너비 맞춤",()=>ZoomAsync(Math.Max(0.1,(canvas.ActualWidth-48)/(image.Source?.Width??600))));
  BuildRibbon();
  thumbnails.SelectionChanged+=async(_,_)=>{if(!refreshing&&!IsBusy&&thumbnails.SelectedItem is ListBoxItem item&&item.Tag is int n)await NavigateAsync(n);};
  pageNumber.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)await NavigateAsync(int.TryParse(pageNumber.Text,out var n)?n:currentPage);};RefreshButtons();
 }
 private void Add(Panel panel,string caption,Func<Task> action,bool needsDocument=true){var button=new Button{Content=caption,Margin=new Thickness(3),Padding=new Thickness(9,6,9,6),Tag=needsDocument};button.Click+=async(_,_)=>await TrackAsync(action);panel.Children.Add(button);tools.Add(button);}
 private void RefreshButtons(){foreach(var b in tools)b.IsEnabled=!IsBusy&&(!(bool)b.Tag||Session!=null);foreach(var b in tools){if((string)b.Content=="실행 취소")b.IsEnabled=!IsBusy&&Session?.CanUndo==true;if((string)b.Content=="다시 실행")b.IsEnabled=!IsBusy&&Session?.CanRedo==true;}thumbnails.IsEnabled=!IsBusy;StateChanged?.Invoke();}
 private async Task RunAsync(string message,Func<CancellationToken,Task> action)
 {
  if(IsBusy)return;using var cancel=new CancellationTokenSource();operation=cancel;status.Text=message+" · 작업 취소 가능";RefreshButtons();
  try{await action(cancel.Token);status.Text=Session==null?"문서 없음 · Ctrl+O":$"{Path.GetFileName(Session.SourcePath)}{(Session.IsDirty?" *":"")} · {Session.PageCount}쪽 · 원본은 저장 전까지 보존됩니다.";}
  catch(OperationCanceledException){status.Text="작업 취소됨 · 저장 전 원본 유지";}
  catch(Exception ex){status.Text="실패: "+ex.Message;if(ReportError!=null)ReportError(ex);else MessageBox.Show(Window.GetWindow(this),ex.Message,"PDF 작업 실패",MessageBoxButton.OK,MessageBoxImage.Warning);}
  finally{operation=null;RefreshButtons();}
 }
 public async Task OpenDialogAsync(){if(IsBusy)return;if(PickOpenFile!=null){var picked=PickOpenFile();if(picked!=null)await OpenAsync(picked);return;}var d=new OpenFileDialog{Filter="PDF 문서|*.pdf",CheckFileExists=true};if(d.ShowDialog(Window.GetWindow(this))==true)await OpenAsync(d.FileName);}
 public async Task OpenAsync(string path){if(IsBusy||!await ConfirmCloseAsync())return;await RunAsync("PDF 여는 중",async t=>{var next=await PdfDocumentSession.OpenAsync(path,t);var old=Session;Session=next;currentPage=1;if(old!=null)await old.DisposeAsync();await RefreshAsync(t);});}
 private static BitmapImage Decode(byte[] bytes){using var s=new MemoryStream(bytes);var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.StreamSource=s;b.EndInit();b.Freeze();return b;}
 private async Task RenderAsync(CancellationToken t){if(Session==null)return;image.Source=Decode(await Session.Viewer.RenderAsync(currentPage,t));image.Width=image.Source.Width*zoom;pageNumber.Text=currentPage.ToString();count.Text=" / "+Session.PageCount;}
 private async Task RefreshAsync(CancellationToken t)
 {
  if(Session==null)return;currentPage=Math.Clamp(currentPage,1,Session.PageCount);await RenderAsync(t);refreshing=true;
  try{thumbnails.Items.Clear();for(int n=1;n<=Session.PageCount;n++){var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=$"{n}쪽",HorizontalAlignment=HorizontalAlignment.Center});thumbnails.Items.Add(new ListBoxItem{Tag=n,Content=stack});}thumbnails.SelectedIndex=currentPage-1;
   for(int n=1;n<=Math.Min(Session.PageCount,24);n++){t.ThrowIfCancellationRequested();var bitmap=n==currentPage?image.Source:Decode(await Session.Viewer.RenderAsync(n,t));((StackPanel)((ListBoxItem)thumbnails.Items[n-1]).Content).Children.Insert(0,new Image{Source=bitmap,Width=150,Height=180,Stretch=Stretch.Uniform});}
  }finally{refreshing=false;}
 }
 public Task NavigateAsync(int n)=>RunAsync("페이지 표시 중",async t=>{if(Session==null)return;currentPage=Math.Clamp(n,1,Session.PageCount);await RenderAsync(t);if(thumbnails.Items.Count>=currentPage){var stack=(StackPanel)((ListBoxItem)thumbnails.Items[currentPage-1]).Content;if(stack.Children.Count==1)stack.Children.Insert(0,new Image{Source=image.Source,Width=150,Height=180,Stretch=Stretch.Uniform});}});
 private Task ZoomAsync(double value){zoom=Math.Clamp(value,0.1,4);if(image.Source!=null)image.Width=image.Source.Width*zoom;return Task.CompletedTask;}
 private int[] Selected()=>thumbnails.SelectedItems.Cast<ListBoxItem>().Select(x=>(int)x.Tag).Order().DefaultIfEmpty(currentPage).ToArray();
 public Task ModifyAsync(PdfOperationKind kind)=>RunAsync("PDF 수정 중",async t=>{if(Session==null)return;await Session.ModifyAsync(kind,Selected(),null,t);await RefreshAsync(t);});
 private Task MoveAsync(int direction)=>RunAsync("페이지 순서 변경 중",async t=>{if(Session==null)return;var selected=Selected().ToHashSet();var order=Enumerable.Range(1,Session.PageCount).ToArray();if(direction<0){for(int i=1;i<order.Length;i++)if(selected.Contains(order[i])&&!selected.Contains(order[i-1]))(order[i-1],order[i])=(order[i],order[i-1]);}else{for(int i=order.Length-2;i>=0;i--)if(selected.Contains(order[i])&&!selected.Contains(order[i+1]))(order[i+1],order[i])=(order[i],order[i+1]);}await Session.ModifyAsync(PdfOperationKind.Reorder,order,null,t);await RefreshAsync(t);});
 private async Task ExtractAsync(){if(Session==null||IsBusy)return;var baseName=Path.GetFileNameWithoutExtension(Session.SourcePath);if(string.IsNullOrWhiteSpace(baseName))baseName="문서";var pages=Selected();var pageSuffix=pages.Length==0?"추출":pages.Length==1?$"{pages[0]}페이지":pages[^1]-pages[0]+1==pages.Length?$"{pages[0]}-{pages[^1]}페이지":pages.Length<=5?$"{string.Join(",",pages)}페이지":$"{pages[0]}-{pages[^1]}({pages.Length}쪽)페이지";var d=new SaveFileDialog{Filter="PDF 문서|*.pdf",DefaultExt=".pdf",FileName=$"{baseName}_{pageSuffix}.pdf",OverwritePrompt=false};if(d.ShowDialog(Window.GetWindow(this))!=true)return;var target=d.FileName;if(File.Exists(target)){MessageBox.Show(Window.GetWindow(this),"추출은 새 파일 이름으로 저장하십시오.");return;}await RunAsync("선택 쪽 추출 중",async t=>{await new PdfCraftOperationsService(ReadOnlyPdfSession.DependencyPath()).ExecuteAsync(new(PdfOperationKind.Extract,[Session.CurrentPath],target,Selected()),t);});}
 private async Task MergeAsync(){if(Session==null||IsBusy)return;var d=new OpenFileDialog{Filter="PDF 문서|*.pdf",Multiselect=true};if(d.ShowDialog(Window.GetWindow(this))!=true)return;await RunAsync("PDF 병합 중",async t=>{await Session.ModifyAsync(PdfOperationKind.Merge,null,d.FileNames,t);await RefreshAsync(t);});}
 public Task UndoAsync(bool redo)=>RunAsync(redo?"다시 실행 중":"실행 취소 중",async t=>{if(Session==null)return;await Session.UndoAsync(redo,t);await RefreshAsync(t);});
 public async Task SaveAsync(bool saveAs)
 {
  if(Session==null||IsBusy)return;var target=Session.SourcePath;if(saveAs){if(PickSaveFile!=null){var picked=PickSaveFile(target);if(picked==null)return;target=picked;}else{var d=new SaveFileDialog{Filter="PDF 문서|*.pdf",FileName=Path.GetFileName(target),OverwritePrompt=false};if(d.ShowDialog(Window.GetWindow(this))!=true)return;target=d.FileName;}}
  var exists=File.Exists(target);string? approvedFingerprint=null;if(exists){try{approvedFingerprint=await PdfDocumentSession.FingerprintAsync(target,CancellationToken.None);}catch(Exception error){if(ReportError!=null)ReportError(error);else MessageBox.Show(Window.GetWindow(this),error.Message,"저장 실패");return;}}if(exists&&!(AskReplace?.Invoke(target)??(MessageBox.Show(Window.GetWindow(this),$"기존 파일을 교체하시겠습니까?\n{target}","PDF 저장 확인",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes)))return;
  await RunAsync("PDF 안전하게 저장 중",t=>Session.SaveAsync(target,exists,t,approvedFingerprint));
 }
 public async Task<bool> ConfirmCloseAsync(){if(IsBusy)return false;if(Session==null)return true;return await Session.ConfirmCloseAsync(()=>{if(AskClose!=null)return AskClose();var a=MessageBox.Show(Window.GetWindow(this),"변경한 PDF를 저장하시겠습니까?\n예: 저장 / 아니요: 버리기 / 취소: 계속 작업","문서 닫기",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);return a==MessageBoxResult.No?1:a==MessageBoxResult.Yes?2:0;},()=>SaveAsync(false));}

 public async Task CloseDocumentAsync(){if(!await ConfirmCloseAsync())return;if(Session!=null)await Session.DisposeAsync();Session=null;image.Source=null;thumbnails.Items.Clear();count.Text="";status.Text="문서 없음 · Ctrl+O";RefreshButtons();}
 public async Task DisposeAsync(){if(Session!=null)await Session.DisposeAsync();}
}
