using Microsoft.Extensions.DependencyInjection;
using Tasks.Domain;
using Tasks.Infrastructure;
using VSA.Module;

namespace Tasks;

// The Notion Tasks flow: a task that goes to Todo runs Claude, then goes to In Review, or to Waiting if it asked a
// question. When the question is answered, the task runs again with the answer. Settings: Modules:Tasks.
public sealed class TasksModule : Module<TaskOptions> {
    public override void Configure(ModuleBuilder module, TaskOptions settings) {
        module.Services.AddSingleton<TaskRun>();
    }
}
