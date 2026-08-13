using AbstractWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace Tests.Combined;

public class EFCoreToNHibernate
{
    [Fact]
    public void ConvertOrder()
    {
        string sourceCode = """
        [Table("Orders", Schema = "Sales")]
        public class Order
        {
            [Key]
            public required int OrderID { get; set; }

            public required int CustomerID { get; set; }

            public required int SalespersonPersonID { get; set; }

            public int? PickedByPersonID { get; set; }

            public required int ContactPersonID { get; set; }

            public int? BackorderOrderID { get; set; }

            [Precision(0)]
            public required DateTime OrderDate { get; set; }

            [Precision(0)]
            public required DateTime ExpectedDeliveryDate { get; set; }

            public string? CustomerPurchaseOrderNumber { get; set; }

            public required bool IsUndersupplyBackordered { get; set; }

            public string? Comments { get; set; }

            public string? DeliveryInstructions { get; set; }

            public string? InternalComments { get; set; }

            public DateTime? PickingCompletedWhen { get; set; }

            public required int LastEditedBy { get; set; }

            [Precision(7)]
            public required DateTime LastEditedWhen { get; set; }

            public List<OrderLine> OrderLines { get; set; } = new();

        }
        """;

        AbstractEntityBuilder builder = new NHibernateEntityBuilder();
        var entityParser = new EFCoreEntityParser(builder);
        entityParser.Parse(sourceCode);

        var results = builder.Build();
        var entityOutput = results.Single(x => x.ContentType == ConversionContentType.CSharpEntity);
        var xmlOutput = results.Single(x => x.ContentType == ConversionContentType.XML);

        string expectedEntity = """
        public class Order
        {
            public required virtual int OrderID { get; set; }

            public required virtual int CustomerID { get; set; }

            public required virtual int SalespersonPersonID { get; set; }

            public virtual int? PickedByPersonID { get; set; }

            public required virtual int ContactPersonID { get; set; }

            public virtual int? BackorderOrderID { get; set; }

            public required virtual DateTime OrderDate { get; set; }

            public required virtual DateTime ExpectedDeliveryDate { get; set; }

            public virtual string? CustomerPurchaseOrderNumber { get; set; }

            public required virtual bool IsUndersupplyBackordered { get; set; }

            public virtual string? Comments { get; set; }

            public virtual string? DeliveryInstructions { get; set; }

            public virtual string? InternalComments { get; set; }

            public virtual DateTime? PickingCompletedWhen { get; set; }

            public required virtual int LastEditedBy { get; set; }

            public required virtual DateTime LastEditedWhen { get; set; }

            public virtual List<OrderLine> OrderLines { get; set; } = new();

        }
        
        """;

        string expectedMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Order" table="Orders" schema="Sales">
                <id name="OrderID" column="OrderID" type="Int32">
                    <generator class="native" />
                </id>
                <property name="CustomerID" not-null="true" />
                <property name="SalespersonPersonID" not-null="true" />
                <property name="PickedByPersonID" not-null="false" />
                <property name="ContactPersonID" not-null="true" />
                <property name="BackorderOrderID" not-null="false" />
                <property name="OrderDate" not-null="true" precision="0" />
                <property name="ExpectedDeliveryDate" not-null="true" precision="0" />
                <property name="CustomerPurchaseOrderNumber" not-null="false" />
                <property name="IsUndersupplyBackordered" not-null="true" />
                <property name="Comments" not-null="false" />
                <property name="DeliveryInstructions" not-null="false" />
                <property name="InternalComments" not-null="false" />
                <property name="PickingCompletedWhen" not-null="false" />
                <property name="LastEditedBy" not-null="true" />
                <property name="LastEditedWhen" not-null="true" precision="7" />
                <bag name="OrderLines" inverse="true" cascade="all-delete-orphan">
                    <key column="OrderID" />
                    <one-to-many class="OrderLine" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

        Assert.Multiple(() =>
        {
            Assert.Equal(ConversionContentType.CSharpEntity, entityOutput.ContentType);
            Assert.Equal(expectedEntity, entityOutput.Content, ignoreLineEndingDifferences: true);

            Assert.Equal(ConversionContentType.XML, xmlOutput.ContentType);
            Assert.Equal(expectedMapping, xmlOutput.Content, ignoreLineEndingDifferences: true);
        });
    }
}
