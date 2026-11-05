namespace App.Host;

// The command line of the app: --mock, --paused, --selftest, --data <folder>.
public sealed record StartOptions(bool Mock, bool Paused, bool SelfTest, string? DataDir) {
    public static StartOptions Parse(IReadOnlyList<string> args) {
        ArgumentNullException.ThrowIfNull(args);
        string? data = null;
        for (var i = 0; i < args.Count - 1; i++) {
            if (args[i] == "--data") {
                data = args[i + 1];
            }
        }
        return new StartOptions(args.Contains("--mock"), args.Contains("--paused"), args.Contains("--selftest"), data);
    }

    // %LOCALAPPDATA%\Runner, or %LOCALAPPDATA%\Runner\mock in mock mode.
    public string DataFolder => DataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runner", Mock ? "mock" : "");
}
