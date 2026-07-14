using AbstractWrappers;
using DapperWrappers;
using System.Text.Json;
using SampleData;
using System.Linq;

namespace Tests.Dapper;

public class DapperToAbstractTest
{
    [Fact]
    public void DapperToAbstractOverall() {
        var sourceCode = CustomerSampleDapper.Entity;

        AbstractEntityBuilder builder = new DummyEntityBuilder();
        var parser = new DapperEntityParser(builder);

        parser.Parse(sourceCode);

        Assert.Equal(JsonSerializer.Serialize(CustomerSampleDapper.Map), JsonSerializer.Serialize(builder.EntityMap), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void DapperParser_SupportsMultipleClasses()
    {
        const string source = @"
namespace Sample.Multi
{
    public class Customer
    {
        public int Id { get; set; }
    }

    public class Order
    {
        public int OrderId { get; set; }
    }
}";

        AbstractEntityBuilder builder = new DummyEntityBuilder();
        var parser = new DapperEntityParser(builder);

        parser.Parse(source);

        Assert.Equal(2, builder.EntityMaps.Count);
        Assert.Contains(builder.EntityMaps, em => em.Entity.Name == "Customer");
        Assert.Contains(builder.EntityMaps, em => em.Entity.Name == "Order");
        Assert.All(builder.EntityMaps, em => Assert.Equal("Sample.Multi", em.Entity.Namespace));
    }
}
