using Homunculus.Sec.Analysis;

namespace Homunculus.Sec.Tests;

public class PromptFileTests
{
    [Test]
    public void TheBenchmarkReadsTheSamePromptTheProgramUses()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deploy", "analyst-system-prompt.txt"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "deploy/analyst-system-prompt.txt not found");

        var file = File.ReadAllText(Path.Combine(dir!.FullName, "deploy", "analyst-system-prompt.txt")).Replace("\r\n", "\n").Trim();

        Assert.That(file, Is.EqualTo(Analyst.SystemPrompt.Replace("\r\n", "\n").Trim()),
            "deploy/analyst-system-prompt.txt is out of date: copy Analyst.SystemPrompt (dedented) into it, then rerun deploy/bench-model.py");
    }
}
