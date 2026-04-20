namespace AdbControl.Core.Modules;

public sealed record ModuleDescriptor(
    string Id,
    string Name,
    string Description,
    int SortOrder = 0);
