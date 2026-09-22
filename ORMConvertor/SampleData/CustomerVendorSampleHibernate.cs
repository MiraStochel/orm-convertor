namespace SampleData;

/// <summary>
/// The Customer as a Hibernate project writes it when it leans on its implementation: a key
/// whose generation it leaves to the framework, the vendor annotation for a national column,
/// a version column, and a query paged by the clause only HQL has. Both Java-to-Java
/// examples of the explanatory page start from it (decision 099), so the difference between
/// their outputs is the difference between the two targets and nothing else - the sibling
/// implementation writes out the defaults the source left unsaid (decision 080), the SQL
/// mapper keeps the pairs of column and property and turns the query into SQL (decision 084).
/// </summary>
public static class CustomerVendorSampleHibernate
{
    public const string Entity = """
        package HibernateEntities;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.Id;
        import jakarta.persistence.Table;
        import jakarta.persistence.Version;
        import java.math.BigDecimal;
        import java.time.LocalDateTime;
        import org.hibernate.annotations.Nationalized;

        @Entity
        @Table(name = "Customers", schema = "Sales")
        public class Customer {

            @Id
            @GeneratedValue
            @Column(name = "CustomerID")
            private Integer CustomerID;

            @Nationalized
            @Column(name = "CustomerName", length = 200, nullable = false)
            private String CustomerName;

            @Column(name = "AccountOpenedDate", secondPrecision = 7, nullable = false)
            private LocalDateTime AccountOpenedDate;

            @Column(name = "CreditLimit", precision = 18, scale = 2)
            private BigDecimal CreditLimit;

            @Version
            @Column(name = "Revision")
            private int Revision;

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

            public int getRevision() {
                return Revision;
            }

            public void setRevision(int value) {
                this.Revision = value;
            }
        }
        """;

    /// <summary>
    /// A page of customers above a credit limit the caller supplies. The window is written
    /// with limit and offset, which HQL has and JPQL does not (decision 060); the parameter
    /// takes its type from the mapping (decision 083).
    /// </summary>
    public const string PageQuery = """
        select c
        from Customer c
        where c.CreditLimit > :minimumCreditLimit
        order by c.CreditLimit desc, c.CustomerName asc
        limit 10 offset 20
        """;
}
