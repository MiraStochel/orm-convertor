using SampleData;

namespace Tests.Combined;

/// <summary>
/// The vendored excerpts of open-source projects (decision 138). What the decision asks of them
/// is checked here rather than trusted: every project carries its licence - a permissive one,
/// so that nothing in the repository comes under copyleft terms - and a README with what was
/// taken and changed; and every code file opens with the note that names its origin and says
/// whether it was changed, which is also what the Apache License asks of a modified file. That
/// the excerpts translate is <see cref="ExampleCatalogTest"/>'s, like every other example.
/// </summary>
public class ThirdPartySourcesTest
{
    private static readonly string[] CodeExtensions = [".cs", ".java", ".xml", ".sql"];

    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (var project in ThirdPartySources.Projects)
        {
            data.Add(project);
        }

        return data;
    }

    [Fact]
    public void TheExcerptsAreEmbedded()
    {
        Assert.NotEmpty(ThirdPartySources.Projects);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryProjectCarriesAPermissiveLicenceAndAReadme(string project)
    {
        var root = ThirdPartySources.Files(project);

        Assert.Contains(root, path => Path.GetFileName(path) == "README.md");

        var licence = root.SingleOrDefault(path => Path.GetFileNameWithoutExtension(path) == "LICENSE");
        Assert.True(licence is not null, $"The project '{project}' carries no LICENSE file.");

        var text = ThirdPartySources.Read(licence!);
        Assert.True(
            text.Contains("MIT License", StringComparison.Ordinal)
                || text.Contains("Apache License", StringComparison.Ordinal)
                || text.Contains("BSD", StringComparison.Ordinal),
            $"The licence of '{project}' is none of MIT, Apache-2.0 or BSD, which decision 138 admits.");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryCodeFileNamesItsOriginAndSaysWhetherItWasChanged(string project)
    {
        var code = ThirdPartySources.Tree(project).Where(path => CodeExtensions.Contains(Path.GetExtension(path))).ToList();
        Assert.NotEmpty(code);

        foreach (var path in code)
        {
            // The note stands at the top, after the project's own licence header where the file
            // has one, so it is looked for among the opening lines.
            var opening = string.Join('\n', ThirdPartySources.Read(path).Split('\n').Take(40));

            var taken = opening.Contains("ORMConvertor: taken from ", StringComparison.Ordinal);
            var added = opening.Contains("ORMConvertor: this file is not part of ", StringComparison.Ordinal);
            Assert.True(taken || added, $"'{path}' does not open with a note naming its origin.");

            if (taken)
            {
                Assert.True(
                    opening.Contains("Unchanged apart from this note.", StringComparison.Ordinal)
                        || opening.Contains("Changed: ", StringComparison.Ordinal),
                    $"The note of '{path}' does not say whether the file was changed.");
            }
        }
    }
}
