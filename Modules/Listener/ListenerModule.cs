using AppSettings;
using AppSettings.Domain;
using Listener.Infrastructure;
using Listener.Slices.Listen;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSA.Module;

namespace Listener;

// The connection to the inbox Worker: socket, catch-up, reconnect and ack. Settings: Modules:Listener.
// The token comes from Modules:Listener:Token or INBOX_TOKEN (op run). Mock mode: a fake inbox in this process.
public sealed class ListenerModule : Module<ListenerOptions> {
    public override void Configure(ModuleBuilder module, ListenerOptions settings) {
        ArgumentNullException.ThrowIfNull(settings);
        var services = module.Services;
        var mock = module.Configuration.GetValue<bool>("Runner:Mock");
        services.PostConfigure<ListenerOptions>(o => {
            if (string.IsNullOrWhiteSpace(o.Token)) {
                o.Token = module.Configuration["INBOX_TOKEN"];
            }
            if (mock) {
                o.BaseUrl = MockInbox.BaseAddress;
                o.Token = "mock-token";
                o.Consumer = "mock";
            }
        });
        services.AddSingleton<ListenerStatus>();
        services.AddSingleton<AckSignal>();
        if (mock) {
            services.AddSingleton<MockInbox>();
            services.AddSingleton<IInboxSocket>(provider => provider.GetRequiredService<MockInbox>());
            services.AddSingleton<INetworkState>(provider => provider.GetRequiredService<MockInbox>());
            services.AddHttpClient<InboxClient>().ConfigurePrimaryHttpMessageHandler(provider => provider.GetRequiredService<MockInbox>().Api());
        } else {
            services.AddSingleton<IInboxSocket, WebSocketInbox>();
            services.AddSingleton<INetworkState, SystemNetwork>();
            services.AddHttpClient<InboxClient>();
        }
        services.AddHostedService<SocketListener>();
        services.AddHostedService<AckService>();
        module.AddSettingsPart(
            new SettingRow("listenerOnStart", SettingGroups.AtStart, "Listener on at start", SettingKind.Switch, "true") {
                Description = "Off: start paused",
                Order = 0,
            },
            new SettingRow("consumer", SettingGroups.About, "Consumer", SettingKind.Info, "") {
                Order = 0,
                Value = () => mock ? "mock" : ConsumerName.Resolve(settings.Consumer),
            },
            new SettingRow("inbox", SettingGroups.About, "Inbox", SettingKind.Info, "") {
                Order = 1,
                Value = () => (mock ? MockInbox.BaseAddress : settings.BaseUrl).Host,
            });
    }
}
