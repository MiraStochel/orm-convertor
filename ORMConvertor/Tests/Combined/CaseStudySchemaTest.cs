using System.Text.RegularExpressions;
using ORMConvertorAPI.Data;
using SampleData;
using Tests.Database;

namespace Tests.Combined;

/// <summary>
/// The schemas of the excerpted projects (decision 176), held without a database: every excerpt
/// the experiment run reads as a case study carries the schema of its tables, every table the
/// excerpt names is in it, no table name is shared between any two schemas the suites create in
/// the case-study database - a lookup that states no schema would otherwise be ambiguous
/// (decision 015) -, and the Java suite creates the same schemas. What the fixture then does
/// with the scripts is <c>Experiment/ExperimentPathTest</c>'s, against the database.
/// </summary>
public class CaseStudySchemaTest
{
    private static readonly Regex CreateTable = new(@"CREATE\s+TABLE\s+\[?\{\{schema\}\}\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The tables a Java or C# excerpt maps: the annotations of JPA, the fluent configuration of EF Core - by literal or by nameof -, the generator table of JPA.</summary>
    private static readonly Regex NamedTable = new(
        @"@(?:Table|JoinTable|CollectionTable)\s*\(\s*name\s*=\s*""(\w+)""|@TableGenerator\s*\([^)]*?\btable\s*=\s*""(\w+)""|ToTable\s*\(\s*""(\w+)""|ToTable\s*\(\s*nameof\((\w+)\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The tables a mapper document reads: what follows FROM and JOIN inside a select, a subquery excluded.</summary>
    private static readonly Regex Select = new(@"<select\b.*?</select>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex FromOrJoin = new(@"\b(?:FROM|JOIN)\s+(?!\()([A-Za-z_]\w*)", RegexOptions.Compiled);

    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (var project in ThirdPartySources.ProjectsWithSchema)
        {
            data.Add(project);
        }

        return data;
    }

    /// <summary>The project of an example key is the folder its name opens with - <c>chinook-dapper</c> is <c>chinook/</c> -, which is how the Java suite reads the key too.</summary>
    internal static string ProjectOf(string exampleKey) => exampleKey[..exampleKey.IndexOf('-')];

    [Fact]
    public void EveryCaseStudyHasTheSchemaOfItsProjectAndEverySchemaACaseStudy()
    {
        var ofCaseStudies = Examples.RealProjectKeys.Select(ProjectOf).Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.All(ofCaseStudies, project => Assert.True(ThirdPartySources.HasSchema(project), $"The excerpt '{project}' carries no {ThirdPartySources.SchemaFileName} (decision 176)."));
        Assert.Equal(ofCaseStudies, ThirdPartySources.ProjectsWithSchema);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryTableTheExcerptNamesIsInItsSchema(string project)
    {
        var tables = Tables(ThirdPartySources.Schema(project));
        Assert.NotEmpty(tables);

        var named = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ThirdPartySources.Tree(project))
        {
            var text = ThirdPartySources.Read(path);
            switch (Path.GetExtension(path))
            {
                case ".java" or ".cs":
                    foreach (Match match in NamedTable.Matches(text))
                    {
                        named.Add(match.Groups.Values.Skip(1).First(group => group.Success).Value);
                    }

                    break;

                case ".xml":
                    foreach (Match select in Select.Matches(text))
                    {
                        foreach (Match match in FromOrJoin.Matches(select.Value))
                        {
                            named.Add(match.Groups[1].Value);
                        }
                    }

                    break;
            }
        }

        Assert.NotEmpty(named);
        var missing = named.Where(table => !tables.Contains(table)).ToList();
        Assert.True(missing.Count == 0, $"The excerpt '{project}' names tables its schema does not create: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// The schemas of the case-study database - one per excerpted project - share no table name,
    /// so a request that states no schema finds one table or none (decision 015,
    /// <c>SqlServerCatalogReader.Resolve</c>); a second match outside dbo would be an ambiguity
    /// the catalog reports and the excerpt's translation would lose its catalog to it. The
    /// fixture's own tables stand in another database for the same reason: JPetStore's
    /// <c>orders</c> and the fixture's <c>Orders</c> are one name.
    /// </summary>
    [Fact]
    public void NoTableNameIsSharedBetweenTheSchemasOfTheCaseStudyDatabase()
    {
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in ThirdPartySources.ProjectsWithSchema)
        {
            scripts[TestDatabase.CaseStudySchemaName(project)] = ThirdPartySources.Schema(project);
        }

        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (schema, script) in scripts)
        {
            foreach (var table in Tables(script))
            {
                owners.TryAdd(table, []);
                owners[table].Add(schema);
            }
        }

        var shared = owners.Where(entry => entry.Value.Count > 1).Select(entry => $"{entry.Key} ({string.Join(", ", entry.Value)})").ToList();
        Assert.True(shared.Count == 0, $"A table name stands in more than one schema of the case-study database: {string.Join("; ", shared)}.");
    }

    /// <summary>The Java suite names the same projects in <c>TestSchema.java</c>, so both suites create the same schemas.</summary>
    [Fact]
    public void TheJavaSuiteCreatesTheSameSchemas()
    {
        var source = File.ReadAllText(Path.Combine(
            TargetFrameworkDescriptorTest.SolutionDirectory(),
            "JavaTests", "src", "test", "java", "cz", "stochel", "ormconvertor", "javatests", "TestSchema.java"));

        var declaration = Regex.Match(source, @"CASE_STUDIES\s*=\s*List\.of\(([^)]*)\)");
        Assert.True(declaration.Success, "The Java suite declares CASE_STUDIES in TestSchema.java.");

        var projects = Regex.Matches(declaration.Groups[1].Value, "\"([^\"]+)\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(ThirdPartySources.ProjectsWithSchema, projects);
    }

    private static SortedSet<string> Tables(string script)
        => new(CreateTable.Matches(script).Select(match => match.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
}
