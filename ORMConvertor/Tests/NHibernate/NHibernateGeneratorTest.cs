using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// The generator is where NHibernate says more than the vocabulary of strategies can hold:
/// variants such as seqhilo, generators written as a type name, and parameters without which
/// the strategy is not usable. What the vocabulary drops has to stay recorded next to it.
/// </summary>
public class NHibernateGeneratorTest
{
    private const string EntityClass = """
        public class Order
        {
            public virtual int OrderID { get; set; }
        }
        """;

    private static string Mapping(string body) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Order" table="Orders">
        {body}
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder ParseMapping(string mapping)
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(mapping);
        return builder;
    }

    [Fact]
    public void GeneratorParametersSurviveTheRoundTrip()
    {
        // The mapping alone is not a supported input for generation - the language type comes
        // from the entity class - so the round trip starts from both artifacts.
        var first = new NHibernateEntityBuilder();
        new NHibernateEntityParser(first).Parse(EntityClass);
        new NHibernateXMLMappingParser(first).Parse(Mapping("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="sequence">
                            <param name="sequence">order_seq</param>
                        </generator>
                    </id>
        """));

        var part = Assert.Single(first.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Sequence, part.Strategy);
        Assert.Equal("order_seq", part.StrategyParameters["sequence"]);

        // Dropping the parameter would leave a mapping that names no sequence: it compiles,
        // and at runtime the target reaches for a sequence of its own choosing.
        var xml = first.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;
        Assert.Contains("<generator class=\"sequence\">", xml);
        Assert.Contains("<param name=\"sequence\">order_seq</param>", xml);
        Assert.Contains("</generator>", xml);
    }

    [Fact]
    public void VariantOfAKnownGeneratorKeepsItsOwnName()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="seqhilo">
                            <param name="sequence">order_hi</param>
                            <param name="max_lo">50</param>
                        </generator>
                    </id>
        """));

        // seqhilo is hi/lo over a sequence: the mechanism is what the vocabulary names, the
        // variant is what it drops - so the variant is kept beside it (decision 011).
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.HiLo, part.Strategy);
        Assert.Equal("seqhilo", part.SourceStrategyName);
        Assert.Equal("order_hi", part.StrategyParameters["sequence"]);
        Assert.Equal("50", part.StrategyParameters["max_lo"]);
    }

    [Fact]
    public void GeneratorOutsideTheVocabularyIsNotSilentlyTurnedIntoAssigned()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="OrderIds.SnowflakeGenerator, OrderIds" />
                    </id>
        """));

        // A custom generator names no mechanism we can carry, but losing it without trace is
        // what decision 011 set out to stop: the value says we do not know, the name says what
        // the source wrote, and diagnostics has something to report.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Unspecified, part.Strategy);
        Assert.Equal("OrderIds.SnowflakeGenerator, OrderIds", part.SourceStrategyName);
    }

    [Fact]
    public void CanonicalGeneratorNameIsNotCopiedIntoTheModel()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="identity" />
                    </id>
        """));

        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Identity, part.Strategy);

        // identity is exactly what we would write back, so there is nothing left to record -
        // otherwise every key would carry a copy of its own strategy as a string.
        Assert.Null(part.SourceStrategyName);
        Assert.Empty(part.StrategyParameters);
    }
}