using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runner.Host;

/// <summary>
/// On/off switches for the listener, each source and each handler. "Off" pauses the work. It never drops an event:
/// the dispatcher keeps the event pending in the local store and does not ack it.
/// </summary>
/// <remarks>
/// <see cref="InMemory"/> gives all switches on and saves nothing. The console host uses it.
/// <see cref="Load"/> reads and saves a JSON file. The tray app uses it.
/// </remarks>
public sealed class RunnerSwitches
{
    /// <summary>The sources that the tray app shows.</summary>
    public static readonly IReadOnlyList<string> KnownSources = ["notion", "github", "linear"];

    /// <summary>Handlers that are off when the file does not give a value. Each page edit of this handler can start Claude.</summary>
    public static readonly IReadOnlySet<string> DefaultOffHandlers = new HashSet<string>(StringComparer.Ordinal) { "NotionPageUpdated" };

    /// <summary><c>%LOCALAPPDATA%\Runner\switches.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runner", "switches.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly Lock _lock = new();
    private readonly string? _path;
    private readonly IReadOnlySet<string> _defaultOff;
    private bool _listener = true;
    private readonly Dictionary<string, bool> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _handlers = new(StringComparer.Ordinal);

    private RunnerSwitches(string? path, IReadOnlySet<string> defaultOff)
    {
        _path = path;
        _defaultOff = defaultOff;
    }

    /// <summary>Occurs after a switch changes. It can occur on any thread.</summary>
    public event Action? Changed;

    /// <summary>Returns switches that are all on and that are not saved.</summary>
    public static RunnerSwitches InMemory() => new(null, new HashSet<string>());

    /// <summary>Reads the switches from <paramref name="path"/>. A missing or bad file gives the defaults.</summary>
    public static RunnerSwitches Load(string path)
    {
        var switches = new RunnerSwitches(path, DefaultOffHandlers);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<SwitchFile>(File.ReadAllText(path)) is { } file)
            {
                switches._listener = file.Listener ?? true;
                foreach (var (k, v) in file.Sources ?? []) switches._sources[k] = v;
                foreach (var (k, v) in file.Handlers ?? []) switches._handlers[k] = v;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // The defaults apply. The next change writes a good file.
        }
        return switches;
    }

    /// <summary>The path of the saved file, or null if the switches are not saved.</summary>
    public string? FilePath => _path;

    public bool ListenerEnabled
    {
        get { lock (_lock) return _listener; }
    }

    /// <summary>A source that has no value is on.</summary>
    public bool IsSourceEnabled(string source)
    {
        lock (_lock) return !_sources.TryGetValue(source, out var on) || on;
    }

    /// <summary>The key is the class name of the handler, for example <c>TaskTodoStarted</c>.</summary>
    public bool IsHandlerEnabled(string typeName)
    {
        lock (_lock) return _handlers.TryGetValue(typeName, out var on) ? on : !_defaultOff.Contains(typeName);
    }

    public void SetListener(bool on) => Set(() => { var old = _listener; _listener = on; return old != on; });

    public void SetSource(string source, bool on) =>
        Set(() => { var old = IsSourceEnabled(source); _sources[source] = on; return old != on; });

    public void SetHandler(string typeName, bool on) =>
        Set(() => { var old = IsHandlerEnabled(typeName); _handlers[typeName] = on; return old != on; });

    /// <summary>Completes when the listener is on.</summary>
    public async Task WaitUntilListenerEnabledAsync(CancellationToken cancellationToken)
    {
        while (!ListenerEnabled)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged() => tcs.TrySetResult();
            Changed += OnChanged;
            try
            {
                if (ListenerEnabled) return;
                await tcs.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Changed -= OnChanged;
            }
        }
    }

    private void Set(Func<bool> change)
    {
        bool changed;
        lock (_lock)
        {
            changed = change();
            if (changed) Save();
        }
        if (changed) Changed?.Invoke();
    }

    private void Save()
    {
        if (_path is null) return;
        var file = new SwitchFile(_listener, new(_sources), new(_handlers));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, _path, overwrite: true);
    }

    private sealed record SwitchFile(
        [property: JsonPropertyName("listener")] bool? Listener,
        [property: JsonPropertyName("sources")] Dictionary<string, bool>? Sources,
        [property: JsonPropertyName("handlers")] Dictionary<string, bool>? Handlers);
}
