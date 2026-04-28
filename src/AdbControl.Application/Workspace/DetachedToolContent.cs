using AdbControl.Application.Tools;

namespace AdbControl.Application.Workspace;

public sealed record DetachedToolContent(
    ToolRegistration Registration,
    object ContentViewModel);
