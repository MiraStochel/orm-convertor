using AbstractWrappers.Diagnostics;
using Model;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// The F6 scenario at the second and third verification level (decisions 015 and 016):
/// from a Dapper source carrying only property names and language types, the catalog
/// completes the mapping and NHibernate itself judges the result. The scenario needs the
/// database to exist at all - without the catalog there is no key, no table and no types -
/// so it skips with the rest of the database-dependent tests when none is configured.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperToNHibernateVerificationTest(TestSchemaFixture fixture)
{
    private static byte[] CompileEntities(IEnumerable<ConversionSource> outputs)
        => GeneratedEntityCompiler.CompileOrFail(
            DapperSourceEntities.AssemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

    [Fact]
    public void CompletedConversionRefusesNothing()
    {
        fixture.SkipIfUnavailable();

        var result = DapperSourceEntities.Convert(ORMEnum.NHibernate);

        // NHibernate requires the identifier; only the catalog can supply it here. A
        // failure record would mean the completion did not reach the key.
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(3, result.Sources.Count(o => o.ContentType == ConversionContentType.XML));
        Assert.NotNull(result.CatalogReadTime);
    }

    [Fact]
    public void GeneratedEntitiesCompile()
    {
        fixture.SkipIfUnavailable();

        CompileEntities(DapperSourceEntities.Convert(ORMEnum.NHibernate).Sources);
    }

    [Fact]
    public void GeneratedMappingsAreValidAgainstTheSchema()
    {
        fixture.SkipIfUnavailable();

        var mappings = DapperSourceEntities.Convert(ORMEnum.NHibernate).Sources
            .Where(o => o.ContentType == ConversionContentType.XML)
            .ToList();
        Assert.Equal(3, mappings.Count);

        Assert.All(mappings, mapping =>
        {
            var errors = NHibernateMappingSchema.Validate(mapping.Content);
            Assert.True(errors.Count == 0, "Generated mapping is invalid:"
                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        });
    }

    [Fact]
    public void TheInverseCollectionsCarryTheChildsKeyColumns()
    {
        fixture.SkipIfUnavailable();

        var mappings = DapperSourceEntities.Convert(ORMEnum.NHibernate).Sources
            .Where(o => o.ContentType == ConversionContentType.XML)
            .ToList();

        // The key of a collection lives in the child's table, so only the catalog can
        // supply it here - a single column for Customer.Orders, the composite key of
        // Orders for Order.OrderLines (decision 012 and the completion of decision 015).
        var customer = Assert.Single(mappings, m => m.Content.Contains("<bag name=\"Orders\""));
        Assert.Contains("<key column=\"CustomerId\" />", customer.Content);

        var order = Assert.Single(mappings, m => m.Content.Contains("<bag name=\"OrderLines\""));
        Assert.Contains("<column name=\"CompanyId\" />", order.Content);
        Assert.Contains("<column name=\"OrderId\" />", order.Content);
    }

    [Fact]
    public void NHibernateBuildsASessionFactoryFromTheArtifacts()
    {
        fixture.SkipIfUnavailable();

        var outputs = DapperSourceEntities.Convert(ORMEnum.NHibernate).Sources;

        // Completing without an exception is the verdict: NHibernate bound the composite
        // keys, the supplied types and the synthesized references to the compiled classes.
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(outputs),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }
}
