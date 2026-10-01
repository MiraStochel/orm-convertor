using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// The query categories requirement T2 divides the matrix by, each written in the language of
/// every source that can state it and run through every direction the enum yields
/// (<see cref="QueryShapeInputs.Categories"/>, read from the shared files under
/// <c>Tests/Database/QueryShapes</c>). Until now the categories other than aggregation and
/// the parameter were proved on the nine .NET directions only, and aggregation on the
/// eighteen with a .NET source; the JPQL and MyBatis rows of the inputs are what puts a
/// Java source under every category the Java languages can state.
///
/// Three claims per direction: the query comes out (or the target's descriptor refuses it
/// with a record naming the feature), nothing about it is refused in silence, and the target
/// writes the category in its own language - the hallmarks the inputs state per target.
/// Level 2 for the two SQL targets follows: what they emit has to parse. The Java targets
/// are judged over the same files by the Java suite (<c>shapes/QueryCategoryTest</c>),
/// which asks the framework rather than the text.
/// </summary>
public class QueryShapeMatrixTest
{
    public static TheoryData<QueryShape, ORMEnum, ORMEnum> CategoryDirections()
        => QueryShapeInputs.Directions(QueryShapeInputs.Categories);

    public static TheoryData<QueryShape, ORMEnum, ORMEnum> SqlTargetDirections()
    {
        var data = new TheoryData<QueryShape, ORMEnum, ORMEnum>();
        foreach (var shape in QueryShapeInputs.Categories)
        {
            foreach (var source in shape.Sources.Keys)
            {
                data.Add(shape, source, ORMEnum.Dapper);
                data.Add(shape, source, ORMEnum.MyBatis);
            }
        }

        return data;
    }

    public static ConversionResult Convert(QueryShape shape, ORMEnum source, ORMEnum target)
        => ConversionHandler.Convert(source, target, QueryShapeInputs.Units(source, shape));

    public static List<ConversionSource> QueryArtifacts(ConversionResult result)
        => result.Sources.Where(s => s.ContentType.IsQuery()).ToList();

    /// <summary>
    /// A refusal of the query, as opposed to one of an entity: an entity refused by a target
    /// that requires a key names the entity and the category (decision 063), which a source
    /// stating no key - Dapper, MyBatis - meets in three targets and which is not this
    /// test's subject.
    /// </summary>
    public static bool IsQueryFailure(ConversionRecord record)
        => record.Kind == ConversionRecordKind.Failure && record.Entity is null && record.Category is null;

    /// <summary>
    /// The text of every query artifact of the target, joined, for the hallmarks. A MyBatis
    /// query is a method of the mapper interface whose statement lives in the mapper
    /// document (decision 084), so for that target the documents are part of the text.
    /// </summary>
    public static string QueryText(ConversionResult result, ORMEnum target)
    {
        var artifacts = QueryArtifacts(result);
        if (target == ORMEnum.MyBatis)
        {
            artifacts.AddRange(result.Sources.Where(s => s.ContentType == ConversionContentType.XML));
        }

        return string.Join("\n", artifacts.Select(s => s.Content));
    }

    [Theory]
    [MemberData(nameof(CategoryDirections))]
    public void EveryDirectionCarriesTheCategory(QueryShape shape, ORMEnum source, ORMEnum target)
    {
        var result = Convert(shape, source, target);
        var queries = QueryArtifacts(result);

        if (shape.RefusedWithoutCatalog.TryGetValue(source, out var sourceFeature))
        {
            // A refusal the tool states as a rule of reading the source in a run without a
            // catalog, which this dry matrix is: no artifact in any direction, and a record
            // naming the feature (decision 083). With a catalog holding the domain the same
            // source yields the artifact into every target (decision 105), which
            // Catalog/QueryDemandCompletionTest proves over a fake one and the differential
            // matrix over the database.
            Assert.Empty(queries);
            Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == sourceFeature);
            return;
        }

        if (shape.RefusedBy.TryGetValue(target, out var feature))
        {
            // An expected refusal of a target: no artifact and a record that names the
            // feature (decision 053).
            Assert.Empty(queries);
            Assert.Contains(result.Records, r => r.Feature == feature);
            return;
        }

        Assert.False(
            result.Records.Any(IsQueryFailure),
            $"{source} -> {target} refused the {shape} query:\n"
            + string.Join("\n", result.Records.Where(IsQueryFailure).Select(r => r.Reason)));
        Assert.NotEmpty(queries);
        Assert.All(queries, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.Content)));

        // The third value of a cell (decision 113): the target's language does not speak
        // the shape, so the query goes out in native SQL through the framework's API, with a
        // record naming the feature - and a target that speaks the shape never falls back,
        // so a translation is never passed off as the other value. A direction that needs a
        // fact only the catalog supplies falls back in this dry run as well.
        if (shape.FallsBack(source, target) is { } unspoken)
        {
            Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == unspoken);
            Assert.Contains(queries, artifact => artifact.ContentType == ConversionContentType.SqlQuery);
            Assert.Contains(NativeSqlCall(target), QueryText(result, target), StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Fallback);
        }

        var text = QueryText(result, target);
        foreach (var mark in shape.HallmarksOf(source, target))
        {
            Assert.True(
                text.Contains(mark, StringComparison.Ordinal),
                $"{source} -> {target}: the {shape} query does not carry '{mark}':\n{text}");
        }
    }

    /// <summary>
    /// Level 2 for the SQL targets (decision 027): the statement Dapper emits and the one
    /// inside the MyBatis mapper have to parse as T-SQL. The mapper's placeholders are read
    /// back as variables first, because <c>#{x}</c> is MyBatis, not T-SQL.
    /// </summary>
    [Theory]
    [MemberData(nameof(SqlTargetDirections))]
    public void TheSqlTargetsEmitAStatementThatParses(QueryShape shape, ORMEnum source, ORMEnum target)
    {
        if (shape.RefusedBy.ContainsKey(target) || shape.RefusedWithoutCatalog.ContainsKey(source))
        {
            return;
        }

        var result = Convert(shape, source, target);

        foreach (var sql in EmittedSql(result, target))
        {
            TSqlAcceptance.ParseOrFail(sql);
        }
    }

    /// <summary>Every direction the manifest states as falling back, the identity directions never among them.</summary>
    public static TheoryData<QueryShape, ORMEnum, ORMEnum> FallbackDirections() => FallbackDirectionsInto(null);

    /// <summary>The directions of <see cref="FallbackDirections"/> into one target.</summary>
    public static TheoryData<QueryShape, ORMEnum, ORMEnum> FallbackDirectionsInto(ORMEnum? only)
    {
        var data = new TheoryData<QueryShape, ORMEnum, ORMEnum>();
        foreach (var shape in QueryShapeInputs.Categories)
        {
            foreach (var source in shape.Sources.Keys.Where(source => !shape.RefusedWithoutCatalog.ContainsKey(source)))
            {
                var targets = shape.FallbackBy.Keys
                    .Concat(shape.FallbackWithoutCatalog.Keys.Where(direction => direction.Source == source).Select(direction => direction.Target));
                foreach (var target in targets.Where(target => only is null || target == only))
                {
                    data.Add(shape, source, target);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// The third level of an artifact of the escape path (decision 113): its framework does
    /// not judge native SQL before running it - CreateSQLQuery and createNativeQuery store
    /// the text, ToQueryString prints it -, so the level is the one the Dapper target has, the
    /// bare SQL beside the method parsed as T-SQL. It is the same statement the Dapper target
    /// writes from the same source, which is what makes the escape path a new wrapping rather
    /// than a new writer.
    /// </summary>
    [Theory]
    [MemberData(nameof(FallbackDirections))]
    public void AFallbackEmitsTheStatementTheDapperTargetWrites(QueryShape shape, ORMEnum source, ORMEnum target)
    {
        var fallback = Convert(shape, source, target);
        var dapper = Convert(shape, source, ORMEnum.Dapper);

        var statements = EmittedSql(fallback, ORMEnum.Dapper).ToList();
        foreach (var sql in statements)
        {
            TSqlAcceptance.ParseOrFail(sql);
        }

        Assert.Equal(EmittedSql(dapper, ORMEnum.Dapper), statements);
    }

    /// <summary>
    /// Level 2 for the escape path of the .NET target the category matrix sends there
    /// (decision 113): NHibernate's CreateSQLQuery method compiles beside the generated
    /// entities, in the consumer's frame. EF Core's is compiled by the theory below, with
    /// every other EF Core method; the Java targets' by the Java suite.
    /// </summary>
    [Theory]
    [MemberData(nameof(FallbackDirectionsInto), ORMEnum.NHibernate)]
    public void AFallbackMethodOfNHibernateCompiles(QueryShape shape, ORMEnum source, ORMEnum target)
    {
        var result = Convert(shape, source, target);
        var entities = result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content).ToList();

        // The namespace the generated entities declare, which a source stating none derives.
        var usings = entities
            .Select(entity => Regex.Match(entity, @"^\s*namespace\s+([\w.]+)\s*;", RegexOptions.Multiline))
            .Where(match => match.Success)
            .Select(match => $"using {match.Groups[1].Value};")
            .Distinct()
            .Prepend("using NHibernate;");

        GeneratedQueryCompiler.CompileOrFail(
            $"QueryShapeFallback_{target}_{source}_{Regex.Replace(shape.Name, @"\W", string.Empty)}",
            result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content,
            entities,
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            string.Join("\n", usings));
    }

    /// <summary>The call of the framework's API for native SQL the method of the escape path makes (decision 113).</summary>
    public static string NativeSqlCall(ORMEnum target) => target switch
    {
        ORMEnum.NHibernate => "session.CreateSQLQuery(",
        ORMEnum.EFCore => "ctx.",
        ORMEnum.Hibernate or ORMEnum.EclipseLink => "em.createNativeQuery(",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, $"{target} has no escape path; its query language is the dialect's SQL."),
    };

    /// <summary>
    /// The statements a SQL target emitted: Dapper's bare SqlQuery artifacts, or the body of
    /// every &lt;select&gt; in the MyBatis mapper documents. A &lt;foreach&gt; stands for a
    /// collection parameter and is read as the parenthesized variable T-SQL would carry. The
    /// bare SQL of an escape path is read as Dapper's (decision 113).
    /// </summary>
    public static IEnumerable<string> EmittedSql(ConversionResult result, ORMEnum target)
    {
        if (target == ORMEnum.Dapper)
        {
            // `IN @ids` is Dapper's own spelling of a collection parameter - Dapper expands
            // the list itself (decision 083) - and no T-SQL; the grammar sees it in the
            // parenthesized form the reader takes.
            var statements = result.Sources
                .Where(s => s.ContentType == ConversionContentType.SqlQuery)
                .Select(s => Regex.Replace(s.Content, @"\bIN @(\w+)", "IN (@$1)"))
                .ToList();
            Assert.NotEmpty(statements);
            return statements;
        }

        var bodies = new List<string>();
        foreach (var document in result.Sources.Where(s => s.ContentType == ConversionContentType.XML))
        {
            var mapper = XDocument.Parse(document.Content);
            foreach (var select in mapper.Descendants("select"))
            {
                foreach (var foreachElement in select.Descendants("foreach").ToList())
                {
                    foreachElement.ReplaceWith(new XText($"(@{foreachElement.Attribute("collection")?.Value})"));
                }

                bodies.Add(Regex.Replace(select.Value, @"#\{(\w+)\}", "@$1"));
            }
        }

        Assert.NotEmpty(bodies);
        return bodies;
    }

    public static TheoryData<QueryShape, ORMEnum, ORMEnum> DotNetTargetDirections()
    {
        var data = new TheoryData<QueryShape, ORMEnum, ORMEnum>();
        foreach (var shape in QueryShapeInputs.Categories)
        {
            foreach (var source in shape.Sources.Keys)
            {
                data.Add(shape, source, ORMEnum.EFCore);
                data.Add(shape, source, ORMEnum.Dapper);
            }
        }

        return data;
    }

    /// <summary>
    /// Level 2 for the two .NET targets whose method carries the query in C# (decision 027):
    /// the generated method compiles beside the generated entities, in the consumer's frame.
    /// It is the level at which a LINQ chain typed against the wrong entity member, or a
    /// parameter object naming a parameter the method lacks, first shows - and the one a
    /// hallmark over text cannot stand in for.
    /// </summary>
    [Theory]
    [MemberData(nameof(DotNetTargetDirections))]
    public void TheDotNetQueryMethodCompiles(QueryShape shape, ORMEnum source, ORMEnum target)
    {
        if (shape.RefusedBy.ContainsKey(target) || shape.RefusedWithoutCatalog.ContainsKey(source))
        {
            return;
        }

        var result = Convert(shape, source, target);

        var method = result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        var entities = result.Sources
            .Where(s => s.ContentType == ConversionContentType.CSharpEntity)
            .Select(s => s.Content)
            .ToList();

        var (references, usings) = target == ORMEnum.EFCore
            ? (GeneratedQueryCompiler.EFCoreConsumerReferences, "using System;\nusing Microsoft.EntityFrameworkCore;\nusing Shop;")
            : (GeneratedQueryCompiler.DapperConsumerReferences, "using System;\nusing System.Data;\nusing Dapper;\nusing Shop;");

        GeneratedQueryCompiler.CompileOrFail(
            $"QueryShape_{target}_{source}_{Regex.Replace(shape.Name, @"\W", string.Empty)}",
            method,
            entities,
            references,
            usings);
    }

    /// <summary>
    /// The matrix is deterministic direction by direction (S2): the same shape converted
    /// twice gives byte-identical query artifacts, over every category at once.
    /// </summary>
    [Theory]
    [MemberData(nameof(CategoryDirections))]
    public void EveryDirectionIsDeterministic(QueryShape shape, ORMEnum source, ORMEnum target)
    {
        var first = QueryArtifacts(Convert(shape, source, target));
        var second = QueryArtifacts(Convert(shape, source, target));

        Assert.Equal(first.Select(s => (s.ContentType, s.Content)), second.Select(s => (s.ContentType, s.Content)));
    }

    /// <summary>
    /// The category is read the same way whichever language states it: into the SQL target,
    /// every source of a category over one table gives the same statement, up to the alias
    /// a projection carries (an HQL or LINQ reader names a projected column after itself,
    /// the T-SQL reader does not) and up to whitespace. It is the query-side counterpart of
    /// <see cref="SharedSqlReadingTest"/>: the intermediate representation, not the text, is
    /// what the parsers agree on. A source the tool refuses by rule is not in the comparison.
    /// </summary>
    [Theory]
    [InlineData("Filtering")]
    [InlineData("SubqueryAsTheRightSideOfIn")]
    [InlineData("ScalarSubquery")]
    [InlineData("InOverAListOfValues")]
    [InlineData("ScalarParameter")]
    [InlineData("ScalarSubqueryAgainstABoundValue")]
    [InlineData("DistinctProjection")]
    [InlineData("Ordering")]
    [InlineData("GroupingOverAGroupedResult")]
    [InlineData("AggregateOverTheWholeResult")]
    [InlineData("RecursiveDescentOfAHierarchy")]
    [InlineData("GroupingByAnExpression")]
    [InlineData("DateArithmetic")]
    [InlineData("CastInAConcatenation")]
    [InlineData("BestRowPerGroup")]
    [InlineData("ListAggregation")]
    [InlineData("JoinBeyondEqualities")]
    public void EverySourceLanguageReadsTheCategoryIntoTheSameSql(string name)
    {
        var shape = QueryShapeInputs.Categories.Single(s => s.Name == name);

        var statements = shape.Sources.Keys
            .Where(source => !shape.RefusedWithoutCatalog.ContainsKey(source))
            .Select(source => Normalized(Convert(shape, source, ORMEnum.Dapper)
                .Sources.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content))
            .Distinct()
            .ToList();

        Assert.True(statements.Count == 1, "The sources disagree about the SQL of the category:\n" + string.Join("\n---\n", statements));
    }

    private static string Normalized(string sql)
    {
        var oneLine = Regex.Replace(sql, @"\s+", " ").Trim();

        // `p.ProductId AS ProductId` and `p.ProductId` are one projection.
        return Regex.Replace(oneLine, @"\b(\w+)\.(\w+) AS \2\b", "$1.$2");
    }
}
