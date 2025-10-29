using AbstractWrappers;
using DapperWrappers;
using Model;
using SampleData;

namespace Tests.Dapper;

public class DapperToDapperTest
{
    [Fact]
    public void DapperToDapperOverall()
    {
        AbstractEntityBuilder builder = new DapperEntityBuilder();
        var parser = new DapperEntityParser(builder);

        parser.Parse(CustomerSampleDapper.Entity);
        var result = builder.Build().Single();

        Assert.Equal(ConversionContentType.CSharpEntity, result.ContentType);
        Assert.Equal(CustomerSampleDapper.Entity, result.Content, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
    }
}
