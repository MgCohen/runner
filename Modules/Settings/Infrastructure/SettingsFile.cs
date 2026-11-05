using System.Text.Json;
using System.Text.Json.Nodes;
using Handling.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Settings.Infrastructure;

// Writes <data folder>\settings.json as {"Modules": {"<module>": {"<key>": <value>}}}, then reloads the configuration,
// so IOptionsMonitor of each module sees the new value at once. A broken file is replaced at the next change.
public sealed partial class SettingsFile(IConfiguration configuration, ILogger<SettingsFile> log) {
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    readonly Lock gate = new();

    public string Path => System.IO.Path.Combine(StorePaths.DataDir(configuration), "settings.json");

    public void Set(string module, string key, JsonNode value) {
        lock (gate) {
            var root = Read();
            var modules = root["Modules"] as JsonObject ?? [];
            root["Modules"] = modules;
            var part = modules[module] as JsonObject ?? [];
            modules[module] = part;
            part[key] = value;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(Indented));
            File.Move(temp, Path, overwrite: true);
        }
        (configuration as IConfigurationRoot)?.Reload();
    }

    JsonObject Read() {
        try {
            return File.Exists(Path) && JsonNode.Parse(File.ReadAllText(Path)) is JsonObject root ? root : [];
        } catch (JsonException e) {
            FileNotValid(log, e, Path);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} is not valid. The settings use the defaults.")]
    static partial void FileNotValid(ILogger logger, Exception e, string path);
}
