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
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using MyBatisWrappers;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A row count that the caller binds (decision 085). The pagination instruction carries two
/// counts and each of them is a number the query stated or a parameter it left open, which
/// is the shape paged code is actually written in - a literal offset asks for one fixed
/// page.
///
/// The scalar of such a parameter comes from the clause rather than from a comparison,
/// because a pagination has no other side, and it is Int: Skip, Take, SetFirstResult,
/// SetMaxResults, setFirstResult and setMaxResults all bind 32 bits, and T-SQL takes an int
/// wherever it takes a bigint. One scalar is what makes one model yield one signature in
/// every direction (S2).
/// </summary>
public class PaginationParameterTest
{
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

    private static T ParseSql<T>(T builder, string sql) where T : AbstractQueryBuilder
    {
        builder.EntityMaps = [Customers()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static T ParseLinq<T>(T builder, string chain) where T : AbstractQueryBuilder
    {
        builder.EntityMaps = [Customers()];
        new EFCoreLinqQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            $$"""
            public void Query()
            {
                var q = {{chain}};
            }
            """,
            [Customers()]);
        return builder;
    }

    /// <summary>HQL is the one query language of the six whose text has a limit clause (decision 076).</summary>
    private static T ParseHql<T>(T builder, string hql) where T : AbstractQueryBuilder
    {
        builder.EntityMaps = [Customers()];
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, hql, [Customers()]);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return outputs.Single(s => s.ContentType == type).Content;
    }

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery);

    private static string CSharp(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.CSharpQuery);

    private static string Java(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.JavaQuery);

    private static ConversionRecord AssertRefused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());

        var record = builder.Records
            .FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);

        Assert.NotNull(record);
        return record;
    }

    /// <summary>A page of the ordered customers, the shape every paging call site has.</summary>
    private const string SqlPage =
        "SELECT * FROM Sales.Customers AS c ORDER BY c.CustomerName OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

    // ---- Into each of the six targets ----------------------------------------------

    [Fact]
    public void ABoundPageRoundTripsToSqlAndTypesTheMethod()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), SqlPage);

        Assert.Contains("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", Sql(builder));

        var method = CSharp(builder);
        Assert.Contains("(IDbConnection connection, int skip, int take)", method);
        Assert.Contains("new { skip, take }", method);
    }

    [Fact]
    public void ABoundPageBecomesSkipAndTakeOverCapturedParameters()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder(), SqlPage);

        var method = CSharp(builder);
        Assert.Contains("(DbContext ctx, int skip, int take)", method);
        Assert.Contains(".Skip(skip)", method);
        Assert.Contains(".Take(take)", method);
    }

    /// <summary>
    /// NHibernate takes the slice on the IQuery rather than in the text, so a bound count is
    /// not a parameter of the query at all but an argument of the call - and the bare HQL
    /// artifact shows no more of it than it shows of a literal, which is a property of the
    /// format and carries no record (decisions 060 and 028).
    /// </summary>
    [Fact]
    public void ABoundPageBecomesArgumentsOfTheNHibernateApi()
    {
        var builder = ParseSql(new NHibernateHqlQueryBuilder(), SqlPage);

        var method = CSharp(builder);
        Assert.Contains("(ISession session, int skip, int take)", method);
        Assert.Contains(".SetFirstResult(skip)", method);
        Assert.Contains(".SetMaxResults(take)", method);

        var hql = Artifact(builder, ConversionContentType.HqlQuery);
        Assert.DoesNotContain("skip", hql, StringComparison.Ordinal);
        Assert.DoesNotContain(builder.Records, r => r.Feature == QueryFeature.Pagination);

        // And it is bound once, not twice: SetParameter would name a parameter the HQL does
        // not have, which NHibernate rejects at run time rather than ignoring.
        Assert.DoesNotContain(".SetParameter", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundPageBecomesArgumentsOfTheJpaApi()
    {
        var builder = ParseSql(new HibernateJpqlQueryBuilder(), SqlPage);

        var method = Java(builder);
        Assert.Contains("(EntityManager em, int skip, int take)", method);
        Assert.Contains(".setFirstResult(skip)", method);
        Assert.Contains(".setMaxResults(take)", method);
        Assert.DoesNotContain(".setParameter", method, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name the query uses as a row count <em>and</em> in a condition is in the text after
    /// all, so the target that binds by name has to bind it - the exclusion above is about
    /// names the text never mentions, not about row counts as such.
    /// </summary>
    [Fact]
    public void ANameThatIsAlsoInTheConditionIsStillBound()
    {
        var builder = ParseSql(
            new NHibernateHqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID > @n ORDER BY c.CustomerName OFFSET @n ROWS");

        var method = CSharp(builder);
        Assert.Contains(".SetParameter(\"n\", n)", method);
        Assert.Contains(".SetFirstResult(n)", method);
    }

    [Fact]
    public void ABoundPageReachesEclipseLinkTheSameWay()
    {
        var method = Java(ParseSql(new EclipseLinkJpqlQueryBuilder(), SqlPage));

        Assert.Contains("(EntityManager em, int skip, int take)", method);
        Assert.Contains(".setMaxResults(take)", method);
    }

    /// <summary>
    /// MyBatis needed no work of its own for any of this: the shared T-SQL builder writes
    /// @take and the wrapper's placeholder pass rewrites it with every other parameter of
    /// the statement (decisions 082 and 084).
    /// </summary>
    [Fact]
    public void ABoundPageBecomesAMyBatisPlaceholderWithoutTheWrapperKnowingOfIt()
    {
        var builder = ParseSql(new MyBatisSqlQueryBuilder(), SqlPage);

        Assert.Contains("OFFSET #{skip} ROWS FETCH NEXT #{take} ROWS ONLY", Artifact(builder, ConversionContentType.XML));
        Assert.Contains("@Param(\"take\") int take", Java(builder));
    }

    // ---- Out of each source that reads a row count ----------------------------------

    /// <summary>
    /// TOP takes a parameter only in parentheses, which is where this step puts a count
    /// either way.
    /// </summary>
    [Fact]
    public void ABoundLimitAloneIsAParenthesizedTop()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), "SELECT TOP (@take) c.CustomerName FROM Sales.Customers AS c");

        Assert.Contains("SELECT TOP (@take) c.CustomerName", Sql(builder));
        Assert.Contains("(IDbConnection connection, int take)", CSharp(builder));
    }

    [Fact]
    public void AValueFromTheEnclosingScopeIsTheRowCountOfALinqChain()
    {
        var builder = ParseLinq(
            new DapperSqlQueryBuilder(),
            "ctx.Customers.OrderBy(c => c.CustomerName).Skip(skip).Take(pageSize).ToList()");

        Assert.Contains("OFFSET @skip ROWS FETCH NEXT @pageSize ROWS ONLY", Sql(builder));
        Assert.Contains("(IDbConnection connection, int skip, int pageSize)", CSharp(builder));
    }

    [Fact]
    public void TheHqlLimitClauseTakesAParameter()
    {
        var builder = ParseHql(
            new DapperSqlQueryBuilder(),
            "select c from Customer c order by c.CustomerName limit :take offset :skip");

        Assert.Contains("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", Sql(builder));
        Assert.Contains("(IDbConnection connection, int skip, int take)", CSharp(builder));
    }

    /// <summary>
    /// A positional row count survives as positional only in JPQL, and HQL is the only
    /// language that can write one into a limit clause; every other target names it after
    /// its order and says so (decision 083).
    /// </summary>
    [Fact]
    public void APositionalRowCountComesOutNamedAfterItsOrder()
    {
        var builder = ParseHql(new DapperSqlQueryBuilder(), "select c from Customer c limit ?1");

        Assert.Contains("SELECT TOP (@p1)", Sql(builder));
        Assert.Contains("(IDbConnection connection, int p1)", CSharp(builder));
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.QueryParameter);
    }

    // ---- One name is one value ------------------------------------------------------

    [Fact]
    public void OneNameInBothCountsIsOneParameterOfTheMethod()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c ORDER BY c.CustomerName OFFSET @n ROWS FETCH NEXT @n ROWS ONLY");

        Assert.Contains("OFFSET @n ROWS FETCH NEXT @n ROWS ONLY", Sql(builder));
        Assert.Contains("(IDbConnection connection, int n)", CSharp(builder));
    }

    /// <summary>
    /// A name that stands both as a row count and in a condition is one parameter where the
    /// two agree - the row count says Int, so the column it is compared against has to be an
    /// Int one.
    /// </summary>
    [Fact]
    public void ANameUsedAsARowCountAndInAConditionUnifiesWhenBothSayInt()
    {
        var builder = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID > @n ORDER BY c.CustomerName OFFSET @n ROWS");

        Assert.Contains("(IDbConnection connection, int n)", CSharp(builder));
    }

    [Fact]
    public void ANameUsedAsARowCountAndAgainstAnotherScalarRefusesNamingBoth()
    {
        var record = AssertRefused(
            ParseSql(
                new DapperSqlQueryBuilder(),
                "SELECT * FROM Sales.Customers AS c WHERE c.CustomerName = @n ORDER BY c.CustomerName OFFSET @n ROWS"),
            QueryFeature.QueryParameter);

        Assert.Contains("Int", record.Reason, StringComparison.Ordinal);
        Assert.Contains("String", record.Reason, StringComparison.Ordinal);
    }

    // ---- The order of the signature -------------------------------------------------

    /// <summary>
    /// The row counts come after the conditions of their scope whatever order the parser
    /// recorded them in (decision 085). T-SQL reads TOP inside the SELECT clause and LINQ
    /// reads Take at the end of the chain, so without the rule one query would yield two
    /// signatures depending on which source it came from.
    /// </summary>
    [Fact]
    public void TwoSourcesOfOneQueryYieldOneSignature()
    {
        var fromSql = ParseSql(
            new DapperSqlQueryBuilder(),
            "SELECT * FROM Sales.Customers AS c WHERE c.CreditLimit > @limit ORDER BY c.CustomerName OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY");

        var fromLinq = ParseLinq(
            new DapperSqlQueryBuilder(),
            "ctx.Customers.Where(c => c.CreditLimit > limit).OrderBy(c => c.CustomerName).Skip(skip).Take(take).ToList()");

        const string signature = "(IDbConnection connection, decimal limit, int skip, int take)";

        Assert.Contains(signature, CSharp(fromSql));
        Assert.Contains(signature, CSharp(fromLinq));
    }

    // ---- What a row count is not ----------------------------------------------------

    /// <summary>
    /// A row count is one number, so a parameter that binds a list is not one - reachable
    /// because MyBatis's foreach writes itself into the text as a one-element IN list
    /// (decision 084) and could land in a FETCH clause.
    /// </summary>
    [Fact]
    public void ACollectionParameterIsNoRowCount()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.From("Sales.Customers", "c");
        builder.Paginate(null, RowCount.Bound(QueryParameter.Named("ids", isCollection: true)));

        var record = AssertRefused(builder, QueryFeature.QueryParameter);
        Assert.Contains("ids", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// MyBatis is the one source that states a scalar of its own. A whole number wider than
    /// Int is carried no further than the generated method's Int - a fact the source stated
    /// and the artifact does not use, which is a loss and not a conflict: the two sides here
    /// are the source and the limit of the targets, not two sources of one fact.
    /// </summary>
    [Fact]
    public void AWiderWholeNumberStatedForARowCountIsALoss()
    {
        var result = ConvertMyBatisPage("long take");

        Assert.Contains("int take", result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
        Assert.Contains(
            result.Records,
            r => r.Kind == ConversionRecordKind.Loss
                 && r.Feature == QueryFeature.QueryParameter
                 && r.Reason.Contains("take", StringComparison.Ordinal));
    }

    [Fact]
    public void AScalarStatedForARowCountThatIsNoWholeNumberRefuses()
    {
        var result = ConvertMyBatisPage("String take");

        Assert.Contains(
            result.Records,
            r => r.Kind == ConversionRecordKind.Failure
                 && r.Feature == QueryFeature.QueryParameter
                 && r.Reason.Contains("take", StringComparison.Ordinal));
    }

    /// <summary>
    /// One paged MyBatis statement whose mapper method declares the count, which is where
    /// the wrapper reads a stated scalar from (decision 084).
    /// </summary>
    private static ConversionResult ConvertMyBatisPage(string declaration)
    {
        const string domain = """
            package Shop;

            public class Customer {
                private Integer CustomerId;
                private String CustomerName;
            }
            """;

        var mapperInterface = $$"""
            package Shop;

            import java.util.List;
            import org.apache.ibatis.annotations.Param;

            public interface CustomerMapper {
                List<Customer> findPage(@Param("take") {{declaration}});
            }
            """;

        const string mapper = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                    "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
            <mapper namespace="Shop.CustomerMapper">
              <select id="findPage" resultType="Customer">
                SELECT TOP (#{take}) c.CustomerName
                FROM Sales.Customers AS c
              </select>
            </mapper>
            """;

        return ConversionHandler.Convert(ORMEnum.MyBatis, ORMEnum.Dapper,
        [
            new ConversionSource { Content = domain, ContentType = ConversionContentType.JavaEntity },
            new ConversionSource { Content = mapperInterface, ContentType = ConversionContentType.JavaQuery },
            new ConversionSource { Content = mapper, ContentType = ConversionContentType.XML },
        ]);
    }
}
