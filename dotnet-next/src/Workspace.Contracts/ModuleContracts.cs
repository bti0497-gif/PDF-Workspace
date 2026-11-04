namespace Workspace.Contracts;

public sealed record ModuleDescriptor(string Id, string Name, Version Version, int ApiVersion);
public sealed record CommandResult(bool Success, string Message);
public interface IWorkspaceCommand
{
    string Id { get; }
    string Caption { get; }
    Task<CommandResult> ExecuteAsync(CancellationToken cancellationToken);
}
public interface IWorkspaceModule
{
    ModuleDescriptor Descriptor { get; }
    IReadOnlyList<IWorkspaceCommand> Commands { get; }
}
public static class ModuleApi { public const int Version = 1; }
