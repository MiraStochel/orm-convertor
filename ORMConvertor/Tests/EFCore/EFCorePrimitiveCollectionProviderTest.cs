using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of a collection of scalars written as an ordinary property, measured by
/// its own model validation without a connection (the third level of decision 016) - the fact
/// decision 177 rejects writing the member of a JPA element collection on: the collection becomes
/// a column of the entity's own table, the one the source's @Column names in the collection table,
/// and two such members over one column refuse the whole model.
/// </summary>
public class EFCorePrimitiveCollectionProviderTest
{
    private static string Entities(bool second) => $$"""
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Elements;

        [Table("CLIENT")]
        public class ClientEntity
        {
            [Key]
            public string id { get; set; } = "";

            [Column("VALUE")]
            public HashSet<string>? webOrigins { get; set; }
            {{(second ? "[Column(\"VALUE\")] public HashSet<string>? redirectUris { get; set; }" : string.Empty)}}
        }
        """;

    private static IModel Build(string name, bool second)
        => EFCoreAcceptance.BuildModel(GeneratedEntityCompiler.CompileOrFail(
            "EFCorePrimitiveCollection_" + name,
            [Entities(second)],
            GeneratedEntityCompiler.EFCoreConsumerReferences));

    [Fact]
    public void ACollectionOfScalarsIsAColumnOfTheEntitysTable()
    {
        var client = Build("One", second: false).FindEntityType("Elements.ClientEntity")!;
        var property = client.FindProperty("webOrigins")!;

        Assert.True(property.IsPrimitiveCollection);
        Assert.Equal("VALUE", property.GetColumnName());
        Assert.Equal("CLIENT", client.GetTableName());
    }

    [Fact]
    public void TwoCollectionsOverOneColumnRefuseTheModel()
    {
        var refused = Assert.ThrowsAny<InvalidOperationException>(() => Build("Two", second: true));

        Assert.Contains("are both mapped to column 'VALUE' in 'CLIENT'", refused.Message, StringComparison.Ordinal);
    }
}
