using Tasks.Domain;
using VSA.Module;

namespace Tasks;

// The Notion Tasks flow: a task that goes to Todo runs Claude, then goes to In Review. Settings: Modules:Tasks.
public sealed class TasksModule : Module<TaskOptions> {
    public override void Configure(ModuleBuilder module, TaskOptions settings) {
    }
}
