using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Combined;
using Tests.Database;
using Tests.Differential;

namespace Tests.Foreign;

/// <summary>
/// The path of a foreign artifact itself (decision 172), held by artifacts the repository
/// carries for that purpose under <c>Tests/Database/Foreign</c>: written by hand, one per
/// target that reaches the fourth level and agrees, and one that stops at each level - every
/// one of them has to end as the row <c>expected.txt</c> states, never as a failure. The
/// tool's own artifact goes through the path too, to show that the path measures what the
/// client measures; and what is not an artifact but a misconfiguration fails by name.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class ForeignArtifactPathTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    internal static string OwnArtifactsDirectory => Path.Combine(AppContext.BaseDirectory, "Database", "Foreign");

    /// <summary>The artifacts of the repository whose target this suite owns; the Java suite takes the rest.</summary>
    public static TheoryData<string> OwnArtifacts()
    {
        var data = new TheoryData<string>();
        foreach (var expected in Expected())
        {
            var artifact = ForeignArtifact.Read(Path.Combine(OwnArtifactsDirectory, expected.Id), "schema");
            if (ForeignArtifactRun.Owns(artifact.Target))
            {
                data.Add(expected.Id);
            }
        }

        return data;
    }

    /// <summary>What every own artifact is written to reach, from the file beside them.</summary>
    internal static List<(string Id, int Reached, string Outcome)> Expected()
        => SharedInputs.Sections("expected.txt", File.ReadAllLines(Path.Combine(OwnArtifactsDirectory, "expected.txt")))
            .Select(section =>
            {
                var values = section.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                return (section.Id, int.Parse(values["reached"], System.Globalization.CultureInfo.InvariantCulture), values["outcome"]);
            })
            .ToList();

    [Theory]
    [MemberData(nameof(OwnArtifacts))]
    public void TheOwnArtifactEndsAsTheRowItWasWrittenToEnd(string id)
    {
        fixture.SkipIfUnavailable();

        var expected = Expected().Single(candidate => candidate.Id == id);
        var artifact = ForeignArtifact.Read(Path.Combine(OwnArtifactsDirectory, id), TestDatabase.SchemaName);

        var verdict = ForeignArtifactRun.Run(artifact);
        output.WriteLine(verdict.Describe());

        Assert.Equal((expected.Reached, expected.Outcome), (verdict.Reached, verdict.Outcome));
    }

    /// <summary>Every own artifact is stated in expected.txt and every section of it is an artifact, so neither can be forgotten.</summary>
    [Fact]
    public void EveryOwnArtifactHasAnExpectedRowAndEveryRowAnArtifact()
    {
        var stated = Expected().Select(expected => expected.Id).Order(StringComparer.Ordinal).ToList();
        var present = Directory.GetDirectories(OwnArtifactsDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, ForeignArtifact.ManifestFileName)))
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(present, stated);
        Assert.All(Expected(), expected => Assert.Contains(expected.Outcome, new[]
        {
            ForeignVerdict.Unreadable, ForeignVerdict.NotCompiled, ForeignVerdict.NotAccepted,
            ForeignVerdict.Failed, ForeignVerdict.Differs, ForeignVerdict.Agrees, ForeignVerdict.Accepted,
        }));
    }

    /// <summary>The own artifacts stop at every level once between them, so the path is seen refusing at each.</summary>
    [Fact]
    public void TheOwnArtifactsStopAtEveryLevel()
    {
        var outcomes = Expected().Select(expected => expected.Outcome).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(ForeignVerdict.Unreadable, outcomes);
        Assert.Contains(ForeignVerdict.NotCompiled, outcomes);
        Assert.Contains(ForeignVerdict.NotAccepted, outcomes);
        Assert.Contains(ForeignVerdict.Failed, outcomes);
        Assert.Contains(ForeignVerdict.Differs, outcomes);

        foreach (var target in Enum.GetValues<ORMEnum>())
        {
            Assert.Contains(Expected(), expected =>
                expected.Outcome == ForeignVerdict.Agrees
                && ForeignArtifact.Read(Path.Combine(OwnArtifactsDirectory, expected.Id), "schema").Target == target);
        }
    }

    public static TheoryData<string, ORMEnum, ORMEnum> ToolPairs() => new()
    {
        { "q1-entity-filter-ordered", ORMEnum.Dapper, ORMEnum.EFCore },
        { "q1-entity-filter-ordered", ORMEnum.Dapper, ORMEnum.NHibernate },
        { "q2-entity-unordered", ORMEnum.EFCore, ORMEnum.Dapper },
    };

    /// <summary>
    /// The two ends of the path meet: the input the suite writes, read back and translated by
    /// the tool, put through the path as an artifact of producer <c>tool</c>, reaches the
    /// fourth level and agrees - the same verdict the client gives the same translation. This
    /// is the one time an artifact of the tool goes this way, and it goes to prove that the
    /// path measures what the client measures (decision 172).
    /// </summary>
    [Theory]
    [MemberData(nameof(ToolPairs))]
    public void TheToolsOwnArtifactPassesThePathAsItPassesTheClient(string id, ORMEnum source, ORMEnum target)
    {
        fixture.SkipIfUnavailable();

        var query = DifferentialVerificationTest.Query(id);
        var temp = Directory.CreateTempSubdirectory("ormconvertor-foreign-");
        try
        {
            var inputs = Path.Combine(temp.FullName, "inputs");
            ForeignInputs.WriteOne(inputs, new ForeignKey(ForeignKey.Matrix, id), source, query.Units(source), questions: null);

            var (key, readSource, units) = ForeignInputs.Read(ForeignInputs.DirectoryOf(inputs, new ForeignKey(ForeignKey.Matrix, id), source), TestDatabase.SchemaName);
            Assert.Equal(source, readSource);

            var conversion = ConversionHandler.Convert(readSource, target, units, fixture.CatalogReader);
            Assert.DoesNotContain(conversion.Records, record => record.Kind == ConversionRecordKind.Failure);

            var directory = Path.Combine(temp.FullName, $"{id}-{target}-tool");
            ForeignArtifact.Write(directory, key, readSource, target, "tool",
                conversion.Sources.Select((artifact, index) => new ForeignUnit($"{artifact.ContentType}-{index + 1}{ForeignInputs.Extension(artifact.ContentType)}", artifact.ContentType, artifact.Content)),
                "{\"toolVersion\": \"" + conversion.ToolVersion + "\"}");

            var verdict = ForeignArtifactRun.Run(ForeignArtifact.Read(directory, TestDatabase.SchemaName));
            output.WriteLine(verdict.Describe());

            Assert.Equal(ForeignLevel.Ran, verdict.Reached);
            Assert.Equal(ForeignVerdict.Agrees, verdict.Outcome);

            // The client's verdict over the same translation: the same rows against the same measure.
            Assert.Equal(query.CanonicalResult(), DotNetQueryRunner.Run(query, source, target, fixture));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The scenario of <c>CrossFrameworkInputs</c> as a key (decision 172): its inputs are
    /// written, read back and translated, and the artifact goes through the path up to the
    /// third level - the scenario has no measure at the fourth, and the row says so rather
    /// than failing.
    /// </summary>
    [Fact]
    public void AScenarioArtifactEndsAtTheThirdLevel()
    {
        fixture.SkipIfUnavailable();

        var temp = Directory.CreateTempSubdirectory("ormconvertor-foreign-");
        try
        {
            var inputs = Path.Combine(temp.FullName, "inputs");
            var key = new ForeignKey(ForeignKey.Cross, ORMEnum.EFCore.ToString());
            ForeignInputs.WriteOne(inputs, key, ORMEnum.EFCore, CrossFrameworkInputs.Units(ORMEnum.EFCore), questions: null);

            var (readKey, source, units) = ForeignInputs.Read(ForeignInputs.DirectoryOf(inputs, key, ORMEnum.EFCore), TestDatabase.SchemaName);
            var conversion = ConversionHandler.Convert(source, ORMEnum.NHibernate, units, fixture.CatalogReader);
            Assert.DoesNotContain(conversion.Records, record => record.Kind == ConversionRecordKind.Failure);

            var directory = Path.Combine(temp.FullName, "cross-efcore-nhibernate-tool");
            ForeignArtifact.Write(directory, readKey, source, ORMEnum.NHibernate, "tool",
                conversion.Sources.Select((artifact, index) => new ForeignUnit($"{artifact.ContentType}-{index + 1}{ForeignInputs.Extension(artifact.ContentType)}", artifact.ContentType, artifact.Content)));

            var verdict = ForeignArtifactRun.Run(ForeignArtifact.Read(directory, TestDatabase.SchemaName));
            output.WriteLine(verdict.Describe());

            Assert.Equal(ForeignLevel.Accepted, verdict.Reached);
            Assert.Equal(ForeignVerdict.Accepted, verdict.Outcome);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void AManifestWithoutAKeyFailsByName()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReadManifest("""{ "source": "Dapper", "target": "EFCore", "producer": "human", "units": [] }"""));
        Assert.Contains("answers", error.Message);
    }

    [Fact]
    public void AKindNobodyMeasuresFailsByName()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReadManifest("""{ "answers": "scenario:x", "source": "Dapper", "target": "EFCore", "producer": "human", "units": [{ "path": "a.sql" }] }"""));
        Assert.Contains("scenario", error.Message);
        Assert.Contains("matrix", error.Message);
    }

    [Fact]
    public void AUnitWhoseExtensionNamesOnlyALanguageNeedsItsContentType()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReadManifest("""{ "answers": "matrix:q1-entity-filter-ordered", "source": "Dapper", "target": "EFCore", "producer": "human", "units": [{ "path": "a.cs" }] }""", ("a.cs", "class A {}")));
        Assert.Contains("contentType", error.Message);
    }

    [Fact]
    public void AQueryTheMeasureDoesNotHaveFailsByName()
    {
        var artifact = ReadManifest("""{ "answers": "matrix:no-such-query", "source": "Dapper", "target": "EFCore", "producer": "human", "units": [{ "path": "a.cs", "contentType": "CSharpEntity" }] }""", ("a.cs", "class A {}"));

        var error = Assert.Throws<InvalidOperationException>(() => ForeignArtifactRun.Run(artifact));
        Assert.Contains("no-such-query", error.Message);
    }

    [Fact]
    public void AnArtifactOfTheOtherSuiteIsNotOwned()
    {
        var artifact = ReadManifest("""{ "answers": "matrix:q1-entity-filter-ordered", "source": "Dapper", "target": "Hibernate", "producer": "human", "units": [{ "path": "a.java", "contentType": "JavaEntity" }] }""", ("a.java", "class A {}"));

        Assert.False(ForeignArtifactRun.Owns(artifact.Target));
        Assert.Throws<InvalidOperationException>(() => ForeignArtifactRun.Run(artifact));
    }

    /// <summary>The rows of the two suites have the same columns, held against the Java source as the policy of the FIBEN run is.</summary>
    [Fact]
    public void TheRowsHaveTheColumnsTheJavaSuiteWrites()
    {
        var source = File.ReadAllText(Path.Combine(
            TargetFrameworkDescriptorTest.SolutionDirectory(),
            "JavaTests", "src", "test", "java", "cz", "stochel", "ormconvertor", "javatests", "foreign", "ForeignVerdict.java"));

        var header = Regex.Match(source, "HEADER\\s*=\\s*\"([^\"]+)\"");
        Assert.True(header.Success, "The Java suite declares HEADER in ForeignVerdict.java.");
        Assert.Equal(ForeignVerdict.Header, header.Groups[1].Value.Replace("\\t", "\t"));
    }

    private static ForeignArtifact ReadManifest(string manifest, params (string Path, string Content)[] files)
    {
        var temp = Directory.CreateTempSubdirectory("ormconvertor-foreign-");
        try
        {
            File.WriteAllText(Path.Combine(temp.FullName, ForeignArtifact.ManifestFileName), manifest);
            foreach (var (path, content) in files)
            {
                File.WriteAllText(Path.Combine(temp.FullName, path), content);
            }

            return ForeignArtifact.Read(temp.FullName, TestDatabase.SchemaName);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
