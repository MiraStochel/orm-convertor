using AbstractWrappers.Diagnostics;
using DapperWrappers;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Which stage a Dapper query enters is decided by the language the unit declares, not by
/// what its text looks like (decision 047). The string heuristic that used to decide it -
/// "contains Query, a bracket and a semicolon" - refused valid SQL whose table happened to
/// be called QueryLog, and sent a C# call that never spells Query into the T-SQL parser.
/// </summary>
public class QueryInputLanguageTest
{
    private static (List<ConversionSource> Outputs, IReadOnlyList<ConversionRecord> Records) Translate(
        ConversionContentType contentType,
        string source)
    {
        var builder = new DapperSqlQueryBuilder();
        new DapperSqlQueryParser(() => builder).Parse(contentType, source);
        return (builder.Build(), builder.Records);
    }

    [Fact]
    public void SqlThatTripsTheOldHeuristicIsReadAsSql()
    {
        // Every one of the three tests the heuristic made: the word Query, a bracket, a
        // semicolon. Declared as SQL, so it is SQL.
        var (outputs, records) = Translate(
            ConversionContentType.SqlQuery,
            "SELECT q.Id FROM QueryLog q WHERE (q.Id = 1);");

        Assert.NotEmpty(outputs);
        Assert.DoesNotContain(records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains(
            outputs,
            o => o.ContentType == ConversionContentType.SqlQuery && o.Content.Contains("FROM QueryLog", StringComparison.Ordinal));
    }

    [Fact]
    public void ADapperCallWithoutTheWordQueryIsReadAsCSharp()
    {
        // ExecuteScalar is a Dapper method the old heuristic did not recognize, so the whole
        // snippet went into the T-SQL parser and came back as unparsable SQL.
        var (outputs, records) = Translate(
            ConversionContentType.CSharp,
            """
            public int Count(IDbConnection db)
            {
                return db.ExecuteScalar<int>("SELECT c.Id FROM Customers c");
            }
            """);

        Assert.NotEmpty(outputs);
        Assert.DoesNotContain(records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A C# unit is a whole file (decision 111), and one that holds an entity alone carries no
    /// Dapper call: that is no error of the query reading, which yields no query and says
    /// nothing. Whether the unit yielded anything at all is the conversion's to say, across
    /// both passes (decision 081) - and it says so for a text that holds neither.
    /// </summary>
    [Fact]
    public void CSharpWithNoDapperCallYieldsNoQueryAndTheConversionSpeaksAboutTheUnit()
    {
        var parsed = new DapperSqlQueryParser(() => new DapperSqlQueryBuilder()).Parse(ConversionContentType.CSharp, "var x = 1;");

        Assert.Empty(parsed);

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.Dapper,
        [
            new() { ContentType = ConversionContentType.CSharp, Content = "var x = 1;" },
        ]);

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Unit == "unit 1");
        Assert.Contains("neither a mapping fact nor a query", record.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("SQL", StringComparison.Ordinal));
    }
}
