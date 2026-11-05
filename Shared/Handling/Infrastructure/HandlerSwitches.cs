using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Handling.Infrastructure;

// The on/off switches of the listener, each source and each handler, in <data folder>\switches.json.
// A missing or broken file gives the defaults: the listener and the sources are on, a handler uses its DefaultOn.
// "Off" for a source or a handler means: the event is skipped and acked. Only the listener switch makes events wait.
public sealed partial class HandlerSwitches {
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly Lock gate = new();
    readonly string path;
    readonly HandlerCatalog catalog;
    readonly Dictionary<string, bool> sources = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, bool> handlers = new(StringComparer.Ordinal);
    bool listener = true;

    public HandlerSwitches(IConfiguration configuration, HandlerCatalog catalog, ILogger<HandlerSwitches> logger) {
        this.catalog = catalog;
        path = Path.Combine(StorePaths.DataDir(configuration), "switches.json");
        try {
            if (File.Exists(path) && JsonSerializer.Deserialize<SwitchFile>(File.ReadAllText(path)) is { } file) {
                listener = file.Listener ?? true;
                foreach (var (key, on) in file.Sources ?? []) {
                    sources[key] = on;
                }
                foreach (var (key, on) in file.Handlers ?? []) {
                    handlers[key] = on;
                }
            }
        } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) {
            FileNotValid(logger, e, path);
        }
    }

    // Occurs after a switch changes. It can occur on any thread.
    public event Action? Changed;

    public string FilePath => path;

    public bool Listener {
        get {
            lock (gate) {
                return listener;
            }
        }
    }

    public bool IsSourceOn(string source) {
        lock (gate) {
            return !sources.TryGetValue(source, out var on) || on;
        }
    }

    public bool IsHandlerOn(string key) {
        lock (gate) {
            if (handlers.TryGetValue(key, out var on)) {
                return on;
            }
        }
        return catalog.Find(key)?.DefaultOn ?? true;
    }

    // "source off" or "off" if the handler must skip an event of this source, else null.
    public string? OffReason(string key, string source) =>
        !IsSourceOn(source) ? "source off" : !IsHandlerOn(key) ? "off" : null;

    // save false: the change is for this run only, for example --paused at start.
    public void SetListener(bool on, bool save = true) => Set(() => Swap(ref listener, on), save);

    public void SetSource(string source, bool on) => Set(() => {
        var old = IsSourceOnLocked(source);
        sources[source] = on;
        return old != on;
    }, save: true);

    public void SetHandler(string key, bool on) {
        var old = IsHandlerOn(key);
        Set(() => {
            handlers[key] = on;
            return old != on;
        }, save: true);
    }

    // Completes when the listener is on.
    public async Task WaitForListenerAsync(CancellationToken ct) {
        while (!Listener) {
            var on = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => on.TrySetResult();
            Changed += OnChanged;
            try {
                if (Listener) {
                    return;
                }
                await on.Task.WaitAsync(ct);
            } finally {
                Changed -= OnChanged;
            }
        }
    }

    bool IsSourceOnLocked(string source) => !sources.TryGetValue(source, out var on) || on;

    static bool Swap(ref bool field, bool value) {
        var changed = field != value;
        field = value;
        return changed;
    }

    void Set(Func<bool> change, bool save) {
        bool changed;
        lock (gate) {
            changed = change();
            if (changed && save) {
                Save();
            }
        }
        if (changed) {
            Changed?.Invoke();
        }
    }

    void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new SwitchFile(listener, new(sources), new(handlers)), Json));
        File.Move(temp, path, overwrite: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} is not valid. The switches use the defaults.")]
    static partial void FileNotValid(ILogger logger, Exception e, string path);

    sealed record SwitchFile(
        [property: JsonPropertyName("listener")] bool? Listener,
        [property: JsonPropertyName("sources")] Dictionary<string, bool>? Sources,
        [property: JsonPropertyName("handlers")] Dictionary<string, bool>? Handlers);
}
