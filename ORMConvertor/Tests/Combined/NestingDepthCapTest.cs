using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The depth cap of decision 092, over every language the tool reads recursively. Measuring
/// showed all five die the same way - the stack overflows and the process ends, which .NET
/// cannot catch - and that the two foreign grammars are no exception: T-SQL gives out first
/// of all, at 2048 levels of parentheses.
///
/// The suite never goes near those numbers. A test that overflowed the stack would not fail,
/// it would end the whole run, so only the boundary is checked: the cap is read, one level
/// past it is refused with a position.
/// </summary>
public class NestingDepthCapTest
{
    private const int Cap = ParseLimits.DefaultMaxNestingDepth;

    private static string Wrapped(string before, string inner, string after, int depth)
        => before + new string('(', depth) + inner + new string(')', depth) + after;

    /// <summary>
    /// One input shape per row of the table in decision 092, each built to nest exactly as
    /// deep as it is asked to - the fixed levels of its own wrapper already subtracted.
    /// </summary>
    public static TheoryData<string, ORMEnum, ORMEnum, ConversionContentType, Func<int, string>> Shapes => new()
    {
        {
            "HQL, parentheses", ORMEnum.NHibernate, ORMEnum.EFCore, ConversionContentType.HqlQuery,
            depth => Wrapped("from Customer c where ", "c.CustomerName = 'x'", string.Empty, depth)
        },
        {
            "HQL, subqueries", ORMEnum.NHibernate, ORMEnum.EFCore, ConversionContentType.HqlQuery,
            depth => string.Concat(Enumerable.Range(0, depth).Select(i => "from Customer c" + i + " where exists ("))
                + "from Customer c where c.CustomerName = 'x'"
                + new string(')', depth)
        },
        {
            "JPQL, parentheses", ORMEnum.Hibernate, ORMEnum.EFCore, ConversionContentType.JpqlQuery,
            depth => Wrapped("select c from Customer c where ", "c.customerName = 'x'", string.Empty, depth)
        },
        {
            "JPQL, subqueries", ORMEnum.Hibernate, ORMEnum.EFCore, ConversionContentType.JpqlQuery,
            depth => string.Concat(Enumerable.Range(0, depth).Select(i => "select c" + i + " from Customer c" + i + " where exists ("))
                + "select c from Customer c where c.customerName = 'x'"
                + new string(')', depth)
        },
        {
            // Counted by the Java reader itself and not by the shared guard: an angle bracket
            // is a comparison in an initializer and a type argument in a declaration, and only
            // the reader knows which of the two it is looking at.
            "Java, generic types", ORMEnum.Hibernate, ORMEnum.EFCore, ConversionContentType.JavaEntity,
            depth => "class Customer { java.util.List"
                + string.Concat(Enumerable.Repeat("<java.util.List", depth - 1))
                + "<String>"
                + string.Concat(Enumerable.Repeat("> ", depth - 1))
                + " names; }"
        },
        {
            "Java, nested classes", ORMEnum.Hibernate, ORMEnum.EFCore, ConversionContentType.JavaEntity,
            depth => string.Concat(Enumerable.Range(0, depth).Select(i => "class C" + i + " { "))
                + string.Concat(Enumerable.Repeat("} ", depth))
        },
        {
            // The MyBatis mapper interface reads Java through the same reader but by its own
            // call, which is exactly how it was first left uncapped.
            "Java, MyBatis mapper interface", ORMEnum.MyBatis, ORMEnum.EFCore, ConversionContentType.JavaQuery,
            depth => "interface CustomerMapper { java.util.List"
                + string.Concat(Enumerable.Repeat("<java.util.List", depth - 1))
                + "<String>"
                + string.Concat(Enumerable.Repeat("> ", depth - 1))
                + " findAll(); }"
        },
        {
            "T-SQL, parentheses", ORMEnum.Dapper, ORMEnum.EFCore, ConversionContentType.SqlQuery,
            depth => Wrapped("SELECT CustomerID FROM Customers WHERE ", "CustomerID = 1", string.Empty, depth)
        },
        {
            // The class brace is the first level, so the parentheses supply the rest.
            "C# entity, parentheses", ORMEnum.Dapper, ORMEnum.EFCore, ConversionContentType.CSharpEntity,
            depth => Wrapped("public class Customer { public int CustomerID { get; set; } = ", "1", "; }", depth - 1)
        },
        {
            // The method brace and the Where call are the first two levels.
            "LINQ, parentheses", ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharpQuery,
            depth => Wrapped(
                "public List<Customer> Query()\n{\n    return ctx.Customers.Where(c => ",
                "c.CustomerID == 1",
                ").ToList();\n}",
                depth - 2)
        },
    };

    private static List<ConversionRecord> Records(
        ORMEnum source,
        ORMEnum target,
        ConversionContentType contentType,
        string content,
        ParseLimits? limits = null)
        => ConversionHandler.Convert(
            source,
            target,
            [new ConversionSource { ContentType = contentType, Content = content }],
            catalogConnectionString: null,
            declaredSourceDialect: null,
            limits).Records;

    private static ConversionRecord? DepthRefusal(IEnumerable<ConversionRecord> records)
        => records.FirstOrDefault(r => r.Reason?.Contains("nests deeper", StringComparison.Ordinal) == true);

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheCapItselfIsRead(
        string shape,
        ORMEnum source,
        ORMEnum target,
        ConversionContentType contentType,
        Func<int, string> build)
    {
        Assert.False(string.IsNullOrEmpty(shape));
        Assert.Null(DepthRefusal(Records(source, target, contentType, build(Cap))));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void OneLevelPastTheCapIsRefusedWithAPosition(
        string shape,
        ORMEnum source,
        ORMEnum target,
        ConversionContentType contentType,
        Func<int, string> build)
    {
        Assert.False(string.IsNullOrEmpty(shape));

        var refusal = DepthRefusal(Records(source, target, contentType, build(Cap + 1)));

        Assert.NotNull(refusal);
        Assert.Equal(ConversionRecordKind.Failure, refusal!.Kind);

        // S7 asks for the error at the level of the file and the line, and the refusal names
        // the token that crossed the cap rather than the unit as a whole.
        Assert.Contains("line ", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("column ", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains(Cap.ToString(), refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOperatorWhoLowersTheCapIsObeyed()
    {
        var content = Wrapped("from Customer c where ", "c.CustomerName = 'x'", string.Empty, 16);

        Assert.Null(DepthRefusal(Records(
            ORMEnum.NHibernate, ORMEnum.EFCore, ConversionContentType.HqlQuery, content)));

        var refusal = DepthRefusal(Records(
            ORMEnum.NHibernate, ORMEnum.EFCore, ConversionContentType.HqlQuery, content,
            new ParseLimits { MaxNestingDepth = 8 }));

        Assert.NotNull(refusal);
        Assert.Contains("8 levels", refusal!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOperatorWhoSwitchesTheCapOffIsObeyedToo()
    {
        // Well below the 1024 levels every measured shape still read, because a test that
        // overflowed the stack would take the whole run down with it.
        var content = Wrapped("from Customer c where ", "c.CustomerName = 'x'", string.Empty, 300);

        Assert.NotNull(DepthRefusal(Records(
            ORMEnum.NHibernate, ORMEnum.EFCore, ConversionContentType.HqlQuery, content)));

        Assert.Null(DepthRefusal(Records(
            ORMEnum.NHibernate, ORMEnum.EFCore, ConversionContentType.HqlQuery, content,
            new ParseLimits { MaxNestingDepth = ParseLimits.Unlimited })));
    }

    [Fact]
    public void TheRunRecordCarriesTheCapTheRunWasReadUnder()
    {
        List<ConversionSource> sources =
        [
            new()
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = "public class Customer { public int Id { get; set; } }",
            },
        ];

        // S2 promises determinism for the same version of the tool, and a movable cap is the
        // second thing the answer depends on: without this field two instances could refuse
        // and translate the same unit without differing in any version the record names.
        Assert.Equal(
            ParseLimits.DefaultMaxNestingDepth,
            ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, sources).MaxNestingDepth);

        Assert.Equal(
            8,
            ConversionHandler.Convert(
                ORMEnum.Dapper,
                ORMEnum.EFCore,
                sources,
                catalogConnectionString: null,
                declaredSourceDialect: null,
                new ParseLimits { MaxNestingDepth = 8 }).MaxNestingDepth);
    }
}
