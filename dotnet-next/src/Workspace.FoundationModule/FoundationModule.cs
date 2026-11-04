using System.Windows;
using Workspace.Contracts;
using Workspace.Desktop.Contracts;
namespace Workspace.FoundationModule;

// Only a wiring probe; deliberately not a PDF implementation or migrated legacy feature.
public sealed class FoundationModule : IWorkspaceModule, IModuleViewProvider
{
    public ModuleDescriptor Descriptor { get; } = new("foundation","모듈 연결 확인",new Version(0,1,0),ModuleApi.Version);
    public IReadOnlyList<IWorkspaceCommand> Commands { get; } = new IWorkspaceCommand[]{new ConnectionCheck()};
    public FrameworkElement CreateView(Func<string,Task<CommandResult>> execute) => new FoundationView(execute);
}
public sealed class ConnectionCheck : IWorkspaceCommand
{
    private int runs;
    public string Id => "foundation.check";
    public string Caption => "모듈 연결 검사";
    public Task<CommandResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandResult(true,$"모듈 명령 실행 완료 · 이번 실행에서 {Interlocked.Increment(ref runs)}회 · PDF 파일은 읽거나 변경하지 않았습니다."));
    }
}
