using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A name spelled like a keyword of the target's query language. HQL in NHibernate 5.7.0
/// refuses an entity named like one of its keywords as the target of an entity join - and
/// seven of them even at the head of a from clause - but reads the name qualified with its
/// namespace in every position; it refuses an alias spelled like a keyword nearly wherever it
/// stands, and reads the same alias with a trailing underscore everywhere. JPQL has no
/// qualified entity name: what a parser refuses there goes to native SQL (EclipseLink 5.0.0
/// refuses some thirty words, Hibernate 7.4.5 the three literals), and an alias that is a
/// reserved identifier gets the trailing underscore in both implementations. Measured on the
/// pinned releases; the NHibernate cases here are compiled by NHibernate itself.
/// </summary>
public class KeywordNameTest
{
    private static string Customer(string package = "package Shop;") => $$"""
        {{package}}

        import jakarta.persistence.*;
        import java.util.List;

        @Entity
        @Table(name = "Customers")
        public class Customer {
            @Id
            @Column(name = "CustomerID")
            private Integer id;

            @OneToMany(mappedBy = "customer")
            private List<Order> orders;
        }
        """;

    private static string Order(string package = "package Shop;") => $$"""
        {{package}}

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Orders")
        public class Order {
            @Id
            private Integer id;

            private Integer total;

            @ManyToOne
            @JoinColumn(name = "CustomerID")
            private Customer customer;
        }
        """;

    private static ConversionSource Java(string content) => new() { Content = content, ContentType = ConversionContentType.Java };

    private static ConversionSource Jpql(string content) => new() { Content = content, ContentType = ConversionContentType.JpqlQuery };

    private static string Text(ConversionResult result, ConversionContentType type)
    {
        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
        return Assert.Single(result.Sources, s => s.ContentType == type).Content;
    }

    /* ---- HQL in NHibernate ------------------------------------------------------------ */

    /// <summary>The defect as it was found: an entity named Order as the target of an entity join.</summary>
    [Fact]
    public void AnEntityJoinTargetSpelledLikeAKeywordIsQualifiedWithItsNamespace()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.NHibernate,
            [Java(Customer()), Java(Order()), Jpql("select c from Customer c join c.orders o where o.total > 5")]);
        var hql = Text(result, ConversionContentType.HqlQuery);

        Assert.Contains("from Customer c", hql);
        Assert.Contains("inner join Shop.Order o with o.customer = c", hql);

        Compile(result, hql, "KeywordName_Join");
    }

    /// <summary>At the head of a from clause Order is read bare, so the artifact stays as it was.</summary>
    [Fact]
    public void ARootSpelledLikeAKeywordTheParserTakesStaysBare()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.NHibernate,
            [Java(Customer()), Java(Order()), Jpql("select o from Order o join o.customer c")]);
        var hql = Text(result, ConversionContentType.HqlQuery);

        Assert.StartsWith("from Order o", hql.TrimStart());
        Compile(result, hql, "KeywordName_Root");
    }

    /// <summary>An alias the source spelled like a keyword is written with a trailing underscore, in its declaration and in every path over it.</summary>
    [Theory]
    [InlineData("order")]
    [InlineData("count")]
    public void AnAliasSpelledLikeAKeywordGetsATrailingUnderscore(string alias)
    {
        // A lambda parameter is the alias of a LINQ source, and C# takes both for a name.
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate,
        [
            new() { Content = EFCoreUnit($"ctx.Set<Order>().Where({alias} => {alias}.Total > 5).Select({alias} => {alias}.Total)"), ContentType = ConversionContentType.CSharp, Name = "Shop.cs" },
        ]);
        var hql = Text(result, ConversionContentType.HqlQuery);

        Assert.Contains($"from Order {alias}_", hql);
        Assert.Contains($"{alias}_.Total > 5", hql);
        Assert.DoesNotContain($" {alias}.", hql);

        Compile(result, hql, "KeywordName_Alias_" + alias);
    }

    /// <summary>Without a namespace there is no qualified name, and the query goes out in native SQL with the reason.</summary>
    [Fact]
    public void AnEntityJoinTargetWithoutANamespaceGoesToNativeSql()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.NHibernate,
            [Java(Customer(string.Empty)), Java(Order(string.Empty)), Jpql("select c from Customer c join c.orders o")]);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Fallback
            && r.Reason.Contains("'Order', which is spelled like a keyword", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.HqlQuery);
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }

    private static string EFCoreUnit(string chain) => $$"""
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace Shop;

        [Table("Orders")]
        public class Order
        {
            [Key]
            public int Id { get; set; }

            public int Total { get; set; }
        }

        public class Orders(DbContext ctx)
        {
            public object Big() => {{chain}}.ToList();
        }
        """;

    private static void Compile(ConversionResult result, string hql, string assembly)
    {
        var compiled = GeneratedEntityCompiler.CompileOrFail(
            assembly,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        NHibernateQueryAcceptance.CompileQuery(
            compiled,
            result.Sources.Where(s => s.ContentType == ConversionContentType.XML).Select(s => s.Content),
            hql);
    }

    /* ---- JPQL ------------------------------------------------------------------------- */

    private static string Entity(string name, string table) => $$"""
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "{{table}}")
        public class {{name}} {
            @Id
            private Integer id;

            @ManyToOne
            @JoinColumn(name = "CustomerID")
            private Customer customer;
        }
        """;

    private static string Owner(string collection, string element) => $$"""
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
            private List<{{element}}> {{collection}};
        }
        """;

    /// <summary>
    /// An entity name the implementation's parser refuses goes to native SQL - Member only as
    /// the target of an entity join in EclipseLink, Table wherever it stands -, and one it
    /// reads stays JPQL: Hibernate reads both.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EclipseLink, "Member", "select c from Customer c join c.members m", true)]
    [InlineData(ORMEnum.EclipseLink, "Member", "select m from Member m", false)]
    [InlineData(ORMEnum.EclipseLink, "Table", "select t from Table t", true)]
    [InlineData(ORMEnum.EclipseLink, "Order", "select c from Customer c join c.orders o", false)]
    [InlineData(ORMEnum.Hibernate, "Member", "select c from Customer c join c.members m", false)]
    [InlineData(ORMEnum.Hibernate, "Table", "select t from Table t", false)]
    public void AJpqlEntityNameTheParserRefusesGoesToNativeSql(ORMEnum target, string entity, string query, bool refused)
    {
        var collection = entity.ToLowerInvariant() + "s";
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, target,
            [Java(Owner(collection, entity)), Java(Entity(entity, entity + "s")), Jpql(query)]);

        if (refused)
        {
            Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Fallback
                && r.Reason.Contains($"'{entity}', which is spelled like a word of its grammar", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.JpqlQuery);
        }
        else
        {
            Assert.Contains($" {entity} ", Text(result, ConversionContentType.JpqlQuery));
        }
    }

    /// <summary>An alias that is a reserved identifier of JPQL gets the trailing underscore in both implementations.</summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AJpqlAliasThatIsAReservedIdentifierGetsATrailingUnderscore(ORMEnum target)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, target,
        [
            new() { Content = EFCoreUnit("ctx.Set<Order>().Where(value => value.Total > 5)"), ContentType = ConversionContentType.CSharp, Name = "Shop.cs" },
        ]);
        var jpql = Text(result, ConversionContentType.JpqlQuery);

        Assert.Contains("from Order value_", jpql);
        Assert.Contains("value_.Total > 5", jpql);
    }

    /// <summary>
    /// A result variable that is a reserved identifier - <c>as Count</c> from a Dapper source,
    /// a common spelling - EclipseLink 5.0.0 refuses too, in the projection and in an ordering
    /// by it (verified, JavaTests eclipselink/KeywordNameClaimsTest): both get the trailing
    /// underscore, in both implementations alike.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate, "select count(o) as count from Order o", "count(o) as count_")]
    [InlineData(ORMEnum.EclipseLink, "select count(o) as count from Order o", "count(o) as count_")]
    [InlineData(ORMEnum.Hibernate, "select o.id as value from Order o order by value desc", "o.id as value_ from Order o order by value_ desc")]
    [InlineData(ORMEnum.EclipseLink, "select o.id as value from Order o order by value desc", "o.id as value_ from Order o order by value_ desc")]
    public void AJpqlResultVariableThatIsAReservedIdentifierGetsATrailingUnderscore(ORMEnum target, string query, string expected)
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, target,
            [Java(Owner("orders", "Order")), Java(Entity("Order", "Orders")), Jpql(query)]);

        Assert.Contains(expected, string.Join(" ", Text(result, ConversionContentType.JpqlQuery).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())));
    }
}
