using AbstractWrappers;
using Model;
using NHibernateWrappers;
using SampleData;

namespace Tests.NHibernate;

public class AbstractToNHibernateTest
{
    [Fact]
    public void AbstractToNHibernateOverall()
    {
        var source = CustomerSampleNHibernate.Map;

        AbstractEntityBuilder builder = new NHibernateEntityBuilder
        {
            EntityMap = source
        };

        var results = builder.Build();
        var entityOutput = results.Single(x => x.ContentType == ConversionContentType.CSharpEntity);
        var xmlOutput = results.Single(x => x.ContentType == ConversionContentType.XML);

        Assert.Multiple(() =>
        {
            Assert.Equal(ConversionContentType.CSharpEntity, entityOutput.ContentType);
            Assert.Equal(CustomerSampleNHibernate.Entity, entityOutput.Content, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);

            Assert.Equal(ConversionContentType.XML, xmlOutput.ContentType);
            Assert.Equal(CustomerSampleNHibernate.XmlMapping, xmlOutput.Content, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
        });
    }
}
