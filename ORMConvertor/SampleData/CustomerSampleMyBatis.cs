namespace SampleData;

/// <summary>
/// The MyBatis sample (decision 084): the same Customer as the other five, in the three
/// units MyBatis keeps it in. It is the sample that states the least of all six, and that is
/// what it is here to show beside the Hibernate one - the domain class says nothing at all,
/// the mapper says which column fills which property and no more, and the table, the length,
/// the nullability and the key are nowhere, because MyBatis has no place for them.
///
/// Two of the three units are a mapping and a query at once (decision 081), and the third -
/// the interface - is the one place a MyBatis parameter's type lives, which is why
/// #{creditLimit} in the mapper can be translated into a typed parameter of the generated
/// method at all (decision 083).
/// </summary>
public static class CustomerSampleMyBatis
{
    /// <summary>
    /// The domain class, and the only one of the six samples whose entity imports nothing
    /// from its framework - not even from a specification. MyBatis reaches for the no-arg
    /// constructor and for the setters, and asks for nothing else.
    /// </summary>
    public const string Entity = """
        package MyBatisEntities;

        import java.math.BigDecimal;
        import java.time.LocalDateTime;
        import java.util.ArrayList;
        import java.util.List;

        public class Customer {

            private Integer CustomerID;
            private String CustomerName;
            private LocalDateTime AccountOpenedDate;
            private BigDecimal CreditLimit;
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
    /// The mapper interface. It carries no statement of its own here, which is the ordinary
    /// shape of a MyBatis project - the statements live in the XML mapper beside it - and it
    /// is read all the same: @Param and the declared types are where the parameters of those
    /// statements get their names and their types from.
    /// </summary>
    public const string MapperInterface = """
        package MyBatisEntities;

        import java.math.BigDecimal;
        import java.util.List;
        import org.apache.ibatis.annotations.Param;

        public interface CustomerMapper {

            List<Customer> findByCreditLimit(@Param("creditLimit") BigDecimal creditLimit);

            List<CustomerTransaction> findTransactions(@Param("customerId") Integer customerId);
        }
        """;

    /// <summary>
    /// The XML mapper, which is mapping and query in one document: a &lt;resultMap&gt; with
    /// the pairs of column and property, a reusable &lt;sql&gt; fragment, and two statements.
    /// The &lt;where&gt; and the &lt;include&gt; are the static markup the tool expands,
    /// whereas an &lt;if&gt; in their place would refuse the statement - one &lt;select&gt;
    /// would then be a family of statements and no single member of it stands for the rest
    /// (decision 084).
    /// </summary>
    public const string XmlMapper = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                "https://mybatis.org/dtd/mybatis-3-mapper.dtd">

        <mapper namespace="MyBatisEntities.CustomerMapper">

            <resultMap id="customer" type="Customer" autoMapping="false">
                <id     column="CustomerID"        property="CustomerID"/>
                <result column="CustomerName"      property="CustomerName" jdbcType="NVARCHAR"/>
                <result column="AccountOpenedDate" property="AccountOpenedDate" jdbcType="TIMESTAMP"/>
                <result column="CreditLimit"       property="CreditLimit" jdbcType="DECIMAL"/>
                <collection property="Transactions" ofType="CustomerTransaction"
                            select="findTransactions" column="CustomerID"/>
            </resultMap>

            <sql id="customerColumns">
                c.CustomerID, c.CustomerName, c.AccountOpenedDate, c.CreditLimit
            </sql>

            <select id="findByCreditLimit" resultMap="customer">
                SELECT <include refid="customerColumns"/>
                FROM Sales.Customers AS c
                <where>
                    c.CreditLimit &gt; #{creditLimit}
                </where>
                ORDER BY c.AccountOpenedDate DESC, c.CustomerName ASC
            </select>

            <select id="findTransactions" resultType="CustomerTransaction">
                SELECT t.TransactionID, t.Amount
                FROM Sales.CustomerTransactions AS t
                WHERE t.CustomerID = #{customerId}
            </select>

        </mapper>
        """;
}
