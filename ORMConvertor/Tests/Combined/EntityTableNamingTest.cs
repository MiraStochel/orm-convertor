using AbstractWrappers.Diagnostics;
using Common.Naming;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The convention between an entity name and a table name lived in five places in three
/// variants, so one run could answer the same question two ways - the junction phase
/// singularized ADDRESS, the query builders did not (decision 050). The rule is checked here
/// once, and the callers are checked for asking it rather than repeating it.
/// </summary>
public class EntityTableNamingTest
{
    [Theory]
    [InlineData("Customers", "Customer")]
    [InlineData("ADDRESS", "ADDRES")]
    [InlineData("Customer", "Customer")]
    [InlineData("Sales.Customers", "Customer")]
    [InlineData("s", "s")]
    public void SingularizingIsCaseInsensitiveAndNeverEmptiesTheName(string table, string expected)
        => Assert.Equal(expected, EntityTableNaming.EntityNameFor(table));

    [Fact]
    public void TableCandidatesOfferTheStatedNameFirst()
    {
        Assert.Equal(["Customer", "Customers"], EntityTableNaming.TableCandidatesFor("Customer"));
        Assert.Equal(["Customers", "Customer"], EntityTableNaming.TableCandidatesFor("Customers"));

        // A one-character name has no second number - the empty string is not an answer, and
        // an empty candidate used to be sent to the catalog as a table name to look for.
        Assert.Equal(["s"], EntityTableNaming.TableCandidatesFor("s"));
    }

    [Theory]
    [InlineData("Customer", "Customers")]
    [InlineData("Statuses", "Statuses")]
    [InlineData("ADDRESS", "ADDRESS")]
    [InlineData("s", "s")]
    public void TheSingleTableNameIsThePluralAndNeverDoublesTheS(string entity, string expected)
        => Assert.Equal(expected, EntityTableNaming.TableNameFor(entity));

    /// <summary>
    /// The third step of resolving a LINQ source used to glue an "s" onto the entity name
    /// with its own comparison, so an entity already ending in s was matched by a doubled
    /// s instead of by the rule's other number.
    /// </summary>
    [Fact]
    public void TheLinqSourceStepAsksTheSameRuleAsTheRestOfTheTool()
    {
        var customer = new EntityMap { Entity = new() { Name = "Customer" }, Table = "CUST_MASTER" };
        var status = new EntityMap { Entity = new() { Name = "Status" }, Table = "STATUS_TABLE" };

        Assert.Contains("FROM CUST_MASTER", TranslateLinq("var q = ctx.Customers.ToList();", customer));
        Assert.Contains("FROM STATUS_TABLE", TranslateLinq("var q = ctx.Statu.ToList();", status));
        Assert.Contains("FROM Statuss", TranslateLinq("var q = ctx.Statuss.ToList();", status));
    }

    private static string TranslateLinq(string body, params EntityMap[] maps)
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = maps };
        new EFCoreLinqQueryParser(builder).Parse(
            ConversionContentType.CSharpQuery,
            $"public void Query()\n{{\n    {body}\n}}",
            maps);

        return builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;
    }

    [Fact]
    public void TheQueryBuildersAnswerWithTheSameRuleAsTheRestOfTheTool()
    {
        // The two query builders used to singularize only a lowercase s, so a table written
        // in capitals kept its plural while the junction phase shortened it.
        const string sql = "SELECT a.Id FROM ADDRESS a";

        var dapper = new DapperSqlQueryBuilder();
        new DapperSqlQueryParser(dapper).Parse(ConversionContentType.SqlQuery, sql);
        dapper.Build();

        var nhibernate = new NHibernateHqlQueryBuilder();
        new DapperSqlQueryParser(nhibernate).Parse(ConversionContentType.SqlQuery, sql);
        var hql = nhibernate.Build().Single(s => s.ContentType == ConversionContentType.HqlQuery).Content;

        Assert.Contains(dapper.Records, r => r.Kind == ConversionRecordKind.Convention && r.Entity == "ADDRES");
        Assert.Contains("from ADDRES", hql, StringComparison.Ordinal);
    }
}
