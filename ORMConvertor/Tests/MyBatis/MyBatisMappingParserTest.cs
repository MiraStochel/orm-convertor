using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using MyBatisWrappers;

namespace Tests.MyBatis;

/// <summary>
/// The reading half of decision 084: what a MyBatis mapper states and, just as much, what it
/// does not. The two halves that matter most are the ones the tutorial turned up as data
/// rather than as documentation - &lt;id&gt; is the identity of a result row and not a key,
/// and a &lt;collection&gt; carries the shape of a result and not a foreign key - because
/// both look like mapping facts and neither is one.
/// </summary>
public class MyBatisMappingParserTest
{
    private const string DomainClass = """
        package Shop;

        import java.math.BigDecimal;
        import java.util.ArrayList;
        import java.util.List;

        public class Customer {
            private Integer CustomerId;
            private String CustomerName;
            private BigDecimal CreditLimit;
            private String Note;
            private List<Order> Orders = new ArrayList<>();
        }
        """;

    private static DummyEntityBuilder Read(params (ConversionContentType Type, string Content)[] units)
    {
        var builder = new DummyEntityBuilder();
        var context = MyBatisReadingContext.For(builder);

        IEntityParser[] parsers =
        [
            new MyBatisEntityParser(builder),
            new MyBatisMapperInterfaceParser(builder, context),
            new MyBatisXmlMappingParser(builder, context),
        ];

        // The order of the parsers is source precedence ordered in time (decision 017), the
        // same way the orchestration runs them.
        foreach (var parser in parsers)
        {
            foreach (var (type, content) in units.Where(u => parser.CanParse(u.Type)))
            {
                parser.Parse(content);
            }
        }

        return builder;
    }

    private static string Mapper(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
        <mapper namespace="Shop.CustomerMapper">
        {body}
        </mapper>
        """;

    private static EntityMap Entity(DummyEntityBuilder builder, string name)
        => builder.EntityMaps.Single(em => em.Entity.Name == name);

    private static PropertyMap Map(EntityMap map, string property)
        => map.PropertyMaps.Single(pm => pm.Property.Name == property);

    /* ---- the conversion table, read ------------------------------------------------- */

    [Fact]
    public void AResultMapGivesThePairsOfColumnAndProperty()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <id     column="CustomerID"   property="CustomerId"/>
                <result column="FullName"     property="CustomerName"/>
                <result column="CreditLimit"  property="CreditLimit"/>
              </resultMap>
            """)));

        var customer = Entity(builder, "Customer");

        Assert.Equal("CustomerID", Map(customer, "CustomerId").ColumnName);
        Assert.Equal("FullName", Map(customer, "CustomerName").ColumnName);
        Assert.Equal("CreditLimit", Map(customer, "CreditLimit").ColumnName);
    }

    /// <summary>
    /// The entity's package comes from the domain class and the alias is resolved by name
    /// (decision 001), so the typeAliases of a configuration the tool does not read are not
    /// needed to know which class type="Customer" is.
    /// </summary>
    [Fact]
    public void AnAliasResolvesByNameAndTheNamespaceComesFromTheClass()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <result column="CustomerName" property="CustomerName"/>
              </resultMap>
            """)));

        var customer = Assert.Single(builder.EntityMaps);
        Assert.Equal("Shop", customer.Entity.Namespace);
    }

    /// <summary>
    /// The table is nowhere in a mapper: MyBatis has no notion of one, and the name lives
    /// only inside the SQL of a statement, which is a query and not a mapping fact.
    /// </summary>
    [Fact]
    public void NoTableAndNoSchemaAreRead()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <result column="CustomerName" property="CustomerName"/>
              </resultMap>
            """)));

        var customer = Entity(builder, "Customer");
        Assert.Null(customer.Table);
        Assert.Null(customer.Schema);
    }

    [Fact]
    public void JdbcTypeIsReadAsAFamilyWithItsUnicodeFacet()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <result column="CustomerName" property="CustomerName" jdbcType="NVARCHAR"/>
                <result column="CreditLimit"  property="CreditLimit"  jdbcType="DECIMAL"/>
              </resultMap>
            """)));

        var customer = Entity(builder, "Customer");

        Assert.Equal(DatabaseType.VarChar, Map(customer, "CustomerName").Type);
        Assert.True(Map(customer, "CustomerName").IsUnicode);
        Assert.Equal(DatabaseType.Decimal, Map(customer, "CreditLimit").Type);

        // The facets jdbcType has no room for stay absent rather than being guessed at.
        Assert.Null(Map(customer, "CustomerName").Length);
        Assert.Null(Map(customer, "CreditLimit").Precision);
        Assert.Null(Map(customer, "CustomerName").IsNullable);
    }

    /// <summary>
    /// The finding the tutorial turned up as data: &lt;id&gt; says by which column two rows
    /// are recognized as one object, and the table need have no key at all. Materializing it
    /// would not merely be wrong - a materialized fact the catalog cannot overwrite would
    /// turn a silent supplement into a conflict against the real key (decision 084).
    /// </summary>
    [Fact]
    public void AnIdElementFoundsNoPrimaryKeyAndIsReportedAsAnIncompleteness()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <id     column="CustomerID"   property="CustomerId"/>
                <result column="CustomerName" property="CustomerName"/>
              </resultMap>
            """)));

        var customer = Entity(builder, "Customer");

        Assert.Null(customer.PrimaryKey);
        Assert.False(customer.HasNoKey);

        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness);
        Assert.Contains("CustomerID", record.Reason);
        Assert.Contains("identity of a result row", record.Reason);
    }

    /// <summary>An entity without any &lt;id&gt; carries no record at all: a constant one states nothing.</summary>
    [Fact]
    public void AMapperWithoutAnIdCarriesNoIdentityRecord()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <result column="CustomerName" property="CustomerName"/>
              </resultMap>
            """)));

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness);
    }

    /* ---- nested mapping -------------------------------------------------------------- */

    /// <summary>
    /// One &lt;resultMap&gt; carries the mapping of two entities and the navigation between
    /// them - a shape no other of the six frameworks has, and one the model takes without a
    /// change because entities reference each other by name (decision 001). The relation
    /// carries no columns: a join condition is a query, and deriving a foreign key from it
    /// would claim of the schema something that need not be in it.
    /// </summary>
    [Fact]
    public void ANestedCollectionFoundsTheSecondEntityAndARelationWithoutColumns()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass + """


                package Shop;

                public class Order {
                    private Integer OrderId;
                    private String Reference;
                }
                """),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <id     column="CustomerID"   property="CustomerId"/>
                <collection property="Orders" ofType="Order">
                  <id     column="OrderID"   property="OrderId"/>
                  <result column="Reference" property="Reference"/>
                </collection>
              </resultMap>
            """)));

        var customer = Entity(builder, "Customer");
        var order = Entity(builder, "Order");

        Assert.Equal("OrderID", Map(order, "OrderId").ColumnName);
        Assert.Equal("Reference", Map(order, "Reference").ColumnName);

        var relation = Assert.Single(customer.Relations);
        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Equal(RelationRole.Inverse, relation.Role);
        Assert.Equal("Order", relation.TargetEntity);
        Assert.Equal("Orders", relation.SourceNavigationProperty);
        Assert.Empty(relation.ColumnPairs);
    }

    /// <summary>
    /// The other side of the same rule: an &lt;association&gt; is many-to-one and owning,
    /// which follows from the relational model - a collection never holds the physical
    /// foreign key - and not from any convention of MyBatis, which knows no ownership at all.
    /// </summary>
    [Fact]
    public void AnAssociationIsAnOwningManyToOne()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, """
                package Shop;

                public class Order {
                    private Integer OrderId;
                    private Customer Buyer;
                }
                """),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="order" type="Order">
                <id column="OrderID" property="OrderId"/>
                <association property="Buyer" javaType="Customer"/>
              </resultMap>
            """)));

        var relation = Assert.Single(Entity(builder, "Order").Relations);

        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Empty(relation.ColumnPairs);
    }

    /// <summary>The N+1 form maps the navigation and no columns, and says that the select was dropped.</summary>
    [Fact]
    public void ANestedSelectMapsTheNavigationAndReportsTheSelect()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <collection property="Orders" ofType="Order" select="findOrders" column="CustomerID"/>
              </resultMap>
            """)));

        Assert.Single(Entity(builder, "Customer").Relations);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("findOrders") && r.Reason.Contains("CustomerID"));
    }

    /* ---- two views of one class ------------------------------------------------------ */

    /// <summary>
    /// MyBatis maps per statement, so two &lt;resultMap&gt; over one class may disagree; the
    /// model has one mapping per entity (decision 001). Neither refusing the input - MyBatis
    /// accepts it and the project runs - nor founding a second entity would do, so the
    /// mechanism of decision 017 decides it one floor lower than usual: between two elements
    /// of one artifact rather than between two artifacts. The first value stands and the
    /// difference is a record.
    /// </summary>
    [Fact]
    public void TwoResultMapsOfOneClassKeepTheFirstValueAndReportTheDifference()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="plain" type="Customer">
                <result column="Name" property="CustomerName"/>
              </resultMap>
              <resultMap id="other" type="Customer">
                <result column="CustomerName" property="CustomerName"/>
              </resultMap>
            """)));

        Assert.Equal("Name", Map(Entity(builder, "Customer"), "CustomerName").ColumnName);

        var conflict = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Contains("Name", conflict.Reason);
        Assert.Contains("CustomerName", conflict.Reason);
    }

    /// <summary>extends merges the parent's pairs, which is a textual merge like an include.</summary>
    [Fact]
    public void ExtendsMergesTheParentsPairs()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="base" type="Customer">
                <id column="CustomerID" property="CustomerId"/>
              </resultMap>
              <resultMap id="full" type="Customer" extends="base">
                <result column="CustomerName" property="CustomerName"/>
              </resultMap>
            """)));

        var customer = Entity(builder, "Customer");
        Assert.Equal("CustomerID", Map(customer, "CustomerId").ColumnName);
        Assert.Equal("CustomerName", Map(customer, "CustomerName").ColumnName);
    }

    /* ---- the closed mapping ---------------------------------------------------------- */

    /// <summary>
    /// autoMapping="false" is what makes a property with no column expressible at all: the
    /// mapping then says exactly what is written, so a property nobody named has no column
    /// behind it (decision 072).
    /// </summary>
    [Fact]
    public void AClosedMappingMakesAnUnnamedPropertyTransient()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer" autoMapping="false">
                <id     column="CustomerID"   property="CustomerId"/>
                <result column="CustomerName" property="CustomerName"/>
                <result column="CreditLimit"  property="CreditLimit"/>
                <collection property="Orders" ofType="Order"/>
              </resultMap>
            """)));

        var customer = Entity(builder, "Customer");

        Assert.True(Map(customer, "Note").IsTransient);
        Assert.False(Map(customer, "CustomerName").IsTransient);

        // A navigation is not a property with no column; it is mapped by the relation.
        Assert.False(Map(customer, "Orders").IsTransient);
    }

    /// <summary>With automatic mapping left on, silence is silence and nothing is claimed.</summary>
    [Fact]
    public void AnOpenMappingClaimsNothingAboutAnUnnamedProperty()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper("""
              <resultMap id="customer" type="Customer">
                <result column="CustomerName" property="CustomerName"/>
              </resultMap>
            """)));

        Assert.False(Map(Entity(builder, "Customer"), "Note").IsTransient);
    }

    /* ---- what is outside the table --------------------------------------------------- */

    [Theory]
    [InlineData("<discriminator column=\"Kind\" javaType=\"String\"/>", "discriminator")]
    [InlineData("<constructor><idArg column=\"CustomerID\" javaType=\"Integer\"/></constructor>", "constructor")]
    public void AFactOutsideTheTableIsALossThatNamesIt(string element, string name)
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.XML, Mapper($"""
              <resultMap id="customer" type="Customer">
                {element}
              </resultMap>
            """)));

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains(name));
    }

    /* ---- the annotated form ---------------------------------------------------------- */

    /// <summary>
    /// Requirement F8 names both input forms in so many words, and @Results is the annotated
    /// spelling of a &lt;resultMap&gt;: the same pairs, read into the same facts. The entity
    /// is the one the method materializes, which is the only place the annotated form names it.
    /// </summary>
    [Fact]
    public void ResultsOnAMapperMethodGiveTheSamePairs()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.JavaQuery, """
                package Shop;

                import java.util.List;
                import org.apache.ibatis.annotations.Result;
                import org.apache.ibatis.annotations.Results;
                import org.apache.ibatis.annotations.Select;

                public interface CustomerMapper {

                    @Select("SELECT CustomerID, FullName FROM Customers")
                    @Results({
                        @Result(column = "CustomerID", property = "CustomerId", id = true),
                        @Result(column = "FullName", property = "CustomerName", jdbcType = JdbcType.NVARCHAR)
                    })
                    List<Customer> findAll();
                }
                """));

        var customer = Entity(builder, "Customer");

        Assert.Equal("CustomerID", Map(customer, "CustomerId").ColumnName);
        Assert.Equal("FullName", Map(customer, "CustomerName").ColumnName);
        Assert.True(Map(customer, "CustomerName").IsUnicode);
        Assert.Null(customer.PrimaryKey);
    }

    /// <summary>
    /// @Many names no element type - MyBatis takes it from the declaration of the property -
    /// so the entity on the far side is read out of the domain class.
    /// </summary>
    [Fact]
    public void ManyTakesTheTargetEntityFromTheDeclaredProperty()
    {
        var builder = Read(
            (ConversionContentType.JavaEntity, DomainClass),
            (ConversionContentType.JavaQuery, """
                package Shop;

                import java.util.List;
                import org.apache.ibatis.annotations.Many;
                import org.apache.ibatis.annotations.Result;
                import org.apache.ibatis.annotations.Results;
                import org.apache.ibatis.annotations.Select;

                public interface CustomerMapper {

                    @Select("SELECT CustomerID FROM Customers")
                    @Results({
                        @Result(property = "Orders", javaType = List.class, many = @Many(select = "findOrders"))
                    })
                    List<Customer> findAll();
                }
                """));

        var relation = Assert.Single(Entity(builder, "Customer").Relations);

        Assert.Equal("Order", relation.TargetEntity);
        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Empty(relation.ColumnPairs);
    }

    /// <summary>
    /// A mapper interface that carries no statement at all is the ordinary shape of a
    /// MyBatis project - the statements live in the XML mapper beside it - and it is read
    /// all the same, for the types of the parameters. The unit is not barren for it
    /// (decision 066).
    /// </summary>
    [Fact]
    public void AnInterfaceWithoutAnnotationsStillYieldsTheEntityItMaterializes()
    {
        var builder = new DummyEntityBuilder();
        var context = MyBatisReadingContext.For(builder);

        new MyBatisEntityParser(builder).Parse(DomainClass);

        var read = new MyBatisMapperInterfaceParser(builder, context).Parse("""
            package Shop;

            import java.math.BigDecimal;
            import java.util.List;
            import org.apache.ibatis.annotations.Param;

            public interface CustomerMapper {
                List<Customer> findRich(@Param("creditLimit") BigDecimal creditLimit);
            }
            """);

        Assert.Equal("Customer", Assert.Single(read).Entity.Name);
    }
}
