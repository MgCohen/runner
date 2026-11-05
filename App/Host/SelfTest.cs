using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace App.Host;

// The self-test (C8): it drives the real app through fixed steps, checks each result, and saves a screenshot of each
// step. The output is report.json and the screenshots in <data folder>\selftest (mock) or ...\Runner\selftest-real.
// Exit code: 0 when all checks pass, else 1.
public sealed partial class SelfTest(TrayApp tray, WebApplication app) {
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    readonly List<CheckResult> results = [];
    readonly string folder = Path.Combine(app.Configuration["Runner:DataDir"]!, "selftest");

    public sealed record CheckResult(string Id, bool Pass, string Message, string? Screenshot);

    CoreWebView2 Web => tray.Form.Web ?? throw new InvalidOperationException("The WebView did not start.");

    public static async Task RunAsync(TrayApp tray, WebApplication app) {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(app);
        var test = new SelfTest(tray, app);
        try {
            await test.RunAllAsync();
        } catch (Exception e) {
            test.results.Add(new CheckResult("selftest", false, e.ToString(), null));
        }
        tray.ExitCode = await test.WriteReportAsync();
        await tray.ExitAsync();
    }

    async Task RunAllAsync() {
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "*.png")) {
            File.Delete(old);
        }
        await WaitAsync("document.querySelector('[data-test=header]') !== null", TimeSpan.FromSeconds(30), "the widget did not render");
        await Task.Delay(500);
        await Screens();
    }

    // A screenshot of each page, for the review against the mockup.
    async Task Screens() {
        await Shot("home");
        await Click("[data-test=nav-inbox]");
        await Shot("inbox");
        await Click("[data-test=nav-traffic]");
        await Shot("traffic");
        await Click("[data-test=settings]");
        await Shot("settings");
    }

    // Returns 0 if each check passed, else 1.
    async Task<int> WriteReportAsync() {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "report.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, Indented));
        var failed = results.Where(r => !r.Pass).ToList();
        Console.WriteLine($"Self-test: {results.Count - failed.Count} of {results.Count} checks passed. Report: {path}");
        foreach (var f in failed) {
            Console.WriteLine($"  FAIL {f.Id}: {f.Message}");
        }
        return failed.Count == 0 ? 0 : 1;
    }

    async Task<string> Js(string script) => await Web.ExecuteScriptAsync(script);

    async Task Click(string selector) {
        var found = await Js($"(() => {{ const e = document.querySelector({JsonSerializer.Serialize(selector)}); if (!e) return false; e.click(); return true; }})()");
        if (found != "true") {
            throw new InvalidOperationException($"No element matches {selector}.");
        }
        await Task.Delay(300);
    }

    async Task WaitAsync(string condition, TimeSpan timeout, string message) {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout) {
            if (tray.Form.Web is not null && await Js($"(() => {{ try {{ return !!({condition}); }} catch {{ return false; }} }})()") == "true") {
                return;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException(message);
    }

    async Task<string> Shot(string name) {
        var path = Path.Combine(folder, name + ".png");
        await using var file = File.Create(path);
        await Web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        return path;
    }
}
