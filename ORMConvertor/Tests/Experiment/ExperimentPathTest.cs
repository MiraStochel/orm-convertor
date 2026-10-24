using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using OrmConvertor;
using ORMConvertorAPI.Data;
using Tests.Database;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// The path of the experiment run (decision 173) without a configured run: the inputs the export
/// writes come back as the inputs of a run, a translation the tool refuses is the row
/// <c>refused</c> at level 0, an example - the case study of T1 among them (decision 174) - ends
/// at the third level, and the summary reads the rows the run writes.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class ExperimentPathTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void TheExportedInputsAreTheInputsOfARun()
    {
        var temp = Directory.CreateTempSubdirectory("ormconvertor-experiment-");
        try
        {
            var count = ForeignInputs.Export(temp.FullName);
            var inputs = ExperimentInput.Discover(temp.FullName, TestDatabase.SchemaName);

            Assert.Equal(count, inputs.Count);
            Assert.Contains(inputs, input => input.Key.Kind == ForeignKey.Matrix);
            Assert.Contains(inputs, input => input.Key.Kind == ForeignKey.Cross);
            Assert.Contains(inputs, input => input.Key.Kind == ForeignKey.Ldbc);
            Assert.Contains(inputs, input => input.Key.Kind == ForeignKey.Fiben);
            Assert.Contains(inputs, input => input.Key.Kind == ForeignKey.Example && Examples.RealProjectKeys.Contains(input.Key.Id));
            Assert.All(inputs, input => Assert.NotEmpty(input.Units));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>A Dapper source states no key and an example written for the tool runs without a catalog (decision 099), so NHibernate refuses the entities: the row says refused and names the reason.</summary>
    [Fact]
    public void ARefusedTranslationIsARowAtLevelZero()
    {
        var example = Examples.GetExamples.Single(example => example.Key == "dapper-to-efcore");
        var input = new ExperimentInput($"example-{example.Key}-{example.SourceOrm}", new ForeignKey(ForeignKey.Example, example.Key), example.SourceOrm, example.Units);

        var (verdict, _) = ExperimentRun.Judge(input, ORMEnum.NHibernate, fixture);
        output.WriteLine(verdict.Describe());

        Assert.Equal(ForeignVerdict.Refused, verdict.Outcome);
        Assert.Equal(ForeignLevel.None, verdict.Reached);
        Assert.Equal(ForeignArtifact.LanguageRefused, verdict.Language);
        Assert.Equal(ExperimentRun.Producer, verdict.Producer);
        Assert.NotEmpty(verdict.Detail);
    }

    /// <summary>An example of a whole project - four entities, six queries - translated into NHibernate: read, every query compiled, the factory and every HQL text accepted, and nothing to run against.</summary>
    [Fact]
    public void AProjectIsJudgedToTheThirdLevel()
    {
        var example = Examples.GetExamples.Single(example => example.Key == "order-book");
        var input = new ExperimentInput($"example-{example.Key}-{example.SourceOrm}", new ForeignKey(ForeignKey.Example, example.Key), example.SourceOrm, example.Units);

        var (verdict, conversion) = ExperimentRun.Judge(input, ORMEnum.NHibernate, fixture);
        output.WriteLine(verdict.Describe());

        Assert.NotNull(conversion);
        Assert.Equal(ForeignLevel.Accepted, verdict.Reached);
        Assert.Equal(ForeignVerdict.Accepted, verdict.Outcome);
        Assert.Contains("project of", verdict.Detail);
    }

    /// <summary>
    /// The Keycloak excerpt, the application of T1 (decision 174), translated into EF Core:
    /// whatever level it reaches - a refusal included -, it is a row with the level and the
    /// diagnostic, never a failure of the suite; the level itself is a finding of the run, not a
    /// test of a build (decision 173). With the case-study database at hand the translation reads
    /// the project's schema as its catalog (decision 176).
    /// </summary>
    [Fact]
    public void TheCaseStudyIsARow()
    {
        var example = Examples.GetExamples.Single(example => example.Key == "keycloak-identity");
        var input = new ExperimentInput($"example-{example.Key}-{example.SourceOrm}", new ForeignKey(ForeignKey.Example, example.Key), example.SourceOrm, example.Units);

        var (verdict, conversion) = ExperimentRun.Judge(input, ORMEnum.EFCore, fixture);
        output.WriteLine(verdict.Describe());

        Assert.NotNull(conversion);
        Assert.Equal(ExperimentRun.Producer, verdict.Producer);
        Assert.True(verdict.Outcome == ForeignVerdict.Accepted || verdict.Outcome == ForeignVerdict.Ran || verdict.Detail.Length > 0, verdict.Describe());

        if (fixture.IsCaseStudyAvailable)
        {
            Assert.Equal(CatalogConnectionState.Reached, conversion.CatalogState);
        }
    }

    /// <summary>
    /// A project of a case study has a fourth level over the empty tables of its schema (decision
    /// 176): the Chinook data layer on Dapper, whose keys the catalog supplies, translated into EF
    /// Core and run query by query - the row says <c>ran</c>, or <c>failed</c> and names the query,
    /// never that a project has no measure. Which of the two is the run's finding.
    /// </summary>
    [Fact]
    public void ACaseStudyProjectHasAFourthLevel()
    {
        fixture.SkipIfCaseStudyUnavailable();

        var example = Examples.GetExamples.Single(example => example.Key == "chinook-dapper");
        var input = new ExperimentInput($"example-{example.Key}-{example.SourceOrm}", new ForeignKey(ForeignKey.Example, example.Key), example.SourceOrm, example.Units);

        var (verdict, conversion) = ExperimentRun.Judge(input, ORMEnum.EFCore, fixture);
        output.WriteLine(verdict.Describe());

        Assert.NotNull(conversion);
        Assert.Equal(CatalogConnectionState.Reached, conversion.CatalogState);
        Assert.True(verdict.Reached >= ForeignLevel.Accepted, verdict.Describe());
        Assert.True(verdict.Outcome is ForeignVerdict.Ran or ForeignVerdict.Failed, verdict.Describe());
    }

    public static TheoryData<string> CaseStudies()
    {
        var data = new TheoryData<string>();
        foreach (var key in Examples.RealProjectKeys.Order(StringComparer.Ordinal))
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>
    /// The schema of every excerpted project stands in the case-study database (decision 176):
    /// translated into NHibernate - a target that asks the catalog for everything - with that
    /// database as its catalog, the excerpt finds a table for every entity it maps, none twice, so
    /// no record says a table is missing or ambiguous and facts are supplied. What the translation
    /// then makes of the project is the run's finding, not this test's.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseStudies))]
    public void EveryCaseStudyFindsItsTablesInTheCaseStudyDatabase(string key)
    {
        fixture.SkipIfCaseStudyUnavailable();

        var example = Examples.GetExamples.Single(example => example.Key == key);
        var conversion = ConversionHandler.Convert(example.SourceOrm, ORMEnum.NHibernate, example.Units, fixture.CaseStudyCatalogReader);

        foreach (var record in conversion.Records.Where(record => record.Kind != ConversionRecordKind.Supplied))
        {
            output.WriteLine($"{record.Kind} {record.Entity}.{record.Property}: {record.Reason}");
        }

        Assert.Equal(CatalogConnectionState.Reached, conversion.CatalogState);
        Assert.DoesNotContain(conversion.Records, record => record.Reason.Contains("No table matching", StringComparison.Ordinal));
        Assert.DoesNotContain(conversion.Records, record => record.Reason.Contains("More than one table", StringComparison.Ordinal));
        Assert.Contains(conversion.Records, record => record.Kind == ConversionRecordKind.Supplied);
    }

    /// <summary>A project whose one query does not compile stops at the second level, and the detail names the query's unit.</summary>
    [Fact]
    public void AProjectWithOneBrokenQueryIsNotCompiledAndTheDetailNamesIt()
    {
        var example = Examples.GetExamples.Single(example => example.Key == "order-book");
        var input = new ExperimentInput($"example-{example.Key}-{example.SourceOrm}", new ForeignKey(ForeignKey.Example, example.Key), example.SourceOrm, example.Units);

        var (_, artifact, refusal) = ExperimentRun.Translate(input, ORMEnum.NHibernate, fixture);
        Assert.Null(refusal);
        Assert.NotNull(artifact);
        Assert.True(artifact.IsProject);

        // A name nothing declares, as the first statement of the method's body: the language reads
        // it (level 1), the compiler does not bind it (level 2).
        var broken = artifact.QueryMethods[1];
        var body = broken.Content.IndexOf('{');
        Assert.True(body > 0, broken.Content);
        var mutated = artifact with
        {
            Units = [.. artifact.Units.Select(unit => unit == broken ? unit with { Content = unit.Content.Insert(body + 1, " UndefinedSymbolX.Call();") } : unit)],
        };

        var verdict = ForeignArtifactRun.Run(mutated);
        output.WriteLine(verdict.Describe());

        Assert.Equal(ForeignLevel.Read, verdict.Reached);
        Assert.Equal(ForeignVerdict.NotCompiled, verdict.Outcome);
        Assert.StartsWith(broken.Path, verdict.Detail, StringComparison.Ordinal);
    }

    /// <summary>The summary over a directory of rows: a row of the tool and a row of a model over the same key land in their tables.</summary>
    [Fact]
    public void TheSummaryGroupsTheRows()
    {
        var temp = Directory.CreateTempSubdirectory("ormconvertor-experiment-");
        try
        {
            File.WriteAllLines(Path.Combine(temp.FullName, ExperimentRun.RowsFileName),
            [
                ForeignVerdict.Header,
                string.Join('\t', "matrix-q1-entity-filter-ordered-Dapper-EFCore", "matrix:q1-entity-filter-ordered", "Dapper", "EFCore", "tool", "target", "4", "agrees", "", "3", "1", "1", "2", "3", "4", "{\"toolVersion\":\"x\"}"),
                string.Join('\t', "matrix-q1-entity-filter-ordered-Dapper-NHibernate", "matrix:q1-entity-filter-ordered", "Dapper", "NHibernate", "tool", "refused", "0", "refused", "no key", "", "", "0", "0", "0", "0", "{}"),
                string.Join('\t', "cross-EFCore-EFCore-Hibernate", "cross:EFCore", "EFCore", "Hibernate", "tool", "target", "3", "accepted", "no fourth level", "", "", "1", "2", "3", "0", "{}"),
            ]);
            File.WriteAllLines(Path.Combine(temp.FullName, ForeignRows.FileName),
            [
                ForeignVerdict.Header,
                string.Join('\t', "q1-llm-1", "matrix:q1-entity-filter-ordered", "Dapper", "EFCore", "llm", "native SQL", "4", "differs", "row 1", "2", "0", "1", "2", "3", "4", "{\"model\":\"m\",\"variant\":\"zero-shot\"}"),
            ]);
            File.WriteAllLines(Path.Combine(temp.FullName, ExperimentRun.MappingRowsFileName),
            [
                MappingMeasure.Header,
                string.Join('\t', "matrix-q1-entity-filter-ordered-Dapper-EFCore", "matrix:q1-entity-filter-ordered", "Dapper", "EFCore", "tool", "10", "9", "8", "1", "", "{}"),
            ]);

            var lines = ExperimentSummary.Write(temp.FullName);
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }

            Assert.Contains(lines, line => line.StartsWith("| .NET → .NET | 2 | 1 | 0 | 1 (50 %) | 1 (50 %) | 1 (50 %) | 2 | 1 (50 %) | 1 (50 %) | 8 (80 %) |", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("| .NET → Java | 1 | 0 | 0 | 1 (100 %) | 1 (100 %) | 1 (100 %) | 0 | — | — | — |", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("| llm | m | zero-shot | .NET → .NET | 1 | 0 | 1 |", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("| query of the matrix's own | .NET → .NET |", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(temp.FullName, ExperimentSummary.FileName)));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheKindAndCategoryOfAKeyAreRead()
    {
        Assert.Equal("case study", ExperimentSummary.KindOf("example:keycloak-identity"));
        Assert.Equal("example", ExperimentSummary.KindOf("example:order-book"));
        Assert.Equal("Filtering", ExperimentSummary.CategoryOf("matrix:Filtering"));
        Assert.Equal("query of the matrix's own", ExperimentSummary.CategoryOf("matrix:q1-entity-filter-ordered"));
        Assert.Equal("InteractiveComplex", ExperimentSummary.CategoryOf("ldbc:ic1"));
        Assert.Equal("cross", ExperimentSummary.CategoryOf("cross:EFCore"));
    }
}
