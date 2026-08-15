using AbstractWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

public class CompositeKeyTest
{
    private const string CompositeEntitySource = """
        namespace EFCoreEntities;

        using Microsoft.EntityFrameworkCore;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("OrderLines", Schema = "Sales")]
        [PrimaryKey(nameof(OrderID), nameof(CompanyID))]
        public class OrderLine
        {
            public required int OrderID { get; set; }

            public required int CompanyID { get; set; }

            public required string Description { get; set; }
        }
        """;
    private const string SingleKeyEntitySource = """
        namespace EFCoreEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public required int CustomerID { get; set; }

            public required string Name { get; set; }
        }
        """;

    /// <summary>
    /// Two-part key whose parts also carry an explicit column name and database type.
    /// </summary>
    private const string TwoPartKeyWithColumnsSource = """
        namespace EFCoreEntities;

        using Microsoft.EntityFrameworkCore;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("OrderLines", Schema = "Sales")]
        [PrimaryKey(nameof(OrderID), nameof(CompanyID))]
        public class OrderLine
        {
            [Column("OrderId", TypeName = "int")]
            public required int OrderID { get; set; }

            [Column("CompanyId", TypeName = "bigint")]
            public required long CompanyID { get; set; }

            public required string Description { get; set; }
        }
        """;

    /// <summary>
    /// Four-part key. The attribute deliberately lists the parts in a different order
    /// than the properties are declared in.
    /// </summary>
    private const string FourPartKeyEntitySource = """
        namespace EFCoreEntities;

        using Microsoft.EntityFrameworkCore;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("OrderLineAllocations", Schema = "Sales")]
        [PrimaryKey(nameof(AllocationID), nameof(LineNumber), nameof(OrderID), nameof(CompanyID))]
        public class OrderLineAllocation
        {
            [Column("OrderId", TypeName = "int")]
            public required int OrderID { get; set; }

            [Column("CompanyId", TypeName = "bigint")]
            public required long CompanyID { get; set; }

            [Column("LineNo", TypeName = "int")]
            public required int LineNumber { get; set; }

            [Column("AllocationId", TypeName = "int")]
            public required int AllocationID { get; set; }

            public required string Notes { get; set; }
        }
        """;

    /// <summary>
    /// Three-part key. NHibernate splits the mapping over two artifacts, so the entity
    /// class carries the language types and the XML descriptor the database facts.
    /// </summary>
    private const string ThreePartKeyEntitySource = """
        namespace NHibernateEntities;

        public class OrderLine
        {
            public virtual int OrderID { get; set; }

            public virtual long CompanyID { get; set; }

            public virtual int LineNumber { get; set; }

            public virtual string Description { get; set; }
        }
        """;

    private const string ThreePartKeyXmlMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.OrderLine, NHibernateEntities" table="OrderLines" schema="Sales">
                <composite-id>
                    <key-property name="OrderID" column="OrderId" type="int" />
                    <key-property name="CompanyID" column="CompanyId" type="Int64" />
                    <key-property name="LineNumber" column="LineNo" type="Int32" />
                </composite-id>
                <property name="Description" column="Description" type="String" />
            </class>
        </hibernate-mapping>
        """;

    [Fact]
    public void EFCoreCompositeKeyIsParsedIntoModel()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(CompositeEntitySource);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(2, pk.Parts.Count);
        Assert.Equal("OrderID", pk.Parts[0].PropertyMap.Property.Name);
        Assert.Equal(1, pk.Parts[0].Order);
        Assert.Equal("CompanyID", pk.Parts[1].PropertyMap.Property.Name);
        Assert.Equal(2, pk.Parts[1].Order);
        Assert.All(pk.Parts, p => Assert.Equal(PrimaryKeyStrategy.Assigned, p.Strategy));
    }

    [Fact]
    public void TwoPartKeyKeepsColumnNamesAndDatabaseTypes()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(TwoPartKeyWithColumnsSource);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(2, pk.Parts.Count);

        // A key part is not just a property name - it carries the whole mapping of
        // that property, so the column name and both type levels travel with the key.
        Assert.Equal("OrderID", pk.Parts[0].PropertyMap.Property.Name);
        Assert.Equal("OrderId", pk.Parts[0].PropertyMap.ColumnName);
        Assert.Equal(DatabaseType.Int, pk.Parts[0].PropertyMap.Type);
        Assert.Equal(ScalarType.Int, pk.Parts[0].PropertyMap.Property.Type!.ScalarType);

        Assert.Equal("CompanyID", pk.Parts[1].PropertyMap.Property.Name);
        Assert.Equal("CompanyId", pk.Parts[1].PropertyMap.ColumnName);
        Assert.Equal(DatabaseType.BigInt, pk.Parts[1].PropertyMap.Type);
        Assert.Equal(ScalarType.Long, pk.Parts[1].PropertyMap.Property.Type!.ScalarType);
    }

    [Fact]
    public void ThreePartKeyKeepsOrderColumnNamesAndDatabaseTypes()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(ThreePartKeyEntitySource);
        new NHibernateXMLMappingParser(builder).Parse(ThreePartKeyXmlMapping);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(3, pk.Parts.Count);

        // Order follows the sequence of <key-property> elements.
        Assert.Equal(new[] { "OrderID", "CompanyID", "LineNumber" },
            pk.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.Equal(new[] { 1, 2, 3 }, pk.Parts.Select(p => p.Order));

        Assert.Equal(new string?[] { "OrderId", "CompanyId", "LineNo" },
            pk.Parts.Select(p => p.PropertyMap.ColumnName));

        // Database types come from the XML descriptor, language types from the entity
        // class - the two artifacts merge into one key in the model.
        Assert.Equal(new DatabaseType?[] { DatabaseType.Int, DatabaseType.BigInt, DatabaseType.Int },
            pk.Parts.Select(p => p.PropertyMap.Type));
        Assert.Equal(new ScalarType?[] { ScalarType.Int, ScalarType.Long, ScalarType.Int },
            pk.Parts.Select(p => p.PropertyMap.Property.Type!.ScalarType));

        // <composite-id> admits no generator, so every part is the application's to assign.
        Assert.All(pk.Parts, p => Assert.Equal(PrimaryKeyStrategy.Assigned, p.Strategy));
    }

    [Fact]
    public void FourPartKeyFollowsAttributeOrderNotDeclarationOrder()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(FourPartKeyEntitySource);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(4, pk.Parts.Count);

        // The properties are declared as OrderID, CompanyID, LineNumber, AllocationID,
        // but [PrimaryKey(...)] lists them in a different order - and that is the order
        // of the key columns, so it is the one the model has to keep.
        Assert.Equal(new[] { "AllocationID", "LineNumber", "OrderID", "CompanyID" },
            pk.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.Equal(new[] { 1, 2, 3, 4 }, pk.Parts.Select(p => p.Order));

        Assert.Equal(new string?[] { "AllocationId", "LineNo", "OrderId", "CompanyId" },
            pk.Parts.Select(p => p.PropertyMap.ColumnName));
        Assert.Equal(new DatabaseType?[] { DatabaseType.Int, DatabaseType.Int, DatabaseType.Int, DatabaseType.BigInt },
            pk.Parts.Select(p => p.PropertyMap.Type));
    }

    [Fact]
    public void EFCoreCompositeKeyRoundTrip()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(CompositeEntitySource);
        var code = builder.Build().First().Content;

        Assert.Contains("using Microsoft.EntityFrameworkCore;", code);
        Assert.Contains("[PrimaryKey(nameof(OrderID), nameof(CompanyID))]", code);
        Assert.DoesNotContain("[Key]", code);
    }

    [Fact]
    public void EFCoreCompositeKeyToNHibernateXml()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(CompositeEntitySource);
        var outputs = builder.Build();
        var xml = outputs.Single(o => o.Content.Contains("<hibernate-mapping")).Content;

        Assert.Contains("<composite-id>", xml);
        Assert.Contains("</composite-id>", xml);

        int orderIdPos = xml.IndexOf("<key-property name=\"OrderID\"");
        int companyIdPos = xml.IndexOf("<key-property name=\"CompanyID\"");
        Assert.True(orderIdPos >= 0 && companyIdPos >= 0, "Both key-property elements must be present.");
        Assert.True(orderIdPos < companyIdPos, "Key parts must keep their declared order.");
        Assert.DoesNotContain("<generator", xml);
    }

    [Fact]
    public void NHibernateCompositeIdXmlIsParsedIntoModel()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="OrderLine" table="OrderLines" schema="Sales">
                    <composite-id>
                        <key-property name="OrderID" column="OrderId" type="int" />
                        <key-property name="CompanyID" column="CompanyId" type="int" />
                    </composite-id>
                    <property name="Description" column="Description" type="string" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(2, pk.Parts.Count);
        Assert.Equal("OrderID", pk.Parts[0].PropertyMap.Property.Name);
        Assert.Equal("OrderId", pk.Parts[0].PropertyMap.ColumnName);
        Assert.Equal("CompanyID", pk.Parts[1].PropertyMap.Property.Name);
        Assert.Equal("CompanyId", pk.Parts[1].PropertyMap.ColumnName);
        Assert.Equal(2, pk.Parts[1].Order);
    }

    [Fact]
    public void NHibernateCompositeKeyRoundTrip()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(ThreePartKeyEntitySource);
        new NHibernateXMLMappingParser(builder).Parse(ThreePartKeyXmlMapping);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<composite-id>", xml);
        Assert.Contains("</composite-id>", xml);

        // The input writes the first type as "int", the output as "Int32". The type
        // is not carried through as text - it goes through the DatabaseType enum and
        // comes back in the canonical spelling of the target framework.
        Assert.Contains("<key-property name=\"OrderID\" column=\"OrderId\" type=\"Int32\" />", xml);
        Assert.Contains("<key-property name=\"CompanyID\" column=\"CompanyId\" type=\"Int64\" />", xml);
        Assert.Contains("<key-property name=\"LineNumber\" column=\"LineNo\" type=\"Int32\" />", xml);

        int orderIdPos = xml.IndexOf("<key-property name=\"OrderID\"");
        int companyIdPos = xml.IndexOf("<key-property name=\"CompanyID\"");
        int lineNumberPos = xml.IndexOf("<key-property name=\"LineNumber\"");
        Assert.True(orderIdPos < companyIdPos && companyIdPos < lineNumberPos,
            "Key parts must keep their declared order.");
    }

    [Fact]
    public void NHibernateCompositeKeyToEFCoreEntity()
    {
        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse(ThreePartKeyEntitySource);
        new NHibernateXMLMappingParser(builder).Parse(ThreePartKeyXmlMapping);

        var code = builder.Build().First().Content;

        // All three parts have to reach the target artifact, in the same order and
        // through the mechanism EF Core uses for composite keys.
        Assert.Contains("using Microsoft.EntityFrameworkCore;", code);
        Assert.Contains("[PrimaryKey(nameof(OrderID), nameof(CompanyID), nameof(LineNumber))]", code);
        Assert.DoesNotContain("[Key]", code);

        // Column names and database types survive the change of ecosystem as well.
        Assert.Contains("[Column(\"OrderId\", TypeName=\"int\")]", code);
        Assert.Contains("[Column(\"CompanyId\", TypeName=\"bigint\")]", code);
        Assert.Contains("[Column(\"LineNo\", TypeName=\"int\")]", code);
    }

    [Fact]
    public void EFCoreCompositeKeyToNHibernateEntityHasIdentityMembers()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(CompositeEntitySource);
        var code = builder.Build().Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;

        // NHibernate refuses to compile a <composite-id> mapping unless the persistent
        // class overrides Equals and GetHashCode and is marked [Serializable].
        Assert.Contains("using System;", code);
        Assert.Contains("[Serializable]", code);
        Assert.Contains("public override bool Equals(object? obj)", code);
        Assert.Contains("public override int GetHashCode()", code);

        // Every key part has to take part in both members.
        Assert.Contains("Equals(OrderID, other.OrderID)", code);
        Assert.Contains("Equals(CompanyID, other.CompanyID)", code);
        Assert.Contains("HashCode.Combine(OrderID, CompanyID)", code);

        // A proxy is a subclass of the entity, so the type check must not compare
        // runtime types - otherwise a proxy would never equal its own entity.
        Assert.Contains("obj is not OrderLine other", code);
        Assert.DoesNotContain("GetType()", code);
    }

    [Fact]
    public void NHibernateSingleKeyEntityHasNoIdentityMembers()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(SingleKeyEntitySource);
        var code = builder.Build().Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;

        // Identity members are only required for composite identifiers. A single
        // <id> mapping must stay a plain POCO.
        Assert.DoesNotContain("[Serializable]", code);
        Assert.DoesNotContain("public override bool Equals", code);
        Assert.DoesNotContain("public override int GetHashCode", code);
        Assert.DoesNotContain("using System;", code);
    }
}