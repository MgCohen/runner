using System.Reflection;
using Runner.Host;
#if RUNNER_HANDLERS
using Runner.Handlers;
#endif

var builder = Host.CreateApplicationBuilder(args);

// User secrets also load outside Development, because the Windows service runs as Production.
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);
builder.Configuration.AddInboxTokenVariable();

builder.Services.AddWindowsService(o => o.ServiceName = "Runner.Host");
builder.Services.AddRunnerHost(builder.Configuration);

#if RUNNER_HANDLERS
// Contract B: Runner.Handlers adds its services, and the host scans it for [On] handlers.
builder.Services.AddHandlers(builder.Configuration);
builder.Services.AddHandlerAssembly(Assembly.Load(new AssemblyName("Runner.Handlers")));
#endif

builder.Build().Run();
