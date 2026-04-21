namespace AdbControl.Application.Diagnostics;

public interface ICommandTraceStore
{
    Task<IReadOnlyList<CommandTraceEntry>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task AppendAsync(CommandTraceEntry entry, CancellationToken cancellationToken = default);

    Task RewriteAsync(IReadOnlyList<CommandTraceEntry> entries, CancellationToken cancellationToken = default);
}
