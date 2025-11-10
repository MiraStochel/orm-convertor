using AbstractWrappers;
using DapperWrappers;
using Model;
using SampleData;
using System.Linq;

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

    [Fact]
    public void DapperBuilder_ProducesMultipleOutputs()
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

        AbstractEntityBuilder builder = new DapperEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(source);

        var results = builder.Build();
        var entityResults = results.Where(r => r.ContentType == ConversionContentType.CSharpEntity).ToList();

        Assert.Equal(2, entityResults.Count);
        Assert.Contains(entityResults, r => r.Content.Contains("class Customer"));
        Assert.Contains(entityResults, r => r.Content.Contains("class Order"));
        Assert.All(entityResults, r => Assert.Contains("namespace Sample.Multi;", r.Content));
    }
}
