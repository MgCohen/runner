using System.Text.Json;
using App.Host;

namespace App.Tests;

public sealed class SelfTestReportTests {
    [Test]
    public void A37_one_failed_check_gives_exit_code_1_and_the_report_names_it() {
        var folder = Path.Combine(Path.GetTempPath(), "runner-tests", Guid.NewGuid().ToString("N")[..8]);

        var code = SelfTest.WriteReport([new SelfTest.CheckResult("A1", true, "ok", null), new SelfTest.CheckResult("A9", false, "two chips", null)], folder);

        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "report.json")));
        Assert.That(code, Is.EqualTo(1));
        Assert.That(report.RootElement.EnumerateArray().Where(r => !r.GetProperty("Pass").GetBoolean()).Select(r => r.GetProperty("Id").GetString()), Is.EqualTo(new[] { "A9" }));
        Directory.Delete(folder, recursive: true);
    }

    [Test]
    public void All_checks_passed_gives_exit_code_0() {
        var folder = Path.Combine(Path.GetTempPath(), "runner-tests", Guid.NewGuid().ToString("N")[..8]);

        Assert.That(SelfTest.WriteReport([new SelfTest.CheckResult("A1", true, "ok", null)], folder), Is.Zero);
        Directory.Delete(folder, recursive: true);
    }
}
