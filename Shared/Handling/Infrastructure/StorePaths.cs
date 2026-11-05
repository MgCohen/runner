using Microsoft.Extensions.Configuration;

namespace Handling.Infrastructure;

// The data folder of the app: Runner:DataDir, else %LOCALAPPDATA%\Runner. The host sets the mock folder in mock mode.
public static class StorePaths {
    public static string DataDir(IConfiguration configuration) =>
        configuration["Runner:DataDir"] is { Length: > 0 } dir ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runner");

    public static bool IsMock(IConfiguration configuration) => configuration.GetValue<bool>("Runner:Mock");
}
