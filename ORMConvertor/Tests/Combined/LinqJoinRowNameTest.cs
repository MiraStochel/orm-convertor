using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A row enters the tuple a LINQ join yields under the alias the rest of the chain reads it
/// by, also where its lambda parameter had to take another name. The parameter is renamed
/// where the alias is taken - by the variable that holds a derived table of the same name
/// (decision 112), or by the lambda of an enclosing query whose alias a subquery reuses
/// (decision 061) -, and a tuple member named after the renamed parameter left every later
/// reference to the alias pointing at a member the tuple did not have: the method did not
/// compile. Each shape here is compiled and translated by the provider of its target - the
/// third level (decision 027) - in EF Core and in the LINQ form of NHibernate (decision 118).
/// </summary>
public class LinqJoinRowNameTest
{
    private static List<EntityMap> Maps()
    {
        var customerId = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "Name", Type = LangType.Scalar(ScalarType.String, isNullable: true) };
        var creditLimit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal) };

        var customers = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [customerId, name, creditLimit] },
            Table = "Customers",
            PropertyMaps =
            [
                new PropertyMap { Property = customerId, ColumnName = "CustomerId" },
                new PropertyMap { Property = name, ColumnName = "Name" },
                new PropertyMap { Property = creditLimit, ColumnName = "CreditLimit" },
            ],
        };

        var orderId = new Property { Name = "OrderId", Type = LangType.Scalar(ScalarType.Int) };
        var orderCustomer = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };

        var orders = new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [orderId, orderCustomer, total] },
            Table = "Orders",
            PropertyMaps =
            [
                new PropertyMap { Property = orderId, ColumnName = "OrderId" },
                new PropertyMap { Property = orderCustomer, ColumnName = "CustomerId" },
                new PropertyMap { Property = total, ColumnName = "Total" },
            ],
        };

        return [customers, orders];
    }

    private const string EFCoreEntities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace JoinRowNameDomain;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }

            public string? Name { get; set; }

            public decimal CreditLimit { get; set; }
        }

        [Table("Orders")]
        public class Order
        {
            [Key]
            public int OrderId { get; set; }

            public int CustomerId { get; set; }

            public decimal Total { get; set; }
        }
        """;

    private const string NHibernateEntities = """
        namespace JoinRowNameDomain;

        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string? Name { get; set; }
            public virtual decimal CreditLimit { get; set; }
        }

        public class Order
        {
            public virtual int OrderId { get; set; }
            public virtual int CustomerId { get; set; }
            public virtual decimal Total { get; set; }
        }
        """;

    private const string NHibernateMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="JoinRowNameDomain">
          <class name="Customer" table="Customers">
            <id name="CustomerId" column="CustomerId" type="Int32"><generator class="assigned" /></id>
            <property name="Name" type="String" />
            <property name="CreditLimit" type="Decimal" />
          </class>
          <class name="Order" table="Orders">
            <id name="OrderId" column="OrderId" type="Int32"><generator class="assigned" /></id>
            <property name="CustomerId" type="Int32" />
            <property name="Total" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

    /// <summary>A derived table as the root of the chain, joined onwards: its variable takes the alias, so the root's lambda cannot.</summary>
    private const string DerivedTableAsTheRoot = """
        SELECT c.Name AS Name, x.Orders AS Orders
        FROM (SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Orders AS o GROUP BY o.CustomerId) AS x
        INNER JOIN Customers c ON c.CustomerId = x.CustomerId
        WHERE x.Orders > 1
        """;

    /// <summary>A subquery that reuses the outer alias for its own root and joins inside: the enclosing lambda holds the name.</summary>
    private const string SubqueryReusingTheOuterAlias = """
        SELECT o.OrderId AS OrderId
        FROM Orders AS o
        WHERE o.CustomerId IN (
            SELECT o.CustomerId FROM Orders AS o
            INNER JOIN Customers c ON c.CustomerId = o.CustomerId
            WHERE c.CreditLimit > 100)
        """;

    /// <summary>The same with a left join, which NHibernate writes as a GroupJoin flattened by SelectMany.</summary>
    private const string SubqueryReusingTheOuterAliasWithALeftJoin = """
        SELECT o.OrderId AS OrderId
        FROM Orders AS o
        WHERE o.CustomerId IN (
            SELECT o.CustomerId FROM Orders AS o
            LEFT JOIN Customers c ON c.CustomerId = o.CustomerId
            WHERE c.Name IS NULL)
        """;

    private static string Method(AbstractQueryBuilder builder, string sql, ConversionContentType contentType)
    {
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, Maps());
        var built = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback or ConversionRecordKind.Loss);
        return built.Single(s => s.ContentType == contentType).Content;
    }

    private static string EFCoreSql(string sql, string name)
    {
        var method = Method(new EFCoreLinqQueryBuilder(), sql, ConversionContentType.CSharpQuery);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "LinqJoinRowName_EFCore_" + name,
            method,
            [EFCoreEntities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore;\nusing JoinRowNameDomain;");

        return EFCoreQueryAcceptance.Translate(compiled);
    }

    private static string NHibernateSql(string sql, string name)
    {
        var method = Method(new NHibernateLinqQueryBuilder(), sql, ConversionContentType.CSharpLinqQuery);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "LinqJoinRowName_NHibernate_" + name,
            method,
            [NHibernateEntities],
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            "using NHibernate;\nusing NHibernate.Linq;\nusing JoinRowNameDomain;");

        return NHibernateLinqAcceptance.Sql(compiled, [NHibernateMapping]);
    }

    /// <summary>
    /// The root's lambda is renamed away from the variable of the derived table, and the tuple
    /// still holds the row under the alias the filter and the projection read it by.
    /// </summary>
    [Fact]
    public void ADerivedTableAtTheRootKeepsItsAliasThroughAJoin()
    {
        var method = Method(new EFCoreLinqQueryBuilder(), DerivedTableAsTheRoot, ConversionContentType.CSharpQuery);

        Assert.Contains("(x1, c) => new { x = x1, c }", method, StringComparison.Ordinal);
        Assert.Contains("t.x.Orders", method, StringComparison.Ordinal);

        var sql = EFCoreSql(DerivedTableAsTheRoot, nameof(ADerivedTableAtTheRootKeepsItsAliasThroughAJoin));
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The inner root's lambda is renamed away from the enclosing one, and the inner tuple keeps the alias.</summary>
    [Fact]
    public void ASubqueryReusingTheOuterAliasKeepsItThroughAJoin()
    {
        var method = Method(new EFCoreLinqQueryBuilder(), SubqueryReusingTheOuterAlias, ConversionContentType.CSharpQuery);

        Assert.Contains("(o1, c) => new { o = o1, c }", method, StringComparison.Ordinal);

        var sql = EFCoreSql(SubqueryReusingTheOuterAlias, nameof(ASubqueryReusingTheOuterAliasKeepsItThroughAJoin));
        Assert.Contains("IN (", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The same two shapes in the LINQ form of NHibernate, the left join through its GroupJoin.</summary>
    [Fact]
    public void TheNHibernateLinqFormKeepsTheAliasInBothJoinShapes()
    {
        var inner = Method(new NHibernateLinqQueryBuilder(), SubqueryReusingTheOuterAlias, ConversionContentType.CSharpLinqQuery);
        Assert.Contains("(o1, c) => new { o = o1, c }", inner, StringComparison.Ordinal);
        Assert.Contains(" in (", NHibernateSql(SubqueryReusingTheOuterAlias, "Inner"), StringComparison.OrdinalIgnoreCase);

        var left = Method(new NHibernateLinqQueryBuilder(), SubqueryReusingTheOuterAliasWithALeftJoin, ConversionContentType.CSharpLinqQuery);
        Assert.Contains("o = pair.o1", left, StringComparison.Ordinal);
        Assert.Contains("left outer join", NHibernateSql(SubqueryReusingTheOuterAliasWithALeftJoin, "Left"), StringComparison.OrdinalIgnoreCase);
    }
}
