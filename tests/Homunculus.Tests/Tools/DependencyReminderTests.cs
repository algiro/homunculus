using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

/// <summary>The reminder that names the unread files a newly created file builds on.</summary>
public class DependencyReminderTests
{
    private const string UsesBoth = "public sealed class New : ICommand\n{\n    public CommandResult Execute(string argument) => CommandResult.Continue;\n}";

    private TempProject _project = null!;
    private ReadFiles _reads = null!;
    private EditFileTool _edit = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _reads = new ReadFiles();
        _edit = new EditFileTool(_project.Location, reads: _reads);
        _project.Write("Commands/ICommand.cs", "interface ICommand {}");
        _project.Write("Commands/CommandResult.cs", "enum CommandResult {}");
        _project.Write("Commands/HelpCommand.cs", "class HelpCommand {}");
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Create(string path, string content) =>
        _edit.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(
            new { path, old_str = "", new_str = content })), CancellationToken.None);

    private void Read(string path) => _reads.Read(_project.Location.Resolve(path));

    [Test]
    public async Task NamesTheUnreadFilesTheNewCodeUses()
    {
        var result = await Create("Commands/New.cs", UsesBoth);

        Assert.That(result, Does.StartWith("Created")
            .And.Contain("It uses CommandResult, ICommand, defined in Commands/CommandResult.cs, Commands/ICommand.cs, which you have not read")
            .And.Contain("check that this file matches"));
    }

    [Test]
    public async Task LeavesOutTheFilesAlreadyRead()
    {
        Read("Commands/ICommand.cs");

        var result = await Create("Commands/New.cs", UsesBoth);

        Assert.That(result, Does.Contain("It uses CommandResult, defined in Commands/CommandResult.cs").And.Not.Contain("ICommand.cs"));
    }

    [Test]
    public async Task SaysNothingOnceAllOfThemWereRead()
    {
        Read("Commands/ICommand.cs");
        Read("Commands/CommandResult.cs");

        var result = await Create("Commands/New.cs", UsesBoth);

        Assert.That(result, Does.Not.Contain("not read").And.Not.Contain("have not"));
    }

    [Test]
    public async Task ReplacesTheGenericReminderWhenItCanNameFiles()
    {
        var result = await Create("Commands/New.cs", UsesBoth);

        Assert.That(result, Does.Not.Contain("You have not read any other file"));
    }

    [Test]
    public async Task FallsBackToTheGenericReminderWhenNothingIsNamed()
    {
        var result = await Create("Commands/New.cs", "class New {}");

        Assert.That(result, Does.Contain("You have not read any other file in Commands"));
    }

    [Test]
    public async Task FindsAFileInAFolderAboveTheNewFile()
    {
        _project.Write("IUserInterface.cs", "interface IUserInterface {}");

        var result = await Create("Commands/New.cs", "class New(IUserInterface ui) {}");

        Assert.That(result, Does.Contain("It uses IUserInterface, defined in IUserInterface.cs"));
    }

    [Test]
    public async Task DoesNotLookInSiblingFolders()
    {
        _project.Write("Tools/Plan.cs", "class Plan {}");

        var result = await Create("Commands/New.cs", "class New(Plan plan) {}");

        Assert.That(result, Does.Not.Contain("Plan.cs"));
    }

    [Test]
    public async Task MatchesWholeNamesOnly()
    {
        var result = await Create("Commands/New.cs", "class New : ICommandX {}");

        Assert.That(result, Does.Not.Contain("ICommand.cs"));
    }

    [Test]
    public async Task NeverNamesTheFileItJustCreated()
    {
        var result = await Create("Commands/New.cs", "class New {}");

        Assert.That(result, Does.Not.Contain("New.cs, which"));
    }

    [Test]
    public async Task NamesAtMostFiveFiles()
    {
        var names = Enumerable.Range(1, 7).Select(i => $"Dep{i}").ToArray();
        foreach (var name in names) _project.Write($"Commands/{name}.cs", "x");

        var result = await Create("Commands/New.cs", string.Join(' ', names));

        Assert.That(result, Does.Contain("Dep1, Dep2, Dep3, Dep4, Dep5, defined in").And.Not.Contain("Dep6"));
    }

    [Test]
    public async Task SaysNothingWhenReadsAreNotTracked()
    {
        var result = await new EditFileTool(_project.Location).RunAsync(
            Json.Parse("""{"path":"Commands/New.cs","old_str":"","new_str":"class New : ICommand {}"}"""), CancellationToken.None);

        Assert.That(result, Does.Not.Contain("not read").And.Not.Contain("have not"));
    }
}
