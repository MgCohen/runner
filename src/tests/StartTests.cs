using App.Host;
using App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests;

// The start options, the saved mode and the commands of the desktop app.
public sealed class StartTests {
    [TestCase(new string[0], false, false)]
    [TestCase(new string[0], true, true)]
    [TestCase(new[] { "--real" }, true, false)]
    [TestCase(new[] { "--mock" }, false, true)]
    [TestCase(new[] { "--restart" }, true, true)]
    public void The_mode_comes_from_the_command_line_then_from_the_saved_mode(string[] args, bool saved, bool expected) {
        Assert.That(StartOptions.Parse(args, () => saved).Mock, Is.EqualTo(expected));
    }

    [Test]
    public void A_self_test_starts_paused_and_restart_is_read() {
        var selfTest = StartOptions.Parse(["--mock", "--selftest"], () => false);
        var restart = StartOptions.Parse(["--restart"], () => false);

        Assert.That(selfTest.Paused && selfTest.SelfTest, Is.True);
        Assert.That(restart.Restart, Is.True);
    }

    [Test]
    public void The_mock_self_test_does_not_use_the_mock_data_folder() {
        var selfTest = StartOptions.Parse(["--mock", "--selftest"], () => false).DataFolder;
        var mock = StartOptions.Parse(["--mock"], () => false).DataFolder;

        Assert.That(Path.GetFileName(selfTest), Is.EqualTo("selftest-mock"));
        Assert.That(Path.GetFileName(mock), Is.EqualTo("mock"));
    }

    [TestCase("status", true)]
    [TestCase("mode", true)]
    [TestCase("show", true)]
    [TestCase("quit", true)]
    [TestCase("--mock", false)]
    public void Commands_are_known_by_their_first_word(string first, bool expected) {
        Assert.That(CommandClient.IsCommand([first]), Is.EqualTo(expected));
        Assert.That(CommandClient.IsCommand([]), Is.False);
    }

    [Test]
    public void The_self_test_has_its_own_instance_name() {
        Assert.That(SingleInstance.Name(selfTest: true), Is.Not.EqualTo(SingleInstance.Name(selfTest: false)));
    }

    [Test]
    public async Task The_views_see_the_mode_of_the_app() {
        await using var app = await TestApp.StartAsync(paused: true);

        Assert.That(app.Services.GetRequiredService<IAppMode>().Mock, Is.True);
        Assert.That(Directory.EnumerateFiles(FileLogProvider.Folder(app.Folder), "runner-*.log"), Is.Not.Empty);
    }

    [Test]
    public void The_path_gets_the_saved_entries_that_the_process_does_not_have() {
        var before = Environment.GetEnvironmentVariable("PATH");
        try {
            Environment.SetEnvironmentVariable("PATH", @"C:\runner-test");
            Secrets.RefreshPath();
            var entries = Environment.GetEnvironmentVariable("PATH")!.Split(';');

            Assert.That(entries[0], Is.EqualTo(@"C:\runner-test"));
            Assert.That(entries, Has.Some.EndsWith(@"\System32").IgnoreCase);
            Assert.That(entries.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(entries.Length));
        } finally {
            Environment.SetEnvironmentVariable("PATH", before);
        }
    }

    [Test]
    public void The_module_list_of_the_build_has_the_modules_that_VSA_discovery_finds() {
        var built = ModuleList.Create().Select(m => m.GetType().FullName).ToArray();
        var discovered = VSA.Module.ModuleDiscovery.CreateModules(VSA.Module.ModuleDiscovery.FindRoots()).Select(m => m.GetType().FullName).ToArray();

        Assert.That(built, Is.Not.Empty);
        Assert.That(built, Is.EqualTo(discovered));
    }
}
