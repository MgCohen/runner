using System.Diagnostics;
using System.Text.Json;
using App.Views;
using Microsoft.Web.WebView2.Core;

namespace App.Host;

// The self-test (C8): it drives the real app through fixed steps, checks each result, and saves a screenshot of each
// check. It starts paused (--selftest implies --paused, A1). Output: report.json and the screenshots in
// %LOCALAPPDATA%\Runner\mock\selftest (real mode: ...\Runner\selftest-real). Exit code: 0 when all checks pass, else 1 (A37).
// Mock mode runs each check. Real mode only makes the screenshots of the pages, with the Claude handlers off.
public sealed class SelfTest {
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    readonly TrayApp tray;
    readonly WebApplication app;
    readonly List<CheckResult> results = [];
    readonly string folder;
    readonly DateTime started = DateTime.Now;

    SelfTest(TrayApp tray, WebApplication app) {
        this.tray = tray;
        this.app = app;
        var mock = app.Services.GetService<SelfTestProbe>() is not null;
        folder = mock ? Path.Combine(app.Configuration["Runner:DataDir"]!, "selftest")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runner", "selftest-real");
    }

    public sealed record CheckResult(string Id, bool Pass, string Message, string? Screenshot);

    CoreWebView2 Web => tray.Form.Web ?? throw new InvalidOperationException("The WebView did not start.");

    SelfTestProbe? Probe => app.Services.GetService<SelfTestProbe>();

    public static async Task RunAsync(TrayApp tray, WebApplication app) {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(app);
        var test = new SelfTest(tray, app);
        try {
            await test.RunAllAsync();
        } catch (Exception e) {
            test.results.Add(new CheckResult("selftest", false, e.ToString(), null));
        }
        tray.ExitCode = WriteReport(test.results, test.folder);
        await tray.ExitAsync();
    }

    // Writes report.json and returns the exit code: 0 if each check passed, else 1.
    public static int WriteReport(IReadOnlyList<CheckResult> results, string folder) {
        ArgumentNullException.ThrowIfNull(results);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, Indented));
        var failed = results.Where(r => !r.Pass).ToList();
        Console.WriteLine($"Self-test: {results.Count - failed.Count} of {results.Count} checks passed. Report: {path}");
        foreach (var f in failed) {
            Console.WriteLine($"  FAIL {f.Id}: {f.Message}");
        }
        return failed.Count == 0 && results.Count > 0 ? 0 : 1;
    }

    async Task RunAllAsync() {
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "*.png")) {
            File.Delete(old);
        }
        await Until("document.querySelector('[data-test=header]') !== null", "the widget renders");
        await Task.Delay(500);
        if (Probe is null) {
            await Screens("real");
            results.Add(new CheckResult("screens", true, "Screenshots of each page (real mode).", null));
            return;
        }
        await Checks();
        await Screens("light");
        await SetTheme("Dark");
        await Screens("dark");
        await SetTheme("System");
    }

    async Task Checks() {
        var probe = Probe!;
        long task = 0, github = 0;
        await Check("A1", "With --paused the switch is off, the subtitle says Paused, and no event runs", async () => {
            await Until("text('[data-test=subtitle]') === 'Paused'", "subtitle Paused");
            Expect(await Js("document.querySelector('[data-test=listener-switch]').classList.contains('on')") == "false", "the switch is off");
            task = await probe.SendTaskAsync();
            github = await probe.SendAsync("github", "push");
            await Task.Delay(1000);
            await Click("[data-test=nav-inbox]");
            await Until("text('[data-test=summary]') === '0 match · 0 in store'", "no event in the store");
        });
        await Check("A2", "The listener turns on: the task runs to In Review, and the Inbox shows it as done", async () => {
            await Click("[data-test=listener-switch]");
            await Until("text('[data-test=subtitle]') === 'Connected · mock'", "subtitle Connected");
            await Until($"rowText({task}).startsWith('#{task} notion') && rowText({task}).includes('done')", "the task event is done");
            await OpenEvent(task);
            await Until("text('[data-test=results]').includes('TaskTodoStarted') && text('[data-test=results]').includes('done (')", "TaskTodoStarted done");
        });
        await Check("A18", "Catch-up gets the events that came while paused, runs them and acks each one", async () => {
            await Click("[data-test=nav-inbox]");
            await Until($"rowText({github}).includes('done · no handler')", "the GitHub event is done");
            await OpenEvent(github);
            await Until("acked() !== 'not yet'", "the event is acked");
        });
        await Check("A29", "The tray icon goes gray, amber, green", () => {
            Expect(Seen("Paused", "Connecting", "Connected"), $"icon states {string.Join(" > ", tray.IconHistory)}");
            return Task.CompletedTask;
        });
        await Check("A8", "A test-only handler shows under its source", async () => {
            await Click("[data-test=nav-home]");
            await Until("document.querySelector('[data-test=source-mock] [data-test=handler-MockEcho]') !== null", "MockEcho under mock");
        });
        await Check("A6", "A search shows only the matching sources and handlers, unfolded", async () => {
            await Type("[data-test=search]", "fail");
            await Until("names('.hc .ell:first-child').join() === 'MockAlwaysFails'", "only MockAlwaysFails");
            await Type("[data-test=search]", "");
        });
        await Check("A4", "A handler that is off: the event is done, and its detail shows skipped (off)", async () => {
            await Click("[data-test=handler-switch-MockEcho]");
            var id = await probe.SendAsync("mock", "echo");
            await OpenEventWhenDone(id);
            await Until("text('[data-test=results]').includes('skipped (off)')", "skipped (off)");
            await Click("[data-test=nav-home]");
            await Click("[data-test=handler-switch-MockEcho]");
        });
        await Check("A5", "A source that is off: the event is done, and its detail shows skipped (source off)", async () => {
            await Click("[data-test=nav-home]");
            await Click("[data-test=source-switch-mock]");
            var id = await probe.SendAsync("mock", "echo");
            await OpenEventWhenDone(id);
            await Until("text('[data-test=results]').includes('skipped (source off)')", "skipped (source off)");
            await Click("[data-test=nav-home]");
            await Click("[data-test=source-switch-mock]");
        });
        await Check("A7", "Open in inbox opens the Inbox with the filter Handler = that handler", async () => {
            await Click("[data-test=nav-home]");
            await Click("[data-test=handler-MockEcho]");
            await Until("text('.dh-title') === 'MockEcho'", "the handler page");
            await Click("[data-test=open-in-inbox]");
            await Until("location.pathname === '/inbox' && text('[data-test=active-chips]') === 'MockEcho'", "the inbox with the MockEcho chip");
        });
        long failing = 0, permanent = 0, timeout = 0;
        await Check("A33", "An always failing handler runs 5 times, then the event is failed and acked", async () => {
            failing = await probe.SendAsync("mock", "fail");
            await OpenEventWhenDone(failing);
            await Until("text('.badge') === 'failed' && field('Attempts') === '5' && text('[data-test=results]').includes('failed: mock failure')", "5 attempts and failed");
            await Until("acked() !== 'not yet'", "acked");
        });
        await Check("A34", "A permanent failure is final at once, with no retry", async () => {
            permanent = await probe.SendAsync("mock", "permanent");
            await OpenEventWhenDone(permanent);
            await Until("text('.badge') === 'failed' && field('Attempts') === '1'", "one attempt");
        });
        await Check("A35", "A run longer than the timeout is failed: timeout, and the handler runs again", async () => {
            timeout = await probe.SendAsync("mock", "timeout");
            await UntilRuns(probe, $"MockTimeout:{timeout}", 2);
            await OpenEvent(timeout);
            await Until("text('[data-test=results]').includes('failed: timeout')", "failed: timeout");
        });
        await Check("A38", "Two handlers on two events: the fast one finishes both before the slow one finishes the first", async () => {
            await probe.SetDelayAsync("MockSlow", 1500);
            var first = await probe.SendAsync("mock", "pair");
            var second = await probe.SendAsync("mock", "pair");
            await UntilRuns(probe, $"MockSlow:{second}", 1);
            var runs = (await probe.RunsAsync()).ToList();
            Expect(runs.IndexOf($"MockFast:{second}") < runs.IndexOf($"MockSlow:{first}"), string.Join(", ", runs));
            await probe.SetDelayAsync("MockSlow", 100);
        });
        await Check("A36", "A duplicate of a done event: one event, nothing runs, and it is acked again", async () => {
            var id = await probe.SendAsync("mock", "echo");
            await OpenEventWhenDone(id);
            await Until("acked() !== 'not yet'", "acked");
            var acked = await Js("acked()");
            await Task.Delay(1100);
            await probe.SendAgainAsync(id);
            await Task.Delay(1500);
            await OpenEvent(id);
            await Until($"acked() !== {acked}", "a second ack");
            Expect((await probe.RunsAsync()).Count(r => r == $"MockEcho:{id}") == 1, "MockEcho ran one time");
        });
        await Check("A9", "State = failed and Source = mock: only failed mock events, and two chips", async () => {
            await Click("[data-test=nav-inbox]");
            await ClearFilters();
            await Click("[data-test=filter]");
            await Click("[data-test=filter-panel] [data-test=chip-failed]");
            await Click("[data-test=filter-panel] [data-test=chip-mock]");
            await Until($"rows().length >= 2 && rows().every(r => r.includes(' mock ') && r.includes('failed ·')) && rows().some(r => r.startsWith('#{failing} '))", "failed mock rows");
            Expect(await Js("document.querySelectorAll('[data-test=active-chips] .ac').length") == "2", "two chips");
            await Shot("inbox-filter");
        });
        await Check("A10", "The filter panel is 420 px or less; it scrolls, and Clear all and Done stay visible", async () => {
            var check = await Js("""(() => { const p = document.querySelector('.pop').getBoundingClientRect(); const f = document.querySelector('.pf').getBoundingClientRect(); return p.height <= 420.5 && f.bottom <= p.bottom + 0.5 && f.top >= p.top; })()""");
            Expect(check == "true", "panel and footer bounds");
            await Click("[data-test=filter-done]");
            await ClearFilters();
        });
        await Check("A25", "120 events: Load older events two times shows all 120, newest first, and the button goes away", async () => {
            long last = 0;
            for (var i = 0; i < 120; i++) {
                last = await probe.SendAsync("bulk", "push");
            }
            await Click("[data-test=filter]");
            await Until("document.querySelector('[data-test=filter-panel] [data-test=chip-bulk]') !== null", "the bulk chip");
            await Click("[data-test=filter-panel] [data-test=chip-bulk]");
            await Click("[data-test=filter-done]");
            await Until("text('[data-test=summary]').startsWith('120 match')", "120 bulk events stored", TimeSpan.FromSeconds(40));
            await Click("[data-test=load-older]");
            await Click("[data-test=load-older]");
            await Until("rows().length === 120 && document.querySelector('[data-test=load-older]') === null", "120 rows and no button");
            Expect(await Js("(() => { const ids = rows().map(r => +r.slice(1, r.indexOf(' '))); return ids.every((v, i) => i === 0 || ids[i - 1] > v); })()") == "true", "newest first");
            await ClearFilters();
        });
        await Check("A26", "A search for task shows only events whose ID, source or type contains task", async () => {
            await probe.SendAsync("mock", "task");
            await probe.SendAsync("mock", "task");
            await Click("[data-test=nav-inbox]");
            await Type("[data-test=search]", "task");
            await Until("rows().length >= 2 && rows().every(r => r.split(' · ')[0].toLowerCase().includes('task'))", "only task rows");
            await Type("[data-test=search]", "");
        });
        await Check("A23", "The network goes down, then comes back: Error: No network, Connecting…, Connected", async () => {
            await probe.SetNetworkAsync(false);
            await Until("text('[data-test=subtitle]') === 'Error: No network'", "Error: No network");
            await probe.SetNetworkAsync(true);
            await Until("text('[data-test=subtitle]') === 'Connected · mock'", "Connected again");
            var seen = probe.Subtitles.SkipWhile(s => s != "Error: No network").ToList();
            Expect(seen.Contains("Connecting…") && seen[^1] == "Connected · mock", string.Join(" > ", seen));
            Expect(Seen("Error", "Connected"), $"icon states {string.Join(" > ", tray.IconHistory)} (A29 red)");
        });
        await Check("A11", "Result = error shows only errors, and the summary counts them in red", async () => {
            await Click("[data-test=nav-traffic]");
            await Click("[data-test=filter]");
            await Click("[data-test=filter-panel] [data-test=chip-error]");
            await Click("[data-test=filter-done]");
            await Until("document.querySelectorAll('.ev').length > 0 && [...document.querySelectorAll('.ev .nowrap')].every(e => e.classList.contains('danger'))", "only errors");
            Expect(await Js("document.querySelector('[data-test=errors]').classList.contains('danger')") == "true", "the error count is red");
            await ClearFilters();
        });
        await Check("A12", "Capture bodies off: an HTTP entry says Body not kept, with a link to Settings", async () => {
            await OpenTraffic("GET", "", null);
            await Until("document.querySelector('[data-test=no-body]') !== null", "Body not kept");
            await Shot("traffic-detail");
            await Click("[data-test=go-settings]");
            await Until("location.pathname === '/settings'", "the Settings page");
        });
        await Check("A27", "Capture bodies on: the kept body of a Notion call shows the token as •••", async () => {
            await Click("[data-test=switch-captureBodies]");
            await Until("document.querySelector('[data-test=switch-captureBodies]').classList.contains('on')", "capture on");
            var id = await probe.SendTaskAsync();
            await OpenEventWhenDone(id);
            await OpenTraffic("GET", "/v1/pages/", "markdown");
            await Until("text('[data-test=body]').includes('\"token\":\"•••\"') && !text('[data-test=body]').includes('abc')", "the token is hidden");
            await Click("[data-test=settings]");
            await Click("[data-test=switch-captureBodies]");
        });
        await Check("A20", "Entries to keep = 200: Traffic says … of 200 kept", async () => {
            await GoSettings();
            await Click("[data-test=seg-trafficKeep] [data-test=opt-200]");
            await Click("[data-test=nav-traffic]");
            await Until("text('[data-test=summary]').includes(' of 200 kept')", "of 200 kept");
            await GoSettings();
            await Click("[data-test=seg-trafficKeep] [data-test=opt-500]");
        });
        await Check("A21", "Theme = Dark: the page uses the dark tokens at once, and settings.json has \"theme\": \"Dark\"", async () => {
            await SetTheme("Dark");
            Expect(await Js("getComputedStyle(document.body).backgroundColor") == "\"rgb(48, 48, 46)\"", "dark background");
            var file = await File.ReadAllTextAsync(Path.Combine(app.Configuration["Runner:DataDir"]!, "settings.json"));
            Expect(file.Contains("\"theme\": \"Dark\"", StringComparison.Ordinal), "settings.json has the theme");
            await Shot("A21-dark");
        });
        await Check("A16", "A second theme file: the computed colors equal its token values", async () => {
            await SetTheme("Contrast");
            await Until("getComputedStyle(document.querySelector('.title')).color === 'rgb(0, 0, 0)'", "text-primary #000000");
            await Click("[data-test=nav-home]");
            Expect(await Js("getComputedStyle(document.querySelector('.sec')).backgroundColor") == "\"rgb(242, 242, 242)\"", "surface-1 #f2f2f2");
            await Shot("A16-contrast");
            await SetTheme("System");
        });
        await Check("A22", "A setting with the later tag does not change, and the file does not change", async () => {
            await GoSettings();
            var path = Path.Combine(app.Configuration["Runner:DataDir"]!, "settings.json");
            var before = await File.ReadAllTextAsync(path);
            await Click("[data-test=switch-startWithWindows]");
            await Task.Delay(500);
            Expect(await Js("document.querySelector('[data-test=switch-startWithWindows]').classList.contains('on')") == "false", "still off");
            Expect(await File.ReadAllTextAsync(path) == before, "the file did not change");
        });
        await Check("A15", "Content that grows does not move an element sideways", async () => {
            await Click("[data-test=nav-inbox]");
            var before = await Js("document.querySelector('.bar').getBoundingClientRect().right");
            await Click("[data-test=nav-home]");
            await Click("[data-test=nav-inbox]");
            Expect(await Js("getComputedStyle(document.getElementById('page')).scrollbarGutter") == "\"stable\"", "scrollbar-gutter: stable");
            Expect(await Js("document.querySelector('.bar').getBoundingClientRect().right") == before, "the search bar does not move");
        });
        await Check("A32", "storeDays 7 to 1 deletes the events older than 1 day at once", async () => {
            var old = await probe.SendAsync("github", "old", null, ageDays: 3);
            await OpenEventWhenDone(old);
            await Until("acked() !== 'not yet'", "acked");
            await GoSettings();
            await Click("[data-test=seg-storeDays] [data-test=opt-1]");
            await Click("[data-test=nav-inbox]");
            await Type("[data-test=search]", $"#{old} ");
            await Until("text('[data-test=summary]').startsWith('0 match')", "the old event is deleted");
            await Type("[data-test=search]", "");
            await GoSettings();
            await Click("[data-test=seg-storeDays] [data-test=opt-7]");
        });
        await Check("A17", "Mock mode: no host outside *.mock.invalid, and no claude process starts", async () => {
            await Click("[data-test=nav-traffic]");
            await Until("rows().length > 0", "traffic rows");
            var labels = JsonSerializer.Deserialize<string[]>(await Js("[...document.querySelectorAll('.ev .secondary')].map(e => e.textContent)"))!;
            var hosts = labels.Where(l => !l.StartsWith("event ", StringComparison.Ordinal)).Select(l => l.Split(' ')[0]).Distinct().ToList();
            Expect(hosts.All(h => h.EndsWith(".mock.invalid", StringComparison.Ordinal)), string.Join(", ", hosts));
            var claude = Process.GetProcessesByName("claude").Count(p => Started(p) >= started);
            Expect(claude == 0, $"{claude} claude processes started");
        });
    }

    // A screenshot of each page, as the reference images of docs/specs/images.
    async Task Screens(string theme) {
        await Click("[data-test=nav-home]");
        await Shot($"home-{theme}");
        if (await Js("document.querySelector('.hc[data-test^=handler-]') !== null") == "true") {
            await Click(".hc[data-test^=handler-]");
            await Until("document.querySelector('.dh-title') !== null", "a handler page");
            await Shot($"handler-detail-{theme}");
        }
        await Click("[data-test=nav-inbox]");
        await Shot($"inbox-{theme}");
        if (await Js("document.querySelector('.ev') !== null") == "true") {
            await Click(".ev");
            await Until("document.querySelector('.badge') !== null", "an event page");
            await Shot($"event-detail-{theme}");
        }
        await Click("[data-test=nav-traffic]");
        await Shot($"traffic-{theme}");
        await GoSettings();
        await Shot($"settings-{theme}");
        await Click("[data-test=nav-home]");
    }

    async Task Check(string id, string description, Func<Task> body) {
        string? shot = null;
        try {
            await body();
            shot = await Shot(id);
            results.Add(new CheckResult(id, true, description, shot));
        } catch (Exception e) {
            try {
                shot = await Shot(id + "-fail");
            } catch (Exception) {
            }
            results.Add(new CheckResult(id, false, $"{description}: {e.Message}", shot));
        }
    }

    static void Expect(bool condition, string message) {
        if (!condition) {
            throw new InvalidOperationException(message);
        }
    }

    bool Seen(params string[] states) {
        var icons = tray.IconHistory.ToList();
        var at = 0;
        foreach (var state in states) {
            at = icons.IndexOf(state, at);
            if (at < 0) {
                return false;
            }
        }
        return true;
    }

    async Task UntilRuns(SelfTestProbe probe, string run, int count) {
        var watch = Stopwatch.StartNew();
        while ((await probe.RunsAsync()).Count(r => r == run) < count) {
            Expect(watch.Elapsed < Wait, $"{run} ran {count} times");
            await Task.Delay(200);
        }
    }

    async Task OpenEvent(long id) {
        await Nav($"/inbox/{id}");
        await Until($"text('.dh-title').startsWith('#{id} ')", $"the page of event {id}");
    }

    async Task OpenEventWhenDone(long id) {
        await Click("[data-test=nav-inbox]");
        await ClearFilters();
        await Type("[data-test=search]", $"#{id} ");
        await Until($"rowText({id}) !== null && (rowText({id}).includes('done ·') || rowText({id}).includes('failed ·'))", $"event {id} finished");
        await Type("[data-test=search]", "");
        await OpenEvent(id);
    }

    // Opens the newest traffic entry of a kind whose label contains one text and not another.
    async Task OpenTraffic(string kind, string include, string? exclude) {
        await Click("[data-test=nav-traffic]");
        await ClearFilters();
        var match = $"e => e.textContent.trim().startsWith('{kind} ') && e.textContent.includes({JsonSerializer.Serialize(include)}) " + (exclude is null ? "" : $" && !e.textContent.includes({JsonSerializer.Serialize(exclude)})");
        await Until($"[...document.querySelectorAll('.ev')].some({match})", $"a {kind} entry");
        await Js($"[...document.querySelectorAll('.ev')].find({match}).click()");
        await Until("document.querySelector('.badge') !== null", "the entry page");
    }

    async Task ClearFilters() {
        if (await Js("document.querySelector('[data-test=active-chips]') !== null") == "true") {
            await Click("[data-test=filter]");
            await Click("[data-test=clear-all]");
            await Click("[data-test=filter-done]");
        }
    }

    async Task GoSettings() {
        if (await Js("location.pathname") != "\"/settings\"") {
            await Click("[data-test=settings]");
        }
        await Until("document.querySelector('[data-test=setting-theme]') !== null", "the Settings page");
    }

    async Task SetTheme(string theme) {
        await GoSettings();
        await Click($"[data-test=seg-theme] [data-test=opt-{theme}]");
        var expected = theme == "System" ? "null" : $"'{theme.ToLowerInvariant()}'";
        await Until($"document.documentElement.getAttribute('data-theme') === {expected}", $"theme {theme}");
        await Task.Delay(300);
    }

    // Blazor navigation, as a click on a link.
    async Task Nav(string path) {
        await Js($"(() => {{ const a = document.createElement('a'); a.href = '{path}'; document.body.appendChild(a); a.click(); a.remove(); }})()");
        await Task.Delay(300);
    }

    // The helpers of the checks: text, rows, rowText, names, field, acked.
    const string Helpers = """
        window.text = s => document.querySelector(s)?.textContent.trim() ?? null;
        window.rows = () => [...document.querySelectorAll('.ev')].map(e => e.textContent.replace(/\s+/g, ' ').trim());
        window.rowText = id => rows().find(r => r.startsWith('#' + id + ' ')) ?? null;
        window.names = s => [...document.querySelectorAll(s)].map(e => e.textContent.trim());
        window.field = k => [...document.querySelectorAll('.kr')].find(r => r.querySelector('.k')?.textContent.trim() === k)?.querySelector('.v')?.textContent.trim() ?? null;
        window.acked = () => field('Acked');
        """;

    async Task<string> Js(string script) {
        await Web.ExecuteScriptAsync(Helpers);
        return await Web.ExecuteScriptAsync(script);
    }

    async Task Click(string selector) {
        var found = await Js($"(() => {{ const e = document.querySelector({JsonSerializer.Serialize(selector)}); if (!e) return false; e.click(); return true; }})()");
        Expect(found == "true", $"no element matches {selector}");
        await Task.Delay(250);
    }

    // Types the text into an input: the input event makes Blazor read the value.
    async Task Type(string selector, string text) {
        await Js($"(() => {{ const e = document.querySelector({JsonSerializer.Serialize(selector)}); e.value = {JsonSerializer.Serialize(text)}; e.dispatchEvent(new Event('input', {{ bubbles: true }})); }})()");
        await Task.Delay(400);
    }

    async Task Until(string condition, string message, TimeSpan? timeout = null) {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < (timeout ?? Wait)) {
            if (tray.Form.Web is not null && await Js($"(() => {{ try {{ return !!({condition}); }} catch {{ return false; }} }})()") == "true") {
                return;
            }
            await Task.Delay(150);
        }
        throw new TimeoutException($"Timeout: {message}");
    }

    async Task<string> Shot(string name) {
        var path = Path.Combine(folder, name + ".png");
        await using var file = File.Create(path);
        await Web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        return path;
    }

    static DateTime Started(Process process) {
        try {
            return process.StartTime;
        } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) {
            return DateTime.MinValue;
        }
    }
}
