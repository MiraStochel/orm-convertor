using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// The generator is where NHibernate says more than the vocabulary of strategies can hold:
/// variants such as seqhilo, generators written as a type name, and parameters without which
/// the strategy is not usable. Parameters are carried canonically - the vocabulary fixes
/// meaning and unit, not spelling (decision 020) - and the output selects the generator name
/// from facts first, the source's name second, the canonical name last (decision 021).
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

    /// <summary>
    /// The mapping alone is not a supported input for generation - the language type comes
    /// from the entity class - so every round trip starts from both artifacts.
    /// </summary>
    private static NHibernateEntityBuilder ParseBoth(string idBody, string entityClass = EntityClass)
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(entityClass);
        new NHibernateXMLMappingParser(builder).Parse(Mapping(idBody));
        return builder;
    }

    private static string BuildXml(NHibernateEntityBuilder builder)
        => builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

    [Fact]
    public void SequenceParametersSurviveTheRoundTrip()
    {
        var builder = ParseBoth("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="sequence">
                            <param name="sequence">order_seq</param>
                        </generator>
                    </id>
        """);

        // The parser canonicalizes: sequence names the sequence, whatever a source calls it.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Sequence, part.Strategy);
        Assert.Equal("order_seq", part.StrategyParameters[GeneratorParameter.SequenceName]);
        Assert.Empty(part.SourceStrategyParameters);

        // Dropping the parameter would leave a mapping that names no sequence: it compiles,
        // and at runtime the target reaches for a sequence of its own choosing.
        var xml = BuildXml(builder);
        Assert.Contains("<generator class=\"sequence\">", xml);
        Assert.Contains("<param name=\"sequence\">order_seq</param>", xml);
        Assert.Contains("</generator>", xml);
    }

    [Fact]
    public void SeqHiLoSurvivesTheRoundTripAsItself()
    {
        var builder = ParseBoth("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="seqhilo">
                            <param name="sequence">order_hi</param>
                            <param name="max_lo">50</param>
                        </generator>
                    </id>
        """);

        // seqhilo is hi/lo over a sequence: the mechanism is what the vocabulary names, the
        // name is kept beside it (decision 011), and max_lo is the highest low value, so the
        // block holds one more - the canonical unit is the block, not the spelling.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.HiLo, part.Strategy);
        Assert.Equal("seqhilo", part.SourceStrategyName);
        Assert.Equal("order_hi", part.StrategyParameters[GeneratorParameter.SequenceName]);
        Assert.Equal("51", part.StrategyParameters[GeneratorParameter.BlockSize]);

        // The output must not degrade to hilo - that would move the counter from the sequence
        // into a table (decision 021) - and max_lo has to come back as the same number.
        var xml = BuildXml(builder);
        Assert.Contains("<generator class=\"seqhilo\">", xml);
        Assert.Contains("<param name=\"sequence\">order_hi</param>", xml);
        Assert.Contains("<param name=\"max_lo\">50</param>", xml);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.PrimaryKeyStrategy);
    }

    [Fact]
    public void HiLoCounterParametersSurviveTheRoundTrip()
    {
        var builder = ParseBoth("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="hilo">
                            <param name="table">order_hilo</param>
                            <param name="column">next_hi</param>
                            <param name="max_lo">50</param>
                        </generator>
                    </id>
        """);

        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.HiLo, part.Strategy);
        Assert.Null(part.SourceStrategyName);
        Assert.Equal("order_hilo", part.StrategyParameters[GeneratorParameter.CounterTable]);
        Assert.Equal("next_hi", part.StrategyParameters[GeneratorParameter.CounterValueColumn]);
        Assert.Equal("51", part.StrategyParameters[GeneratorParameter.BlockSize]);

        var xml = BuildXml(builder);
        Assert.Contains("<generator class=\"hilo\">", xml);
        Assert.Contains("<param name=\"table\">order_hilo</param>", xml);
        Assert.Contains("<param name=\"column\">next_hi</param>", xml);
        Assert.Contains("<param name=\"max_lo\">50</param>", xml);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.PrimaryKeyStrategy);
    }

    [Fact]
    public void SeqHiLoWithoutParametersComesOutAsHiLoWithALoss()
    {
        var builder = ParseBoth("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="seqhilo" />
                    </id>
        """);

        // Facts before names (decision 021): HiLo without a SequenceName is hilo, and the
        // source's name does not get asked. The dropped name is a record, not silence.
        var xml = BuildXml(builder);
        Assert.Contains("<generator class=\"hilo\" />", xml);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Contains("seqhilo", record.Reason);
    }

    [Fact]
    public void GuidCombKeepsItsOwnNameInTheOutput()
    {
        var builder = ParseBoth("""
                    <id name="OrderID" column="OrderId" type="Guid">
                        <generator class="guid.comb" />
                    </id>
        """, """
        public class Order
        {
            public virtual Guid OrderID { get; set; }
        }
        """);

        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Uuid, part.Strategy);
        Assert.Equal("guid.comb", part.SourceStrategyName);

        // guid and guid.comb do not differ in anything the model carries, so the source's
        // name arbitrates the spelling: the target knows it and it means the same mechanism
        // (decision 021). Writing guid instead would turn index-ordered values into random ones.
        var xml = BuildXml(builder);
        Assert.Contains("<generator class=\"guid.comb\" />", xml);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.PrimaryKeyStrategy);
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
    public void ParametersOfAGeneratorOutsideTheVocabularyStayLiteral()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="OrderID" column="OrderId" type="Int32">
                        <generator class="enhanced-sequence">
                            <param name="sequence_name">order_seq</param>
                            <param name="initial_value">100</param>
                        </generator>
                    </id>
        """));

        // A strategy that stayed on the escape path takes its parameters with it (decision
        // 020): they are the named generator's words, not ours to interpret.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Unspecified, part.Strategy);
        Assert.Equal("enhanced-sequence", part.SourceStrategyName);
        Assert.Empty(part.StrategyParameters);
        Assert.Equal("order_seq", part.SourceStrategyParameters["sequence_name"]);
        Assert.Equal("100", part.SourceStrategyParameters["initial_value"]);
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
        Assert.Empty(part.SourceStrategyParameters);
    }
}
