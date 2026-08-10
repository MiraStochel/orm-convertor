using AbstractWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace Tests.Combined;

public class NHibernateToEFCore
{
    [Fact]
    public void ConvertOrder()
    {
        string sourceCode = """
        public class Order
        {
            public virtual int OrderID { get; set; }

            public virtual int CustomerID { get; set; }

            public virtual int SalespersonPersonID { get; set; }

            public virtual int? PickedByPersonID { get; set; }

            public virtual int ContactPersonID { get; set; }

            public virtual int? BackorderOrderID { get; set; }

            public virtual DateTime OrderDate { get; set; }

            public virtual DateTime ExpectedDeliveryDate { get; set; }

            public virtual string? CustomerPurchaseOrderNumber { get; set; }

            public virtual bool IsUndersupplyBackordered { get; set; }

            public virtual string? Comments { get; set; }

            public virtual string? DeliveryInstructions { get; set; }

            public virtual string? InternalComments { get; set; }

            public virtual DateTime? PickingCompletedWhen { get; set; }

            public virtual int LastEditedBy { get; set; }

            public virtual DateTime LastEditedWhen { get; set; }

            public virtual List<OrderLine> OrderLines { get; set; } = [];
        }
        """;

        string sourceMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.Order, NHibernateEntities" table="Orders" schema="Sales">
                <id name="OrderID" column="OrderID" type="int">
                    <generator class="identity" />
                </id>
                <property name="CustomerID" not-null="true" type="int" />
                <property name="SalespersonPersonID" not-null="true" />
                <property name="PickedByPersonID" not-null="false" />
                <property name="ContactPersonID" not-null="true" />
                <property name="BackorderOrderID" not-null="false" />
                <property name="OrderDate" not-null="true" type="datetime" />
                <property name="ExpectedDeliveryDate" not-null="true" type="datetime" />
                <property name="CustomerPurchaseOrderNumber" not-null="false" />
                <property name="IsUndersupplyBackordered" not-null="true" />
                <property name="Comments" not-null="false" type="string" length="4000" />
                <property name="DeliveryInstructions" not-null="false" type="ansistring" length="1000" />
                <property name="InternalComments" not-null="false" />
                <property name="PickingCompletedWhen" not-null="false" />
                <property name="LastEditedBy" not-null="true" />
                <property name="LastEditedWhen" not-null="true" />
                <bag name="OrderLines" inverse="true" cascade="all-delete-orphan">
                    <key column="OrderID" />
                    <one-to-many class="OrderLine" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

        AbstractEntityBuilder builder = new EFCoreEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);
        entityParser.Parse(sourceCode);
        mappingParser.Parse(sourceMapping);

        var results = builder.Build();
        var entityOutput = results.Single(x => x.ContentType == ConversionContentType.CSharpEntity);

        string expectedEntity = """
        namespace NHibernateEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Orders", Schema = "Sales")]
        public class Order
        {
            [Key]
            [Column("OrderID", TypeName="int")]
            public virtual required int OrderID { get; set; }

            [Column(TypeName="int")]
            public virtual required int CustomerID { get; set; }

            public virtual required int SalespersonPersonID { get; set; }

            public virtual int? PickedByPersonID { get; set; }

            public virtual required int ContactPersonID { get; set; }

            public virtual int? BackorderOrderID { get; set; }

            [Column(TypeName="datetime2")]
            public virtual required DateTime OrderDate { get; set; }

            [Column(TypeName="datetime2")]
            public virtual required DateTime ExpectedDeliveryDate { get; set; }

            public virtual string? CustomerPurchaseOrderNumber { get; set; }

            public virtual required bool IsUndersupplyBackordered { get; set; }

            [Column(TypeName="nvarchar")]
            [MaxLength(4000)]
            public virtual string? Comments { get; set; }

            [Column(TypeName="varchar")]
            [MaxLength(1000)]
            public virtual string? DeliveryInstructions { get; set; }

            public virtual string? InternalComments { get; set; }

            public virtual DateTime? PickingCompletedWhen { get; set; }

            public virtual required int LastEditedBy { get; set; }

            public virtual required DateTime LastEditedWhen { get; set; }

            public virtual List<OrderLine> OrderLines { get; set; } = [];

        }

        """;

        Assert.Multiple(() =>
        {
            Assert.Equal(ConversionContentType.CSharpEntity, entityOutput.ContentType);
            Assert.Equal(expectedEntity, entityOutput.Content, ignoreLineEndingDifferences: true);
        });
    }
}
