using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Runner.Handlers.Tasks;

/// <summary>A task with a running session.</summary>
public sealed record TrackedTask(string PageId, string SessionId, DateTimeOffset StartedAt, string? Link);

/// <summary>Remembers the running tasks in a JSON file, so that a restart of the runner does not lose them.</summary>
public sealed class TaskStore
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public TaskStore(IOptions<TaskOptions> options)
    {
        _path = Path.GetFullPath(options.Value.StatePath, AppContext.BaseDirectory);
    }

    public IReadOnlyList<TrackedTask> All()
    {
        lock (_lock) return Load();
    }

    public void Upsert(TrackedTask task)
    {
        lock (_lock)
        {
            var tasks = Load().Where(t => t.PageId != task.PageId).Append(task).ToList();
            Save(tasks);
        }
    }

    public void Remove(string pageId)
    {
        lock (_lock) Save(Load().Where(t => t.PageId != pageId).ToList());
    }

    private List<TrackedTask> Load() =>
        File.Exists(_path) ? JsonSerializer.Deserialize<List<TrackedTask>>(File.ReadAllText(_path)) ?? [] : [];

    private void Save(List<TrackedTask> tasks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(tasks, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
