using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using HibernateWrappers;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;

namespace Tests.Hibernate;

/// <summary>
/// orm.xml, the descriptor Jakarta Persistence puts above the annotations (decisions 068
/// and 077): read first, so that an annotation claiming otherwise is the conflict, and
/// able to switch the annotations off through metadata-complete.
/// </summary>
public class HibernateOrmXmlTest
{
    private const string Entity = """
        import jakarta.persistence.*;

        @Entity
        @Table(name = "Customers")
        public class Customer {
            @Id private Integer id;
            @Column(name = "Name") private String name;
            private String email;
        }
        """;

    private static (DummyEntityBuilder Builder, JpaReadingContext Context) Read(string xml, string java)
    {
        var builder = new DummyEntityBuilder();
        var context = new JpaReadingContext();

        // The wrapper's order: the descriptor before the class (decision 068).
        new JpaOrmXmlParser(builder, context).Parse(xml);
        new HibernateEntityParser(builder, context).Parse(java);

        return (builder, context);
    }

    [Fact]
    public void TheDescriptorStatesTheMappingAndTheClassSuppliesTheLanguageFacts()
    {
        var (builder, _) = Read("""
            <?xml version="1.0" encoding="UTF-8"?>
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <entity class="Shop.Customer">
                    <table name="Customers" schema="Sales"/>
                    <attributes>
                        <id name="id">
                            <column name="CustomerId"/>
                            <generated-value strategy="IDENTITY"/>
                        </id>
                        <basic name="email">
                            <column name="Email" length="100" nullable="false"/>
                        </basic>
                    </attributes>
                </entity>
            </entity-mappings>
            """, """
            package Shop;

            import jakarta.persistence.Entity;

            @Entity
            public class Customer {
                private Integer id;
                private String email;
            }
            """);

        var map = Assert.Single(builder.EntityMaps);
        Assert.Equal("Shop", map.Entity.Namespace);
        Assert.Equal("Customers", map.Table);
        Assert.Equal("Sales", map.Schema);
        Assert.Equal(PrimaryKeyStrategy.Identity, map.PrimaryKey!.Parts.Single().Strategy);
        Assert.Equal("CustomerId", map.PropertyMaps.Single(pm => pm.Property.Name == "id").ColumnName);

        var email = map.PropertyMaps.Single(pm => pm.Property.Name == "email");
        Assert.Equal("Email", email.ColumnName);
        Assert.Equal(100, email.Length);
        Assert.False(email.IsNullable);
        Assert.Equal(ScalarType.String, email.Property.Type!.ScalarType);
    }

    [Fact]
    public void TheDescriptorOutranksTheAnnotationsAndTheDifferenceIsAConflict()
    {
        var (builder, _) = Read("""
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <entity class="Customer">
                    <table name="Clients"/>
                    <attributes>
                        <basic name="name"><column name="FullName"/></basic>
                    </attributes>
                </entity>
            </entity-mappings>
            """, Entity);

        var map = Assert.Single(builder.EntityMaps);
        Assert.Equal("Clients", map.Table);
        Assert.Equal("FullName", map.PropertyMaps.Single(pm => pm.Property.Name == "name").ColumnName);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.TableName && r.Reason.Contains("Customers"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.ColumnName && r.Reason.Contains("'Name'"));
    }

    [Fact]
    public void MetadataCompleteSwitchesTheAnnotationsOff()
    {
        var (builder, context) = Read("""
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <entity class="Customer" metadata-complete="true">
                    <table name="Clients"/>
                    <attributes>
                        <id name="id"/>
                    </attributes>
                </entity>
            </entity-mappings>
            """, Entity);

        Assert.True(context.IsMetadataComplete("Customer"));

        var map = Assert.Single(builder.EntityMaps);
        Assert.Equal("Clients", map.Table);
        Assert.Null(map.PropertyMaps.Single(pm => pm.Property.Name == "name").ColumnName);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void UnreadElementsAreReported()
    {
        var (builder, _) = Read("""
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <named-query name="Customer.all"><query>select c from Customer c</query></named-query>
                <entity class="Customer">
                    <inheritance strategy="JOINED"/>
                    <attributes>
                        <id name="id"/>
                        <element-collection name="tags"/>
                    </attributes>
                </entity>
            </entity-mappings>
            """, Entity);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("<named-query>"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("<inheritance>"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("<element-collection name=\"tags\">"));
    }

    [Fact]
    public void AnEmbeddedIdDeclaredInTheDescriptorTakesItsClassFromTheJavaDeclaration()
    {
        var (builder, _) = Read("""
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <entity class="OrderLine">
                    <attributes>
                        <embedded-id name="id"/>
                    </attributes>
                </entity>
            </entity-mappings>
            """, """
            import jakarta.persistence.*;

            @Entity
            public class OrderLine {
                private OrderLineId id;
                private int quantity;
            }

            @Embeddable
            public class OrderLineId implements java.io.Serializable {
                private Integer orderId;
                private Integer lineNo;
            }
            """);

        builder.DissolveKeyClasses();

        var map = Assert.Single(builder.EntityMaps);
        Assert.Equal(["orderId", "lineNo"], map.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.Equal(KeyClassForm.Embedded, map.PrimaryKey.SourceKeyClass!.Form);
    }

    [Fact]
    public void TheOrchestrationReadsTheSampleDescriptorBesideTheClass()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
        [
            new() { Content = SampleData.CustomerSampleHibernate.OrmXml, ContentType = ConversionContentType.XML },
            new() { Content = SampleData.CustomerSampleHibernate.Entity, ContentType = ConversionContentType.JavaEntity },
        ]);

        var entity = result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;
        Assert.Contains("[Table(\"Customers\", Schema = \"Sales\")]", entity);
        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Conflict);
    }
}
