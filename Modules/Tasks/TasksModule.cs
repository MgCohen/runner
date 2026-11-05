using Handling.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tasks.Domain;
using Tasks.Infrastructure;
using Tasks.Slices.StartTodoTask;
using VSA.Module;

namespace Tasks;

// The Notion Tasks flow: a task that goes to Todo runs Claude, then goes to In Review. Settings: Modules:Tasks.
public sealed class TasksModule : Module<TaskOptions> {
    public override void Configure(ModuleBuilder module, TaskOptions settings) {
        var dataDir = StorePaths.DataDir(module.Configuration);
        module.Services.AddSingleton(provider => new TaskStore(Path.Combine(dataDir, "tasks.json")));
        module.Services.AddHostedService<TaskSessionWatcher>();
    }
}
