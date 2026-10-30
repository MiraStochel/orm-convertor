using System.Text.RegularExpressions;
using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;
using OrmConvertor;
using ORMConvertorAPI.Data;
using Tests.Verification;
using TransactSql;

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

        // The source selects the order out of the join, and HQL without the select clause
        // would return the pair of rows the join composes.
        Assert.StartsWith("select o", hql.TrimStart());
        Assert.Contains("from Order o", hql);
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

    /* ---- T-SQL ------------------------------------------------------------------------- */

    /// <summary>
    /// Which words T-SQL reserves is ScriptDom's own list, read through its lexer
    /// (<see cref="SqlNames"/>), and the lexer is a faithful proxy of the parser of the pinned
    /// release: a name it tokenizes as a keyword the parser refuses bare as the table of a
    /// from clause, and reads in brackets with the bare name as the identifier's value; a
    /// name it tokenizes as an identifier it reads bare as a table, a column, a qualified
    /// column, a table alias and a result column alike, so the tool leaves it as the source
    /// spelled it. The one word the lexer tokenizes as an identifier and the parser still
    /// refuses in one position is <c>Window</c> as a table alias, where it opens the WINDOW
    /// clause of SQL Server 2022; it is not bracketed.
    /// </summary>
    [Theory]
    [InlineData("User", true)]
    [InlineData("Order", true)]
    [InlineData("Rule", true)]
    [InlineData("Set", true)]
    [InlineData("Plan", true)]
    [InlineData("Key", true)]
    [InlineData("Index", true)]
    [InlineData("Table", true)]
    [InlineData("Left", true)]
    [InlineData("Default", true)]
    [InlineData("Transaction", true)]
    [InlineData("Name", false)]
    [InlineData("Count", false)]
    [InlineData("Value", false)]
    [InlineData("Date", false)]
    [InlineData("Type", false)]
    [InlineData("Role", false)]
    [InlineData("Status", false)]
    [InlineData("Version", false)]
    [InlineData("Offset", false)]
    [InlineData("Format", false)]
    public void ATSqlNameIsBracketedWhenTheLexerOfScriptDomReadsItAsAKeyword(string name, bool reserved)
    {
        Assert.Equal(reserved, SqlNames.IsReserved(name));
        Assert.Equal(reserved ? $"[{name}]" : name, SqlNames.Written(name));

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        parser.Parse(new StringReader($"SELECT 1 FROM {name}"), out var bare);
        Assert.Equal(reserved, bare.Count > 0);

        if (!reserved)
        {
            foreach (var statement in new[] { $"SELECT {name} FROM T", $"SELECT T.{name} FROM T", $"SELECT 1 FROM T {name}", $"SELECT 1 AS {name} FROM T" })
            {
                parser.Parse(new StringReader(statement), out var errors);
                Assert.Empty(errors);
            }
        }

        var fragment = parser.Parse(new StringReader($"SELECT 1 FROM [{name}]"), out var bracketed);
        Assert.Empty(bracketed);
        var select = (SelectStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        var table = (NamedTableReference)((QuerySpecification)select.QueryExpression).FromClause.TableReferences[0];
        Assert.Equal(name, table.SchemaObject.BaseIdentifier.Value);
    }

    /// <summary>
    /// The defect as the whole-input run of 2026-10-30 found it: the User table of the
    /// Cofoundry example went into the SQL of Dapper and of MyBatis bare, and USER is a
    /// reserved word the server refuses and ScriptDom does not read back. Bracketed, every
    /// query over it is one the tool's own reader reads.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.SqlQuery)]
    [InlineData(ORMEnum.MyBatis, ConversionContentType.XML)]
    public void TheUserTableOfCofoundryIsBracketedInSql(ORMEnum target, ConversionContentType type)
    {
        var example = Examples.GetExamples.Single(e => e.Key == "cofoundry-pages");
        var result = ConversionHandler.Convert(example.SourceOrm, target, example.Units);

        var artifacts = result.Sources.Where(s => s.ContentType == type).ToList();
        var overUser = artifacts.Where(s => s.Content.Contains("[User]", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(overUser);
        Assert.DoesNotContain(artifacts, s => Regex.IsMatch(s.Content, @"\b(FROM|JOIN) User\b"));

        if (type == ConversionContentType.SqlQuery)
        {
            foreach (var sql in overUser)
            {
                new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(sql.Content), out var errors);
                Assert.Empty(errors);
            }
        }
    }

    /// <summary>A column, a table alias and a common table expression spelled like a reserved word are bracketed by the same rule, and the statement reads back.</summary>
    [Fact]
    public void AColumnAnAliasAndADefinitionSpelledLikeAReservedWordAreBracketed()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = [] };
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery,
            "WITH [Set] AS (SELECT o.[Key] AS Total FROM Orders o) SELECT [rule].Total FROM [Set] [rule] WHERE [rule].Total > 5");
        var sql = Assert.Single(builder.Build(), s => s.ContentType == ConversionContentType.SqlQuery).Content;
        var flat = Regex.Replace(sql, @"\s+", " ").Trim();

        Assert.Contains("WITH [Set] AS (", flat, StringComparison.Ordinal);
        Assert.Contains("o.[Key] AS Total FROM Orders AS o", flat, StringComparison.Ordinal);
        Assert.Contains("FROM [Set] AS [rule] WHERE [rule].Total > 5", flat, StringComparison.Ordinal);
        new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(sql), out var errors);
        Assert.Empty(errors);
    }

    /// <summary>A result column named like a reserved word by a source that has no reserved words of its own - a LINQ projection - is bracketed as well.</summary>
    [Fact]
    public void AResultColumnSpelledLikeAReservedWordIsBracketed()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper,
        [
            new() { Content = EFCoreUnit("ctx.Set<Order>().Select(o => new { Order = o.Id })"), ContentType = ConversionContentType.CSharp, Name = "Shop.cs" },
        ]);

        Assert.Contains("SELECT o.Id AS [Order]", Text(result, ConversionContentType.SqlQuery), StringComparison.Ordinal);
    }
}
