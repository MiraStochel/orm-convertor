using AbstractWrappers;
using DapperWrappers;
using Model;
using SampleData;

namespace Tests.Dapper;

public class AbstractToDapperTest
{
    [Fact]
    public void AbstractToDapperOverall()
    {
        var source = CustomerSampleDapper.Map;

        AbstractEntityBuilder builder = new DapperEntityBuilder
        {
            EntityMap = source
        };

        var results = builder.Build();
        var entityOutput = results.Single();

        Assert.Equal(ConversionContentType.CSharpEntity, entityOutput.ContentType);
        Assert.Equal(CustomerSampleDapper.Entity, entityOutput.Content, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
    }
}
