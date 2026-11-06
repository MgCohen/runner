using System.Reflection;
using VSA.Module;

namespace App.Host;

// The modules of the runner, from the list that the build records in App.Host (App.Host.csproj, RunnerModuleRoot).
// VSA ModuleDiscovery reads deps.json, and a one-file app (install.ps1) has no deps.json that it can read.
// The order is the order of ModuleDiscovery: the Shared modules first, then by name.
public static class ModuleList {
    public const string Key = "RunnerModule";

    public static IReadOnlyList<string> Names() =>
        [.. typeof(ModuleList).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => a.Key == Key && a.Value is { Length: > 0 })
            .Select(a => a.Value!)];

    public static VSA.Module.Module[] Create() {
        var roots = Names()
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .Where(ModuleRoot.IsRoot)
            .Select(ModuleRoot.Of)
            .OrderBy(root => !root.IsShared)
            .ThenBy(root => root.Name, StringComparer.Ordinal);
        return [.. ModuleDiscovery.CreateModules(roots)];
    }
}
