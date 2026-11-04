using System.Windows.Media;
namespace Workspace.Shell.Presentation;

// Independently authored presentation descriptors. These are not implemented PDF commands.
// All PDF controls remain disabled until a real feature module supplies a verified command.
public sealed record RibbonTool(string Caption,Geometry Icon,string? Purpose=null)
{
    public System.Windows.Input.ICommand? Command { get; init; }
    public string Availability => (Purpose ?? Caption) + (Command is null ? "\n현재 UI 설계 단계: PDF 엔진/기능 모듈 미연결" : "\n합성 샘플의 메모리 상태만 변경합니다. 실제 PDF 처리/저장은 미연결입니다.");
}
public sealed record RibbonGroup(string Name,IReadOnlyList<RibbonTool> Tools);
public static class RibbonLayout
{
    // Simple original line drawings, not extracted NesPDF artwork.
    public static readonly Geometry Page=Draw("M7,2 L22,2 29,9 29,32 7,32 Z M22,2 L22,9 29,9 M12,15 L24,15 M12,21 L24,21 M12,27 L20,27");
    public static readonly Geometry Text=Draw("M5,5 L29,5 M17,5 L17,29 M10,29 L24,29");
    public static readonly Geometry Comment=Draw("M4,4 L30,4 30,24 17,24 9,31 9,24 4,24 Z M9,10 L25,10 M9,16 L21,16");
    public static readonly Geometry Pages=Draw("M4,5 L22,5 22,28 4,28 Z M10,1 L28,1 28,24 M10,11 L17,11 M10,17 L17,17");
    public static readonly Geometry Search=Draw("M22,12 A9,9 0 1 1 4,12 A9,9 0 1 1 22,12 M20,20 L31,31");
    public static readonly Geometry SearchList=Draw("M3,3 L24,3 M3,10 L18,10 M3,17 L13,17 M3,24 L11,24 M27,20 A7,7 0 1 1 13,20 A7,7 0 1 1 27,20 M25,25 L32,32");
    public static readonly Geometry Image=Draw("M3,4 L31,4 31,29 3,29 Z M4,25 L12,16 18,22 24,13 31,22 M12,11 A2,2 0 1 1 8,11 A2,2 0 1 1 12,11");
    public static readonly Geometry Select=Draw("M8,2 L8,29 15,22 22,32 26,30 19,20 29,19 Z");
    public static readonly Geometry Mark=Draw("M5,26 L23,5 29,11 11,31 Z M3,33 L29,33");
    public static readonly Geometry Rotate=Draw("M27,12 A12,12 0 1 0 26,26 M27,3 L27,12 18,12");
    public static readonly Geometry Folder=Draw("M3,10 L14,10 17,6 30,6 30,29 3,29 Z M3,14 L30,14");
    public static IReadOnlyList<RibbonGroup> For(string tab) => tab switch
    {
        "홈" => new[]{Group("내용 편집",Tool("텍스트 편집",Text),Tool("이미지 추가",Image)),Group("기본 주석",Tool("노트",Comment),Tool("텍스트 상자",Text)),Group("텍스트 마크업",Tool("강조 표시",Mark),Tool("밑줄",Text),Tool("취소선",Text)),Group("검색",new RibbonTool("문서 찾기",Search,"문서 찾기: 현재 문서 안에서 이전/다음 일치 위치로 이동합니다. 예정 단축키 Ctrl+F."),new RibbonTool("상세 검색",SearchList,"상세 검색: 현재 문서 또는 여러 PDF/폴더를 대상으로 조건을 지정하고 결과 목록을 봅니다. 예정 단축키 Ctrl+Shift+F."))},
        "기본" => new[]{Group("선택",Tool("손 도구",Select),Tool("선택 도구",Select)),Group("클립보드",Tool("복사",Pages),Tool("붙여넣기",Page)),Group("편집",Tool("실행 취소",Rotate),Tool("다시 실행",Rotate)),Group("보기",Tool("페이지 맞춤",Page),Tool("너비 맞춤",Pages))},
        "주석" => new[]{Group("기본 주석",Tool("노트",Comment),Tool("텍스트 상자",Text)),Group("텍스트 마크업",Tool("강조 표시",Mark),Tool("밑줄",Text),Tool("취소선",Text)),Group("그리기",Tool("사각형",Image),Tool("선",Mark)),Group("스탬프",Tool("도장",Page))},
        "문서" => new[]{Group("페이지",Tool("회전",Rotate),Tool("자르기",Page),Tool("삭제",Page)),Group("어셈블",Tool("삽입",Pages),Tool("추출",Page),Tool("순서 변경",Pages)),Group("문서 구성",Tool("병합",Pages),Tool("분할",Pages))},
        _ => Array.Empty<RibbonGroup>()
    };
    private static Geometry Draw(string path){var geometry=Geometry.Parse(path);geometry.Freeze();return geometry;}
    private static RibbonTool Tool(string caption,Geometry icon)=>new(caption,icon);
    private static RibbonGroup Group(string name,params RibbonTool[] tools)=>new(name,tools);
}
