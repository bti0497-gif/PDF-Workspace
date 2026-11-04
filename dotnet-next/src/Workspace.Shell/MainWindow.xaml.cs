using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using Workspace.Runtime;
using Workspace.Shell.Presentation;
namespace Workspace.Shell;
public partial class MainWindow : Window
{
    public ShellViewModel Model {get;}
    private double preferredPanelWidth=260;
    public MainWindow(ModuleRegistry registry,ModuleLoadReport report)
    {
        InitializeComponent();Model=new ShellViewModel(registry,report);DataContext=Model;
        Model.PropertyChanged+=ModelChanged;
        Model.LayoutResetRequested+=ResetPanelWidth;
        BodyGrid.SizeChanged+=(_,_)=>ApplyPanelWidth();
        // Keep room for the thumbnail surface at the supported 640x480 minimum.
        // Preserve font sizes and the user's ribbon visibility; only limit its scroll viewport.
        ShellRoot.SizeChanged+=(_,e)=>{
            RibbonScroller.MaxHeight=e.NewSize.Height<600?112:174;
            var compact=e.NewSize.Width<700;
            QuickAccessBar.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            QuickAccessOverflow.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
        };
        Closing+=GuardClosing;
        Closed+=(_,_)=>{Model.PropertyChanged-=ModelChanged;Model.LayoutResetRequested-=ResetPanelWidth;};
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(Model.SidebarVisible)) ApplyPanelWidth();
    }
    private void ResetPanelWidth(){preferredPanelWidth=260;ApplyPanelWidth();if(Model.PreviewActive)Model.Preview.ResetLayout();}
    private void ApplyPanelWidth()
    {
        // Reserve at least 320 logical pixels for the work area on narrow windows.
        if(!Model.SidebarVisible){SidebarColumn.MinWidth=0;SidebarColumn.Width=new GridLength(0);SplitterColumn.Width=new GridLength(0);return;}
        var available=BodyGrid.ActualWidth>0?BodyGrid.ActualWidth:Width;
        var maximum=Math.Max(200,Math.Min(420,available-325));
        SidebarColumn.MinWidth=200;SidebarColumn.MaxWidth=maximum;
        SidebarColumn.Width=new GridLength(Math.Clamp(preferredPanelWidth,200,maximum));SplitterColumn.Width=new GridLength(5);
    }
    private void PanelResizeCompleted(object sender,DragCompletedEventArgs e)
    {
        preferredPanelWidth=Math.Clamp(SidebarColumn.ActualWidth,200,420);ApplyPanelWidth();
    }
    private void GuardClosing(object? sender,CancelEventArgs e){if(Model.IsBusy)e.Cancel=true;}
    private void ShowInteractionHelp(object sender,RoutedEventArgs e)=>MessageBox.Show(this,
        "UI/UX 검증용 샘플 문서입니다. 실제 PDF 파일은 열거나 저장하지 않습니다.\n\n"+
        "• 문서 탭: 샘플 사이 전환, ×로 닫기\n• 축소판 패널은 페이지 관리 전용이며 아래에는 페이지 번호만 표시\n• 우클릭: 선택 유지, 회전·삭제·빈 쪽 삽입·새 샘플 탭으로 추출\n• 페이지 이동/회전/삭제/삽입은 문서별 Ctrl+Z / Ctrl+Y로 복구\n• 전체 페이지 삭제는 막고 최소 1쪽을 남깁니다.\n• 선택한 축소판 드래그: 빨간 삽입선 위치로 페이지 이동 (샘플 메모리 안에서만)\n• 여러 장은 현재 문서 순서를 유지하며 함께 이동, Esc로 이동 취소\n• 축소판: 크기 슬라이더와 패널 분리 막대로 크기/열 조절\n• 빈 여백: 클릭으로 선택 해제, 드래그로 여러 쪽 선택\n• Ctrl 클릭: 개별 추가/해제, Shift 클릭: 범위 선택\n• Ctrl 드래그: 기존 선택 반전, Shift 드래그: 선택 추가\n• 축소판 영역의 Ctrl+A: 모두 선택, 드래그 중 Esc: 취소\n• 파란 테두리: 보는 쪽, 주황 배경: 선택한 쪽\n• 본문: 휠로 스크롤, Ctrl+휠로 배율 변경\n• 하단: 페이지 번호 입력 후 Enter, 단일/연속, 배율과 맞춤\n\n"+
        "Ctrl+O  샘플 열기/복귀\nCtrl+W  현재 샘플 닫기\nCtrl+Home  시작 화면 (열린 샘플 유지)\nCtrl+F1  리본 접기/펼치기\nF4  탐색 패널 접기/펼치기\nCtrl+0  페이지 맞춤\nCtrl+2  너비 맞춤\nCtrl+더하기/빼기  배율 변경\nAlt+왼쪽/오른쪽  이전/다음 페이지\n\n"+
        "문서 찾기: 현재 문서의 일치 위치를 이전/다음으로 이동하는 기능입니다.\n상세 검색: 여러 문서/폴더와 검색 조건을 지정하고 결과 목록을 보는 기능입니다.\n두 검색 도구는 아직 미연결 상태이며 Ctrl+F / Ctrl+Shift+F도 연결 전입니다.\n\n"+
        "비활성 도구는 미연결이거나 현재 수행할 작업이 없는 상태입니다. 추출은 원본 유지·새 탭 복사이며 파일을 저장하지 않습니다. 앱 종료 시 샘플 내용과 실행 취소 기록은 사라집니다.","조작 안내 · 샘플 전용",MessageBoxButton.OK,MessageBoxImage.Information);
    private void ExitApp(object sender,RoutedEventArgs e)=>Close();
}
