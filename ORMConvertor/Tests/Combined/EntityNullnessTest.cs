using System.Collections;
using System.Reflection;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Database;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// <c>p.customer is null</c> in JPQL tests a whole entity, not a column. Read as a column, the
/// association came out under its own name - <c>p.customer IS NULL</c> in SQL, a column no
/// table has - in every target. A single-valued association that owns its foreign key is now
/// read as the nullness of the key's columns, derived from the relation like a join along it
/// (decision 101); every other entity tested for null is refused by name. Measured at the
/// fourth level over the self-reference of the shared domain, whose two roots have no parent.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class EntityNullnessTest(TestSchemaFixture fixture)
{
    private const string Customer = """
        package Shop;

        import jakarta.persistence.*;
        import java.util.List;

        @Entity
        @Table(name = "Customers")
        public class Customer {
            @Id
            @Column(name = "CustomerID")
            private Integer id;

            @OneToMany(mappedBy = "customer")
            private List<Purchase> purchases;
        }
        """;

    private const string Purchase = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Purchases")
        public class Purchase {
            @Id
            private Integer id;

            @ManyToOne
            private Customer customer;
        }
        """;

    private const string Composite = """
        package Shop;

        import jakarta.persistence.*;
        import java.io.Serializable;
        import java.util.Objects;

        @Entity
        @Table(name = "Headers")
        @IdClass(Header.HeaderKey.class)
        public class Header {
            @Id
            @Column(name = "CompanyId")
            private Integer CompanyId;

            @Id
            @Column(name = "OrderId")
            private Integer OrderId;

            public static class HeaderKey implements Serializable {
                private Integer CompanyId;
                private Integer OrderId;

                public HeaderKey() {
                }

                @Override
                public boolean equals(Object obj) {
                    return obj instanceof HeaderKey other
                        && Objects.equals(CompanyId, other.CompanyId)
                        && Objects.equals(OrderId, other.OrderId);
                }

                @Override
                public int hashCode() {
                    return Objects.hash(CompanyId, OrderId);
                }
            }
        }

        @Entity
        @Table(name = "Lines")
        public class Line {
            @Id
            @Column(name = "LineId")
            private Integer LineId;

            @ManyToOne
            @JoinColumns({
                @JoinColumn(name = "HeadCompanyId", referencedColumnName = "CompanyId"),
                @JoinColumn(name = "HeadNo", referencedColumnName = "OrderId")
            })
            private Header Head;
        }
        """;

    /// <summary>The same reference without its join columns: over a composite key no default column is derived, so the relation states none.</summary>
    private static readonly string CompositeWithoutJoinColumns = Composite
        .Replace("@JoinColumns({", "/*", StringComparison.Ordinal)
        .Replace("})", "*/", StringComparison.Ordinal);

    private const string OneToOne = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "People")
        public class Person {
            @Id
            @Column(name = "PersonId")
            private Integer id;

            @OneToOne(mappedBy = "person")
            private Passport passport;
        }

        @Entity
        @Table(name = "Passports")
        public class Passport {
            @Id
            @Column(name = "PassportId")
            private Integer id;

            @OneToOne
            @JoinColumn(name = "PersonId")
            private Person person;
        }
        """;

    private const string Department = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "ShopDepartments", schema = "{{schema}}")
        public class ShopDepartment {
            @Id
            @Column(name = "DepartmentId")
            private Integer DepartmentId;

            @Column(name = "Name", nullable = false)
            private String Name;

            @ManyToOne
            @JoinColumn(name = "ParentDepartmentId")
            private ShopDepartment parent;
        }
        """;

    private static ConversionSource Java(string content) => new() { Content = content, ContentType = ConversionContentType.Java };

    private static ConversionSource Jpql(string content) => new() { Content = content, ContentType = ConversionContentType.JpqlQuery };

    private static ConversionResult Convert(ORMEnum target, string query, params string[] entities)
        => ConversionHandler.Convert(ORMEnum.Hibernate, target, [.. entities.Select(Java), Jpql(query)]);

    /// <summary>The query in the target's own language: the bare SQL of Dapper, HQL, JPQL, the LINQ method of EF Core.</summary>
    private static string Written(ConversionResult result, ORMEnum target)
    {
        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);

        var language = target switch
        {
            ORMEnum.Dapper => ConversionContentType.SqlQuery,
            ORMEnum.NHibernate => ConversionContentType.HqlQuery,
            ORMEnum.EFCore => ConversionContentType.CSharpQuery,
            _ => ConversionContentType.JpqlQuery,
        };

        return Assert.Single(result.Sources, s => s.ContentType == language).Content;
    }

    /* ---- a reference that owns its foreign key -------------------------------------- */

    [Theory]
    [InlineData(ORMEnum.Dapper, "select p from Purchase p where p.customer is null", "WHERE p.customer_CustomerID IS NULL")]
    [InlineData(ORMEnum.Dapper, "select p from Purchase p where p.customer is not null", "WHERE p.customer_CustomerID IS NOT NULL")]
    [InlineData(ORMEnum.Dapper, "select p from Purchase p where not (p.customer is null)", "WHERE NOT (p.customer_CustomerID IS NULL)")]
    [InlineData(ORMEnum.NHibernate, "select p from Purchase p where p.customer is null", "where p.customer.id is null")]
    [InlineData(ORMEnum.Hibernate, "select p from Purchase p where p.customer is not null", "where p.customer.id is not null")]
    [InlineData(ORMEnum.EFCore, "select p from Purchase p where p.customer is null", "p.customerid == null")]
    public void AReferenceTestedForNullIsItsForeignKeyTestedForNull(ORMEnum target, string query, string expected)
    {
        var written = Written(Convert(target, query, Customer, Purchase), target);

        Assert.Contains(expected, written);
        Assert.DoesNotMatch(@"customer\s+(IS|is)\b", written);
    }

    /// <summary>Over a composite foreign key every column is tested, a conjunction - null means no reference, not a half of one.</summary>
    [Theory]
    [InlineData("select l from Line l where l.Head is null", "WHERE l.HeadCompanyId IS NULL AND l.HeadNo IS NULL")]
    [InlineData("select l from Line l where l.Head is not null", "WHERE l.HeadCompanyId IS NOT NULL AND l.HeadNo IS NOT NULL")]
    public void ACompositeForeignKeyIsTestedColumnByColumn(string query, string expected)
        => Assert.Contains(expected, Written(Convert(ORMEnum.Dapper, query, Composite), ORMEnum.Dapper));

    /* ---- what is not read ----------------------------------------------------------- */

    /// <summary>
    /// Only an owning reference has columns of its own to test. A collection is never null,
    /// an inverse reference is the absence of a row on the other side, the whole row of an
    /// identification variable has no column, and a reference whose columns nobody states -
    /// a composite key without join columns, which neither specification nor implementation
    /// spell alike - leaves nothing: each is refused by name, no artifact is written.
    /// </summary>
    [Theory]
    [InlineData("select c from Customer c where c.purchases is null", "collection")]
    [InlineData("select p from Purchase p left join p.customer c where c is null", "whole row")]
    [InlineData("select p from Person p where p.passport is null", "other side holds")]
    [InlineData("select l from Line l where l.Head is not null", "neither the relation states")]
    public void AnyOtherEntityTestedForNullIsRefused(string query, string reason)
    {
        string[] entities = query.Contains("Person", StringComparison.Ordinal) ? [OneToOne]
            : query.Contains("Line", StringComparison.Ordinal) ? [CompositeWithoutJoinColumns]
            : [Customer, Purchase];

        var result = Convert(ORMEnum.Dapper, query, entities);

        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains(reason, StringComparison.Ordinal));
    }

    /* ---- the fourth level ------------------------------------------------------------ */

    /// <summary>
    /// The departments of the shared domain without a parent, and those with one, as SQL
    /// Server answers the generated SQL and the generated EF Core query - against the source
    /// query's own answer over the foreign key column.
    /// </summary>
    [Theory]
    [InlineData("is null", "IS NULL")]
    [InlineData("is not null", "IS NOT NULL")]
    public void TheRowsAreThoseWhoseForeignKeyIsNull(string jpql, string sql)
    {
        fixture.SkipIfUnavailable();

        var query = $"select d.DepartmentId as DepartmentId from ShopDepartment d where d.parent {jpql}";
        var entity = Department.Replace("{{schema}}", fixture.SchemaName);
        var expected = Direct($"SELECT d.DepartmentId FROM {fixture.SchemaName}.ShopDepartments AS d WHERE d.ParentDepartmentId {sql}");
        Assert.NotEmpty(expected);

        var dapper = Written(Convert(ORMEnum.Dapper, query, entity), ORMEnum.Dapper);
        Assert.Equal(expected, Direct(dapper));

        var efCore = Convert(ORMEnum.EFCore, query, entity);
        var compiled = GeneratedQueryCompiler.CompileOrFail(
            $"EntityNullness_EFCore_{(sql.Contains("NOT", StringComparison.Ordinal) ? "NotNull" : "Null")}",
            Written(efCore, ORMEnum.EFCore),
            efCore.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Shop;");
        Assert.Equal(expected, Run(compiled, "DepartmentId"));
    }

    /// <summary>The first column of every row SQL Server returns for the statement, ordered.</summary>
    private List<string> Direct(string sql)
    {
        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add($"{reader.GetValue(0)}");
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>The projected column of every row the generated EF Core query returns, ordered.</summary>
    private List<string> Run(byte[] compiled, string column)
    {
        var assembly = Assembly.Load(compiled);
        using var connection = fixture.OpenConnection();
        using var context = EFCoreAcceptance.OpenContext(assembly, connection);

        var method = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(candidate => candidate.DeclaringType?.Name == "GeneratedQueries");

        var rows = new List<string>();
        foreach (var row in (IEnumerable)method.Invoke(null, [context])!)
        {
            rows.Add($"{row.GetType().GetProperty(column)!.GetValue(row)}");
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }
}
