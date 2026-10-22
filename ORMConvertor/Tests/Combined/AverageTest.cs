using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EclipseLinkWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The average of the model is SQL's AVG (decision 161): every target converts its own average
/// back into the scalar SQL answers in, and every reader reads the average of its language as
/// the SQL it computes. The categories AverageInAProjection and AverageInFloatingPoint carry
/// both through every direction and the fourth level; this class names what they do not: the
/// scale of the decimal from the mapping, the conversion in a subquery and over a window, the
/// escape where the argument has no scalar, the refusal where a reader cannot type it, and the
/// conversions that restore nothing.
/// </summary>
public class AverageTest
{
    /// <summary>Lines with a whole number, a decimal of eight places and one of two, and a column nothing maps.</summary>
    private static EntityMap Lines()
    {
        var id = new Property { Name = "LineId", Type = LangType.Scalar(ScalarType.Int) };
        var quantity = new Property { Name = "Quantity", Type = LangType.Scalar(ScalarType.Int) };
        var rate = new Property { Name = "Rate", Type = LangType.Scalar(ScalarType.Decimal) };
        var price = new Property { Name = "Price", Type = LangType.Scalar(ScalarType.Decimal) };
        return new EntityMap
        {
            Entity = new Entity { Name = "Line", Properties = [id, quantity, rate, price] },
            Table = "Lines",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "LineId" },
                new PropertyMap { Property = quantity, ColumnName = "Quantity" },
                new PropertyMap { Property = rate, ColumnName = "Rate", Precision = 18, Scale = 8 },
                new PropertyMap { Property = price, ColumnName = "Price", Precision = 18, Scale = 2 },
            ],
        };
    }

    private static AbstractQueryBuilder Builder(ORMEnum target) => target switch
    {
        ORMEnum.Dapper => new DapperSqlQueryBuilder(),
        ORMEnum.EFCore => new EFCoreLinqQueryBuilder(),
        ORMEnum.NHibernate => new NHibernateHqlQueryBuilder(),
        ORMEnum.Hibernate => new HibernateJpqlQueryBuilder(),
        ORMEnum.EclipseLink => new EclipseLinkJpqlQueryBuilder(),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static AbstractQueryBuilder Sql(ORMEnum target, string sql)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Lines()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, [Lines()]);
        return builder;
    }

    private static AbstractQueryBuilder Linq(ORMEnum target, string chain)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Lines()];
        var source = $$"""
            public void Query()
            {
                var q = ctx.Lines.{{chain}}.ToList();
            }
            """;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, [Lines()]);
        return builder;
    }

    private static AbstractQueryBuilder Hql(ORMEnum target, string hql)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Lines()];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, [Lines()]);
        return builder;
    }

    private static AbstractQueryBuilder Jpql(ORMEnum target, string jpql, bool hibernate)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Lines()];
        JakartaPersistence.JpqlQueryParser parser = hibernate
            ? new HibernateJpqlQueryParser(() => builder)
            : new EclipseLinkJpqlQueryParser(() => builder);
        parser.Parse(ConversionContentType.JpqlQuery, jpql, [Lines()]);
        return builder;
    }

    /// <summary>The one query artifact, on one line, with neither a refusal nor an escape recorded.</summary>
    private static string Text(AbstractQueryBuilder builder)
    {
        var built = builder.Build();
        Assert.True(built.Count > 0, "No artifact:\n" + Records(builder));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);

        var query = built.FirstOrDefault(s => s.ContentType is ConversionContentType.SqlQuery or ConversionContentType.HqlQuery or ConversionContentType.JpqlQuery)
            ?? built.First();
        return string.Join(" ", query.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
    }

    private static string Records(AbstractQueryBuilder builder)
        => string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}"));

    // ---- writing -----------------------------------------------------------------------

    private const string Averages = "SELECT AVG(l.Quantity) AS Quantity, AVG(l.Rate) AS Rate, AVG(l.Price) AS Price FROM Lines AS l";

    /// <summary>
    /// Each target converts into SQL's scalar in its own names; a decimal takes SQL's scale for
    /// it, max(s, 6), from the mapping - eight places for Rate, six for Price, whose two SQL
    /// Server widens.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate, "select cast(avg(l.Quantity) as Integer) as Quantity, cast(avg(l.Rate) as BigDecimal(38,8)) as Rate, cast(avg(l.Price) as BigDecimal(38,6)) as Price from Line l")]
    [InlineData(ORMEnum.EclipseLink, "select cast(avg(l.Quantity) as INTEGER) as Quantity, cast(avg(l.Rate) as DECIMAL(38,8)) as Rate, cast(avg(l.Price) as DECIMAL(38,6)) as Price from Line l")]
    [InlineData(ORMEnum.NHibernate, "select cast(avg(l.Quantity) as int) as Quantity, cast(avg(l.Rate) as decimal) as Rate, cast(avg(l.Price) as decimal) as Price from Line l")]
    [InlineData(ORMEnum.Dapper, Averages)]
    public void EveryTargetWritesSqlsAverage(ORMEnum target, string expected)
    {
        Assert.Equal(expected, Text(Sql(target, Averages)));
    }

    /// <summary>A whole number's average in a subquery: LINQ casts the terminal Average into a nullable int, since the subquery may select nothing.</summary>
    [Fact]
    public void LinqCastsTheAverageOfASubqueryIntoANullableInteger()
    {
        var text = Text(Sql(ORMEnum.EFCore, "SELECT l.LineId AS LineId FROM Lines AS l WHERE l.Quantity > (SELECT AVG(x.Quantity) FROM Lines AS x)"));

        Assert.Contains(".Where(l => l.Quantity > (int?)ctx.Set<Line>().Average(x => x.Quantity))", text, StringComparison.Ordinal);
    }

    /// <summary>The average over a window is converted as without one.</summary>
    [Fact]
    public void HibernateConvertsTheAverageOverAWindow()
    {
        var text = Text(Sql(ORMEnum.Hibernate, "SELECT l.LineId AS LineId, AVG(l.Quantity) OVER (PARTITION BY l.Price) AS Average FROM Lines AS l"));

        Assert.Contains("cast(avg(l.Quantity) over (partition by l.Price) as Integer) as Average", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the argument has no scalar, no conversion can be chosen, and the targets that would
    /// need one write native SQL. In a condition, so that EF Core's row, which declares every
    /// column it materializes, has a scalar for each.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AnAverageWithoutAScalarGoesToNativeSql(ORMEnum target)
    {
        var builder = Sql(target, "SELECT l.LineId AS LineId FROM Lines AS l WHERE l.Quantity > (SELECT AVG(x.Discount) FROM Lines AS x)");
        var built = builder.Build();

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Aggregation);
        Assert.Contains(built, s => s.ContentType == ConversionContentType.SqlQuery && s.Content.Contains("(SELECT AVG(x.Discount) FROM Lines AS x)", StringComparison.Ordinal));
    }

    // ---- reading -----------------------------------------------------------------------

    /// <summary>
    /// The bare average of each language as the SQL it computes: Hibernate and C# average a
    /// whole number in floating point, JPQL of EclipseLink and HQL of NHibernate as SQL does;
    /// over a decimal every language computes SQL's AVG.
    /// </summary>
    [Fact]
    public void EveryReaderReadsItsAverageAsTheSqlItComputes()
    {
        const string sql = "SELECT AVG(CAST(l.Quantity AS FLOAT)) AS Quantity, AVG(l.Price) AS Price FROM Lines AS l GROUP BY l.LineId";
        const string truncated = "SELECT AVG(l.Quantity) AS Quantity, AVG(l.Price) AS Price FROM Lines AS l GROUP BY l.LineId";

        Assert.Equal(sql, Text(Jpql(ORMEnum.Dapper, "select avg(l.Quantity) as Quantity, avg(l.Price) as Price from Line l group by l.LineId", hibernate: true)));
        Assert.Equal(sql, Text(Linq(ORMEnum.Dapper, "GroupBy(l => l.LineId).Select(g => new { Quantity = g.Average(x => x.Quantity), Price = g.Average(x => x.Price) })")));
        Assert.Equal(truncated, Text(Jpql(ORMEnum.Dapper, "select avg(l.Quantity) as Quantity, avg(l.Price) as Price from Line l group by l.LineId", hibernate: false)));
        Assert.Equal(truncated, Text(Hql(ORMEnum.Dapper, "select avg(l.Quantity) as Quantity, avg(l.Price) as Price from Line l group by l.LineId")));
    }

    /// <summary>The conversions the writers write read back as SQL's average, in each language.</summary>
    [Fact]
    public void TheConversionsBackReadAsSqlsAverage()
    {
        const string sql = "SELECT AVG(l.Quantity) AS Quantity, AVG(l.Rate) AS Rate FROM Lines AS l GROUP BY l.LineId";

        Assert.Equal(sql, Text(Jpql(ORMEnum.Dapper, "select cast(avg(l.Quantity) as Integer) as Quantity, cast(avg(l.Rate) as BigDecimal(38,8)) as Rate from Line l group by l.LineId", hibernate: true)));
        Assert.Equal(sql, Text(Jpql(ORMEnum.Dapper, "select cast(avg(l.Quantity) as INTEGER) as Quantity, cast(avg(l.Rate) as DECIMAL(38,8)) as Rate from Line l group by l.LineId", hibernate: false)));
        Assert.Equal(sql, Text(Hql(ORMEnum.Dapper, "select cast(avg(l.Quantity) as int) as Quantity, cast(avg(l.Rate) as decimal) as Rate from Line l group by l.LineId")));
        Assert.Equal(sql, Text(Linq(ORMEnum.Dapper, "GroupBy(l => l.LineId).Select(g => new { Quantity = (int)g.Average(x => x.Quantity), Rate = g.Average(x => x.Rate) })")));
    }

    /// <summary>C#'s cast back over the terminal Average of a subquery reads as SQL's average, the bare one in floating point.</summary>
    [Fact]
    public void LinqReadsTheAverageOfASubquery()
    {
        Assert.Contains(
            "WHERE l.Quantity > (SELECT AVG(x.Quantity) FROM Lines AS x)",
            Text(Linq(ORMEnum.Dapper, "Where(l => l.Quantity > (int?)ctx.Lines.Average(x => x.Quantity))")),
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE l.Quantity > (SELECT AVG(CAST(x.Quantity AS FLOAT)) FROM Lines AS x)",
            Text(Linq(ORMEnum.Dapper, "Where(l => l.Quantity > ctx.Lines.Average(x => x.Quantity))")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A conversion that restores nothing stays what it is: into a decimal of fewer places than
    /// SQL's average has, it would round the average, and a type outside the vocabulary is not
    /// read - a projection of it is dropped, as any projection the representation does not
    /// carry (decision 070); into a long over an int, it is a conversion of the vocabulary.
    /// </summary>
    [Fact]
    public void AConversionThatRestoresNothingStaysWhatItIs()
    {
        var rounded = Jpql(ORMEnum.Dapper, "select l.LineId as LineId, cast(avg(l.Rate) as BigDecimal(38,6)) as Rate from Line l group by l.LineId", hibernate: true);
        rounded.Build();
        Assert.Contains(rounded.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Expression && r.Reason.Contains("BigDecimal(38,6)", StringComparison.Ordinal));

        Assert.Equal(
            "SELECT CAST(AVG(l.Quantity) AS BIGINT) AS Quantity FROM Lines AS l GROUP BY l.LineId",
            Text(Hql(ORMEnum.Dapper, "select cast(avg(l.Quantity) as long) as Quantity from Line l group by l.LineId")));
    }

    /// <summary>Where the mapping states no number for what Hibernate or C# averages, the reader refuses the average by name.</summary>
    [Fact]
    public void AReaderThatCannotTypeTheAverageRefusesIt()
    {
        var hibernate = Jpql(ORMEnum.Dapper, "select avg(l.Discount) as Discount from Line l group by l.LineId", hibernate: true);
        Assert.Empty(hibernate.Build());
        Assert.Contains(hibernate.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Aggregation);

        // EclipseLink averages as SQL does, whatever the type: nothing to refuse.
        Assert.Equal(
            "SELECT AVG(l.Discount) AS Discount FROM Lines AS l GROUP BY l.LineId",
            Text(Jpql(ORMEnum.Dapper, "select avg(l.Discount) as Discount from Line l group by l.LineId", hibernate: false)));
    }
}
