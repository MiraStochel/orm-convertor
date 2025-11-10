using AbstractWrappers;
using Model;
using NHibernateWrappers;
using SampleData;
using System.Linq;

namespace Tests.NHibernate;

public class NHibernateToNHibernateTest
{
    [Fact]
    public void NHibernateToNHibernateOverall()
    {
        AbstractEntityBuilder builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);
        entityParser.Parse(CustomerSampleNHibernate.Entity);
        mappingParser.Parse(CustomerSampleNHibernate.XmlMapping);

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

    [Fact]
    public void NHibernateBuilder_ProducesMultipleOutputs()
    {
        const string entitySource = @"
namespace Sample.Multi
{
    public class Customer
    {
        public virtual int Id { get; set; }
    }

    public class Order
    {
        public virtual int OrderId { get; set; }
    }
}";

        const string mappingSource = @"
<hibernate-mapping namespace=""Sample.Multi"">
  <class name=""Sample.Multi.Customer, Sample.Multi"" table=""Customers"">
    <id name=""Id"">
      <generator class=""identity"" />
    </id>
  </class>
  <class name=""Sample.Multi.Order, Sample.Multi"" table=""Orders"">
    <id name=""OrderId"">
      <generator class=""identity"" />
    </id>
  </class>
</hibernate-mapping>";

        AbstractEntityBuilder builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);

        entityParser.Parse(entitySource);
        mappingParser.Parse(mappingSource);

        var results = builder.Build();
        var entities = results.Where(r => r.ContentType == ConversionContentType.CSharpEntity).ToList();
        var mappings = results.Where(r => r.ContentType == ConversionContentType.XML).ToList();

        Assert.Equal(2, entities.Count);
        Assert.Equal(2, mappings.Count);

        Assert.Contains(entities, r => r.Content.Contains("class Customer"));
        Assert.Contains(entities, r => r.Content.Contains("class Order"));
        Assert.Contains(mappings, r => r.Content.Contains("<class name=\"Sample.Multi.Customer"));
        Assert.Contains(mappings, r => r.Content.Contains("<class name=\"Sample.Multi.Order"));
    }
}
