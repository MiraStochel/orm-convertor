namespace SampleData;

/// <summary>
/// The marketplace: the showcase domain of the examples (decision 099), ten JPA entities that
/// state every mapping fact the annotations can - a key the database generates, a sequence
/// with its parameters, assigned keys, a composite key through @IdClass, a composite foreign
/// key, a self-reference, a many-to-many over @JoinTable, unique constraints over one column,
/// over two and over foreign-key columns, literal column types, precision, fractional
/// seconds, a version column and a property the database never sees.
///
/// The annotations are the specification's alone, so the same files are a Hibernate and an
/// EclipseLink source: the query tour hands them over with Hibernate, whose HQL 7.4 says the
/// most, and the long query with EclipseLink, whose JPQL is the plain specification.
///
/// The tables live in a schema of their own, Market, which the sample database of the
/// container does not have - the same reason the order book has Ordering.
/// </summary>
public static class MarketplaceSampleJpa
{
    /// <summary>An assigned key, a unique column and a literal fixed-length type.</summary>
    public const string Region = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.Table;
        import java.util.ArrayList;
        import java.util.List;

        @Entity
        @Table(name = "Regions", schema = "Market")
        public class Region {

            @Id
            @Column(name = "RegionId")
            private Integer RegionId;

            @Column(name = "Name", length = 100, nullable = false, unique = true)
            private String Name;

            @Column(name = "CountryCode", columnDefinition = "char(2)", nullable = false)
            private String CountryCode;

            @OneToMany(mappedBy = "Region")
            private List<Customer> Customers = new ArrayList<>();

            public Integer getRegionId() { return RegionId; }
            public void setRegionId(Integer value) { this.RegionId = value; }
            public String getName() { return Name; }
            public void setName(String value) { this.Name = value; }
            public String getCountryCode() { return CountryCode; }
            public void setCountryCode(String value) { this.CountryCode = value; }
            public List<Customer> getCustomers() { return Customers; }
            public void setCustomers(List<Customer> value) { this.Customers = value; }
        }
        """;

    /// <summary>
    /// A key the database generates, a named unique constraint, a national column stated by
    /// its literal type, a nullable decimal with precision, a version column and a transient
    /// property.
    /// </summary>
    public const string Customer = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.Table;
        import jakarta.persistence.Transient;
        import jakarta.persistence.UniqueConstraint;
        import jakarta.persistence.Version;
        import java.math.BigDecimal;
        import java.time.LocalDate;
        import java.util.ArrayList;
        import java.util.List;

        @Entity
        @Table(name = "Customers", schema = "Market",
               uniqueConstraints = @UniqueConstraint(name = "UQ_Customers_Email", columnNames = {"Email"}))
        public class Customer {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "CustomerId")
            private Long CustomerId;

            @Column(name = "Email", length = 254, nullable = false)
            private String Email;

            @Column(name = "DisplayName", columnDefinition = "nvarchar(200)", nullable = false)
            private String DisplayName;

            @Column(name = "RegionId", nullable = false)
            private Integer RegionId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "RegionId", insertable = false, updatable = false)
            private Region Region;

            @Column(name = "CreditLimit", precision = 12, scale = 2)
            private BigDecimal CreditLimit;

            @Column(name = "SignedUpOn", nullable = false)
            private LocalDate SignedUpOn;

            @Version
            @Column(name = "Revision")
            private int Revision;

            @OneToMany(mappedBy = "Customer")
            private List<SalesOrder> Orders = new ArrayList<>();

            @Transient
            private String Greeting;

            public Long getCustomerId() { return CustomerId; }
            public void setCustomerId(Long value) { this.CustomerId = value; }
            public String getEmail() { return Email; }
            public void setEmail(String value) { this.Email = value; }
            public String getDisplayName() { return DisplayName; }
            public void setDisplayName(String value) { this.DisplayName = value; }
            public Integer getRegionId() { return RegionId; }
            public void setRegionId(Integer value) { this.RegionId = value; }
            public Region getRegion() { return Region; }
            public void setRegion(Region value) { this.Region = value; }
            public BigDecimal getCreditLimit() { return CreditLimit; }
            public void setCreditLimit(BigDecimal value) { this.CreditLimit = value; }
            public LocalDate getSignedUpOn() { return SignedUpOn; }
            public void setSignedUpOn(LocalDate value) { this.SignedUpOn = value; }
            public int getRevision() { return Revision; }
            public void setRevision(int value) { this.Revision = value; }
            public List<SalesOrder> getOrders() { return Orders; }
            public void setOrders(List<SalesOrder> value) { this.Orders = value; }
            public String getGreeting() { return Greeting; }
            public void setGreeting(String value) { this.Greeting = value; }
        }
        """;

    /// <summary>A hierarchy: every category but the top ones names the category it belongs to.</summary>
    public const string Category = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.Table;

        @Entity
        @Table(name = "Categories", schema = "Market")
        public class Category {

            @Id
            @Column(name = "CategoryId")
            private Integer CategoryId;

            @Column(name = "Name", length = 100, nullable = false)
            private String Name;

            @Column(name = "ParentCategoryId")
            private Integer ParentCategoryId;

            @ManyToOne
            @JoinColumn(name = "ParentCategoryId", insertable = false, updatable = false)
            private Category Parent;

            public Integer getCategoryId() { return CategoryId; }
            public void setCategoryId(Integer value) { this.CategoryId = value; }
            public String getName() { return Name; }
            public void setName(String value) { this.Name = value; }
            public Integer getParentCategoryId() { return ParentCategoryId; }
            public void setParentCategoryId(Integer value) { this.ParentCategoryId = value; }
            public Category getParent() { return Parent; }
            public void setParent(Category value) { this.Parent = value; }
        }
        """;

    /// <summary>A generated key, a foreign key kept as a plain attribute beside its relation, a small decimal.</summary>
    public const string Supplier = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.Table;
        import java.math.BigDecimal;

        @Entity
        @Table(name = "Suppliers", schema = "Market")
        public class Supplier {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "SupplierId")
            private Integer SupplierId;

            @Column(name = "Name", length = 200, nullable = false)
            private String Name;

            @Column(name = "RegionId", nullable = false)
            private Integer RegionId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "RegionId", insertable = false, updatable = false)
            private Region Region;

            @Column(name = "Rating", precision = 3, scale = 2)
            private BigDecimal Rating;

            public Integer getSupplierId() { return SupplierId; }
            public void setSupplierId(Integer value) { this.SupplierId = value; }
            public String getName() { return Name; }
            public void setName(String value) { this.Name = value; }
            public Integer getRegionId() { return RegionId; }
            public void setRegionId(Integer value) { this.RegionId = value; }
            public Region getRegion() { return Region; }
            public void setRegion(Region value) { this.Region = value; }
            public BigDecimal getRating() { return Rating; }
            public void setRating(BigDecimal value) { this.Rating = value; }
        }
        """;

    /// <summary>
    /// A sequence the source names and sizes, a unique column, two foreign keys, a primitive
    /// boolean, and the owning side of the many-to-many with the tags.
    /// </summary>
    public const string Product = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.JoinTable;
        import jakarta.persistence.ManyToMany;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.SequenceGenerator;
        import jakarta.persistence.Table;
        import java.math.BigDecimal;
        import java.time.LocalDate;
        import java.util.HashSet;
        import java.util.Set;

        @Entity
        @Table(name = "Products", schema = "Market")
        public class Product {

            @Id
            @GeneratedValue(strategy = GenerationType.SEQUENCE, generator = "product_ids")
            @SequenceGenerator(name = "product_ids", sequenceName = "ProductIds", schema = "Market", allocationSize = 50)
            @Column(name = "ProductId")
            private Long ProductId;

            @Column(name = "Sku", length = 32, nullable = false, unique = true)
            private String Sku;

            @Column(name = "Name", columnDefinition = "nvarchar(200)", nullable = false)
            private String Name;

            @Column(name = "CategoryId", nullable = false)
            private Integer CategoryId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "CategoryId", insertable = false, updatable = false)
            private Category Category;

            @Column(name = "SupplierId", nullable = false)
            private Integer SupplierId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "SupplierId", insertable = false, updatable = false)
            private Supplier Supplier;

            @Column(name = "ListPrice", precision = 10, scale = 2, nullable = false)
            private BigDecimal ListPrice;

            @Column(name = "Discontinued", nullable = false)
            private boolean Discontinued;

            @Column(name = "LaunchedOn")
            private LocalDate LaunchedOn;

            @ManyToMany
            @JoinTable(name = "ProductTags", schema = "Market",
                       joinColumns = @JoinColumn(name = "ProductId"),
                       inverseJoinColumns = @JoinColumn(name = "TagId"))
            private Set<Tag> Tags = new HashSet<>();

            public Long getProductId() { return ProductId; }
            public void setProductId(Long value) { this.ProductId = value; }
            public String getSku() { return Sku; }
            public void setSku(String value) { this.Sku = value; }
            public String getName() { return Name; }
            public void setName(String value) { this.Name = value; }
            public Integer getCategoryId() { return CategoryId; }
            public void setCategoryId(Integer value) { this.CategoryId = value; }
            public Category getCategory() { return Category; }
            public void setCategory(Category value) { this.Category = value; }
            public Integer getSupplierId() { return SupplierId; }
            public void setSupplierId(Integer value) { this.SupplierId = value; }
            public Supplier getSupplier() { return Supplier; }
            public void setSupplier(Supplier value) { this.Supplier = value; }
            public BigDecimal getListPrice() { return ListPrice; }
            public void setListPrice(BigDecimal value) { this.ListPrice = value; }
            public boolean isDiscontinued() { return Discontinued; }
            public void setDiscontinued(boolean value) { this.Discontinued = value; }
            public LocalDate getLaunchedOn() { return LaunchedOn; }
            public void setLaunchedOn(LocalDate value) { this.LaunchedOn = value; }
            public Set<Tag> getTags() { return Tags; }
            public void setTags(Set<Tag> value) { this.Tags = value; }
        }
        """;

    /// <summary>The inverse side of the many-to-many, which the target receives as a junction entity.</summary>
    public const string Tag = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.ManyToMany;
        import jakarta.persistence.Table;
        import java.util.HashSet;
        import java.util.Set;

        @Entity
        @Table(name = "Tags", schema = "Market")
        public class Tag {

            @Id
            @Column(name = "TagId")
            private Integer TagId;

            @Column(name = "Label", length = 50, nullable = false, unique = true)
            private String Label;

            @ManyToMany(mappedBy = "Tags")
            private Set<Product> Products = new HashSet<>();

            public Integer getTagId() { return TagId; }
            public void setTagId(Integer value) { this.TagId = value; }
            public String getLabel() { return Label; }
            public void setLabel(String value) { this.Label = value; }
            public Set<Product> getProducts() { return Products; }
            public void setProducts(Set<Product> value) { this.Products = value; }
        }
        """;

    /// <summary>Fractional seconds, a nullable moment and the inverse side of the order lines.</summary>
    public const string SalesOrder = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.Table;
        import java.math.BigDecimal;
        import java.time.LocalDateTime;
        import java.util.ArrayList;
        import java.util.List;

        @Entity
        @Table(name = "SalesOrders", schema = "Market")
        public class SalesOrder {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "OrderId")
            private Long OrderId;

            @Column(name = "CustomerId", nullable = false)
            private Long CustomerId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "CustomerId", insertable = false, updatable = false)
            private Customer Customer;

            @Column(name = "OrderedAt", secondPrecision = 3, nullable = false)
            private LocalDateTime OrderedAt;

            @Column(name = "Status", length = 20, nullable = false)
            private String Status;

            @Column(name = "ShippedAt", secondPrecision = 3)
            private LocalDateTime ShippedAt;

            @Column(name = "ShippingCost", precision = 8, scale = 2, nullable = false)
            private BigDecimal ShippingCost;

            @OneToMany(mappedBy = "SalesOrder")
            private List<OrderLine> Lines = new ArrayList<>();

            public Long getOrderId() { return OrderId; }
            public void setOrderId(Long value) { this.OrderId = value; }
            public Long getCustomerId() { return CustomerId; }
            public void setCustomerId(Long value) { this.CustomerId = value; }
            public Customer getCustomer() { return Customer; }
            public void setCustomer(Customer value) { this.Customer = value; }
            public LocalDateTime getOrderedAt() { return OrderedAt; }
            public void setOrderedAt(LocalDateTime value) { this.OrderedAt = value; }
            public String getStatus() { return Status; }
            public void setStatus(String value) { this.Status = value; }
            public LocalDateTime getShippedAt() { return ShippedAt; }
            public void setShippedAt(LocalDateTime value) { this.ShippedAt = value; }
            public BigDecimal getShippingCost() { return ShippingCost; }
            public void setShippingCost(BigDecimal value) { this.ShippingCost = value; }
            public List<OrderLine> getLines() { return Lines; }
            public void setLines(List<OrderLine> value) { this.Lines = value; }
        }
        """;

    /// <summary>
    /// A composite key through @IdClass whose first part is at the same time the foreign key
    /// to the order, and a nullable decimal of small scale.
    /// </summary>
    public const string OrderLine = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.IdClass;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.Table;
        import java.io.Serializable;
        import java.math.BigDecimal;
        import java.util.Objects;

        @Entity
        @IdClass(OrderLine.OrderLineId.class)
        @Table(name = "OrderLines", schema = "Market")
        public class OrderLine {

            @Id
            @Column(name = "OrderId")
            private Long OrderId;

            @Id
            @Column(name = "LineNumber")
            private Short LineNumber;

            @ManyToOne(optional = false)
            @JoinColumn(name = "OrderId", referencedColumnName = "OrderId", insertable = false, updatable = false)
            private SalesOrder SalesOrder;

            @Column(name = "ProductId", nullable = false)
            private Long ProductId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "ProductId", insertable = false, updatable = false)
            private Product Product;

            @Column(name = "Quantity", nullable = false)
            private int Quantity;

            @Column(name = "UnitPrice", precision = 10, scale = 2, nullable = false)
            private BigDecimal UnitPrice;

            @Column(name = "Discount", precision = 4, scale = 3)
            private BigDecimal Discount;

            public Long getOrderId() { return OrderId; }
            public void setOrderId(Long value) { this.OrderId = value; }
            public Short getLineNumber() { return LineNumber; }
            public void setLineNumber(Short value) { this.LineNumber = value; }
            public SalesOrder getSalesOrder() { return SalesOrder; }
            public void setSalesOrder(SalesOrder value) { this.SalesOrder = value; }
            public Long getProductId() { return ProductId; }
            public void setProductId(Long value) { this.ProductId = value; }
            public Product getProduct() { return Product; }
            public void setProduct(Product value) { this.Product = value; }
            public int getQuantity() { return Quantity; }
            public void setQuantity(int value) { this.Quantity = value; }
            public BigDecimal getUnitPrice() { return UnitPrice; }
            public void setUnitPrice(BigDecimal value) { this.UnitPrice = value; }
            public BigDecimal getDiscount() { return Discount; }
            public void setDiscount(BigDecimal value) { this.Discount = value; }

            public static class OrderLineId implements Serializable {
                private Long OrderId;
                private Short LineNumber;

                @Override
                public boolean equals(Object other) {
                    return other instanceof OrderLineId that
                        && Objects.equals(OrderId, that.OrderId)
                        && Objects.equals(LineNumber, that.LineNumber);
                }

                @Override
                public int hashCode() {
                    return Objects.hash(OrderId, LineNumber);
                }
            }
        }
        """;

    /// <summary>A composite foreign key over @JoinColumns, its column pairs in the order the key states them.</summary>
    public const string Refund = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.JoinColumns;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.Table;
        import java.math.BigDecimal;
        import java.time.LocalDateTime;

        @Entity
        @Table(name = "Refunds", schema = "Market")
        public class Refund {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "RefundId")
            private Long RefundId;

            @Column(name = "OrderId", nullable = false)
            private Long OrderId;

            @Column(name = "LineNumber", nullable = false)
            private Short LineNumber;

            @ManyToOne(optional = false)
            @JoinColumns({
                @JoinColumn(name = "OrderId", referencedColumnName = "OrderId", insertable = false, updatable = false),
                @JoinColumn(name = "LineNumber", referencedColumnName = "LineNumber", insertable = false, updatable = false)
            })
            private OrderLine Line;

            @Column(name = "Amount", precision = 10, scale = 2, nullable = false)
            private BigDecimal Amount;

            @Column(name = "RefundedAt", secondPrecision = 3, nullable = false)
            private LocalDateTime RefundedAt;

            @Column(name = "Reason", length = 500)
            private String Reason;

            public Long getRefundId() { return RefundId; }
            public void setRefundId(Long value) { this.RefundId = value; }
            public Long getOrderId() { return OrderId; }
            public void setOrderId(Long value) { this.OrderId = value; }
            public Short getLineNumber() { return LineNumber; }
            public void setLineNumber(Short value) { this.LineNumber = value; }
            public OrderLine getLine() { return Line; }
            public void setLine(OrderLine value) { this.Line = value; }
            public BigDecimal getAmount() { return Amount; }
            public void setAmount(BigDecimal value) { this.Amount = value; }
            public LocalDateTime getRefundedAt() { return RefundedAt; }
            public void setRefundedAt(LocalDateTime value) { this.RefundedAt = value; }
            public String getReason() { return Reason; }
            public void setReason(String value) { this.Reason = value; }
        }
        """;

    /// <summary>
    /// A unique constraint over two foreign-key columns, which a target with no constraint of
    /// its own states on the navigations (decision 130), and a long national column.
    /// </summary>
    public const string Review = """
        package Marketplace;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.Table;
        import jakarta.persistence.UniqueConstraint;
        import java.time.LocalDateTime;

        @Entity
        @Table(name = "Reviews", schema = "Market",
               uniqueConstraints = @UniqueConstraint(name = "UQ_Reviews_ProductCustomer", columnNames = {"ProductId", "CustomerId"}))
        public class Review {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "ReviewId")
            private Long ReviewId;

            @Column(name = "ProductId", nullable = false)
            private Long ProductId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "ProductId", insertable = false, updatable = false)
            private Product Product;

            @Column(name = "CustomerId", nullable = false)
            private Long CustomerId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "CustomerId", insertable = false, updatable = false)
            private Customer Customer;

            @Column(name = "Rating", nullable = false)
            private short Rating;

            @Column(name = "Body", columnDefinition = "nvarchar(4000)")
            private String Body;

            @Column(name = "WrittenAt", secondPrecision = 3, nullable = false)
            private LocalDateTime WrittenAt;

            public Long getReviewId() { return ReviewId; }
            public void setReviewId(Long value) { this.ReviewId = value; }
            public Long getProductId() { return ProductId; }
            public void setProductId(Long value) { this.ProductId = value; }
            public Product getProduct() { return Product; }
            public void setProduct(Product value) { this.Product = value; }
            public Long getCustomerId() { return CustomerId; }
            public void setCustomerId(Long value) { this.CustomerId = value; }
            public Customer getCustomer() { return Customer; }
            public void setCustomer(Customer value) { this.Customer = value; }
            public short getRating() { return Rating; }
            public void setRating(short value) { this.Rating = value; }
            public String getBody() { return Body; }
            public void setBody(String value) { this.Body = value; }
            public LocalDateTime getWrittenAt() { return WrittenAt; }
            public void setWrittenAt(LocalDateTime value) { this.WrittenAt = value; }
        }
        """;

    /// <summary>
    /// The query tour: fourteen reports in one class, as a project keeps them, each resting on
    /// another construct of the query vocabulary - a bound pattern, a collection parameter,
    /// grouping with a filter over the groups, outer joins, a scalar subquery under a slice,
    /// quantified comparisons, a set operation, common table expressions with a ranking
    /// window, a recursive one, a list joined from a group, date arithmetic, grouping by an
    /// expression and grouping over a grouped result. Every method hands one query of HQL 7.4
    /// to the framework (decisions 109 and 111); which of them a target writes in its own
    /// language and which on its native SQL API is what the records say (decision 113).
    /// </summary>
    public const string Reports = """"
        package Marketplace;

        import jakarta.persistence.EntityManager;
        import jakarta.persistence.Tuple;
        import java.math.BigDecimal;
        import java.time.LocalDateTime;
        import java.util.List;

        public class MarketplaceReports {

            private final EntityManager em;

            public MarketplaceReports(EntityManager em) {
                this.em = em;
            }

            public List<Tuple> customersWithNamesLike(String prefix) {
                return em.createQuery("""
                        select c.CustomerId as CustomerId, upper(c.DisplayName) as Name, c.Email as Email
                        from Customer c
                        where c.DisplayName like concat(:prefix, '%')
                          and c.Email not like '%!_noreply@%' escape '!'
                        order by c.DisplayName asc
                        """, Tuple.class)
                    .setParameter("prefix", prefix)
                    .getResultList();
            }

            public List<Product> productsInCategories(List<Integer> categoryIds) {
                return em.createQuery("""
                        select p
                        from Product p
                        where p.CategoryId in :categoryIds
                          and p.Sku not in ('TEST-0', 'TEST-1')
                          and p.Discontinued = false
                        order by p.Sku asc
                        """, Product.class)
                    .setParameter("categoryIds", categoryIds)
                    .getResultList();
            }

            public List<Tuple> revenueByCategory(LocalDateTime since, BigDecimal minRevenue) {
                return em.createQuery("""
                        select cat.Name as Category, sum(ol.Quantity * ol.UnitPrice) as Revenue,
                               count(distinct o.CustomerId) as Buyers
                        from OrderLine ol
                        join SalesOrder o on o.OrderId = ol.OrderId
                        join Product p on p.ProductId = ol.ProductId
                        join Category cat on cat.CategoryId = p.CategoryId
                        where o.OrderedAt >= :since
                        group by cat.Name
                        having sum(ol.Quantity * ol.UnitPrice) >= :minRevenue
                        order by sum(ol.Quantity * ol.UnitPrice) desc
                        """, Tuple.class)
                    .setParameter("since", since)
                    .setParameter("minRevenue", minRevenue)
                    .getResultList();
            }

            public List<Tuple> customersWithoutOrders() {
                return em.createQuery("""
                        select c.CustomerId as CustomerId, c.DisplayName as Name, r.Name as Region
                        from Customer c
                        join Region r on r.RegionId = c.RegionId
                        left join SalesOrder o on o.CustomerId = c.CustomerId
                        where o.OrderId is null
                        order by c.CustomerId asc
                        """, Tuple.class)
                    .getResultList();
            }

            public List<Tuple> regionsAndSuppliers() {
                return em.createQuery("""
                        select r.Name as Region, s.Name as Supplier
                        from Region r
                        full join Supplier s on s.RegionId = r.RegionId
                        order by r.Name asc, s.Name asc
                        """, Tuple.class)
                    .getResultList();
            }

            public List<Tuple> wellReviewedProducts(int skip, int take) {
                return em.createQuery("""
                        select p.Sku as Sku, p.Name as Name,
                               (select avg(rv.Rating) from Review rv where rv.ProductId = p.ProductId) as AverageRating
                        from Product p
                        where exists (select rv2.ReviewId from Review rv2 where rv2.ProductId = p.ProductId and rv2.Rating = 5)
                        order by p.Sku asc
                        limit :take offset :skip
                        """, Tuple.class)
                    .setParameter("take", take)
                    .setParameter("skip", skip)
                    .getResultList();
            }

            public List<Tuple> pricedAboveEveryRefund() {
                return em.createQuery("""
                        select p.ProductId as ProductId, p.ListPrice as ListPrice
                        from Product p
                        where p.ListPrice > all (
                                select rf.Amount
                                from Refund rf
                                where rf.OrderId in (select ol.OrderId from OrderLine ol where ol.ProductId = p.ProductId))
                          and p.CategoryId = any (select cat.CategoryId from Category cat where cat.ParentCategoryId is not null)
                        order by p.ProductId asc
                        """, Tuple.class)
                    .getResultList();
            }

            public List<String> everyLabel() {
                return em.createQuery("""
                        select t.Label as Label from Tag t
                        union
                        select cat.Name as Label from Category cat
                        """, String.class)
                    .getResultList();
            }

            public List<Tuple> bestSellerOfEachCategory() {
                return em.createQuery("""
                        with Sales as (
                            select p.CategoryId as CategoryId, p.ProductId as ProductId, sum(ol.Quantity) as Units
                            from OrderLine ol
                            join Product p on p.ProductId = ol.ProductId
                            group by p.CategoryId, p.ProductId),
                        Ranked as (
                            select s.CategoryId as CategoryId, s.ProductId as ProductId, s.Units as Units,
                                   row_number() over (partition by s.CategoryId order by s.Units desc, s.ProductId asc) as Position
                            from Sales s)
                        select r.CategoryId as CategoryId, r.ProductId as ProductId, r.Units as Units
                        from Ranked r
                        where r.Position = 1
                        order by r.CategoryId asc
                        """, Tuple.class)
                    .getResultList();
            }

            public List<Tuple> categoriesBelow(int categoryId) {
                return em.createQuery("""
                        with Tree as (
                            select c.CategoryId as CategoryId, c.Name as Name, 0 as Depth
                            from Category c
                            where c.CategoryId = :categoryId
                            union all
                            select child.CategoryId as CategoryId, child.Name as Name, t.Depth + 1 as Depth
                            from Category child
                            join Tree t on child.ParentCategoryId = t.CategoryId)
                        select t.CategoryId as CategoryId, t.Name as Name, t.Depth as Depth
                        from Tree t
                        order by t.Depth asc, t.Name asc
                        """, Tuple.class)
                    .setParameter("categoryId", categoryId)
                    .getResultList();
            }

            public List<Tuple> skusOfEachSupplier() {
                return em.createQuery("""
                        select p.SupplierId as SupplierId, listagg(p.Sku, ', ') within group (order by p.Sku asc) as Skus
                        from Product p
                        group by p.SupplierId
                        order by p.SupplierId asc
                        """, Tuple.class)
                    .getResultList();
            }

            public List<Tuple> slowShipments(int days) {
                return em.createQuery("""
                        select o.OrderId as OrderId,
                               timestampdiff(day, o.OrderedAt, o.ShippedAt) as DaysToShip,
                               case when o.ShippingCost = 0 then 'free'
                                    when o.ShippingCost < 10 then 'reduced'
                                    else 'standard' end as ShippingBand
                        from SalesOrder o
                        where o.ShippedAt is not null
                          and timestampdiff(day, o.OrderedAt, o.ShippedAt) > :days
                        order by DaysToShip desc, o.OrderId asc
                        """, Tuple.class)
                    .setParameter("days", days)
                    .getResultList();
            }

            public List<Tuple> ordersPerYear() {
                return em.createQuery("""
                        select extract(year from o.OrderedAt) as OrderYear, count(o) as Orders,
                               round(avg(o.ShippingCost), 2) as AverageShipping
                        from SalesOrder o
                        group by extract(year from o.OrderedAt)
                        order by OrderYear asc
                        """, Tuple.class)
                    .getResultList();
            }

            public List<Tuple> loyalCustomersPerRegion(long minOrders) {
                return em.createQuery("""
                        select c.RegionId as RegionId, count(x.CustomerId) as LoyalCustomers
                        from (select o.CustomerId as CustomerId, count(o) as Orders
                              from SalesOrder o
                              group by o.CustomerId) x
                        join Customer c on c.CustomerId = x.CustomerId
                        where x.Orders >= :minOrders
                        group by c.RegionId
                        order by c.RegionId asc
                        """, Tuple.class)
                    .setParameter("minOrders", minOrders)
                    .getResultList();
            }
        }
        """";

    /// <summary>
    /// The long query: one report of plain JPQL handed to EclipseLink with a page on the
    /// query object. Seven entity joins in the outer query, one of them outer over two
    /// columns, and one more inside a subquery; thirteen aggregates in the projection, three
    /// of them over distinct values and one over a CASE; fourteen subqueries - in the filter,
    /// in the filter over the groups and in the projection -, five levels deep at the deepest,
    /// with a correlation from the fifth level back to the outermost scope; quantified and
    /// scalar comparisons, a list of values, a range, an escaped pattern and functions. Every
    /// construct is one each of the six query languages has, so every target writes the whole
    /// query in its own language - EF Core as a single LINQ chain.
    /// </summary>
    public const string Dashboard = """"
        package Marketplace;

        import jakarta.persistence.EntityManager;
        import jakarta.persistence.Tuple;
        import java.math.BigDecimal;
        import java.time.LocalDateTime;
        import java.util.List;

        public class SalesDashboard {

            private final EntityManager em;

            public SalesDashboard(EntityManager em) {
                this.em = em;
            }

            public List<Tuple> regionalChampions(LocalDateTime fromDate, LocalDateTime toDate, BigDecimal minRevenue,
                                                 long minOrders, short minRating, int skip, int take) {
                return em.createQuery("""
                        select r.Name as Region,
                               c.CustomerId as CustomerId,
                               c.DisplayName as Customer,
                               count(distinct o.OrderId) as Orders,
                               count(ol) as Lines,
                               sum(ol.Quantity) as Units,
                               sum(ol.Quantity * ol.UnitPrice) as GrossRevenue,
                               sum(case when ol.Discount is null then 0 else ol.Quantity * ol.UnitPrice * ol.Discount end) as DiscountGiven,
                               coalesce(sum(rf.Amount), 0) as Refunded,
                               avg(ol.UnitPrice) as AverageUnitPrice,
                               min(ol.UnitPrice) as CheapestUnitPrice,
                               max(ol.UnitPrice) as DearestUnitPrice,
                               min(o.OrderedAt) as FirstOrderAt,
                               max(o.OrderedAt) as LastOrderAt,
                               count(distinct cat.CategoryId) as Categories,
                               count(distinct s.SupplierId) as Suppliers,
                               (select count(rv3) from Review rv3 where rv3.CustomerId = c.CustomerId) as ReviewsWritten
                        from OrderLine ol
                        join SalesOrder o on o.OrderId = ol.OrderId
                        join Customer c on c.CustomerId = o.CustomerId
                        join Region r on r.RegionId = c.RegionId
                        join Product p on p.ProductId = ol.ProductId
                        join Category cat on cat.CategoryId = p.CategoryId
                        join Supplier s on s.SupplierId = p.SupplierId
                        left join Refund rf on rf.OrderId = ol.OrderId and rf.LineNumber = ol.LineNumber
                        where o.Status in ('SHIPPED', 'DELIVERED')
                          and o.OrderedAt between :fromDate and :toDate
                          and p.Discontinued = false
                          and length(trim(c.DisplayName)) > 2
                          and lower(c.Email) not like '%!_test@%' escape '!'
                          and cat.CategoryId in (
                              select leaf.CategoryId
                              from Category leaf
                              where leaf.ParentCategoryId in (
                                  select branch.CategoryId
                                  from Category branch
                                  where branch.ParentCategoryId is null
                                    and exists (
                                        select p2.ProductId
                                        from Product p2
                                        where p2.CategoryId = leaf.CategoryId
                                          and p2.ListPrice > (
                                              select avg(p3.ListPrice)
                                              from Product p3
                                              where p3.SupplierId in (
                                                  select s2.SupplierId
                                                  from Supplier s2
                                                  where s2.RegionId = c.RegionId
                                                    and s2.Rating >= 4)))))
                          and p.ProductId in (
                              select rv.ProductId
                              from Review rv
                              where rv.Rating >= :minRating
                                and rv.CustomerId in (
                                    select c2.CustomerId
                                    from Customer c2
                                    where c2.RegionId = c.RegionId
                                      and c2.CustomerId <> c.CustomerId))
                          and (c.CreditLimit is null or c.CreditLimit >= (
                              select avg(c3.CreditLimit)
                              from Customer c3
                              where c3.RegionId = c.RegionId))
                          and not exists (
                              select rf2.RefundId
                              from Refund rf2
                              join SalesOrder o2 on o2.OrderId = rf2.OrderId
                              where o2.CustomerId = c.CustomerId
                                and rf2.Amount > (
                                    select max(ol2.UnitPrice)
                                    from OrderLine ol2
                                    where ol2.OrderId = o2.OrderId))
                          and ol.UnitPrice > any (
                              select p4.ListPrice
                              from Product p4
                              where p4.CategoryId = p.CategoryId
                                and p4.Discontinued = true)
                        group by r.Name, c.CustomerId, c.DisplayName
                        having sum(ol.Quantity * ol.UnitPrice) > :minRevenue
                           and count(distinct o.OrderId) >= :minOrders
                           and avg(ol.UnitPrice) >= (
                               select avg(p5.ListPrice)
                               from Product p5
                               where p5.Discontinued = false
                                 and p5.CategoryId in (
                                     select cat5.CategoryId
                                     from Category cat5
                                     where cat5.ParentCategoryId is not null))
                        order by GrossRevenue desc, r.Name asc, c.CustomerId asc
                        """, Tuple.class)
                    .setParameter("fromDate", fromDate)
                    .setParameter("toDate", toDate)
                    .setParameter("minRating", minRating)
                    .setParameter("minRevenue", minRevenue)
                    .setParameter("minOrders", minOrders)
                    .setFirstResult(skip)
                    .setMaxResults(take)
                    .getResultList();
            }
        }
        """";
}
