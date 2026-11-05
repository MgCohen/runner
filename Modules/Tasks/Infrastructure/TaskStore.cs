using System.Text.Json;

namespace Tasks.Infrastructure;

// A task with a running background session.
public sealed record TrackedTask(string PageId, string SessionId, DateTimeOffset StartedAt, string? Link);

// Remembers the running tasks in a JSON file, so a restart does not lose them.
public sealed class TaskStore(string path) {
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    readonly Lock gate = new();

    public IReadOnlyList<TrackedTask> All() {
        lock (gate) {
            return Load();
        }
    }

    public void Upsert(TrackedTask task) {
        ArgumentNullException.ThrowIfNull(task);
        lock (gate) {
            Save([.. Load().Where(t => t.PageId != task.PageId), task]);
        }
    }

    public void Remove(string pageId) {
        lock (gate) {
            Save([.. Load().Where(t => t.PageId != pageId)]);
        }
    }

    List<TrackedTask> Load() => File.Exists(path) ? JsonSerializer.Deserialize<List<TrackedTask>>(File.ReadAllText(path)) ?? [] : [];

    void Save(List<TrackedTask> tasks) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(tasks, Indented));
        File.Move(temp, path, overwrite: true);
    }
}
