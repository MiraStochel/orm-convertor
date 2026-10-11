using AbstractWrappers.Diagnostics;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// The attribute half of the flat-reading boundary (decisions 048 and 004). The element
/// half has had records since decision 030 (<see cref="NHibernateUnreadElementTest"/>),
/// but the attributes the model has no place for, and the children of the elements read
/// below &lt;class&gt;, used to vanish without a word - among them formula, where, subselect
/// and filter, which do not merely make the output poorer but make it mean something else.
/// An attribute the generated mapping writes itself from the model is derived, not lost,
/// and has no record (decision 067).
/// </summary>
public class NHibernateUnmodelledAttributeTest
{
    private static string Mapping(string classAttributes, string body) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Product" table="Products"{classAttributes}>
                <id name="Id" type="Int32">
                    <generator class="identity" />
                </id>
        {body}
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder ParseMapping(string mapping)
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(mapping);
        return builder;
    }

    private static List<ConversionRecord> Losses(NHibernateEntityBuilder builder)
        => [.. builder.Records.Where(r => r.Kind == ConversionRecordKind.Loss)];

    [Theory]
    [InlineData("formula", "SubTotal * Rate")]
    [InlineData("access", "field.camelcase")]
    [InlineData("insert", "false")]
    [InlineData("update", "false")]
    [InlineData("lazy", "true")]
    [InlineData("generated", "always")]
    [InlineData("optimistic-lock", "false")]
    public void UnmodelledPropertyAttributeLeavesARecord(string attribute, string value)
    {
        var builder = ParseMapping(Mapping(string.Empty, $"""
                    <property name="Tax" type="Decimal" {attribute}="{value}" />
        """));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Product", loss.Entity);
        Assert.Equal("Tax", loss.Property);
        Assert.Contains($"{attribute}=\"{value}\"", loss.Reason);
        Assert.Contains("<property>", loss.Reason);

        // Decision 048: the categories are a closed vocabulary of facts the model holds and
        // none of these is one of them, so the record names none.
        Assert.Null(loss.Category);
    }

    [Theory]
    [InlineData("discriminator-value", "P")]
    [InlineData("where", "IsDeleted = 0")]
    [InlineData("mutable", "false")]
    [InlineData("optimistic-lock", "dirty")]
    [InlineData("dynamic-insert", "true")]
    [InlineData("dynamic-update", "true")]
    [InlineData("batch-size", "25")]
    [InlineData("lazy", "false")]
    [InlineData("abstract", "true")]
    [InlineData("subselect", "select * from ActiveProducts")]
    [InlineData("catalog", "Archive")]
    [InlineData("entity-name", "StockedProduct")]
    [InlineData("select-before-update", "true")]
    [InlineData("check", "Price >= 0")]
    [InlineData("polymorphism", "explicit")]
    [InlineData("persister", "Shop.ProductPersister")]
    [InlineData("proxy", "Shop.IProduct")]
    [InlineData("rowid", "rowid")]
    [InlineData("schema-action", "none")]
    public void UnmodelledClassAttributeLeavesARecord(string attribute, string value)
    {
        var builder = ParseMapping(Mapping($" {attribute}=\"{value}\"", string.Empty));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Product", loss.Entity);
        Assert.Contains($"{attribute}=\"{value}\"", loss.Reason);
        Assert.Contains("<class>", loss.Reason);

        // The attribute concerns the entity as a whole, so the record points at no property.
        Assert.Null(loss.Property);
        Assert.Null(loss.Category);
    }

    [Theory]
    [InlineData("access", "field.camelcase")]
    [InlineData("unsaved-value", "0")]
    public void UnmodelledIdAttributeLeavesARecord(string attribute, string value)
    {
        var builder = ParseMapping($"""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Product" table="Products">
                    <id name="Id" type="Int32" {attribute}="{value}">
                        <generator class="identity" />
                    </id>
                </class>
            </hibernate-mapping>
            """);

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Id", loss.Property);
        Assert.Contains($"{attribute}=\"{value}\"", loss.Reason);
        Assert.Contains("<id>", loss.Reason);
        Assert.Null(loss.Category);
    }

    [Theory]
    [InlineData("version", "type=\"Int32\" generated=\"always\"", "generated=\"always\"")]
    [InlineData("version", "type=\"binary\" generated=\"never\"", "generated=\"never\"")]
    [InlineData("version", "type=\"Int32\" insert=\"false\"", "insert=\"false\"")]
    [InlineData("version", "type=\"Int32\" unsaved-value=\"0\"", "unsaved-value=\"0\"")]
    [InlineData("version", "type=\"Int32\" access=\"field\"", "access=\"field\"")]
    [InlineData("timestamp", "source=\"db\"", "source=\"db\"")]
    [InlineData("timestamp", "generated=\"always\"", "generated=\"always\"")]
    public void UnmodelledVersionAttributeLeavesARecord(string element, string attributes, string stated)
    {
        var builder = ParseMapping(Mapping(string.Empty, $"""
                    <{element} name="Revision" {attributes} />
        """));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Revision", loss.Property);
        Assert.Contains(stated, loss.Reason);
        Assert.Contains($"<{element}>", loss.Reason);
    }

    [Fact]
    public void GeneratedAlwaysOnABinaryVersionIsDerivedNotLost()
    {
        // The generated mapping writes exactly this for a binary version
        // (Combined/VersionColumnTest), so reading it back loses nothing.
        var builder = ParseMapping(Mapping(string.Empty, """
                    <version name="RowVersion" generated="always" type="binary" />
        """));

        Assert.Empty(Losses(builder));
    }

    [Theory]
    [InlineData("many-to-one", "formula", "CategoryCode")]
    [InlineData("many-to-one", "not-null", "true")]
    [InlineData("many-to-one", "not-found", "ignore")]
    [InlineData("many-to-one", "cascade", "save-update")]
    [InlineData("many-to-one", "insert", "false")]
    [InlineData("many-to-one", "update", "false")]
    [InlineData("many-to-one", "entity-name", "ProductCategory")]
    [InlineData("many-to-one", "foreign-key", "FK_Products_Categories")]
    [InlineData("many-to-one", "index", "IX_Products_Category")]
    [InlineData("many-to-one", "lazy", "false")]
    [InlineData("many-to-one", "fetch", "join")]
    [InlineData("many-to-one", "outer-join", "true")]
    [InlineData("many-to-one", "optimistic-lock", "false")]
    [InlineData("many-to-one", "access", "field")]
    [InlineData("one-to-one", "formula", "CategoryCode")]
    [InlineData("one-to-one", "cascade", "all")]
    [InlineData("one-to-one", "lazy", "proxy")]
    [InlineData("one-to-one", "fetch", "select")]
    [InlineData("one-to-one", "foreign-key", "FK_Products_Categories")]
    [InlineData("one-to-one", "access", "field")]
    public void UnmodelledReferenceAttributeLeavesARecord(string element, string attribute, string value)
    {
        var column = element == "many-to-one" ? " column=\"CategoryId\"" : string.Empty;

        var builder = ParseMapping(Mapping(string.Empty, $"""
                    <{element} name="Category" class="Category"{column} {attribute}="{value}" />
        """));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Category", loss.Property);
        Assert.Contains($"{attribute}=\"{value}\"", loss.Reason);
        Assert.Contains($"<{element}>", loss.Reason);
        Assert.Null(loss.Category);
    }

    private static string CompositeKeyMapping(string compositeAttributes, string keyPropertyAttributes, string keyReferenceAttributes, string keyReferenceBody = "") => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="OrderLine" table="OrderLines">
                <composite-id{compositeAttributes}>
                    <key-property name="LineNo" type="Int32"{keyPropertyAttributes} />
                    <key-many-to-one name="Order" class="Order"{keyReferenceAttributes}>
                        <column name="OrderId"{keyReferenceBody} />
                    </key-many-to-one>
                </composite-id>
            </class>
        </hibernate-mapping>
        """;

    // The key-many-to-one reading itself leaves a record (decision 006); these are the others.
    private static List<ConversionRecord> AttributeLosses(NHibernateEntityBuilder builder)
        => [.. Losses(builder).Where(r => r.Reason.Contains("The attribute "))];

    [Theory]
    [InlineData(" access=\"field\"", "", "", "", "<composite-id>", "access=\"field\"")]
    [InlineData(" unsaved-value=\"any\"", "", "", "", "<composite-id>", "unsaved-value=\"any\"")]
    [InlineData(" mapped=\"true\"", "", "", "", "<composite-id>", "mapped=\"true\"")]
    [InlineData("", " access=\"field\"", "", "", "<key-property>", "access=\"field\"")]
    [InlineData("", "", " lazy=\"false\"", "", "<key-many-to-one>", "lazy=\"false\"")]
    [InlineData("", "", " not-found=\"ignore\"", "", "<key-many-to-one>", "not-found=\"ignore\"")]
    [InlineData("", "", " foreign-key=\"FK_Lines_Orders\"", "", "<key-many-to-one>", "foreign-key=\"FK_Lines_Orders\"")]
    [InlineData("", "", " entity-name=\"PlacedOrder\"", "", "<key-many-to-one>", "entity-name=\"PlacedOrder\"")]
    [InlineData("", "", "", " sql-type=\"bigint\"", "<column>", "sql-type=\"bigint\"")]
    [InlineData("", "", "", " not-null=\"true\"", "<column>", "not-null=\"true\"")]
    public void UnmodelledCompositeKeyAttributeLeavesARecord(
        string composite, string keyProperty, string keyReference, string keyReferenceColumn, string element, string stated)
    {
        var builder = ParseMapping(CompositeKeyMapping(composite, keyProperty, keyReference, keyReferenceColumn));

        var loss = Assert.Single(AttributeLosses(builder));

        Assert.Contains(stated, loss.Reason);
        Assert.Contains(element, loss.Reason);
        Assert.Null(loss.Category);
    }

    [Fact]
    public void MappedBesideAMirroredKeyClassIsReadNotLost()
    {
        // class without name is the mirrored key class the model keeps (decision 011), and
        // mapped="true" says exactly that.
        var builder = ParseMapping(CompositeKeyMapping(" class=\"OrderLineId\" mapped=\"true\"", "", ""));

        Assert.Empty(AttributeLosses(builder));
    }

    private static string CollectionMapping(string collection, string collectionAttributes, string keyAttributes, string association, string keyBody = "") => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Order" table="Orders">
                <id name="Id" type="Int32">
                    <generator class="identity" />
                </id>
                <{collection} name="Lines"{collectionAttributes}>
                    <key{keyAttributes}>
                        <column name="OrderId"{keyBody} />
                    </key>
                    {association}
                </{collection}>
            </class>
        </hibernate-mapping>
        """;

    [Theory]
    [InlineData("bag", " where=\"IsDeleted = 0\"", "<bag>", "where=\"IsDeleted = 0\"")]
    [InlineData("set", " order-by=\"LineNo\"", "<set>", "order-by=\"LineNo\"")]
    [InlineData("set", " sort=\"natural\"", "<set>", "sort=\"natural\"")]
    [InlineData("bag", " subselect=\"select * from ActiveLines\"", "<bag>", "subselect=\"select * from ActiveLines\"")]
    [InlineData("bag", " catalog=\"Archive\"", "<bag>", "catalog=\"Archive\"")]
    [InlineData("bag", " lazy=\"extra\"", "<bag>", "lazy=\"extra\"")]
    [InlineData("bag", " fetch=\"subselect\"", "<bag>", "fetch=\"subselect\"")]
    [InlineData("bag", " batch-size=\"10\"", "<bag>", "batch-size=\"10\"")]
    [InlineData("bag", " mutable=\"false\"", "<bag>", "mutable=\"false\"")]
    [InlineData("bag", " access=\"field\"", "<bag>", "access=\"field\"")]
    [InlineData("bag", " collection-type=\"Shop.LineCollection\"", "<bag>", "collection-type=\"Shop.LineCollection\"")]
    public void UnmodelledCollectionAttributeLeavesARecord(string collection, string attributes, string element, string stated)
    {
        var builder = ParseMapping(CollectionMapping(collection, attributes, "", "<one-to-many class=\"OrderLine\" />"));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Lines", loss.Property);
        Assert.Contains(stated, loss.Reason);
        Assert.Contains(element, loss.Reason);
        Assert.Null(loss.Category);
    }

    [Theory]
    [InlineData(" on-delete=\"cascade\"", "", "<one-to-many class=\"OrderLine\" />", "<key>", "on-delete=\"cascade\"")]
    [InlineData(" not-null=\"true\"", "", "<one-to-many class=\"OrderLine\" />", "<key>", "not-null=\"true\"")]
    [InlineData(" property-ref=\"Number\"", "", "<one-to-many class=\"OrderLine\" />", "<key>", "property-ref=\"Number\"")]
    [InlineData(" update=\"false\"", "", "<one-to-many class=\"OrderLine\" />", "<key>", "update=\"false\"")]
    [InlineData("", " not-null=\"true\"", "<one-to-many class=\"OrderLine\" />", "<column>", "not-null=\"true\"")]
    [InlineData("", " unique-key=\"UQ_Line\"", "<one-to-many class=\"OrderLine\" />", "<column>", "unique-key=\"UQ_Line\"")]
    [InlineData("", "", "<one-to-many class=\"OrderLine\" not-found=\"ignore\" />", "<one-to-many>", "not-found=\"ignore\"")]
    [InlineData("", "", "<many-to-many class=\"Tag\" column=\"TagId\" where=\"Active = 1\" />", "<many-to-many>", "where=\"Active = 1\"")]
    [InlineData("", "", "<many-to-many class=\"Tag\" column=\"TagId\" order-by=\"Name\" />", "<many-to-many>", "order-by=\"Name\"")]
    [InlineData("", "", "<many-to-many class=\"Tag\" column=\"TagId\" unique=\"true\" />", "<many-to-many>", "unique=\"true\"")]
    [InlineData("", "", "<many-to-many class=\"Tag\"><column name=\"TagId\" length=\"10\" /></many-to-many>", "<column>", "length=\"10\"")]
    public void UnmodelledCollectionPartAttributeLeavesARecord(string keyAttributes, string keyColumn, string association, string element, string stated)
    {
        var builder = ParseMapping(CollectionMapping("bag", association.StartsWith("<many") ? " table=\"OrderTags\"" : "", keyAttributes, association, keyColumn));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Lines", loss.Property);
        Assert.Contains(stated, loss.Reason);
        Assert.Contains(element, loss.Reason);
    }

    [Fact]
    public void AUniqueKeyOnAReferenceColumnIsReadNotLost()
    {
        // unique-key on every column of a many-to-one names the navigation as a member of the
        // constraint (decision 130); the other facets of the column are lost.
        var builder = ParseMapping(Mapping(string.Empty, """
                    <many-to-one name="Category" class="Category">
                        <column name="CategoryId" unique-key="UQ_Category" length="10" />
                    </many-to-one>
        """));

        var loss = Assert.Single(Losses(builder));
        Assert.Contains("length=\"10\"", loss.Reason);
        Assert.Contains("<column>", loss.Reason);
    }

    [Theory]
    [InlineData("<property name=\"Total\" type=\"Decimal\"><formula>Quantity * UnitPrice</formula></property>", "Total", "<formula>", "<property>")]
    [InlineData("<property name=\"Code\"><type name=\"Shop.CodeType, Shop\"><param name=\"Length\">8</param></type></property>", "Code", "<type name=\"Shop.CodeType, Shop\">", "<property>")]
    [InlineData("<property name=\"Code\" type=\"String\"><meta attribute=\"field-description\">Code</meta></property>", "Code", "<meta>", "<property>")]
    [InlineData("<property name=\"Code\" type=\"String\"><column name=\"Code\"><comment>The stock code</comment></column></property>", "Code", "<comment>", "<column>")]
    [InlineData("<many-to-one name=\"Category\" class=\"Category\"><formula>CategoryCode</formula></many-to-one>", "Category", "<formula>", "<many-to-one>")]
    [InlineData("<one-to-one name=\"Detail\" class=\"Detail\"><formula>DetailId</formula></one-to-one>", "Detail", "<formula>", "<one-to-one>")]
    [InlineData("<version name=\"Revision\" type=\"Int32\"><meta attribute=\"scope-set\">private</meta></version>", "Revision", "<meta>", "<version>")]
    [InlineData("<many-to-one name=\"Category\" class=\"Category\"><column name=\"CategoryId\"><comment>Owner</comment></column></many-to-one>", "Category", "<comment>", "<column>")]
    public void UnreadChildOfAReadElementLeavesARecord(string body, string property, string child, string parent)
    {
        var builder = ParseMapping(Mapping(string.Empty, body));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal(property, loss.Property);
        Assert.Contains($"The element {child} of {parent}", loss.Reason);
        Assert.Null(loss.Category);
    }

    [Fact]
    public void UnreadChildOfTheIdentifierLeavesARecord()
    {
        var builder = ParseMapping("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Product" table="Products">
                    <id name="Id">
                        <type name="Int64" />
                        <generator class="identity" />
                    </id>
                </class>
            </hibernate-mapping>
            """);

        var loss = Assert.Single(Losses(builder));
        Assert.Equal("Id", loss.Property);
        Assert.Contains("The element <type name=\"Int64\"> of <id>", loss.Reason);
    }

    [Theory]
    [InlineData("<filter name=\"active\" condition=\"IsDeleted = 0\" />", "<filter name=\"active\">", "never restricts them")]
    [InlineData("<subselect>select * from ActiveLines</subselect>", "<subselect>", "reads the table")]
    [InlineData("<loader query-ref=\"loadLines\" />", "<loader>", "generates its own")]
    [InlineData("<sql-insert>exec InsertLine ?, ?</sql-insert>", "<sql-insert>", "generates its own")]
    [InlineData("<sql-delete-all>exec ClearLines ?</sql-delete-all>", "<sql-delete-all>", "generates its own")]
    [InlineData("<synchronize table=\"OrderLines\" />", "<synchronize>", "flushes none")]
    [InlineData("<cache usage=\"read-write\" />", "<cache>", "caching strategy")]
    [InlineData("<comment>Lines of the order</comment>", "<comment>", "does not carry")]
    public void UnreadChildOfACollectionLeavesARecord(string child, string opening, string consequence)
    {
        // The schema puts these before <key> or after the association; either way they are
        // children of the collection element, and the order does not change the reading.
        var builder = ParseMapping($"""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Order" table="Orders">
                    <id name="Id" type="Int32">
                        <generator class="identity" />
                    </id>
                    <bag name="Lines">
                        <key column="OrderId" />
                        <one-to-many class="OrderLine" />
                        {child}
                    </bag>
                </class>
            </hibernate-mapping>
            """);

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Lines", loss.Property);
        Assert.Contains($"The element {opening} of <bag>", loss.Reason);
        Assert.Contains(consequence, loss.Reason);
    }

    [Fact]
    public void EveryOccurrenceOfAnUnreadChildLeavesItsOwnRecord()
    {
        var builder = ParseMapping(CollectionMapping("bag", " table=\"OrderTags\"", "", """
            <many-to-many class="Tag" column="TagId">
                        <filter name="active" condition="Active = 1" />
                        <filter name="visible" condition="Hidden = 0" />
                    </many-to-many>
            """));

        var losses = Losses(builder);

        Assert.Equal(2, losses.Count);
        Assert.Contains("<filter name=\"active\"> of <many-to-many>", losses[0].Reason);
        Assert.Contains("<filter name=\"visible\"> of <many-to-many>", losses[1].Reason);
    }

    [Fact]
    public void AReadOnlyReferenceOverKeyColumnsIsDerivedNotLost()
    {
        // The generated mapping writes a reference over key columns read-only itself
        // (Combined/JunctionEntitySynthesisTest), so reading that back loses nothing - while
        // the same pair on a reference over a column of its own is the source's claim.
        var builder = ParseMapping("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="OrderLine" table="OrderLines">
                    <composite-id>
                        <key-property name="OrderId" type="Int32" />
                        <key-property name="LineNo" type="Int32" />
                    </composite-id>
                    <many-to-one name="Order" class="Order" column="ORDERID" insert="false" update="false" />
                    <many-to-one name="Product" class="Product" column="ProductId" insert="false" update="false" />
                </class>
            </hibernate-mapping>
            """);

        var losses = Losses(builder);

        Assert.Equal(2, losses.Count);
        Assert.All(losses, loss => Assert.Equal("Product", loss.Property));
    }

    [Fact]
    public void AReadOnlyPropertyOverAReferenceColumnIsDerivedNotLost()
    {
        // The generated mapping writes a property over a column a reference names
        // read-only itself, so the pair is derived there and claimed everywhere else.
        var builder = ParseMapping(Mapping(string.Empty, """
                    <property name="CategoryId" type="Int32" insert="false" update="false" />
                    <property name="Tax" type="Decimal" insert="false" />
                    <many-to-one name="Category" class="Category" column="CategoryId" />
        """));

        var loss = Assert.Single(Losses(builder));
        Assert.Equal("Tax", loss.Property);
    }

    [Fact]
    public void FormulaSaysThatTheOutputInventsAColumn()
    {
        var builder = ParseMapping(Mapping(string.Empty, """
                    <property name="Total" type="Decimal" formula="Quantity * UnitPrice" />
        """));

        // The property is read - what is lost is the expression behind it, not the member.
        var map = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Total");
        Assert.Null(map.ColumnName);

        var loss = Assert.Single(Losses(builder));
        Assert.Contains("Quantity * UnitPrice", loss.Reason);
        Assert.Contains("names it after the property", loss.Reason);
    }

    [Fact]
    public void WhereSaysThatTheTargetReadsTheWholeTable()
    {
        var builder = ParseMapping(Mapping(" where=\"IsDeleted = 0\"", string.Empty));

        var loss = Assert.Single(Losses(builder));
        Assert.Contains("IsDeleted = 0", loss.Reason);
        Assert.Contains("reads the whole table", loss.Reason);
    }

    [Fact]
    public void EveryStatedAttributeOfOneElementLeavesItsOwnRecord()
    {
        var builder = ParseMapping(Mapping(" mutable=\"false\" dynamic-update=\"true\"", """
                    <property name="Tax" type="Decimal" insert="false" update="false" />
        """));

        var losses = Losses(builder);
        Assert.Equal(4, losses.Count);

        Assert.Equal(2, losses.Count(r => r.Property is null));
        Assert.Equal(2, losses.Count(r => r.Property == "Tax"));
    }

    [Fact]
    public void AnAttributeTheParserReadsIsNotReportedAsDropped()
    {
        var builder = ParseMapping(Mapping(" schema=\"Warehouse\"", """
                    <property name="Name" type="String" column="ProductName" length="120" not-null="true" unique="true" />
        """));

        Assert.Empty(Losses(builder));
    }

    [Fact]
    public void AnEmptyAttributeStatesNothingAndIsNotReported()
    {
        // The source wrote the attribute without writing a value, so there is no fact to
        // lose - the same threshold ReportUnmodelledFacets applies to index, check and
        // default.
        var builder = ParseMapping(Mapping(" where=\"\"", """
                    <property name="Tax" type="Decimal" formula="" />
        """));

        Assert.Empty(Losses(builder));
    }
}
