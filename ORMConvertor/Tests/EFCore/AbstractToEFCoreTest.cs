using AbstractWrappers;
using EFCoreWrappers;
using Model;
using SampleData;

namespace Tests.EFCore;

public class AbstractToEFCoreTest
{
    [Fact]
    public void AbstractToEFCoreOverall()
    {
        var source = CustomerSampleEFCore.Map;

        AbstractEntityBuilder builder = new EFCoreEntityBuilder
        {
            EntityMap = source
        };

        var results = builder.Build();
        var entityOutput = results.Single();

        Assert.Equal(ConversionContentType.CSharpEntity, entityOutput.ContentType);
        Assert.Equal(CustomerSampleEFCore.Entity, entityOutput.Content, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
    }
}
