using System.Globalization;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The rule of decision 093 over the language it left aside, as decision 128 extends it: a C#
/// unit whose text Roslyn does not read without an error is a Failure of that unit with the
/// line and the column of its first error, and the rest of the conversion goes on. Roslyn
/// recovers - it builds the tree it guesses the author meant - so such a unit used to be
/// translated without a word from what Roslyn had written in, or to end with the general
/// record of decision 045 and no position at all.
///
/// The theories run over every framework the enum yields, for the reason the XML ones do: the
/// rule is that nobody falls silent and nobody says it twice. Each .NET framework reads a C#
/// unit on both passes - the entity pass for its classes, the query pass for its queries - and
/// has to speak once; a framework that reads no C# has to say the other thing instead.
/// </summary>
public class UnreadableCSharpTest
{
    /// <summary>
    /// A class whose second property lacks the semicolon after its setter, the first row of
    /// the table in decision 128: Roslyn writes it in and reads both properties, which is how
    /// this unit used to come out translated with no record at all.
    /// </summary>
    private const string Broken = """
        public class Supplier
        {
            public int SupplierID { get; set; }
            public string SupplierName { get; set }
        }
        """;

    private const string Name = "Supplier.cs";

    private const string Position = "The C# source could not be read at line ";

    /// <summary>The frameworks whose parsers claim a C# unit at all.</summary>
    private static bool ReadsCSharp(ORMEnum framework)
        => framework is ORMEnum.Dapper or ORMEnum.EFCore or ORMEnum.NHibernate;

    /// <summary>The unit beside the healthy input of the framework's cross-framework sample.</summary>
    private static ConversionResult Convert(ORMEnum source, string unit = Broken, ParseLimits? limits = null)
    {
        List<ConversionSource> units =
        [
            .. CrossFrameworkInputs.Units(source),
            new() { Name = Name, ContentType = ConversionContentType.CSharp, Content = unit },
        ];

        return ConversionHandler.Convert(source, ORMEnum.EFCore, units, catalogConnectionString: null, declaredSourceDialect: null, limits);
    }

    private static ConversionResult ConvertAlone(ORMEnum source, string unit)
        => ConversionHandler.Convert(
            source,
            ORMEnum.EFCore,
            [new ConversionSource { Name = Name, ContentType = ConversionContentType.CSharp, Content = unit }]);

    private static List<ConversionRecord> About(ConversionResult result)
        => [.. result.Records.Where(r => r.Unit == Name)];

    private static List<ConversionRecord> Unreadable(ConversionResult result)
        => [.. result.Records.Where(r => r.Reason.StartsWith(Position, StringComparison.Ordinal))];

    private static bool Barren(ConversionRecord record)
        => record.Reason.Contains("neither a mapping fact nor a query", StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Frameworks), MemberType = typeof(CrossFrameworkInputs))]
    public void ABrokenUnitIsRefusedOnce(ORMEnum source)
    {
        var result = Convert(source);

        if (!ReadsCSharp(source))
        {
            // Nobody claimed the unit, which is the older sentence of decision 045 and still
            // the right one: the text was never read, so there is nothing to say about it.
            Assert.Empty(Unreadable(result));
            Assert.Contains(About(result), r => r.Reason.Contains("no parser", StringComparison.Ordinal));
            return;
        }

        // Once, although both passes read the unit: the entity pass says it, and the query
        // pass, which reads the same text after it, keeps quiet.
        var refusal = Assert.Single(Unreadable(result));

        Assert.Equal(ConversionRecordKind.Failure, refusal.Kind);
        Assert.Equal(ConversionContentType.CSharp, refusal.Artifact);

        // S7 asks for the error at the level of the file and the line, so the record names
        // both: the unit as the client named it (decision 066) and the position in it - the
        // closing brace that stands where Roslyn expected the semicolon.
        Assert.Equal(Name, refusal.Unit);
        Assert.StartsWith(Position + "4, column 43: ", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the rule is for (decision 093): the units that were fine are translated anyway,
    /// and nothing comes out of the broken one - not even the property Roslyn mended.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Frameworks), MemberType = typeof(CrossFrameworkInputs))]
    public void TheHealthyUnitsOfTheSameRunAreTranslatedAnyway(ORMEnum source)
    {
        var result = Convert(source);

        Assert.NotEmpty(result.Sources);
        Assert.Contains(result.Sources, s => s.Content.Contains("Customer", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.Content.Contains("Supplier", StringComparison.Ordinal));
    }

    /// <summary>
    /// The texts Roslyn used to recover into a translation without a word - the rows of the
    /// table in decision 128 that came out with an artifact. A missing semicolon and a missing
    /// closing brace were written in; a misspelt modifier and a stray line were dropped, the
    /// first taking the property's access modifier with it. Each is now refused at the line
    /// and the column of its error, and nothing is translated from what Roslyn wrote in.
    /// </summary>
    [Theory]
    [InlineData("""
        public class Supplier
        {
            public int SupplierID { get; set; }
            public string SupplierName { get; set }
        }
        """, "4, column 43")]
    [InlineData("""
        public class Supplier
        {
            public int SupplierID { get; set; }
            public string SupplierName { get; set; }
        """, "4, column 45")]
    [InlineData("""
        public class Supplier
        {
            public int SupplierID { get; set; }
            publc string SupplierName { get; set; }
        }
        """, "4, column 11")]
    [InlineData("""
        public class Supplier
        {
            public int SupplierID { get; set; }
            this is not a member
            public string SupplierName { get; set; }
        }
        """, "4, column 5")]
    public void WhatRoslynRecoversIsNoLongerTranslated(string unit, string position)
    {
        var result = ConvertAlone(ORMEnum.Dapper, unit);

        Assert.Empty(result.Sources);
        Assert.StartsWith(Position + position + ": ", Assert.Single(Unreadable(result)).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fragment is read inside the wrapper its reader gives it (decision 111) - six lines
    /// ahead of it for LINQ, two for Dapper - and the record names the line the client wrote,
    /// not the line of the wrapped text. Both wrappers, since each reads its own.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, """
        public List<Supplier> Query()
        {
            return ctx.Suppliers
                .Where(s => s.SupplierID > 10)
                .ToList()
        }
        """, "5, column 18")]
    [InlineData(ORMEnum.Dapper, """
        public IEnumerable<Supplier> Query(IDbConnection connection)
        {
            return connection.Query<Supplier>("SELECT SupplierID FROM Purchasing.Suppliers")
        }
        """, "3, column 85")]
    public void TheLineOfAFragmentIsTheLineTheClientWrote(ORMEnum source, string unit, string position)
    {
        var result = ConvertAlone(source, unit);

        Assert.Empty(result.Sources);
        Assert.StartsWith(Position + position + ": ", Assert.Single(Unreadable(result)).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A closing brace the fragment lacks is missed where the wrapper ends, on a line the
    /// client never sent; the record puts it after the last thing the client wrote instead,
    /// trailing blank lines left out.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, "")]
    [InlineData(ORMEnum.EFCore, "\n\n")]
    [InlineData(ORMEnum.Dapper, "")]
    [InlineData(ORMEnum.NHibernate, "\n")]
    public void AnErrorAtTheEndOfAFragmentNamesALineTheClientSent(ORMEnum source, string tail)
    {
        var unit = "public List<Supplier> Query()\n{\n    return ctx.Suppliers.ToList();" + tail;

        var refusal = Assert.Single(Unreadable(ConvertAlone(source, unit)));

        Assert.StartsWith(Position + "3, column 35: ", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cap of decision 092 runs before Roslyn and keeps its precedence: a text beyond it is
    /// refused for its depth, and its syntax is never asked about - it is never parsed.
    /// </summary>
    [Fact]
    public void TheCapSpeaksBeforeTheSyntax()
    {
        var result = Convert(
            ORMEnum.Dapper,
            "public class Supplier { public int SupplierID { get; set; } = ((((1)))) }",
            new ParseLimits { MaxNestingDepth = 4 });

        Assert.Empty(Unreadable(result));
        Assert.Contains(About(result), r => r.Reason.Contains("nests deeper", StringComparison.Ordinal));
    }

    /// <summary>
    /// The general record of decision 045 stays beside the specific one rather than being
    /// suppressed by it, as it does for a broken Java class, SQL text or XML document: one says
    /// why the reading stopped, the other that nothing came of the unit.
    /// </summary>
    [Fact]
    public void TheRecordAboutABarrenUnitStaysBesideTheOneAboutTheText()
    {
        var about = About(Convert(ORMEnum.Dapper));

        Assert.Equal(2, about.Count);
        Assert.All(about, r => Assert.Equal(ConversionRecordKind.Failure, r.Kind));
        Assert.Contains(about, r => r.Reason.StartsWith(Position, StringComparison.Ordinal));
        Assert.Contains(about, Barren);
    }

    /// <summary>
    /// A text Roslyn reads without an error and that simply holds nothing the tool reads is no
    /// failure of the reading, and the decision does not make it one: the unit yields nothing
    /// and the run says that much, as it did before. A bare method is no error either, though
    /// it would be one at the top of a file - it is read inside its wrapper.
    /// </summary>
    [Theory]
    [InlineData("public enum Status { Active, Inactive }")]
    [InlineData("public int Answer() => 42;")]
    public void AReadableUnitThatHoldsNothingIsNotRefusedForItsShape(string unit)
    {
        var result = Convert(ORMEnum.EFCore, unit);

        Assert.Empty(Unreadable(result));
        Assert.Contains(About(result), Barren);
    }

    /// <summary>
    /// A text is read in the other shape where only that one reads (decision 128). A method
    /// beside a class is an error at the top of a file and none inside the class a fragment is
    /// wrapped in, and it is what the EF Core escape path writes: a method and the class of its
    /// rows. The class stays a class of the unit and not a member of the wrapper: it is the same
    /// entity as its other half in a namespace, which a class nested in another would not be
    /// (decision 094), and its facts stand in the text the client sent. The method's query is
    /// read beside it.
    /// </summary>
    [Fact]
    public void AMethodBesideAClassIsReadInsideTheWrapper()
    {
        const string unit = """
            public static IQueryable<Supplier> Rich(DbContext ctx)
            {
                return ctx.Set<Supplier>().Where(s => s.SupplierID > 10);
            }

            [Table("Suppliers")]
            public partial class Supplier
            {
                public int SupplierID { get; set; }
            }
            """;

        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [
                new ConversionSource { Name = Name, ContentType = ConversionContentType.CSharp, Content = unit },
                new ConversionSource
                {
                    ContentType = ConversionContentType.CSharp,
                    Content = "namespace Purchasing;\n\npublic partial class Supplier\n{\n    public string SupplierName { get; set; }\n}\n",
                },
            ]);

        Assert.Empty(Unreadable(result));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);

        var entity = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
        Assert.Contains("SupplierID", entity.Content, StringComparison.Ordinal);
        Assert.Contains("SupplierName", entity.Content, StringComparison.Ordinal);

        var table = Assert.Single(result.Facts, f => f.Category == MappingFactCategory.TableName && f.Origin == FactOrigin.Source);
        Assert.Equal(Name, table.Source!.Unit);
        Assert.Contains("Suppliers", unit.Substring(table.Source.Start, table.Source.Length), StringComparison.Ordinal);
    }

    /// <summary>
    /// The other way round: statements alone are a file of top-level statements as they stand
    /// and an error inside a class, so a program that hands its query over at the top level is
    /// read as it stands and is not refused (decision 128).
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, """
        using Dapper;

        using var connection = new SqlConnection("Server=.");
        var customers = connection.Query<Customer>("SELECT c.CustomerID FROM Sales.Customers AS c");
        """)]
    [InlineData(ORMEnum.EFCore, """
        using var ctx = new ShopContext();
        return ctx.Customers.Where(c => c.CustomerID > 10).ToList();
        """)]
    public void StatementsAloneAreReadAsTheyStand(ORMEnum source, string unit)
    {
        var result = Convert(source, unit);

        Assert.Empty(Unreadable(result));
        Assert.DoesNotContain(About(result), Barren);
    }

    /// <summary>
    /// The using directives a fragment opens with belong to the file, so they stand in front of
    /// the wrapper rather than inside its class, where no using may stand (decision 128). A
    /// method pasted with its usings reads; inside the class it was refused at line 1 with a
    /// closing brace it never lacked.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, """
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        public List<Customer> Rich(ShopContext ctx)
        {
            return ctx.Customers.Where(c => c.CustomerID > 10).ToList();
        }
        """)]
    [InlineData(ORMEnum.Dapper, """
        using Dapper;

        public IEnumerable<Customer> Rich(IDbConnection connection)
            => connection.Query<Customer>("SELECT c.CustomerID FROM Sales.Customers AS c");
        """)]
    public void UsingsAboveAFragmentStandInFrontOfTheWrapper(ORMEnum source, string unit)
    {
        var result = Convert(source, unit);

        Assert.Empty(Unreadable(result));
        Assert.DoesNotContain(About(result), Barren);
    }

    /// <summary>
    /// With the usings in front of the wrapper, the wrapper's head stands in the middle of the
    /// text, and a position after it still names the line the client wrote.
    /// </summary>
    [Fact]
    public void AnErrorAfterTheUsingsOfAFragmentNamesTheLineTheClientWrote()
    {
        var unit = "using System.Linq;\n\npublic List<Supplier> Query()\n{\n    return ctx.Suppliers.ToList()\n}";

        var refusal = Assert.Single(Unreadable(ConvertAlone(ORMEnum.EFCore, unit)));

        Assert.StartsWith(Position + "5, column 34: ", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The text is parsed at the preview language version (decision 128): the tool reads syntax
    /// and compiles nothing, so a declaration Roslyn 5.6.0 accepts only in preview - a union -
    /// does not refuse the entity beside it.
    /// </summary>
    [Fact]
    public void SyntaxNewerThanTheDefaultVersionIsNoReasonToRefuse()
    {
        const string unit = """
            public union Status(int, string);

            public class Supplier
            {
                public int SupplierID { get; set; }
            }
            """;

        var result = ConvertAlone(ORMEnum.EFCore, unit);

        Assert.Empty(Unreadable(result));
        Assert.Contains(result.Sources, s => s.Entity == "Supplier");
    }

    /// <summary>
    /// Roslyn carries its messages in other languages too, Czech among them, and would answer
    /// in the culture of the thread. The record is the same whatever the culture of the server
    /// (S2), so the message is taken in the invariant one.
    /// </summary>
    [Fact]
    public void TheMessageDoesNotDependOnTheCultureOfTheServer()
    {
        var culture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("cs-CZ");

            Assert.EndsWith(
                ": { or ; or => expected.",
                Assert.Single(Unreadable(ConvertAlone(ORMEnum.Dapper, Broken))).Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
