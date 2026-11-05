using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotionApi.Infrastructure;
using VSA.Module;

namespace NotionApi;

// The Notion API client that more than one business module uses. Settings: Modules:NotionApi.
// The token comes from Modules:NotionApi:Token or the NOTION_API_TOKEN environment variable (op run), never from git.
// Mock mode: a fake Notion with one task page. Nothing leaves the PC.
public sealed class NotionApiModule : Module<NotionOptions> {
    public override void Configure(ModuleBuilder module, NotionOptions settings) {
        var services = module.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PageCooldown>();
        services.AddSingleton(new NotionToken(settings.Token is { Length: > 0 } token ? token : module.Configuration["NOTION_API_TOKEN"]));
        if (module.Configuration.GetValue<bool>("Runner:Mock")) {
            services.AddSingleton<MockNotion>();
            services.AddHttpClient<INotionClient, NotionClient>(http => http.BaseAddress = MockNotion.BaseAddress)
                .ConfigurePrimaryHttpMessageHandler(provider => new MockNotionHandler(provider.GetRequiredService<MockNotion>()));
        } else {
            services.AddHttpClient<INotionClient, NotionClient>(http => http.BaseAddress = new Uri(settings.BaseUrl));
        }
    }
}
