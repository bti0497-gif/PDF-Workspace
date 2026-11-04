using Workspace.Contracts;
namespace Workspace.Runtime;

// One command path for menus, module screens, shortcuts and future automation.
// In-process modules are trusted application code, NOT isolated/sandboxed plugins.
public sealed class ModuleRegistry
{
    private readonly Dictionary<string,IWorkspaceModule> modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string,IWorkspaceCommand> commands = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim execution = new(1,1);
    public IReadOnlyList<IWorkspaceModule> Modules => modules.Values.ToArray();
    public IReadOnlyList<IWorkspaceCommand> Commands => commands.Values.ToArray();

    public void Register(IWorkspaceModule module)
    {
        var d = module.Descriptor;
        if (!ValidId(d.Id) || string.IsNullOrWhiteSpace(d.Name) || d.Version is null)
            throw new InvalidOperationException("모듈 정보가 올바르지 않습니다.");
        if (d.ApiVersion != ModuleApi.Version) throw new InvalidOperationException("호환되지 않는 모듈 API: " + d.Id);
        if (modules.ContainsKey(d.Id)) throw new InvalidOperationException("중복 모듈: " + d.Id);
        // Validate the whole contribution first: no partial registration on failure.
        var pending = module.Commands.ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in pending)
            if (command is null || string.IsNullOrWhiteSpace(command.Caption) ||
                string.IsNullOrWhiteSpace(command.Id) || !command.Id.StartsWith(d.Id + ".",StringComparison.Ordinal) ||
                !ValidId(command.Id[(d.Id.Length + 1)..]) || !seen.Add(command.Id) || commands.ContainsKey(command.Id))
                throw new InvalidOperationException("잘못되거나 중복된 명령: " + d.Id);
        modules.Add(d.Id,module);
        foreach (var command in pending) commands.Add(command.Id,command);
    }

    private static bool ValidId(string? id) => !string.IsNullOrEmpty(id) &&
        id[0] is >= 'a' and <= 'z' && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public async Task<CommandResult> ExecuteAsync(string id, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return new(false,"작업을 취소했습니다.");
        if (!commands.TryGetValue(id,out var command)) return new(false,"등록되지 않은 명령입니다: " + id);
        try { if (!await execution.WaitAsync(0,token)) return new(false,"다른 작업이 진행 중입니다."); }
        catch (OperationCanceledException) { return new(false,"작업을 취소했습니다."); }
        try { return await command.ExecuteAsync(token); }
        catch (OperationCanceledException) { return new(false,"작업을 취소했습니다."); }
        catch (Exception ex) { return new(false,"작업 실패: " + ex.Message); }
        finally { execution.Release(); }
    }
}
