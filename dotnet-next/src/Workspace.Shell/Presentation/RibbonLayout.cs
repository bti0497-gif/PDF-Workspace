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
    // Simple original 28 x 30 line drawings. A shape is shared only by the same command on another tab.
    public static readonly Geometry TextEdit=Draw("M4,5 L30,5 M17,5 L17,28 M10,28 L24,28 M6,31 L22,15 27,20 11,32 Z");
    public static readonly Geometry TextBox=Draw("M3,4 L31,4 31,28 3,28 Z M9,10 L25,10 M17,10 L17,22 M12,22 L22,22");
    public static readonly Geometry Note=Draw("M4,4 L30,4 30,24 17,24 9,31 9,24 4,24 Z M9,10 L25,10 M9,16 L21,16");
    public static readonly Geometry Comment=Note;
    public static readonly Geometry Highlight=Draw("M5,26 L23,5 29,11 11,31 Z M3,33 L29,33");
    public static readonly Geometry Underline=Draw("M7,5 L7,19 A10,7 0 0 0 27,19 L27,5 M4,28 L30,28");
    public static readonly Geometry Strikethrough=Draw("M7,6 L27,6 M17,6 L17,28 M6,20 L28,20 M8,28 L26,28");
    public static readonly Geometry Search=Draw("M22,12 A9,9 0 1 1 4,12 A9,9 0 1 1 22,12 M20,20 L31,31");
    public static readonly Geometry SearchList=Draw("M3,3 L24,3 M3,10 L18,10 M3,17 L13,17 M3,24 L11,24 M27,20 A7,7 0 1 1 13,20 A7,7 0 1 1 27,20 M25,25 L32,32");
    public static readonly Geometry Image=Draw("M3,4 L31,4 31,29 3,29 Z M4,25 L12,16 18,22 24,13 31,22 M12,11 A2,2 0 1 1 8,11 A2,2 0 1 1 12,11");
    public static readonly Geometry Hand=Draw("M11,17 L11,5 A2,2 0 0 1 15,5 L15,14 L15,3 A2,2 0 0 1 19,3 L19,14 L19,5 A2,2 0 0 1 23,5 L23,16 25,12 A2,2 0 0 1 28,15 L25,27 A6,6 0 0 1 19,31 L13,31 7,24 A4,4 0 0 1 11,17");
    public static readonly Geometry Select=Draw("M8,2 L8,29 15,22 22,32 26,30 19,20 29,19 Z");
    public static readonly Geometry Copy=Draw("M5,8 L23,8 23,30 5,30 Z M11,3 L29,3 29,25");
    public static readonly Geometry Paste=Draw("M9,5 L13,5 14,2 20,2 21,5 25,5 25,30 7,30 7,5 Z M12,12 L20,12 M16,8 L16,22 M11,17 L16,22 21,17");
    public static readonly Geometry Undo=Draw("M12,4 L4,12 12,20 M5,12 L20,12 C29,12 29,28 19,28");
    public static readonly Geometry Redo=Draw("M20,4 L28,12 20,20 M27,12 L12,12 C3,12 3,28 13,28");
    public static readonly Geometry FitPage=Draw("M9,3 L3,3 3,9 M25,3 L31,3 31,9 M3,25 L3,31 9,31 M31,25 L31,31 25,31 M10,7 L24,7 24,27 10,27 Z");
    public static readonly Geometry FitWidth=Draw("M3,10 L9,10 M3,10 L7,6 M3,10 L7,14 M31,10 L25,10 M31,10 L27,6 M31,10 L27,14 M7,16 L27,16 27,28 7,28 Z");
    public static readonly Geometry Rectangle=Draw("M4,5 L30,5 30,29 4,29 Z");
    public static readonly Geometry Line=Draw("M5,29 L29,5");
    public static readonly Geometry Stamp=Draw("M8,5 L26,5 26,19 30,23 30,28 4,28 4,23 8,19 Z M12,10 L22,10 M12,15 L22,15");
    public static readonly Geometry Rotate=Draw("M27,12 A12,12 0 1 0 26,26 M27,3 L27,12 18,12");
    public static readonly Geometry Crop=Draw("M9,2 L9,25 32,25 M2,9 L25,9 25,32");
    public static readonly Geometry DeletePage=Draw("M7,3 L23,3 28,8 28,31 7,31 Z M23,3 L23,8 28,8 M11,14 L23,26 M23,14 L11,26");
    public static readonly Geometry Insert=Draw("M7,3 L23,3 28,8 28,31 7,31 Z M23,3 L23,8 28,8 M17,13 L17,25 M11,19 L23,19");
    public static readonly Geometry Extract=Draw("M5,6 L21,6 21,27 5,27 Z M17,15 L31,15 M26,10 L31,15 26,20");
    public static readonly Geometry Reorder=Draw("M6,8 L27,8 M23,4 L27,8 23,12 M27,24 L6,24 M10,20 L6,24 10,28 M11,13 L23,13 M11,19 L23,19");
    public static readonly Geometry Merge=Draw("M4,5 L13,5 13,27 4,27 Z M21,5 L30,5 30,27 21,27 Z M15,10 L18,10 M15,22 L18,22 M13,16 L21,16 M17,12 L21,16 17,20");
    public static readonly Geometry Split=Draw("M12,5 L22,5 22,27 12,27 Z M17,10 L17,16 M17,16 L5,16 M5,16 L9,12 M5,16 L9,20 M17,16 L29,16 M29,16 L25,12 M29,16 L25,20");
    public static readonly Geometry SinglePage=Draw("M8,3 L24,3 29,8 29,31 8,31 Z M24,3 L24,8 29,8 M12,14 L25,14 M12,20 L25,20");
    public static readonly Geometry ContinuousPages=Draw("M10,2 L25,2 29,6 29,14 10,14 Z M25,2 L25,6 29,6 M10,19 L25,19 29,23 29,31 10,31 Z M25,19 L25,23 29,23");
    public static readonly Geometry Thumbnails=Draw("M3,3 L14,3 14,14 3,14 Z M20,3 L31,3 31,14 20,14 Z M3,20 L14,20 14,31 3,31 Z M20,20 L31,20 31,31 20,31 Z");
    public static readonly Geometry Folder=Draw("M3,10 L14,10 17,6 30,6 30,29 3,29 Z M3,14 L30,14");
    public static readonly Geometry Save=Draw("M4,3 L26,3 30,7 30,30 4,30 Z M9,3 L9,12 23,12 23,3 M9,30 L9,19 25,19 25,30");
    public static readonly Geometry Restore=Draw("M27,12 A12,12 0 1 0 25,26 M27,4 L27,13 18,13 M11,18 L22,18 M11,24 L19,24");
    public static readonly Geometry Print=Draw("M8,12 L8,3 24,3 24,12 M8,25 L4,25 4,12 28,12 28,25 24,25 M8,20 L24,20 24,31 8,31 Z M22,16 L25,16");
    public static readonly Geometry Home=Draw("M3,15 L17,3 31,15 M7,13 L7,30 14,30 14,21 20,21 20,30 27,30 27,13");
    public static readonly Geometry Exit=Draw("M4,3 L21,3 21,9 M4,3 L4,31 21,31 21,25 M15,17 L31,17 M25,11 L31,17 25,23");
    public static readonly Geometry Bookmark=Draw("M7,3 L26,3 26,31 17,24 7,31 Z");
    public static readonly Geometry Panel=Draw("M3,4 L31,4 31,30 3,30 Z M12,4 L12,30 M17,12 L26,12 M17,18 L26,18");
    public static readonly Geometry ResetLayout=Draw("M3,4 L25,4 25,13 M3,4 L3,29 13,29 M10,4 L10,29 M29,21 A7,7 0 1 0 27,30 M29,15 L29,21 23,21");
    public static readonly Geometry Help=Draw("M17,31 A14,14 0 1 1 17,3 A14,14 0 1 1 17,31 M12,13 A5,5 0 1 1 20,17 C17,19 17,20 17,23 M17,27 L17,27.5");
    public static readonly Geometry Info=Draw("M17,31 A14,14 0 1 1 17,3 A14,14 0 1 1 17,31 M17,15 L17,25 M17,10 L17,10.5");
    public static readonly Geometry History=Draw("M27,12 A12,12 0 1 0 25,26 M27,4 L27,13 18,13 M17,9 L17,18 23,22");
    public static readonly Geometry Diagnostic=Draw("M4,4 L30,4 30,30 4,30 Z M10,23 L14,17 18,21 24,11 M10,10 L19,10");

    public static IReadOnlyList<RibbonGroup> For(string tab) => tab switch
    {
        "홈" => new[]{Group("내용 편집",Tool("텍스트 편집",TextEdit),Tool("이미지 추가",Image)),Group("기본 주석",Tool("노트",Note),Tool("텍스트 상자",TextBox)),Group("텍스트 마크업",Tool("강조 표시",Highlight),Tool("밑줄",Underline),Tool("취소선",Strikethrough)),Group("검색",new RibbonTool("문서 찾기",Search,"문서 찾기: 현재 문서 안에서 이전/다음 일치 위치로 이동합니다. 예정 단축키 Ctrl+F."),new RibbonTool("상세 검색",SearchList,"상세 검색: 현재 문서 또는 여러 PDF/폴더를 대상으로 조건을 지정하고 결과 목록을 봅니다. 예정 단축키 Ctrl+Shift+F."))},
        "기본" => new[]{Group("선택",Tool("손 도구",Hand),Tool("선택 도구",Select)),Group("클립보드",Tool("복사",Copy),Tool("붙여넣기",Paste)),Group("편집",Tool("실행 취소",Undo),Tool("다시 실행",Redo)),Group("보기",Tool("페이지 맞춤",FitPage),Tool("너비 맞춤",FitWidth))},
        "주석" => new[]{Group("기본 주석",Tool("노트",Note),Tool("텍스트 상자",TextBox)),Group("텍스트 마크업",Tool("강조 표시",Highlight),Tool("밑줄",Underline),Tool("취소선",Strikethrough)),Group("그리기",Tool("사각형",Rectangle),Tool("선",Line)),Group("스탬프",Tool("도장",Stamp))},
        "문서" => new[]{Group("페이지",Tool("회전",Rotate),Tool("자르기",Crop),Tool("삭제",DeletePage)),Group("어셈블",Tool("삽입",Insert),Tool("추출",Extract),Tool("순서 변경",Reorder)),Group("문서 구성",Tool("병합",Merge),Tool("분할",Split))},
        _ => Array.Empty<RibbonGroup>()
    };
    private static Geometry Draw(string path){var geometry=Geometry.Parse(path);geometry.Freeze();return geometry;}
    private static RibbonTool Tool(string caption,Geometry icon)=>new(caption,icon);
    private static RibbonGroup Group(string name,params RibbonTool[] tools)=>new(name,tools);
}