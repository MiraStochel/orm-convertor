using AbstractWrappers;
using System.Text.Json;
using NHibernateWrappers;
using SampleData;
using System.Linq;

namespace Tests.NHibernate;
public class NHibernateToAbstractTest
{
    [Fact]
    public void NHibernateToAbstractOverall()
    {
        // Using dummy builder because we need a concrete implementation,
        // however we will only be inspecting how the abstract representation is built
        AbstractEntityBuilder builder = new DummyEntityBuilder();  
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);
        entityParser.Parse(CustomerSampleNHibernate.Entity);
        mappingParser.Parse(CustomerSampleNHibernate.XmlMapping);

        Assert.Equal(JsonSerializer.Serialize(CustomerSampleNHibernate.Map), JsonSerializer.Serialize(builder.EntityMap), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void NHibernateParsers_SupportMultipleEntities()
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

        AbstractEntityBuilder builder = new DummyEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);

        entityParser.Parse(entitySource);
        mappingParser.Parse(mappingSource);

        Assert.Equal(2, builder.EntityMaps.Count);
        var customer = builder.EntityMaps.First(em => em.Entity.Name == "Customer");
        var order = builder.EntityMaps.First(em => em.Entity.Name == "Order");
        Assert.Equal("Customers", customer.Table);
        Assert.Equal("Orders", order.Table);
        Assert.All(builder.EntityMaps, em => Assert.Equal("Sample.Multi", em.Entity.Namespace));
    }
}
