using Model;
using OrmConvertor;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// The deliberately bad query of <see cref="QueryShapeInputs.DeeplyNested"/> - eight
/// subqueries nested four levels deep, three joins of which one runs over two columns and one
/// over three, grouping with a HAVING over a parameter, ordering and bound pagination -
/// written in the language of every source and carried by every target. What a simple
/// sample cannot show is whether scopes, aliases and correlations survive one another: a
/// correlated reference two scopes up, a DISTINCT and a list of values inside the nesting, a
/// NOT over an EXISTS that itself holds a scalar subquery. Every direction has to carry all
/// of it, refuse none of it, and write it in its own language.
/// </summary>
public class DeeplyNestedQueryTest
{
    public static TheoryData<ORMEnum, ORMEnum> Directions()
    {
        var data = new TheoryData<ORMEnum, ORMEnum>();
        foreach (var source in QueryShapeInputs.DeeplyNested.Sources.Keys)
        {
            foreach (var target in Enum.GetValues<ORMEnum>())
            {
                data.Add(source, target);
            }
        }

        return data;
    }

    public static TheoryData<ORMEnum> Sources()
    {
        var data = new TheoryData<ORMEnum>();
        foreach (var source in QueryShapeInputs.DeeplyNested.Sources.Keys)
        {
            data.Add(source);
        }

        return data;
    }

    public static ConversionResult Convert(ORMEnum source, ORMEnum target)
        => QueryShapeMatrixTest.Convert(QueryShapeInputs.DeeplyNested, source, target);

    /// <summary>The sources whose query language carries a slice, so their row states one.</summary>
    public static bool StatesPagination(ORMEnum source)
        => source is ORMEnum.Dapper or ORMEnum.EFCore or ORMEnum.Hibernate or ORMEnum.MyBatis;

    /// <summary>
    /// Every source's query is read whole and every target writes it back with every one of
    /// its parts: the hallmarks the inputs state per target, and as many query scopes as the
    /// query has - one per subquery, counted by the word each language opens a scope with.
    /// </summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryDirectionCarriesTheWholeQuery(ORMEnum source, ORMEnum target)
    {
        var result = Convert(source, target);

        Assert.False(
            result.Records.Any(QueryShapeMatrixTest.IsQueryFailure),
            $"{source} -> {target} refused the query:\n"
            + string.Join("\n", result.Records.Where(QueryShapeMatrixTest.IsQueryFailure).Select(r => r.Reason)));

        var queries = QueryShapeMatrixTest.QueryArtifacts(result);
        Assert.NotEmpty(queries);

        var text = QueryShapeMatrixTest.QueryText(result, target);
        foreach (var mark in QueryShapeInputs.DeeplyNested.Hallmarks[target])
        {
            Assert.True(
                text.Contains(mark, StringComparison.Ordinal),
                $"{source} -> {target}: the query does not carry '{mark}':\n{text}");
        }

        var scopeOpener = target switch
        {
            ORMEnum.Dapper or ORMEnum.MyBatis => "FROM ",
            ORMEnum.EFCore => "ctx.Set<",
            _ => "from ",
        };
        var scopes = Occurrences(text, scopeOpener);

        Assert.True(
            scopes >= QueryShapeInputs.DeeplyNestedScopes,
            $"{source} -> {target}: only {scopes} query scopes reached the artifact, {QueryShapeInputs.DeeplyNestedScopes} were sent:\n{text}");
    }

    /// <summary>The sources whose row binds the two thresholds; the Dapper row writes them as literals (see the inputs).</summary>
    public static bool StatesParameters(ORMEnum source) => source != ORMEnum.Dapper;

    /// <summary>
    /// The two parameters of the query reach the generated method of every target, typed
    /// from the columns they are compared with (decision 083): both are whole numbers, the
    /// one under SUM by the column's own scalar.
    /// </summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void BothParametersReachTheGeneratedMethod(ORMEnum source, ORMEnum target)
    {
        if (!StatesParameters(source))
        {
            return;
        }

        var text = QueryShapeMatrixTest.QueryText(Convert(source, target), target);

        string[] expected = target switch
        {
            ORMEnum.Dapper => ["int minQuantity", "int minTotal", "@minQuantity", "@minTotal"],
            ORMEnum.EFCore => ["int minQuantity", "int minTotal"],
            ORMEnum.NHibernate => [".SetParameter(\"minQuantity\", minQuantity)", ".SetParameter(\"minTotal\", minTotal)"],
            ORMEnum.Hibernate or ORMEnum.EclipseLink => [".setParameter(\"minQuantity\", minQuantity)", ".setParameter(\"minTotal\", minTotal)"],
            ORMEnum.MyBatis => ["@Param(\"minQuantity\")", "@Param(\"minTotal\")", "#{minQuantity}", "#{minTotal}"],
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        foreach (var mark in expected)
        {
            Assert.True(text.Contains(mark, StringComparison.Ordinal), $"{source} -> {target} lacks '{mark}':\n{text}");
        }
    }

    /// <summary>
    /// Where the source states the slice, every target carries it in its own place: in the
    /// text for the SQL targets, on the chain for EF Core, on the query object for the three
    /// that take it there (decisions 060 and 085) - bound where the source bound it, as the
    /// two literals where the Dapper row wrote them.
    /// </summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void TheSliceReachesEveryTargetThatWasSentOne(ORMEnum source, ORMEnum target)
    {
        if (!StatesPagination(source))
        {
            return;
        }

        var text = QueryShapeMatrixTest.QueryText(Convert(source, target), target);

        var (skip, take) = StatesParameters(source) ? ("skip", "take") : ("5", "10");
        string[] expected = target switch
        {
            ORMEnum.Dapper => [$"OFFSET @{skip} ROWS FETCH NEXT @{take} ROWS ONLY", "int skip", "int take"],
            ORMEnum.EFCore => [$".Skip({skip})", $".Take({take})"],
            ORMEnum.NHibernate => [$".SetFirstResult({skip})", $".SetMaxResults({take})"],
            ORMEnum.Hibernate or ORMEnum.EclipseLink => [$".setFirstResult({skip})", $".setMaxResults({take})"],
            ORMEnum.MyBatis => [$"OFFSET #{{{skip}}} ROWS FETCH NEXT #{{{take}}} ROWS ONLY"],
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        if (!StatesParameters(source))
        {
            expected = target switch
            {
                ORMEnum.Dapper => ["OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY"],
                ORMEnum.MyBatis => ["OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY"],
                _ => expected,
            };
        }

        foreach (var mark in expected)
        {
            Assert.True(text.Contains(mark, StringComparison.Ordinal), $"{source} -> {target} lacks '{mark}':\n{text}");
        }
    }

    /// <summary>Level 2 for the SQL targets: the whole nested statement parses as T-SQL.</summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void TheSqlTargetsEmitAStatementThatParses(ORMEnum source)
    {
        foreach (var target in new[] { ORMEnum.Dapper, ORMEnum.MyBatis })
        {
            foreach (var sql in QueryShapeMatrixTest.EmittedSql(Convert(source, target), target))
            {
                TSqlAcceptance.ParseOrFail(sql);
            }
        }
    }

    /// <summary>
    /// Two conversions of the bad query give byte-identical artifacts in every direction (S2);
    /// a query this deep is where an alias or a scope counter drifting between runs would
    /// show first.
    /// </summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryDirectionIsDeterministic(ORMEnum source, ORMEnum target)
    {
        var first = QueryShapeMatrixTest.QueryArtifacts(Convert(source, target));
        var second = QueryShapeMatrixTest.QueryArtifacts(Convert(source, target));

        Assert.Equal(first.Select(s => (s.ContentType, s.Content)), second.Select(s => (s.ContentType, s.Content)));
    }

    private static int Occurrences(string text, string word)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += word.Length;
        }

        return count;
    }
}
