using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace App.Host;

// App.Host.exe warmup: install.ps1 runs it after it copies the files. It loads each DLL of the app one time and
// exits, with no window. The virus scanner examines a new DLL when a process loads it, so the scan occurs at
// install time and not at the first start. A native DLL of another processor type (runtimes\<rid>) is not loaded.
public static class Warmup {
    public static int Run() {
        ParentConsole.Attach();
        var watch = Stopwatch.StartNew();
        var folder = AppContext.BaseDirectory;
        var rid = RuntimeInformation.RuntimeIdentifier;
        int managed = 0, native = 0, failed = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(folder, path);
            if (relative.StartsWith("runtimes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !relative.Contains(Path.DirectorySeparatorChar + rid + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (IsManaged(path)) {
                try {
                    Assembly.LoadFrom(path);
                    managed++;
                } catch (Exception e) when (e is BadImageFormatException or FileLoadException or FileNotFoundException) {
                    failed++;
                }
            } else if (NativeLibrary.TryLoad(path, out _)) {
                native++;
            } else {
                failed++;
            }
        }
        Console.WriteLine($"Warm-up: {managed} managed and {native} native DLLs loaded, {failed} not loaded, in {watch.Elapsed.TotalSeconds:0} s.");
        return 0;
    }

    static bool IsManaged(string path) {
        try {
            AssemblyName.GetAssemblyName(path);
            return true;
        } catch (BadImageFormatException) {
            return false;
        }
    }
}
