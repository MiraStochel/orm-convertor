using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using EclipseLinkWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using NHibernateWrappers;
using OrmConvertor;
using SampleData;

namespace Tests.Combined;

/// <summary>
/// The parameter operand end to end (decision 083): the fifth shape of
/// <see cref="QueryOperand"/>, carrying the name the source wrote without its decoration,
/// or the order where the source wrote none. Each of the four parsers reads its own
/// spelling - @id, a bare identifier from the enclosing scope, :id, ?1 - the shared gate of
/// the builder template derives the scalar from the other side of the comparison, and each
/// builder writes its target's placeholder together with a typed parameter of the generated
/// method and its binding.
///
/// Two limits are deliberate and tested as such: a parameter among the values of an IN list
/// and a parameter in the pagination stay refused (the latter in
/// <see cref="PaginationQueryTest"/>, the former in <see cref="InValueListTest"/>).
/// </summary>
public class QueryParameterTest
{
    /// <summary>The column deliberately differs from the property, so both name mappings are exercised.</summary>
    private static EntityMap Customers()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal, isNullable: true) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, name, limit] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = limit, ColumnName = "CreditLimit" },
            ],
        };
    }

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = [Customers()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string predicate)
    {
        builder.EntityMaps = [Customers()];
        new EFCoreLinqQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            $$"""
            public void Query()
            {
                var q = ctx.Customers.Where(c => {{predicate}}).ToList();
            }
            """,
            [Customers()]);
        return builder;
    }

    private static AbstractQueryBuilder ParseHql(AbstractQueryBuilder builder, string hql)
    {
        builder.EntityMaps = [Customers()];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, [Customers()]);
        return builder;
    }

    private static AbstractQueryBuilder ParseJpql(AbstractQueryBuilder builder, string jpql)
    {
        builder.EntityMaps = [Customers()];
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, [Customers()]);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return outputs.Single(s => s.ContentType == type).Content;
    }

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery);

    private static string Hql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.HqlQuery);

    private static string Jpql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.JpqlQuery);

    private static string CSharp(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.CSharpQuery);

    private static string Java(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.JavaQuery);

    /// <summary>
    /// The artifact does not come out and a record of the given category says why. The first
    /// matching record is returned rather than the only one: a query naming two unusable
    /// parameters reports both, because reading and the gate alike go on so that every
    /// reason reaches the caller at once.
    /// </summary>
    private static ConversionRecord AssertRefused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());

        var record = builder.Records
            .FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);

        Assert.NotNull(record);
        return record;
    }

    private const string SqlByLimit = "SELECT * FROM Sales.Customers AS c WHERE c.CreditLimit > @limit";

    // ---- One named parameter, from each source into each target ---------------------

    [Fact]
    public void ASqlParameterRoundTripsToSqlAndTypesTheMethod()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), SqlByLimit);

        Assert.Contains("WHERE c.CreditLimit > @limit", Sql(builder));

        // The scalar comes from the column the parameter is compared against, in its
        // non-nullable form: a comparison never tests NULL (decision 002).
        var method = CSharp(builder);
        Assert.Contains("(IDbConnection connection, decimal limit)", method);
        Assert.Contains("new { limit }", method);
    }

    [Fact]
    public void ASqlParameterBecomesACapturedLinqValue()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder(), SqlByLimit);

        var method = CSharp(builder);
        Assert.Contains("(DbContext ctx, decimal limit)", method);
        Assert.Contains("c.CreditLimit > limit", method);
    }

    [Fact]
    public void ASqlParameterBecomesAnHqlParameterAndItsBinding()
    {
        var builder = ParseSql(new NHibernateHqlQueryBuilder(), SqlByLimit);

        Assert.Contains("where c.CreditLimit > :limit", Hql(builder));

        var method = CSharp(builder);
        Assert.Contains("(ISession session, decimal limit)", method);
        Assert.Contains(".SetParameter(\"limit\", limit)", method);
    }

    [Fact]
    public void ASqlParameterBecomesAJpqlParameterAndItsBinding()
    {
        var builder = ParseSql(new HibernateJpqlQueryBuilder(), SqlByLimit);

        Assert.Contains("where c.CreditLimit > :limit", Jpql(builder));

        var method = Java(builder);
        Assert.Contains("(EntityManager em, BigDecimal limit)", method);
        Assert.Contains(".setParameter(\"limit\", limit)", method);
    }

    [Fact]
    public void ALinqValueFromTheScopeBecomesASqlParameter()
    {
        var builder = ParseLinq(new DapperSqlQueryBuilder(), "c.CreditLimit > limit");

        Assert.Contains("WHERE c.CreditLimit > @limit", Sql(builder));
        Assert.Contains("(IDbConnection connection, decimal limit)", CSharp(builder));
    }

    [Fact]
    public void AnHqlParameterBecomesASqlParameter()
    {
        var builder = ParseHql(new DapperSqlQueryBuilder(), "from Customer c where c.CreditLimit > :limit");

        Assert.Contains("WHERE c.CreditLimit > @limit", Sql(builder));
    }

    [Fact]
    public void AJpqlParameterBecomesAnEclipseLinkParameter()
    {
        var builder = ParseJpql(
            new EclipseLinkJpqlQueryBuilder(),
            "select c from Customer c where c.CreditLimit > :limit");

        Assert.Contains("where c.CreditLimit > :limit", Jpql(builder));
        Assert.Contains("(EntityManager em, BigDecimal limit)", Java(builder));
    }

    // ---- Where the scalar comes from ------------------------------------------------

    [Fact]
    public void TheScalarComesFromTheColumnTheParameterIsComparedAgainst()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID = @id");

        Assert.Contains("(IDbConnection connection, int id)", CSharp(builder));
    }

    [Fact]
    public void TheScalarComesFromTheConstantOnTheOtherSide()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Customers AS c WHERE @flag = 1");

        Assert.Contains("(IDbConnection connection, int flag)", CSharp(builder));
    }

    /// <summary>COUNT answers with a count, not in the type of the column it counted.</summary>
    [Fact]
    public void TheScalarUnderCountIsLong()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT c.CustomerName FROM Sales.Customers AS c GROUP BY c.CustomerName HAVING COUNT(c.CustomerID) > @least");

        Assert.Contains("(IDbConnection connection, long least)", CSharp(builder));
    }

    /// <summary>The other aggregates answer in the type of their column.</summary>
    [Fact]
    public void TheScalarUnderAnotherAggregateIsTheColumnsOwn()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT c.CustomerName FROM Sales.Customers AS c GROUP BY c.CustomerName HAVING SUM(c.CreditLimit) > @total");

        Assert.Contains("(IDbConnection connection, decimal total)", CSharp(builder));
    }

    /// <summary>A LIKE pattern is a string whatever the column it matches is typed as.</summary>
    [Fact]
    public void TheScalarOfALikePatternIsString()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerName LIKE @pattern");

        Assert.Contains("(IDbConnection connection, string pattern)", CSharp(builder));

        // A pattern unknown at translation time cannot be decomposed into StartsWith and
        // friends, so EF Core writes the LIKE it has (decision 051).
        var linq = ParseSql(new EFCoreLinqQueryBuilder(), "SELECT * FROM Sales.Customers AS c WHERE c.CustomerName LIKE @pattern");
        Assert.Contains("EF.Functions.Like(c.CustomerName, pattern)", CSharp(linq));
    }

    [Fact]
    public void TheSameParameterTwiceIsOneParameterOfTheMethod()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c WHERE c.CreditLimit > @limit OR c.CreditLimit < @limit");

        var method = CSharp(builder);
        Assert.Contains("(IDbConnection connection, decimal limit)", method);
        Assert.DoesNotContain("decimal limit, decimal limit", method);
    }

    /// <summary>
    /// A scalar the source states wins over the derived one, and the difference is a record
    /// rather than a silence - the same answer decision 015 gives when two sources of one
    /// fact disagree. No parser states a scalar yet, so the model is built by hand.
    /// </summary>
    [Fact]
    public void AStatedScalarWinsOverTheDerivedOneWithAConflict()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.From("Sales.Customers", "c");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("c", "CustomerID"),
            ComparisonOperator.Equal,
            QueryOperand.Bound(QueryParameter.Named("id", ScalarType.Long))));

        var method = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains("(IDbConnection connection, long id)", method);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Conflict && r.Feature == QueryFeature.QueryParameter);
    }

    // ---- Positional parameters ------------------------------------------------------

    /// <summary>
    /// JPQL is the one target language with a positional form, so a positional parameter
    /// stays positional there and is renamed after its order everywhere else, with the
    /// record that says so (decision 083).
    /// </summary>
    [Fact]
    public void APositionalParameterStaysPositionalInJpql()
    {
        var builder = ParseJpql(new HibernateJpqlQueryBuilder(), "select c from Customer c where c.CreditLimit > ?1");

        Assert.Contains("where c.CreditLimit > ?1", Jpql(builder));

        var method = Java(builder);
        Assert.Contains("(EntityManager em, BigDecimal p1)", method);
        Assert.Contains(".setParameter(1, p1)", method);
        Assert.DoesNotContain(builder.Records, r => r.Feature == QueryFeature.QueryParameter);
    }

    [Fact]
    public void APositionalParameterComesOutNamedElsewhereWithAConventionRecord()
    {
        var builder = ParseJpql(new DapperSqlQueryBuilder(), "select c from Customer c where c.CreditLimit > ?1");

        Assert.Contains("WHERE c.CreditLimit > @p1", Sql(builder));
        Assert.Contains("(IDbConnection connection, decimal p1)", CSharp(builder));
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.QueryParameter);
    }

    /// <summary>HQL writes a bare ?, whose order is the order of its occurrence in the text.</summary>
    [Fact]
    public void AnHqlBareQuestionMarkTakesTheOrderOfItsOccurrence()
    {
        var builder = ParseHql(
            new DapperSqlQueryBuilder(),
            "from Customer c where c.CustomerName = ? and c.CreditLimit > ?");

        var sql = Sql(builder);
        Assert.Contains("c.CustomerName = @p1", sql);
        Assert.Contains("c.CreditLimit > @p2", sql);
        Assert.Contains("(IDbConnection connection, string p1, decimal p2)", CSharp(builder));
    }

    [Fact]
    public void MixingNamedAndPositionalParametersRefusesTheArtifact()
    {
        var builder = ParseHql(
            new DapperSqlQueryBuilder(),
            "from Customer c where c.CustomerName = :name and c.CreditLimit > ?");

        var record = AssertRefused(builder, QueryFeature.QueryParameter);
        Assert.Contains("named and positional", record.Reason, StringComparison.Ordinal);
    }

    // ---- Collection parameters ------------------------------------------------------

    [Fact]
    public void AnHqlCollectionParameterBecomesTheTargetsOwnListBinding()
    {
        const string hql = "from Customer c where c.CustomerID in (:ids)";

        var dapper = ParseHql(new DapperSqlQueryBuilder(), hql);

        // Dapper expands a list itself, so the placeholder carries no parentheses.
        Assert.Contains("WHERE c.CustomerID IN @ids", Sql(dapper));
        Assert.Contains("(IDbConnection connection, IEnumerable<int> ids)", CSharp(dapper));

        var nhibernate = ParseHql(new NHibernateHqlQueryBuilder(), hql);
        Assert.Contains("where c.CustomerID in (:ids)", Hql(nhibernate));
        Assert.Contains(".SetParameterList(\"ids\", ids)", CSharp(nhibernate));

        var efcore = ParseHql(new EFCoreLinqQueryBuilder(), hql);
        Assert.Contains("ids.Contains(c.CustomerID)", CSharp(efcore));
    }

    /// <summary>
    /// The grammar of Jakarta Persistence puts a collection-valued input parameter in IN's
    /// place itself; both spellings are read and the standard one is written.
    /// </summary>
    [Theory]
    [InlineData("select c from Customer c where c.CustomerID in :ids")]
    [InlineData("select c from Customer c where c.CustomerID in (:ids)")]
    public void AJpqlCollectionParameterIsWrittenWithoutParentheses(string jpql)
    {
        var builder = ParseJpql(new HibernateJpqlQueryBuilder(), jpql);

        Assert.Contains("where c.CustomerID in :ids", Jpql(builder));
        Assert.Contains("(EntityManager em, Collection<Integer> ids)", Java(builder));
    }

    [Fact]
    public void ALinqContainsOverACollectionFromTheScopeBecomesASqlInParameter()
    {
        var builder = ParseLinq(new DapperSqlQueryBuilder(), "ids.Contains(c.CustomerID)");

        Assert.Contains("WHERE c.CustomerID IN @ids", Sql(builder));
        Assert.Contains("(IDbConnection connection, IEnumerable<int> ids)", CSharp(builder));
    }

    [Fact]
    public void ACollectionParameterOutsideInRefusesTheArtifact()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.From("Sales.Customers", "c");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("c", "CustomerID"),
            ComparisonOperator.Equal,
            QueryOperand.Bound(QueryParameter.Named("ids", isCollection: true))));

        var record = AssertRefused(builder, QueryFeature.QueryParameter);
        Assert.Contains("only as the right side of IN", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AScalarParameterAsInsRightSideRefusesTheArtifact()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.From("Sales.Customers", "c");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("c", "CustomerID"),
            ComparisonOperator.In,
            QueryOperand.Bound(QueryParameter.Named("id"))));

        AssertRefused(builder, QueryFeature.QueryParameter);
    }

    // ---- What the gate refuses ------------------------------------------------------

    [Fact]
    public void AParameterComparedWithASubqueryRefusesTheArtifact()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c WHERE @limit > (SELECT MAX(o.CreditLimit) FROM Sales.Customers AS o)");

        AssertRefused(builder, QueryFeature.QueryParameter);
    }

    [Fact]
    public void AParameterComparedWithAnotherParameterRefusesTheArtifact()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Customers AS c WHERE @floor > @ceiling");

        var record = AssertRefused(builder, QueryFeature.QueryParameter);
        Assert.Contains("floor", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterComparedWithAColumnNoMappingKnowsRefusesTheArtifact()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Customers AS c WHERE c.Unmapped > @value");

        AssertRefused(builder, QueryFeature.QueryParameter);
    }

    [Fact]
    public void TheSameParameterWithTwoScalarsRefusesTheArtifact()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID = @x OR c.CustomerName = @x");

        var record = AssertRefused(builder, QueryFeature.QueryParameter);
        Assert.Contains("two scalars", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name that is not a plain identifier has no form in the signature of a method. No
    /// parser produces one yet - <c>#{user.name}</c> arrives with the MyBatis wrapper - so
    /// the model is built by hand, and the rule is in place before its producer is.
    /// </summary>
    [Fact]
    public void ANameThatIsNotAPlainIdentifierRefusesTheArtifact()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.From("Sales.Customers", "c");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("c", "CustomerID"),
            ComparisonOperator.Equal,
            QueryOperand.Bound(QueryParameter.Named("user.name"))));

        var record = AssertRefused(builder, QueryFeature.QueryParameter);
        Assert.Contains("user.name", record.Reason, StringComparison.Ordinal);
    }

    // ---- The sample the interface offers --------------------------------------------

    /// <summary>
    /// The NHibernate samples carry a parameter (decision 083), so the capability is visible
    /// in the interface and in the sample the T2 matrix is filled from, rather than only in
    /// this file. Both of that framework's query languages are asserted into all five
    /// targets: the sample is worth nothing if it refuses anywhere, and a mapping that
    /// states its table is what lets the scalar be derived without reaching the catalog.
    /// </summary>
    [Theory]
    [InlineData(ConversionContentType.CSharpQuery, ORMEnum.Dapper, ConversionContentType.SqlQuery, "@minimumCreditLimit")]
    [InlineData(ConversionContentType.CSharpQuery, ORMEnum.EFCore, ConversionContentType.CSharpQuery, "decimal minimumCreditLimit")]
    [InlineData(ConversionContentType.CSharpQuery, ORMEnum.NHibernate, ConversionContentType.HqlQuery, ":minimumCreditLimit")]
    [InlineData(ConversionContentType.CSharpQuery, ORMEnum.Hibernate, ConversionContentType.JpqlQuery, ":minimumCreditLimit")]
    [InlineData(ConversionContentType.CSharpQuery, ORMEnum.EclipseLink, ConversionContentType.JavaQuery, "BigDecimal minimumCreditLimit")]
    [InlineData(ConversionContentType.HqlQuery, ORMEnum.Dapper, ConversionContentType.SqlQuery, "@minimumCreditLimit")]
    [InlineData(ConversionContentType.HqlQuery, ORMEnum.EFCore, ConversionContentType.CSharpQuery, "decimal minimumCreditLimit")]
    [InlineData(ConversionContentType.HqlQuery, ORMEnum.NHibernate, ConversionContentType.CSharpQuery, "SetParameter(\"minimumCreditLimit\"")]
    [InlineData(ConversionContentType.HqlQuery, ORMEnum.Hibernate, ConversionContentType.JavaQuery, "setParameter(\"minimumCreditLimit\"")]
    [InlineData(ConversionContentType.HqlQuery, ORMEnum.EclipseLink, ConversionContentType.JpqlQuery, ":minimumCreditLimit")]
    public void TheParameterizedSampleConvertsIntoEveryTarget(
        ConversionContentType language,
        ORMEnum target,
        ConversionContentType artifact,
        string hallmark)
    {
        var query = language == ConversionContentType.HqlQuery
            ? CustomerSampleNHibernate.HqlQuery
            : CustomerSampleNHibernate.Query;

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, target,
        [
            new() { Content = CustomerSampleNHibernate.Entity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = CustomerSampleNHibernate.XmlMapping, ContentType = ConversionContentType.XML },
            new() { Content = query, ContentType = language },
        ]);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains(hallmark, result.Sources.Single(s => s.ContentType == artifact).Content);
    }

    // ---- The parameter is never written back as a value -----------------------------

    /// <summary>
    /// A parameter inside a subquery operand reaches the signature of the outer method:
    /// the gate walks every scope before anything is rendered, so a nested scope cannot
    /// leave a parameter unbound (decision 083).
    /// </summary>
    [Fact]
    public void AParameterInsideASubqueryReachesTheSignature()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            """
            SELECT * FROM Sales.Customers AS c
            WHERE c.CustomerID IN (SELECT o.CustomerID FROM Sales.Customers AS o WHERE o.CreditLimit > @limit)
            """);

        Assert.Contains("o.CreditLimit > @limit", Sql(builder));
        Assert.Contains("(IDbConnection connection, decimal limit)", CSharp(builder));
    }
}
