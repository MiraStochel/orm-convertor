using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The rule of decision 093 over the language that used to be the exception to it: a unit
/// whose text the reader cannot read is a Failure of that unit, with the position where the
/// reading stopped, and the rest of the conversion goes on. Malformed XML used to leave the
/// wrapper as an XmlException and end as a 400, so one broken document took the artifacts of
/// every healthy unit beside it with it - which is what F14 and decision 045 forbid.
///
/// The theories run over every framework the enum yields rather than over the four that read
/// XML, because the two halves of the rule are exactly that nobody falls silent and nobody
/// says it twice. A framework whose XML is read on both passes - the hbm.xml and the MyBatis
/// mapper - has to speak once, and a framework that reads no XML at all has to say the other
/// thing instead.
/// </summary>
public class UnreadableXmlTest
{
    /// <summary>
    /// One broken document for all of them: the end tag on line 3 does not match the start
    /// tag on line 2. Which root it names does not matter - no reader gets that far.
    /// </summary>
    private const string Broken = """
        <hibernate-mapping>
          <class name="Customer">
        </hibernate-mapping>
        """;

    private const string Position = "The XML could not be read at line ";

    /// <summary>The frameworks whose parsers claim an XML unit at all.</summary>
    private static bool ReadsXml(ORMEnum framework)
        => framework is ORMEnum.NHibernate or ORMEnum.Hibernate or ORMEnum.EclipseLink or ORMEnum.MyBatis;

    private static ConversionResult Convert(ORMEnum source, string broken = Broken)
    {
        List<ConversionSource> units =
        [
            .. CrossFrameworkInputs.Units(source),
            new() { Name = "broken.xml", ContentType = ConversionContentType.XML, Content = broken },
        ];

        return ConversionHandler.Convert(source, ORMEnum.EFCore, units);
    }

    private static List<ConversionRecord> About(ConversionResult result, string unit)
        => [.. result.Records.Where(r => r.Unit == unit)];

    private static List<ConversionRecord> Unreadable(ConversionResult result)
        => [.. result.Records.Where(r => r.Reason.StartsWith(Position, StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Frameworks), MemberType = typeof(CrossFrameworkInputs))]
    public void ABrokenDocumentIsRefusedOnce(ORMEnum source)
    {
        var result = Convert(source);

        if (!ReadsXml(source))
        {
            // Nobody claimed the unit, which is the older sentence of decision 045 and still
            // the right one: the document was never read, so there is nothing to say about
            // its shape.
            Assert.Empty(Unreadable(result));
            Assert.Contains(About(result, "broken.xml"), r => r.Reason.Contains("no parser", StringComparison.Ordinal));
            return;
        }

        var refusal = Assert.Single(Unreadable(result));

        Assert.Equal(ConversionRecordKind.Failure, refusal.Kind);
        Assert.Equal(ConversionContentType.XML, refusal.Artifact);

        // S7 asks for the error at the level of the file and the line, so the record names
        // both: the unit as the client named it (decision 066) and the position in it.
        Assert.Equal("broken.xml", refusal.Unit);
        Assert.Contains("column ", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of the whole decision: the units that were fine are translated anyway, and
    /// the caller gets what the run produced (decision 045). Before it, this conversion
    /// answered 400 and handed over nothing at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Frameworks), MemberType = typeof(CrossFrameworkInputs))]
    public void TheHealthyUnitsOfTheSameRunAreTranslatedAnyway(ORMEnum source)
    {
        var result = Convert(source);

        Assert.NotEmpty(result.Sources);
        Assert.Contains(result.Sources, s => s.Content.Contains("Customer", StringComparison.Ordinal));
    }

    /// <summary>
    /// The general record of decision 045 stays beside the specific one rather than being
    /// suppressed by it: one says why the reading stopped, the other that nothing came of the
    /// unit. It is what a malformed Java class or a malformed SQL unit has always produced.
    /// </summary>
    [Fact]
    public void TheRecordAboutABarrenUnitStaysBesideTheOneAboutTheDocument()
    {
        var about = About(Convert(ORMEnum.NHibernate), "broken.xml");

        Assert.Equal(2, about.Count);
        Assert.All(about, r => Assert.Equal(ConversionRecordKind.Failure, r.Kind));
        Assert.Contains(about, r => r.Reason.StartsWith(Position, StringComparison.Ordinal));
        Assert.Contains(about, r => r.Reason.Contains("neither a mapping fact nor a query", StringComparison.Ordinal));
    }

    /// <summary>
    /// The line in the record is the line in the text the client sent. The reading trims the
    /// head of the unit, because whitespace before the XML declaration is itself an error,
    /// and a trim that is not counted back would name a line nobody can find.
    /// </summary>
    [Fact]
    public void ThePositionNamesTheLineTheClientWrote()
    {
        Assert.Contains(
            "line 3, column ",
            Assert.Single(Unreadable(Convert(ORMEnum.NHibernate))).Reason,
            StringComparison.Ordinal);

        Assert.Contains(
            "line 5, column ",
            Assert.Single(Unreadable(Convert(ORMEnum.NHibernate, "\n\n" + Broken))).Reason,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A document that reads perfectly well and simply is not the mapping artifact of this
    /// framework is no failure of the reading, and the decision does not make it one: the
    /// unit yields nothing and the run says that much, as it did before.
    /// </summary>
    [Fact]
    public void AWellFormedDocumentOfAnotherKindIsNotRefusedForItsShape()
    {
        var result = Convert(ORMEnum.NHibernate, "<something-else><item /></something-else>");

        Assert.Empty(Unreadable(result));
        Assert.Contains(About(result, "broken.xml"), r => r.Reason.Contains("neither a mapping fact nor a query", StringComparison.Ordinal));
    }
}
