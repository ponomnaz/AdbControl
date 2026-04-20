using AdbControl.Core.Tools;

namespace AdbControl.Application.Tools;

public sealed class ToolCatalog
{
    private readonly IReadOnlyDictionary<string, ToolRegistration> _toolsById;

    public ToolCatalog(IEnumerable<ToolRegistration> registrations)
    {
        var materialized = registrations
            .OrderBy(x => x.Module.SortOrder)
            .ThenBy(x => x.Tool.Category)
            .ThenBy(x => x.Tool.SortOrder)
            .ThenBy(x => x.Tool.Title)
            .ToArray();

        var duplicateIds = materialized
            .GroupBy(x => x.Tool.Id, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"Duplicate tool ids detected: {string.Join(", ", duplicateIds)}.");
        }

        Registrations = materialized;
        NavigationTools = materialized.Where(x => x.ShowInNavigation).ToArray();
        StartupTools = materialized.Where(x => x.OpenOnStartup).ToArray();
        _toolsById = materialized.ToDictionary(x => x.Tool.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolRegistration> Registrations { get; }

    public IReadOnlyList<ToolRegistration> NavigationTools { get; }

    public IReadOnlyList<ToolRegistration> StartupTools { get; }

    public ToolRegistration GetRequired(string toolId)
    {
        if (_toolsById.TryGetValue(toolId, out var registration))
        {
            return registration;
        }

        throw new KeyNotFoundException($"Tool '{toolId}' is not registered.");
    }
}
