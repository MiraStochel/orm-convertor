using Tests.Database;

namespace Tests.Foreign;

/// <summary>
/// The run over foreign artifacts (decision 172): every artifact the environment names
/// whose target this suite owns goes through the levels and gets a row; the rows go to the
/// directory the environment names, rewritten after every level, so that a run stopped from
/// outside leaves the level each artifact reached. It is a run on demand, never a test of a
/// build: without the variable the one case skips with the reason, and a red level is a row
/// and not a failure - only the path itself fails, by name.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class ForeignArtifactRunTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    private const string None = "";

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, ForeignVerdict> Verdicts = new(StringComparer.Ordinal);

    /// <summary>The directories of the artifacts this suite owns, or one empty entry where nothing is configured.</summary>
    public static TheoryData<string> Artifacts()
    {
        var data = new TheoryData<string>();
        var root = Environment.GetEnvironmentVariable(ForeignArtifact.ArtifactsVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            data.Add(None);
            return data;
        }

        var owned = ForeignArtifact.Discover(root.Trim(), TestDatabase.SchemaName)
            .Where(artifact => ForeignArtifactRun.Owns(artifact.Target))
            .ToList();

        if (owned.Count == 0)
        {
            data.Add(None);
            return data;
        }

        foreach (var artifact in owned)
        {
            data.Add(artifact.Directory);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Artifacts))]
    public void TheArtifactIsJudgedAndRecorded(string directory)
    {
        if (directory.Length == 0)
        {
            Assert.Skip($"No foreign artifact of a .NET target configured: set {ForeignArtifact.ArtifactsVariable} to an artifact or a directory of artifacts (decision 172).");
        }

        var artifact = ForeignArtifact.Read(directory, TestDatabase.SchemaName);

        if (artifact.Key.Kind == ForeignKey.Matrix && !fixture.IsAvailable)
        {
            Assert.Fail($"{artifact.Id} answers a query of the matrix, which runs over the fixture, and the fixture is not available: {fixture.SkipReason}");
        }

        var verdict = ForeignArtifactRun.Run(artifact, Record);
        output.WriteLine(verdict.Describe());
    }

    /// <summary>The other direction of the path: the inputs the suite sends, written as files for whoever makes artifacts of them.</summary>
    [Fact]
    public void TheInputsAreWrittenWhereAsked()
    {
        var root = Environment.GetEnvironmentVariable(ForeignInputs.Variable);
        if (string.IsNullOrWhiteSpace(root))
        {
            Assert.Skip($"No directory to write the inputs into: set {ForeignInputs.Variable} (decision 172).");
        }

        var count = ForeignInputs.Export(root.Trim());
        output.WriteLine($"{count} inputs written into {root}.");
        Assert.True(count > 0);
    }

    private static void Record(ForeignVerdict verdict)
    {
        lock (Gate)
        {
            Verdicts[verdict.Artifact] = verdict;

            var directory = Environment.GetEnvironmentVariable(ForeignArtifact.ResultsVariable);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                ForeignRows.Write(directory.Trim(), ForeignRows.FileName, Verdicts.Values);
            }
        }
    }
}
