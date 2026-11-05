using Notion.Domain;
using VSA.Module;

namespace Notion;

// Handlers for Notion page edits. Settings: Modules:Notion.
public sealed class NotionModule : Module<PageUpdatedOptions> {
    public override void Configure(ModuleBuilder module, PageUpdatedOptions settings) {
    }
}
