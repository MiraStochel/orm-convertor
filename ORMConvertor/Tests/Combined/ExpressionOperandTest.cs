using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using NHibernateWrappers;
using Tests.Database;

namespace Tests.Combined;

/// <summary>
/// The expression as the sixth operand shape, end to end (decision 107): every one of the
/// four readers reads arithmetic, concatenation, the functions of the closed vocabulary and
/// CASE in its own spelling, the builder template types the expression from the mapping
/// and holds it to the four rules of the gate, and every one of the four visitors writes
/// it in the spelling of its target - so that the same query goes into all six targets
/// and comes back from EF Core as what it was. What no target could write faithfully is
/// refused by name under the category of expressions (decision 070), and what the model
/// does not carry - a null concatenated in C# - is said in a record (decision 048).
/// </summary>
public class ExpressionOperandTest
{
    private static EntityMap Products()
    {
        var id = new Property { Name = "ProductId", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "ProductName", Type = LangType.Scalar(ScalarType.String) };
        var code = new Property { Name = "Sku", Type = LangType.Scalar(ScalarType.String) };
        var price = new Property { Name = "UnitPrice", Type = LangType.Scalar(ScalarType.Decimal) };
        var quantity = new Property { Name = "Quantity", Type = LangType.Scalar(ScalarType.Int) };
        var weight = new Property { Name = "Weight", Type = LangType.Scalar(ScalarType.Double, isNullable: true) };
        var introduced = new Property { Name = "IntroducedOn", Type = LangType.Scalar(ScalarType.DateTime) };
        var notes = new Property { Name = "Notes", Type = LangType.Scalar(ScalarType.String, isNullable: true) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Product", Properties = [id, name, code, price, quantity, weight, introduced, notes] },
            Table = "Products",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "ProductId" },
                new PropertyMap { Property = name, ColumnName = "ProductName" },
                new PropertyMap { Property = code, ColumnName = "Sku" },
                new PropertyMap { Property = price, ColumnName = "UnitPrice" },
                new PropertyMap { Property = quantity, ColumnName = "Quantity" },
                new PropertyMap { Property = weight, ColumnName = "Weight" },
                new PropertyMap { Property = introduced, ColumnName = "IntroducedOn" },
                new PropertyMap { Property = notes, ColumnName = "Notes" },
            ],
        };
    }

    // ---- the four readers into the four targets -----------------------------------------

    private static AbstractQueryBuilder Sql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = [Products()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, [Products()]);
        return builder;
    }

    private static AbstractQueryBuilder Linq(AbstractQueryBuilder builder, string chain, bool nhibernate = false)
    {
        builder.EntityMaps = [Products()];
        var root = nhibernate ? "session.Query<Product>()" : "ctx.Products";
        var source = $$"""
            public void Query()
            {
                var q = {{root}}.{{chain}}.ToList();
            }
            """;
        LinqParsing.LinqQueryParser parser = nhibernate ? new NHibernateLinqQueryParser(() => builder) : new EFCoreLinqQueryParser(() => builder);
        parser.Parse(ConversionContentType.CSharpQuery, source, [Products()]);
        return builder;
    }

    private static AbstractQueryBuilder Hql(AbstractQueryBuilder builder, string hql)
    {
        builder.EntityMaps = [Products()];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, [Products()]);
        return builder;
    }

    private static AbstractQueryBuilder Jpql(AbstractQueryBuilder builder, string jpql)
    {
        builder.EntityMaps = [Products()];
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, [Products()]);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return outputs.Single(s => s.ContentType == type).Content;
    }

    private static string ToSql(AbstractQueryBuilder read) => Artifact(read, ConversionContentType.SqlQuery);

    private static string ToHql(AbstractQueryBuilder read) => Artifact(read, ConversionContentType.HqlQuery);

    private static string ToJpql(AbstractQueryBuilder read) => Artifact(read, ConversionContentType.JpqlQuery);

    private static string ToLinq(AbstractQueryBuilder read) => Artifact(read, ConversionContentType.CSharpQuery);

    private static ConversionRecord AssertRefused(AbstractQueryBuilder read, QueryFeature feature)
    {
        Assert.Empty(read.Build());
        var record = read.Records.FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
        Assert.NotNull(record);
        return record;
    }

    // ---- carried: one query into every target -------------------------------------------

    /// <summary>Arithmetic in a projection, from each of the four languages into each of the four; the expression is typed Decimal from the mapping.</summary>
    [Fact]
    public void ArithmeticInAProjectionReachesEveryTarget()
    {
        Assert.Contains("p.UnitPrice * p.Quantity AS Total", ToSql(Sql(new DapperSqlQueryBuilder(), "SELECT p.UnitPrice * p.Quantity AS Total FROM Sales.Products p")), StringComparison.Ordinal);
        Assert.Contains("Total = p.UnitPrice * p.Quantity", ToLinq(Sql(new EFCoreLinqQueryBuilder(), "SELECT p.UnitPrice * p.Quantity AS Total FROM Sales.Products p")), StringComparison.Ordinal);
        Assert.Contains("cast(p.UnitPrice * p.Quantity as decimal) as Total", ToHql(Sql(new NHibernateHqlQueryBuilder(), "SELECT p.UnitPrice * p.Quantity AS Total FROM Sales.Products p")), StringComparison.Ordinal);
        Assert.Contains("p.UnitPrice * p.Quantity as Total", ToJpql(Sql(new HibernateJpqlQueryBuilder(), "SELECT p.UnitPrice * p.Quantity AS Total FROM Sales.Products p")), StringComparison.Ordinal);

        Assert.Contains("p.UnitPrice * p.Quantity AS Total", ToSql(Linq(new DapperSqlQueryBuilder(), "Select(p => new { Total = p.UnitPrice * p.Quantity })")), StringComparison.Ordinal);
        Assert.Contains("p.UnitPrice * p.Quantity AS Total", ToSql(Hql(new DapperSqlQueryBuilder(), "select p.UnitPrice * p.Quantity as Total from Product p")), StringComparison.Ordinal);
        Assert.Contains("p.UnitPrice * p.Quantity AS Total", ToSql(Jpql(new DapperSqlQueryBuilder(), "select p.UnitPrice * p.Quantity as Total from Product p")), StringComparison.Ordinal);
    }

    /// <summary>
    /// NHibernate 5.7.0 does not type an arithmetic result over a decimal and a whole number
    /// as a decimal and cuts the fraction off; the HQL visitor casts the value to the decimal
    /// the gate typed it as and says so. Two decimals need no cast.
    /// </summary>
    [Fact]
    public void TheHqlTargetKeepsTheFractionOfAnArithmeticResult()
    {
        var mixed = Sql(new NHibernateHqlQueryBuilder(), "SELECT p.Quantity * p.UnitPrice AS Total FROM Sales.Products p");
        Assert.Contains("cast(p.Quantity * p.UnitPrice as decimal) as Total", ToHql(mixed), StringComparison.Ordinal);
        Assert.Contains(mixed.Records, r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Expression);

        var decimals = Sql(new NHibernateHqlQueryBuilder(), "SELECT p.UnitPrice * 1.5 AS Raised FROM Sales.Products p");
        Assert.Contains("p.UnitPrice * 1.5 as Raised", ToHql(decimals), StringComparison.Ordinal);
        Assert.DoesNotContain(decimals.Records, r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Expression);
    }

    /// <summary>ISNULL is T-SQL's own spelling of a two-argument COALESCE and comes back as COALESCE.</summary>
    [Fact]
    public void IsNullReadsAsCoalesce()
    {
        Assert.Contains("COALESCE(p.Notes, 'none') = 'none'", ToSql(Sql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Products p WHERE ISNULL(p.Notes, 'none') = 'none'")), StringComparison.Ordinal);
    }

    /// <summary>A parameter inside a concatenation is a string in the signature, from every source.</summary>
    [Fact]
    public void AParameterInAConcatenationIsAStringInTheSignature()
    {
        Assert.Contains("string prefix", ToLinq(Sql(new EFCoreLinqQueryBuilder(), "SELECT * FROM Sales.Products p WHERE p.ProductName LIKE @prefix + '%'")), StringComparison.Ordinal);
        Assert.Contains("string prefix", ToLinq(Hql(new EFCoreLinqQueryBuilder(), "from Product p where p.ProductName like concat(:prefix, '%')")), StringComparison.Ordinal);
        Assert.Contains("string prefix", ToLinq(Jpql(new EFCoreLinqQueryBuilder(), "select p from Product p where p.ProductName like :prefix || '%'")), StringComparison.Ordinal);
        Assert.Contains("string prefix", ToLinq(Linq(new EFCoreLinqQueryBuilder(), "Where(p => EF.Functions.Like(p.ProductName, prefix + \"%\"))")), StringComparison.Ordinal);
    }

    /// <summary>
    /// StartsWith over a bound value from EF Core: LIKE over the escaped value in every SQL
    /// target with the escape clause, and StartsWith(prefix) again in the identity direction.
    /// From NHibernate's LINQ the same call is the concatenation without the escaping, which
    /// the EF Core target writes as the pattern function, because StartsWith would escape.
    /// </summary>
    [Fact]
    public void StartsWithOverABoundValueIsEscapedWhereTheProviderEscapes()
    {
        const string chain = "Where(p => p.ProductName.StartsWith(prefix))";

        Assert.Contains("p.ProductName LIKE REPLACE(REPLACE(REPLACE(REPLACE(@prefix, '!', '!!'), '%', '!%'), '_', '!_'), '[', '![') + '%' ESCAPE '!'", ToSql(Linq(new DapperSqlQueryBuilder(), chain)), StringComparison.Ordinal);
        Assert.Contains("p.ProductName like concat(replace(replace(replace(replace(:prefix, '!', '!!'), '%', '!%'), '_', '!_'), '[', '!['), '%') escape '!'", ToHql(Linq(new NHibernateHqlQueryBuilder(), chain)), StringComparison.Ordinal);
        Assert.Contains("p.ProductName like concat(replace(replace(replace(replace(:prefix, '!', '!!'), '%', '!%'), '_', '!_'), '[', '!['), '%') escape '!'", ToJpql(Linq(new HibernateJpqlQueryBuilder(), chain)), StringComparison.Ordinal);
        Assert.Contains("p.ProductName.StartsWith(prefix)", ToLinq(Linq(new EFCoreLinqQueryBuilder(), chain)), StringComparison.Ordinal);

        Assert.Contains("p.ProductName LIKE @prefix + '%'", ToSql(Linq(new DapperSqlQueryBuilder(), chain, nhibernate: true)), StringComparison.Ordinal);
        Assert.Contains("EF.Functions.Like(p.ProductName, prefix + \"%\")", ToLinq(Linq(new EFCoreLinqQueryBuilder(), chain, nhibernate: true)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Where(p => p.ProductName.EndsWith(suffix))", "LIKE '%' + REPLACE(", "p.ProductName.EndsWith(suffix)")]
    [InlineData("Where(p => p.ProductName.Contains(part))", "LIKE '%' + REPLACE(", "p.ProductName.Contains(part)")]
    public void TheOtherAnchorsRoundTripThroughEFCore(string chain, string sqlMark, string linqMark)
    {
        Assert.Contains(sqlMark, ToSql(Linq(new DapperSqlQueryBuilder(), chain)), StringComparison.Ordinal);
        Assert.Contains(linqMark, ToLinq(Linq(new EFCoreLinqQueryBuilder(), chain)), StringComparison.Ordinal);
    }

    /// <summary>The pattern function over a concatenation the caller composed is written without escaping and survives the identity direction.</summary>
    [Fact]
    public void ThePatternFunctionOverAConcatenationIsCarriedAsWritten()
    {
        const string chain = "Where(p => EF.Functions.Like(p.ProductName, prefix + \"%\"))";
        var read = Linq(new EFCoreLinqQueryBuilder(), chain);

        Assert.Contains("EF.Functions.Like(p.ProductName, prefix + \"%\")", ToLinq(read), StringComparison.Ordinal);
        Assert.DoesNotContain(read.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Contains("p.ProductName LIKE @prefix + '%'", ToSql(Linq(new DapperSqlQueryBuilder(), chain)), StringComparison.Ordinal);
    }

    /// <summary>A + over two columns is untyped for the reader; the gate types it as an addition or a concatenation from the mapping, and HQL and JPQL spell the two differently.</summary>
    [Fact]
    public void ThePlusOverColumnsIsResolvedByTheGate()
    {
        Assert.Contains("cast(p.UnitPrice + p.Quantity as decimal) as Sum", ToHql(Sql(new NHibernateHqlQueryBuilder(), "SELECT p.UnitPrice + p.Quantity AS Sum FROM Sales.Products p")), StringComparison.Ordinal);
        Assert.Contains("concat(p.ProductName, p.Sku) as Label", ToHql(Sql(new NHibernateHqlQueryBuilder(), "SELECT p.ProductName + p.Sku AS Label FROM Sales.Products p")), StringComparison.Ordinal);
        Assert.Contains("concat(p.ProductName, p.Sku) as Label", ToJpql(Linq(new HibernateJpqlQueryBuilder(), "Select(p => new { Label = p.ProductName + p.Sku })")), StringComparison.Ordinal);
        Assert.Contains("p.ProductName + p.Sku AS Label", ToSql(Jpql(new DapperSqlQueryBuilder(), "select p.ProductName || p.Sku as Label from Product p")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UPPER(p.ProductName) = 'W'", "Where(p => p.ProductName.ToUpper() == \"W\")", "upper(p.ProductName) = 'W'", "upper(p.ProductName) = 'W'", "p.ProductName.ToUpper() == \"W\"")]
    [InlineData("LEN(p.ProductName) = 6", "Where(p => p.ProductName.Length == 6)", "length(p.ProductName) = 6", "length(p.ProductName) = 6", "p.ProductName.Length == 6")]
    [InlineData("COALESCE(p.Notes, 'none') = 'none'", "Where(p => (p.Notes ?? \"none\") == \"none\")", "coalesce(p.Notes, 'none') = 'none'", "coalesce(p.Notes, 'none') = 'none'", "(p.Notes ?? \"none\") == \"none\"")]
    [InlineData("SUBSTRING(p.Sku, 1, 3) = 'SKU'", "Where(p => p.Sku.Substring(0, 3) == \"SKU\")", "substring(p.Sku, 1, 3) = 'SKU'", "substring(p.Sku, 1, 3) = 'SKU'", "p.Sku.Substring(0, 3) == \"SKU\"")]
    [InlineData("YEAR(p.IntroducedOn) = 2024", "Where(p => p.IntroducedOn.Year == 2024)", "year(p.IntroducedOn) = 2024", "extract(year from p.IntroducedOn) = 2024", "p.IntroducedOn.Year == 2024")]
    [InlineData("ABS(p.Quantity) > 1", "Where(p => Math.Abs(p.Quantity) > 1)", "abs(p.Quantity) > 1", "abs(p.Quantity) > 1", "Math.Abs(p.Quantity) > 1")]
    [InlineData("p.Quantity % 2 = 0", "Where(p => p.Quantity % 2 == 0)", "mod(p.Quantity, 2) = 0", "mod(p.Quantity, 2) = 0", "p.Quantity % 2 == 0")]
    public void AFunctionInAFilterIsSpelledByEveryTarget(string sqlFilter, string linqChain, string hql, string jpql, string linq)
    {
        var sql = $"SELECT * FROM Sales.Products p WHERE {sqlFilter}";

        Assert.Contains(sqlFilter, ToSql(Sql(new DapperSqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains(hql, ToHql(Sql(new NHibernateHqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains(jpql, ToJpql(Sql(new HibernateJpqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains(linq, ToLinq(Sql(new EFCoreLinqQueryBuilder(), sql)), StringComparison.Ordinal);

        // The same query from the LINQ side, and read back from HQL and JPQL into T-SQL.
        Assert.Contains(sqlFilter, ToSql(Linq(new DapperSqlQueryBuilder(), linqChain)), StringComparison.Ordinal);
        Assert.Contains(linq, ToLinq(Linq(new EFCoreLinqQueryBuilder(), linqChain)), StringComparison.Ordinal);
        Assert.Contains(sqlFilter, ToSql(Hql(new DapperSqlQueryBuilder(), $"from Product p where {hql}")), StringComparison.Ordinal);
        Assert.Contains(sqlFilter, ToSql(Jpql(new DapperSqlQueryBuilder(), $"select p from Product p where {jpql}")), StringComparison.Ordinal);
    }

    /// <summary>The start of a Substring is translated, not carried: a bound position goes out as p + 1 into SQL and back as p into C#.</summary>
    [Fact]
    public void ASubstringPositionIsTranslatedBetweenOneAndZeroBased()
    {
        const string chain = "Where(p => p.Sku.Substring(start, 3) == \"SKU\")";

        Assert.Contains("SUBSTRING(p.Sku, @start + 1, 3) = 'SKU'", ToSql(Linq(new DapperSqlQueryBuilder(), chain)), StringComparison.Ordinal);
        Assert.Contains("p.Sku.Substring(start, 3) == \"SKU\"", ToLinq(Linq(new EFCoreLinqQueryBuilder(), chain)), StringComparison.Ordinal);
        Assert.Contains("SUBSTRING(p.Sku, 2, LEN(p.Sku))", ToSql(Hql(new DapperSqlQueryBuilder(), "from Product p where substring(p.Sku, 2) = 'KU'")), StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentTimestampIsCarriedFromEveryLanguage()
    {
        Assert.Contains("p.IntroducedOn < DateTime.Now", ToLinq(Sql(new EFCoreLinqQueryBuilder(), "SELECT * FROM Sales.Products p WHERE p.IntroducedOn < GETDATE()")), StringComparison.Ordinal);
        Assert.Contains("p.IntroducedOn < CURRENT_TIMESTAMP", ToSql(Linq(new DapperSqlQueryBuilder(), "Where(p => p.IntroducedOn < DateTime.Now)")), StringComparison.Ordinal);
        Assert.Contains("p.IntroducedOn < current_timestamp()", ToHql(Jpql(new NHibernateHqlQueryBuilder(), "select p from Product p where p.IntroducedOn < current_timestamp")), StringComparison.Ordinal);
        Assert.Contains("p.IntroducedOn < current_timestamp\n", ToJpql(Hql(new HibernateJpqlQueryBuilder(), "from Product p where p.IntroducedOn < current_timestamp()")) + "\n", StringComparison.Ordinal);
    }

    /// <summary>CASE with and without ELSE into LINQ and JPQL: the null of the branches' scalar, and JPQL's mandatory else.</summary>
    [Fact]
    public void ACaseIsWrittenWithAndWithoutAnElse()
    {
        const string withElse = "SELECT p.ProductId AS Id, CASE WHEN p.Quantity > 5 THEN 'bulk' ELSE 'single' END AS Size FROM Sales.Products p";
        Assert.Contains("Size = (p.Quantity > 5 ? \"bulk\" : \"single\")", ToLinq(Sql(new EFCoreLinqQueryBuilder(), withElse)), StringComparison.Ordinal);
        Assert.Contains("case when p.Quantity > 5 then 'bulk' else 'single' end as Size", ToJpql(Sql(new HibernateJpqlQueryBuilder(), withElse)), StringComparison.Ordinal);
        Assert.Contains("CASE WHEN p.Quantity > 5 THEN 'bulk' ELSE 'single' END AS Size", ToSql(Linq(new DapperSqlQueryBuilder(), "Select(p => new { Id = p.ProductId, Size = p.Quantity > 5 ? \"bulk\" : \"single\" })")), StringComparison.Ordinal);

        const string withoutElse = "SELECT p.ProductId AS Id, CASE WHEN p.Quantity > 5 THEN 'bulk' END AS Size FROM Sales.Products p";
        Assert.Contains("Size = (p.Quantity > 5 ? \"bulk\" : (string)null)", ToLinq(Sql(new EFCoreLinqQueryBuilder(), withoutElse)), StringComparison.Ordinal);
        Assert.Contains("case when p.Quantity > 5 then 'bulk' else null end as Size", ToJpql(Sql(new HibernateJpqlQueryBuilder(), withoutElse)), StringComparison.Ordinal);

        const string numeric = "SELECT p.ProductId AS Id, CASE WHEN p.Quantity > 5 THEN p.Quantity END AS Big FROM Sales.Products p";
        Assert.Contains("Big = (p.Quantity > 5 ? p.Quantity : (int?)null)", ToLinq(Sql(new EFCoreLinqQueryBuilder(), numeric)), StringComparison.Ordinal);

        // A simple CASE reads as the searched form.
        Assert.Contains("CASE WHEN p.Quantity = 1 THEN 'one' ELSE 'many' END AS Words", ToSql(Sql(new DapperSqlQueryBuilder(), "SELECT CASE p.Quantity WHEN 1 THEN 'one' ELSE 'many' END AS Words FROM Sales.Products p")), StringComparison.Ordinal);
    }

    /// <summary>ORDER BY COUNT(*) DESC of a grouped query, which used to fall as a loss, in every target.</summary>
    [Fact]
    public void OrderingByAnAggregateIsCarried()
    {
        const string sql = "SELECT p.Sku AS Sku, COUNT(*) AS Lines FROM Sales.Products p GROUP BY p.Sku ORDER BY COUNT(*) DESC";

        Assert.Contains("ORDER BY COUNT(*) DESC", ToSql(Sql(new DapperSqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(g => g.Count())", ToLinq(Sql(new EFCoreLinqQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains("order by count(*) desc", ToHql(Sql(new NHibernateHqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains("order by count(p) desc", ToJpql(Sql(new HibernateJpqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains("ORDER BY COUNT(*) DESC", ToSql(Linq(new DapperSqlQueryBuilder(), "GroupBy(p => p.Sku).OrderByDescending(g => g.Count()).Select(g => new { Sku = g.Key, Lines = g.Count() })")), StringComparison.Ordinal);
    }

    /// <summary>An aggregate over an expression, from LINQ and from SQL, into a grouped LINQ chain.</summary>
    [Fact]
    public void AnAggregateOverAnExpressionIsCarried()
    {
        const string sql = "SELECT p.Sku AS Sku, SUM(p.UnitPrice * p.Quantity) AS Total FROM Sales.Products p GROUP BY p.Sku";

        Assert.Contains("SUM(p.UnitPrice * p.Quantity) AS Total", ToSql(Sql(new DapperSqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains("Total = g.Sum(p => p.UnitPrice * p.Quantity)", ToLinq(Sql(new EFCoreLinqQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains("sum(cast(p.UnitPrice * p.Quantity as decimal)) as Total", ToHql(Sql(new NHibernateHqlQueryBuilder(), sql)), StringComparison.Ordinal);
        Assert.Contains("SUM(p.UnitPrice * p.Quantity) AS Total", ToSql(Linq(new DapperSqlQueryBuilder(), "GroupBy(p => p.Sku).Select(g => new { Sku = g.Key, Total = g.Sum(x => x.UnitPrice * x.Quantity) })")), StringComparison.Ordinal);
    }

    /// <summary>A concatenation over a column read from C# says that C# and SQL disagree about NULL (decision 048).</summary>
    [Fact]
    public void AConcatenationOverAColumnInCSharpIsRecorded()
    {
        var read = Linq(new DapperSqlQueryBuilder(), "Select(p => new { Label = p.ProductName + \"!\" })");
        Assert.Contains("p.ProductName + '!' AS Label", ToSql(read), StringComparison.Ordinal);
        Assert.Contains(read.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Expression && r.Reason.Contains("NULL", StringComparison.Ordinal));
    }

    // ---- refused, by name, under the category of expressions ------------------------------

    [Fact]
    public void AFunctionOutsideTheVocabularyInAFilterRefusesTheArtifact()
    {
        var record = AssertRefused(Sql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Products p WHERE ROUND(p.UnitPrice, 0) = 100"), QueryFeature.Expression);
        Assert.Contains("ROUND", record.Reason, StringComparison.Ordinal);

        AssertRefused(Hql(new DapperSqlQueryBuilder(), "from Product p where str(p.Quantity) = '1'"), QueryFeature.Expression);
        AssertRefused(Jpql(new DapperSqlQueryBuilder(), "select p from Product p where year(p.IntroducedOn) = 2024"), QueryFeature.Expression);
        AssertRefused(Linq(new DapperSqlQueryBuilder(), "Where(p => p.IntroducedOn < DateTime.UtcNow)"), QueryFeature.Expression);
    }

    /// <summary>A function the target's descriptor does not speak is refused by the gate, once for every target (rule Q14 at the grain of a function).</summary>
    [Fact]
    public void AFunctionTheDescriptorDoesNotSpeakRefusesTheArtifact()
    {
        var builder = new MuteBuilder();
        var read = Sql(builder, "SELECT * FROM Sales.Products p WHERE UPPER(p.ProductName) = 'W'");
        var record = AssertRefused(read, QueryFeature.Expression);
        Assert.Contains("Upper", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void APlusWithoutATypedSideRefusesTheArtifact()
    {
        var record = AssertRefused(Sql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Products p WHERE p.Quantity = @a + @b"), QueryFeature.Expression);
        Assert.Contains("addition from a concatenation", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DecimalWithAFloatingPointNumberRefusesTheArtifact()
    {
        AssertRefused(Sql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Products p WHERE p.UnitPrice * p.Weight > 1"), QueryFeature.Expression);
    }

    [Fact]
    public void AnExpressionProjectedWithoutAnAliasRefusesTheArtifact()
    {
        var record = AssertRefused(Sql(new DapperSqlQueryBuilder(), "SELECT p.UnitPrice * p.Quantity FROM Sales.Products p"), QueryFeature.Expression);
        Assert.Contains("alias", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAggregateOverAnAggregateRefusesTheArtifact()
    {
        AssertRefused(Sql(new DapperSqlQueryBuilder(), "SELECT SUM(COUNT(*) * 2) AS X FROM Sales.Products p GROUP BY p.Sku"), QueryFeature.Expression);
    }

    [Fact]
    public void GroupingByAnExpressionRefusesTheArtifact()
    {
        AssertRefused(Sql(new DapperSqlQueryBuilder(), "SELECT COUNT(*) AS N FROM Sales.Products p GROUP BY YEAR(p.IntroducedOn)"), QueryFeature.Expression);
        AssertRefused(Linq(new DapperSqlQueryBuilder(), "GroupBy(p => p.IntroducedOn.Year).Select(g => new { N = g.Count() })"), QueryFeature.Expression);
        AssertRefused(Hql(new DapperSqlQueryBuilder(), "select count(*) from Product p group by year(p.IntroducedOn)"), QueryFeature.Expression);
    }

    /// <summary>A function outside the vocabulary in a projection is a loss, as it was, now named under the category of expressions.</summary>
    [Fact]
    public void AFunctionOutsideTheVocabularyInAProjectionIsALoss()
    {
        var read = Sql(new DapperSqlQueryBuilder(), "SELECT p.ProductId AS Id, ROUND(p.UnitPrice, 0) AS Rounded FROM Sales.Products p");
        Assert.NotEmpty(read.Build());
        Assert.Contains(read.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Expression && r.Reason.Contains("ROUND", StringComparison.Ordinal));
    }

    /// <summary>Every descriptor speaks the whole vocabulary today - a fact about today, held here and not by the type (decision 107).</summary>
    [Fact]
    public void EveryDescriptorSpeaksTheWholeVocabulary()
    {
        Assert.All(FrameworkDescriptors.All, descriptor =>
            Assert.All(Enum.GetValues<QueryFunction>(), function => Assert.True(descriptor.Speaks(function), $"{descriptor.Framework} does not speak {function}.")));
    }

    /// <summary>A target whose descriptor speaks no function of the vocabulary, for the gate's refusal.</summary>
    private sealed class MuteBuilder : DapperSqlQueryBuilder
    {
        public override TargetFrameworkDescriptor Descriptor { get; } = new()
        {
            Framework = ORMEnum.Dapper,
            Ecosystem = DapperDescriptor.Instance.Ecosystem,
            Version = DapperDescriptor.Instance.Version,
            Dialect = DapperDescriptor.Instance.Dialect,
            Support = DapperDescriptor.Instance.Support,
            QuerySupport = DapperDescriptor.Instance.QuerySupport,
        };
    }
}
