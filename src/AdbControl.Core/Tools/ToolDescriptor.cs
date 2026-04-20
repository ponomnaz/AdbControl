using AdbControl.Core.Workspace;

namespace AdbControl.Core.Tools;

public sealed record ToolDescriptor(
    string Id,
    string Title,
    string Description,
    ToolCategory Category,
    ToolTargetScope TargetScope,
    WorkspaceHost PreferredHost,
    string Glyph,
    int SortOrder = 0);
