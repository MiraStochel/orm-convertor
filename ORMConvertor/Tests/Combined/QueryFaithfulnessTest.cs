using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A generated query may be poorer than its source, never different from it. Two ways it used
/// to end up different are covered here: a LIKE pattern handed to string.Contains verbatim,
/// so the query searched for literal percent signs (decision 051), and a condition the target
/// could not render replaced by a tautology, so the query returned every row the source
/// filtered out (decision 053).
/// </summary>
public class QueryFaithfulnessTest
{
    private static string TranslateToLinq(string sql)
    {
        var builder = new EFCoreLinqQueryBuilder();
        new DapperSqlQueryParser(builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder.Build().Single().Content;
    }

    private static string Filter(string predicate)
        => TranslateToLinq($"SELECT c.Id FROM Customers c WHERE {predicate}");

    [Theory]
    [InlineData("c.Name LIKE '%Ltd%'", ".Contains(\"Ltd\")")]
    [InlineData("c.Name LIKE 'Ltd%'", ".StartsWith(\"Ltd\")")]
    [InlineData("c.Name LIKE '%Ltd'", ".EndsWith(\"Ltd\")")]
    [InlineData("c.Name LIKE 'Ltd'", "== \"Ltd\"")]
    public void AnAnchoredPatternBecomesItsLinqCounterpart(string predicate, string expected)
    {
        var query = Filter(predicate);

        Assert.Contains(expected, query, StringComparison.Ordinal);

        // The wildcard is the pattern's, not the value's: it must not survive into the argument.
        Assert.DoesNotContain("%", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("c.Name LIKE 'A%Ltd'")]
    [InlineData("c.Name LIKE 'A_C'")]
    public void APatternWithoutALinqCounterpartGoesOutAsLike(string predicate)
    {
        var query = Filter(predicate);

        // Exact rather than approximate: EF.Functions.Like translates to LIKE unchanged, and
        // the artifact already needs the EF Core namespace for DbContext.
        Assert.Contains("EF.Functions.Like(", query, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTargetRendersAComparisonMissingItsRightOperand()
    {
        // One gate in the template answers for all three, so the three cannot drift apart:
        // Dapper used to throw, NHibernate wrote "1 = 1" and EF Core "true".
        AbstractQueryBuilder[] builders =
        [
            new DapperSqlQueryBuilder(),
            new EFCoreLinqQueryBuilder(),
            new NHibernateHqlQueryBuilder(),
        ];

        foreach (var builder in builders)
        {
            builder.From("Customers", "c");
            builder.Project("c", "Id");
            builder.Where(new ComparisonCondition(
                QueryOperand.Column("c", "Id"),
                ComparisonOperator.Equal,
                Right: null));

            var outputs = builder.Build();

            Assert.Empty(outputs);
            var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
            Assert.Contains("right operand", record.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ANullTestStillNeedsNoRightOperand()
    {
        // The one operator whose right side is deliberately unused (decision 002) must not be
        // caught by the same gate.
        var builder = new NHibernateHqlQueryBuilder();
        builder.From("Customers", "c");
        builder.Project("c", "Id");
        builder.Where(new ComparisonCondition(QueryOperand.Column("c", "Id"), ComparisonOperator.IsNull));

        var outputs = builder.Build();

        Assert.NotEmpty(outputs);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AFailedQueryDoesNotTakeTheEntitiesWithIt()
    {
        // Dapper's visitor used to throw, so one unrenderable query aborted the whole request
        // including the entities that had translated fine (decision 053). Through the
        // orchestration the failure is now one record beside a complete entity artifact.
        var result = ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.EFCore,
            [
                new ConversionSource
                {
                    ContentType = ConversionContentType.CSharpEntity,
                    Content = "public class Customer { public int Id { get; set; } }",
                },
                new ConversionSource
                {
                    ContentType = ConversionContentType.SqlQuery,
                    Content = "SELECT FROM WHERE",
                },
            ]);

        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AFullOuterJoinComposesFromLeftAndRightJoinForEFCore()
    {
        // EF Core 10 has no full outer join operator; an inner join in its place used to go
        // out with a Loss record, and an inner join returns fewer rows (decision 065). The
        // faithful composition is the left join concatenated with the right join's rows
        // that found no left match.
        var builder = new EFCoreLinqQueryBuilder();
        new DapperSqlQueryParser(builder).Parse(
            ConversionContentType.SqlQuery,
            "SELECT c.CustomerName FROM Customers c FULL JOIN Orders o ON o.CustomerId = c.CustomerId");

        var query = builder.Build().Single().Content;

        Assert.Contains(".LeftJoin(", query, StringComparison.Ordinal);
        Assert.Contains(".Concat(", query, StringComparison.Ordinal);
        Assert.Contains(".RightJoin(", query, StringComparison.Ordinal);
        Assert.Contains("== null", query, StringComparison.Ordinal);

        // A faithful translation is neither a loss nor a convention of the join kind.
        Assert.DoesNotContain(builder.Records, r => r.Feature == QueryFeature.JoinKind);
    }

    [Fact]
    public void AFullOuterJoinRefusesTheArtifactForNHibernate()
    {
        // HQL 5.7.0 has neither a full outer join nor set operations to compose one from,
        // and the inner join that used to go out in its place returned fewer rows.
        var builder = new NHibernateHqlQueryBuilder();
        builder.From("Customers", "c");
        builder.Join(
            JoinKind.Full,
            "c",
            "Orders",
            new ComparisonCondition(
                QueryOperand.Column("o", "CustomerId"),
                ComparisonOperator.Equal,
                QueryOperand.Column("c", "CustomerId")),
            "o");
        builder.Project("c", "CustomerName");

        var outputs = builder.Build();

        Assert.Empty(outputs);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(QueryFeature.JoinKind, record.Feature);
    }

    [Fact]
    public void AJoinWithoutKeyEqualitiesRefusesTheArtifactForEFCore()
    {
        // A LINQ join takes two key selectors, so this condition has no shape to go into;
        // the join used to be dropped with a Loss, and a query without its join returns
        // different rows - it neither filters nor multiplies (decision 065).
        var builder = new EFCoreLinqQueryBuilder();
        new DapperSqlQueryParser(builder).Parse(
            ConversionContentType.SqlQuery,
            "SELECT c.CustomerName FROM Customers c INNER JOIN Orders o ON o.OrderValue > c.CreditLimit");

        var outputs = builder.Build();

        Assert.Empty(outputs);
        Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join);
    }

    [Fact]
    public void AnInComparisonEfCoreCannotWriteRefusesTheArtifact()
    {
        var builder = new EFCoreLinqQueryBuilder();
        builder.From("Customers", "c");
        builder.Project("c", "Id");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("c", "Id"),
            ComparisonOperator.In,
            QueryOperand.Value(QueryConstant.Of("1", ScalarType.Int))));

        var outputs = builder.Build();

        // Dropping the filter and writing "true" widened the result set to the whole table.
        Assert.Empty(outputs);
        Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }
}
