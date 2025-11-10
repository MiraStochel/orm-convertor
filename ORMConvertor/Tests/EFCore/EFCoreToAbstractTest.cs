using AbstractWrappers;
using EFCoreWrappers;
using Newtonsoft.Json;
using SampleData;
using System.Linq;

namespace Tests.EFCore;

public class EFCoreToAbstractTest
{
    [Fact]
    public void EFCoreToAbstractOverall()
    {
        // Using dummy builder because we need a concrete implementation,
        // however we will only be inspecting how the abstract representation is built
        AbstractEntityBuilder builder = new DummyEntityBuilder();
        var entityParser = new EFCoreEntityParser(builder);
        entityParser.Parse(CustomerSampleEFCore.Entity);

        Assert.Equal(JsonConvert.SerializeObject(CustomerSampleEFCore.Map), JsonConvert.SerializeObject(builder.EntityMap), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void EFCoreParser_SupportsMultipleClasses()
    {
        const string source = @"
using System.ComponentModel.DataAnnotations.Schema;

namespace Sample.Multi
{
    [Table(""Customers"")]
    public class Customer
    {
        public int Id { get; set; }
    }

    [Table(""Orders"")]
    public class Order
    {
        public int OrderId { get; set; }
    }
}";

        AbstractEntityBuilder builder = new DummyEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(source);

        Assert.Equal(2, builder.EntityMaps.Count);
        var customer = builder.EntityMaps.First(em => em.Entity.Name == "Customer");
        var order = builder.EntityMaps.First(em => em.Entity.Name == "Order");
        Assert.Equal("Customers", customer.Table);
        Assert.Equal("Orders", order.Table);
        Assert.All(builder.EntityMaps, em => Assert.Equal("Sample.Multi", em.Entity.Namespace));
    }
}
