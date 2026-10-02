using Model;
using OrmConvertor;
using Tests.Combined;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over an inverse collection whose
/// owner has a composite key, from the two sources that state such a collection without
/// columns (<see cref="InverseCollectionKeyTest"/>). NHibernate refuses a collection key
/// narrower than the key it references with FKUnmatchingColumnsException while it builds the
/// session factory, which no shape assertion would show; everything here runs dry.
/// </summary>
public class InverseCollectionKeyVerificationTest
{
    public static TheoryData<ORMEnum> Sources() => InverseCollectionKeyTest.Sources();

    private static byte[] CompileEntities(ConversionResult result, ORMEnum source)
        => GeneratedEntityCompiler.CompileOrFail(
            $"InverseCollectionKey_From{source}",
            result.Sources.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

    [Theory]
    [MemberData(nameof(Sources))]
    public void GeneratedMappingsAreValidAgainstTheSchema(ORMEnum source)
    {
        var mappings = InverseCollectionKeyTest.Convert(source).Sources
            .Where(o => o.ContentType == ConversionContentType.XML)
            .ToList();
        Assert.Equal(2, mappings.Count);

        Assert.All(mappings, mapping =>
        {
            var errors = NHibernateMappingSchema.Validate(mapping.Content);
            Assert.True(errors.Count == 0, "Generated mapping is invalid:"
                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        });
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void NHibernateBuildsASessionFactoryFromTheArtifacts(ORMEnum source)
    {
        var result = InverseCollectionKeyTest.Convert(source);

        // Completing without an exception is the verdict: the key of the bag is as wide as
        // the composite key of Order and pairs with the many-to-one of OrderLine.
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(result, source),
            result.Sources.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }
}
