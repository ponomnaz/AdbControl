using AdbControl.Application.Workspace;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;

namespace AdbControl.Application.Tools;

public sealed record ToolRegistration(
    ModuleDescriptor Module,
    ToolDescriptor Tool,
    Func<ToolActivationContext, object> CreateContentViewModel,
    bool ShowInNavigation = true,
    bool OpenOnStartup = false,
    bool ReuseExistingTab = true);
