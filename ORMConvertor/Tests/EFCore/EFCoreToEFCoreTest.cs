using AbstractWrappers;
using EFCoreWrappers;
using Model;
using SampleData;
using System.Linq;

namespace Tests.EFCore;

public class EFCoreToEFCoreTest
{
    [Fact]
    public void EFCoreToEFCoreOverall()
    {
        AbstractEntityBuilder builder = new EFCoreEntityBuilder();
        var entityParser = new EFCoreEntityParser(builder);
        entityParser.Parse(CustomerSampleEFCore.Entity);

        var results = builder.Build();
        var entityOutput = results.Single(x => x.ContentType == ConversionContentType.CSharpEntity);

        Assert.Multiple(() =>
        {
            Assert.Equal(ConversionContentType.CSharpEntity, entityOutput.ContentType);
            Assert.Equal(CustomerSampleEFCore.Entity, entityOutput.Content, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
        });
    }

    [Fact]
    public void EFCoreBuilder_ProducesMultipleOutputs()
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

        AbstractEntityBuilder builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);
        parser.Parse(source);

        var results = builder.Build();
        var entityResults = results.Where(r => r.ContentType == ConversionContentType.CSharpEntity).ToList();

        Assert.Equal(2, entityResults.Count);
        Assert.Contains(entityResults, r => r.Content.Contains("class Customer"));
        Assert.Contains(entityResults, r => r.Content.Contains("[Table(\"Customers\")]"));
        Assert.Contains(entityResults, r => r.Content.Contains("[Table(\"Orders\")]") || r.Content.Contains("[Table(\"Orders\","));
        Assert.All(entityResults, r => Assert.Contains("namespace Sample.Multi;", r.Content));
    }
}
