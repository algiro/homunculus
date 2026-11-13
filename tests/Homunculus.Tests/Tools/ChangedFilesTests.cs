using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ChangedFilesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "changed-files-project");

    private ChangedFiles _changes = null!;

    [SetUp]
    public void SetUp() => _changes = new ChangedFiles(new ProjectDirectory(Root));

    private static string File(string name) => Path.Combine(Root, name);

    [Test]
    public void HasNothingToReportAtFirst() => Assert.That(_changes.Summary(), Is.Null);

    [Test]
    public void ListsEachChangedFileByKindWithPathsRelativeToTheProject()
    {
        _changes.Edited(File("App.cs"));
        _changes.Created(File(Path.Combine("sub", "New.cs")));
        _changes.Deleted(File("Old.cs"));

        Assert.That(_changes.Summary(), Is.EqualTo(
            "Changed in this turn:\n" +
            $"  created {Path.Combine("sub", "New.cs")}\n" +
            "  edited App.cs\n" +
            "  deleted Old.cs"));
    }

    [Test]
    public void ReportsAFileEditedManyTimesOnce()
    {
        _changes.Edited(File("App.cs"));
        _changes.Edited(File("App.cs"));

        Assert.That(_changes.Summary(), Does.EndWith("edited App.cs").And.Not.Contain("App.cs\n  edited App.cs"));
    }

    [Test]
    public void ReportsAFileCreatedAndThenEditedAsCreated()
    {
        _changes.Created(File("New.cs"));
        _changes.Edited(File("New.cs"));

        Assert.That(_changes.Summary(), Does.EndWith("created New.cs"));
    }

    [Test]
    public void ReportsNothingForAFileCreatedAndThenDeleted()
    {
        _changes.Created(File("Scratch.cs"));
        _changes.Deleted(File("Scratch.cs"));

        Assert.That(_changes.Summary(), Is.Null);
    }

    [Test]
    public void ReportsAFileDeletedAndThenCreatedAgainAsEdited()
    {
        _changes.Deleted(File("App.cs"));
        _changes.Created(File("App.cs"));

        Assert.That(_changes.Summary(), Does.EndWith("edited App.cs"));
    }

    [Test]
    public void ForgetsTheTurnButRemembersWhatWasCreatedInTheSession()
    {
        _changes.Created(File("New.cs"));

        _changes.StartTurn();

        Assert.That(_changes.Summary(), Is.Null);
        Assert.That(_changes.WasCreatedInSession(File("New.cs")), Is.True);
        Assert.That(_changes.WasCreatedInSession(File("Other.cs")), Is.False);
    }
}
