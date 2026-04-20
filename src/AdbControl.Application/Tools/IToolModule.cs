using AdbControl.Core.Modules;

namespace AdbControl.Application.Tools;

public interface IToolModule
{
    ModuleDescriptor Module { get; }

    IEnumerable<ToolRegistration> GetToolRegistrations();

    IEnumerable<Uri> GetResourceDictionaryUris();
}
