namespace SampleData;

/// <summary>
/// The EclipseLink sample (decision 080): the same Customer as the other samples, in the
/// same four units the Hibernate sample has, because the two implementations read one
/// specification and the source text of a mapping is the same for both (decision 076).
/// What is EclipseLink's own is visible in one line - the national column says so with a
/// literal type, since this implementation has no annotation for it at any level, which is
/// exactly what the sample is here to show beside the Hibernate one.
/// </summary>
public static class CustomerSampleEclipseLink
{
    public const string Entity = """
        package EclipseLinkEntities;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.Table;
        import java.math.BigDecimal;
        import java.time.LocalDateTime;
        import java.util.ArrayList;
        import java.util.List;

        @Entity
        @Table(name = "Customers", schema = "Sales")
        public class Customer {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "CustomerID")
            private Integer CustomerID;

            @Column(name = "CustomerName", length = 200, nullable = false, columnDefinition = "nvarchar(200)")
            private String CustomerName;

            @Column(name = "AccountOpenedDate", secondPrecision = 7, nullable = false)
            private LocalDateTime AccountOpenedDate;

            @Column(name = "CreditLimit", precision = 18, scale = 2, nullable = true)
            private BigDecimal CreditLimit;

            @OneToMany(mappedBy = "Customer")
            private List<CustomerTransaction> Transactions = new ArrayList<>();

            public Integer getCustomerID() {
                return CustomerID;
            }

            public void setCustomerID(Integer value) {
                this.CustomerID = value;
            }

            public String getCustomerName() {
                return CustomerName;
            }

            public void setCustomerName(String value) {
                this.CustomerName = value;
            }

            public LocalDateTime getAccountOpenedDate() {
                return AccountOpenedDate;
            }

            public void setAccountOpenedDate(LocalDateTime value) {
                this.AccountOpenedDate = value;
            }

            public BigDecimal getCreditLimit() {
                return CreditLimit;
            }

            public void setCreditLimit(BigDecimal value) {
                this.CreditLimit = value;
            }

            public List<CustomerTransaction> getTransactions() {
                return Transactions;
            }

            public void setTransactions(List<CustomerTransaction> value) {
                this.Transactions = value;
            }
        }
        """;

    /// <summary>
    /// The orm.xml descriptor for the same entity. It is the standard one, word for word
    /// the form the Hibernate sample uses: EclipseLink reads the very same document with
    /// the very same precedence (decisions 068 and 077), and its own eclipselink-orm.xml
    /// extension is outside what the tool reads.
    /// </summary>
    public const string OrmXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
            <package>EclipseLinkEntities</package>
            <entity class="Customer">
                <table name="Customers" schema="Sales"/>
                <attributes>
                    <id name="CustomerID">
                        <column name="CustomerID"/>
                        <generated-value strategy="IDENTITY"/>
                    </id>
                    <basic name="CustomerName">
                        <column name="CustomerName" length="200" nullable="false"/>
                    </basic>
                </attributes>
            </entity>
        </entity-mappings>
        """;

    /// <summary>
    /// The query in the two languages the wrapper reads (decisions 025 and 080): a Java
    /// method wrapping the JPQL in createQuery - the standard call, with no room for the
    /// createSelectionQuery of the other implementation - and the bare JPQL beside it.
    /// </summary>
    public const string Query = """"
        public static TypedQuery<Customer> query(EntityManager em) {
            return em.createQuery("""
                select c
                from Customer c
                where c.CreditLimit > 2000
                order by c.AccountOpenedDate desc, c.CustomerName asc
                """, Customer.class);
        }
        """";

    public const string JpqlQuery = """
        select c
        from Customer c
        where c.CreditLimit > 2000
        order by c.AccountOpenedDate desc, c.CustomerName asc
        """;
}
