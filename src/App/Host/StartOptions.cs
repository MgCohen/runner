namespace App.Host;

// The command line of the app: --mock, --real, --paused, --selftest, --restart, --data <folder>.
// With no --mock or --real, the app uses the saved mode (ModeFile): the mode that the Settings page or the CLI set last.
public sealed record StartOptions(bool Mock, bool Paused, bool SelfTest, string? DataDir, bool Restart = false) {
    public static StartOptions Parse(IReadOnlyList<string> args, Func<bool>? savedMock = null) {
        ArgumentNullException.ThrowIfNull(args);
        string? data = null;
        for (var i = 0; i < args.Count - 1; i++) {
            if (args[i] == "--data") {
                data = args[i + 1];
            }
        }
        // The self-test starts paused (A1).
        var selfTest = args.Contains("--selftest");
        var mock = args.Contains("--mock") || (!args.Contains("--real") && (savedMock ?? ModeFile.ReadMock)());
        return new StartOptions(mock, args.Contains("--paused") || selfTest, selfTest, data, args.Contains("--restart"));
    }

    // %LOCALAPPDATA%\Runner, or %LOCALAPPDATA%\Runner\mock in mock mode. The mock self-test has its own folder,
    // %LOCALAPPDATA%\Runner\selftest-mock: at start it deletes the store, so it must not use the data of a mock app that runs.
    public string DataFolder => DataDir ?? Path.Combine(ModeFile.BaseFolder, Mock ? (SelfTest ? "selftest-mock" : "mock") : "");
}

// The saved mode: %LOCALAPPDATA%\Runner\mode.txt holds "mock" or "real". No file is real mode.
public static class ModeFile {
    public static string BaseFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runner");

    static string PathOf => Path.Combine(BaseFolder, "mode.txt");

    public static bool ReadMock() {
        try {
            return File.Exists(PathOf) && File.ReadAllText(PathOf).Trim().Equals("mock", StringComparison.OrdinalIgnoreCase);
        } catch (IOException) {
            return false;
        }
    }

    public static void Write(bool mock) {
        Directory.CreateDirectory(BaseFolder);
        File.WriteAllText(PathOf, mock ? "mock" : "real");
    }
}
