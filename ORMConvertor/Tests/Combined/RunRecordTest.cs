using DapperWrappers;
using DatabaseCatalog;
using EFCoreWrappers;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The run record of S6: every conversion carries its own identifier and the framework
/// versions it ran against, and the versions come from the descriptors (decision 013),
/// so the record cannot claim anything the parser or the generator did not assume.
/// </summary>
public class RunRecordTest
{
    private static List<ConversionSource> Sources() =>
    [
        new()
        {
            ContentType = ConversionContentType.CSharpEntity,
            Content = "public class Customer { public int Id { get; set; } }",
        },
    ];

    [Fact]
    public void TheRunRecordCarriesTheVersionsOfBothDescriptors()
    {
        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Sources());

        Assert.NotEqual(Guid.Empty, result.RunId);
        Assert.Equal(ORMEnum.Dapper, result.SourceFramework);
        Assert.Equal(DapperDescriptor.Instance.Version, result.SourceFrameworkVersion);
        Assert.Equal(ORMEnum.EFCore, result.TargetFramework);
        Assert.Equal(EFCoreDescriptor.Instance.Version, result.TargetFrameworkVersion);

        // No connection string was passed, so the record must say the catalog took no
        // part - a first-class field beside the records (decision 030).
        Assert.Equal(CatalogConnectionState.NotConfigured, result.CatalogState);
    }

    [Fact]
    public void EveryRunGetsItsOwnIdentifier()
    {
        var first = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Sources());
        var second = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Sources());

        Assert.NotEqual(first.RunId, second.RunId);
    }
}
