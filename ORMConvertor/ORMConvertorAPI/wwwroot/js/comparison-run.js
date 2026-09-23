/*
 * The frozen half of the interactive comparison (decision 100): one recorded run of every
 * example of the explanatory page, exactly as the server answered it. Nothing here is
 * written by hand - the file is generated from the responses of `GET /examples` followed by
 * `POST /convert` per example, and the display names are the derivation of ui.js, so a file
 * is called here what the explanatory page calls it.
 *
 * Captured on 2026-09-23 from commit 4042360, tool version 2.0.0, against a local instance
 * with a database catalog configured (SQL Server 2025 Express holding the WideWorldImporters
 * sample database). The two whole-domain examples have schemas of their own, which that
 * catalog does not know - hence their "no table matching the entity" records (decision 099).
 *
 * The authored half - which word of the input belongs to which word of the output - is
 * `comparison-links.js`. The two files are separate because they have two different origins,
 * and the page says so. The one thing not copied verbatim is the line ending, which is a bare
 * newline here, because that is what a browser makes of any text it parses back out of a
 * code block.
 */

export const RUN = {
  "efcore-to-nhibernate": {
    "sourceOrm": 30,
    "targetOrm": 20,
    "runId": "70b48668-fabf-4e91-8a1d-7208df6e3ecd",
    "toolVersion": "2.0.0",
    "sourceFrameworkVersion": "10.0.10",
    "targetFrameworkVersion": "5.7.0",
    "targetDatabaseDialect": 10,
    "catalogState": 2,
    "catalogReadMilliseconds": 779.8936,
    "files": {
      "in/Customer.cs": {
        "side": "source",
        "name": "Customer.cs",
        "contentType": 10,
        "content": "namespace EFCoreEntities;\n\nusing Microsoft.EntityFrameworkCore;\nusing System.ComponentModel.DataAnnotations;\nusing System.ComponentModel.DataAnnotations.Schema;\n\n[Table(\"Customers\", Schema = \"Sales\")]\npublic class Customer\n{\n    [Key]\n    public required int CustomerID { get; set; }\n\n    [MaxLength(200)]\n    public required string CustomerName { get; set; }\n\n    [Column(TypeName=\"datetime2\")]\n    [Precision(7)]\n    public required DateTime AccountOpenedDate { get; set; }\n\n    [Column(TypeName=\"decimal\")]\n    [Precision(18, 2)]\n    public decimal? CreditLimit { get; set; }\n\n    public List<CustomerTransaction> Transactions { get; set; } = [];\n\n}\n"
      },
      "in/CustomerQuery.cs": {
        "side": "source",
        "name": "CustomerQuery.cs",
        "contentType": 20,
        "content": "public List<Customer> Query()\n{\n    return ctx.Customers\n       .Where(c => c.CreditLimit > 2000)\n       .Where(c => c.AccountOpenedDate > new System.DateTime(2025, 1, 1))\n       .OrderByDescending(c => c.AccountOpenedDate)\n       .ThenBy(c => c.CustomerName)\n       .ToList();\n}"
      },
      "out/Customer.cs": {
        "side": "target",
        "name": "Customer.cs",
        "contentType": 10,
        "content": "namespace EFCoreEntities;\n\npublic class Customer\n{\n    public virtual required int CustomerID { get; set; }\n\n    public virtual required string CustomerName { get; set; }\n\n    public virtual required DateTime AccountOpenedDate { get; set; }\n\n    public virtual decimal? CreditLimit { get; set; }\n\n    public virtual IList<CustomerTransaction> Transactions { get; set; } = new List<CustomerTransaction>();\n\n}\n"
      },
      "out/Customer.hbm.xml": {
        "side": "target",
        "name": "Customer.hbm.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"EFCoreEntities\">\n    <class name=\"Customer\" table=\"Customers\" schema=\"Sales\">\n        <id name=\"CustomerID\" column=\"CustomerID\" type=\"Int32\">\n            <generator class=\"native\" />\n        </id>\n        <property name=\"CustomerName\" not-null=\"true\" type=\"String\" length=\"200\" unique=\"true\" />\n        <property name=\"AccountOpenedDate\" not-null=\"true\" type=\"DateTime\" precision=\"7\" />\n        <property name=\"CreditLimit\" not-null=\"false\" type=\"Decimal\" precision=\"18\" scale=\"2\" />\n        <bag name=\"Transactions\">\n            <key column=\"CustomerID\" />\n            <one-to-many class=\"CustomerTransaction\" />\n        </bag>\n    </class>\n</hibernate-mapping>"
      },
      "out/query.cs": {
        "side": "target",
        "name": "query.cs",
        "contentType": 20,
        "content": "public static IQuery Query(ISession session)\n{\n    return session.CreateQuery(\n        \"\"\"\n        from Customer c\n        where c.CreditLimit > 2000 and c.AccountOpenedDate > '2025-01-01 00:00:00'\n        order by c.AccountOpenedDate desc, c.CustomerName asc\n        \"\"\");\n}"
      },
      "out/query.hql": {
        "side": "target",
        "name": "query.hql",
        "contentType": 50,
        "content": "from Customer c\nwhere c.CreditLimit > 2000 and c.AccountOpenedDate > '2025-01-01 00:00:00'\norder by c.AccountOpenedDate desc, c.CustomerName asc"
      }
    },
    "records": [
      {
        "kind": 5,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Integer supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type VarChar supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 6,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 5,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states length 200, the catalog column 'CustomerName' has 100. The source outranks the catalog (rule E9, decision 015), so the source value is kept."
      },
      {
        "kind": 6,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source maps the property as Timestamp, the catalog column 'AccountOpenedDate' is Date. The source outranks the catalog (rule E9, decision 015), so the source value is kept."
      },
      {
        "kind": 6,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states the strategy Auto, the catalog column 'CustomerID' is not an IDENTITY column of 'Sales.Customers'. The source outranks the catalog (rule E9, decision 015), so the source value is kept."
      },
      {
        "kind": 5,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 12,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Unique constraint 'UQ_Sales_Customers_CustomerName' (CustomerName) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 4,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 10,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The catalog states the foreign key FK_Sales_Customers_BillToCustomerID_Sales_Customers towards 'Customer', but the entity has no navigation property of that type; the relation is not generated."
      },
      {
        "kind": 4,
        "framework": 20,
        "artifact": null,
        "entity": "Customer",
        "property": "Transactions",
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Target entity 'CustomerTransaction' is not part of the conversion, so nothing can be resolved against it - a reference outside the conversion is the database catalog's case (decision 015)."
      },
      {
        "kind": 3,
        "framework": 20,
        "artifact": 30,
        "entity": "Customer",
        "property": "Transactions",
        "category": 10,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No key columns are known for the collection towards 'CustomerTransaction'; the owner's key column 'CustomerID' is written, which is the tool's fallback, not a fact of the source (decision 012)."
      }
    ]
  },
  "dapper-to-efcore": {
    "sourceOrm": 10,
    "targetOrm": 30,
    "runId": "af654fdc-55a6-4bcd-a8dc-b08c453f060c",
    "toolVersion": "2.0.0",
    "sourceFrameworkVersion": "2.1.79",
    "targetFrameworkVersion": "10.0.10",
    "targetDatabaseDialect": 10,
    "catalogState": 2,
    "catalogReadMilliseconds": 279.1413,
    "files": {
      "in/Customer.cs": {
        "side": "source",
        "name": "Customer.cs",
        "contentType": 10,
        "content": "namespace DapperEntities;\n\npublic class Customer\n{\n    public int CustomerID { get; set; }\n\n    public required string CustomerName { get; set; }\n\n    public DateTime AccountOpenedDate { get; set; }\n\n    public decimal? CreditLimit { get; set; }\n\n    public List<CustomerTransaction> Transactions { get; set; } = [];\n\n}\n"
      },
      "in/CustomerQuery.sql": {
        "side": "source",
        "name": "CustomerQuery.sql",
        "contentType": 40,
        "content": "SELECT c.CustomerName, c.CreditLimit\nFROM Sales.Customers AS c\nWHERE c.CreditLimit > 2000\nORDER BY c.AccountOpenedDate DESC"
      },
      "out/Customer.cs": {
        "side": "target",
        "name": "Customer.cs",
        "contentType": 10,
        "content": "namespace DapperEntities;\n\nusing Microsoft.EntityFrameworkCore;\nusing System.ComponentModel.DataAnnotations;\nusing System.ComponentModel.DataAnnotations.Schema;\n\n[Table(\"Customers\", Schema = \"Sales\")]\n[Index(nameof(CustomerName), IsUnique = true, Name = \"UQ_Sales_Customers_CustomerName\")]\npublic class Customer\n{\n    [Key]\n    [Column(TypeName=\"int\")]\n    public required int CustomerID { get; set; }\n\n    [Column(TypeName=\"nvarchar\")]\n    [MaxLength(100)]\n    public required string CustomerName { get; set; }\n\n    [Column(TypeName=\"date\")]\n    public required DateTime AccountOpenedDate { get; set; }\n\n    [Column(TypeName=\"decimal\")]\n    [Precision(18, 2)]\n    public decimal? CreditLimit { get; set; }\n\n    public List<CustomerTransaction> Transactions { get; set; } = [];\n\n}\n"
      },
      "out/query.cs": {
        "side": "target",
        "name": "query.cs",
        "contentType": 20,
        "content": "public static IQueryable Query(DbContext ctx)\n{\n    return ctx.Set<Customer>()\n        .Where(c => c.CreditLimit > 2000)\n        .OrderByDescending(c => c.AccountOpenedDate)\n        .Select(c => new { CustomerName = c.CustomerName, CreditLimit = c.CreditLimit });\n}"
      }
    },
    "records": [
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 1,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Table name 'Customers' supplied by the database catalog."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 2,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Schema 'Sales' supplied by the database catalog."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Integer supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Nullability (NOT NULL) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type VarChar supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 5,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Length 100 supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Nullability (NOT NULL) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Date supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Nullability (NOT NULL) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Decimal supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 6,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Precision and scale (18, 2) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Nullability (NULL) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 4,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The key column 'CustomerID' of 'Sales.Customers' is filled by a default constraint; the catalog cannot name the mechanism, so the generation strategy stays unspecified (decision 064)."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 8,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Primary key (CustomerID) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 12,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Unique constraint 'UQ_Sales_Customers_CustomerName' (CustomerName) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 4,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 10,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The catalog states the foreign key FK_Sales_Customers_BillToCustomerID_Sales_Customers towards 'Customer', but the entity has no navigation property of that type; the relation is not generated."
      },
      {
        "kind": 4,
        "framework": 30,
        "artifact": null,
        "entity": "Customer",
        "property": "Transactions",
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The element type 'CustomerTransaction' of the collection property is neither a scalar of the neutral type model, nor a collection of one, nor an entity of the conversion; the output repeats the name as the source wrote it, which a target outside the source's ecosystem need not accept (decisions 014 and 075)."
      },
      {
        "kind": 3,
        "framework": 30,
        "artifact": 10,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No generation strategy was stated; EF Core generates the value of a single-property integer or Guid key on its own, which is a convention of the target, not a fact of the source."
      }
    ]
  },
  "hibernate-to-eclipselink": {
    "sourceOrm": 40,
    "targetOrm": 50,
    "runId": "533acb24-3c7a-4bfe-8b4e-fe334ad2bc39",
    "toolVersion": "2.0.0",
    "sourceFrameworkVersion": "7.4.5.Final",
    "targetFrameworkVersion": "5.0.0",
    "targetDatabaseDialect": 10,
    "catalogState": 2,
    "catalogReadMilliseconds": 6.0927,
    "files": {
      "in/Customer.java": {
        "side": "source",
        "name": "Customer.java",
        "contentType": 60,
        "content": "package HibernateEntities;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.Version;\nimport java.math.BigDecimal;\nimport java.time.LocalDateTime;\nimport org.hibernate.annotations.Nationalized;\n\n@Entity\n@Table(name = \"Customers\", schema = \"Sales\")\npublic class Customer {\n\n    @Id\n    @GeneratedValue\n    @Column(name = \"CustomerID\")\n    private Integer CustomerID;\n\n    @Nationalized\n    @Column(name = \"CustomerName\", length = 200, nullable = false)\n    private String CustomerName;\n\n    @Column(name = \"AccountOpenedDate\", secondPrecision = 7, nullable = false)\n    private LocalDateTime AccountOpenedDate;\n\n    @Column(name = \"CreditLimit\", precision = 18, scale = 2)\n    private BigDecimal CreditLimit;\n\n    @Version\n    @Column(name = \"Revision\")\n    private int Revision;\n\n    public Integer getCustomerID() {\n        return CustomerID;\n    }\n\n    public void setCustomerID(Integer value) {\n        this.CustomerID = value;\n    }\n\n    public String getCustomerName() {\n        return CustomerName;\n    }\n\n    public void setCustomerName(String value) {\n        this.CustomerName = value;\n    }\n\n    public LocalDateTime getAccountOpenedDate() {\n        return AccountOpenedDate;\n    }\n\n    public void setAccountOpenedDate(LocalDateTime value) {\n        this.AccountOpenedDate = value;\n    }\n\n    public BigDecimal getCreditLimit() {\n        return CreditLimit;\n    }\n\n    public void setCreditLimit(BigDecimal value) {\n        this.CreditLimit = value;\n    }\n\n    public int getRevision() {\n        return Revision;\n    }\n\n    public void setRevision(int value) {\n        this.Revision = value;\n    }\n}"
      },
      "in/CustomerPage.jpql": {
        "side": "source",
        "name": "CustomerPage.jpql",
        "contentType": 80,
        "content": "select c\nfrom Customer c\nwhere c.CreditLimit > :minimumCreditLimit\norder by c.CreditLimit desc, c.CustomerName asc\nlimit 10 offset 20"
      },
      "out/Customer.java": {
        "side": "target",
        "name": "Customer.java",
        "contentType": 60,
        "content": "package HibernateEntities;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.GenerationType;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.TableGenerator;\nimport jakarta.persistence.UniqueConstraint;\nimport jakarta.persistence.Version;\nimport java.math.BigDecimal;\nimport java.time.LocalDateTime;\n\n@Entity\n@Table(name = \"Customers\", schema = \"Sales\", uniqueConstraints = { @UniqueConstraint(name = \"UQ_Sales_Customers_CustomerName\", columnNames = { \"CustomerName\" }) })\npublic class Customer {\n\n    @Id\n    @GeneratedValue(strategy = GenerationType.TABLE, generator = \"Customer_CustomerID_gen\")\n    @TableGenerator(name = \"Customer_CustomerID_gen\", table = \"SEQUENCE\", pkColumnName = \"SEQ_NAME\", valueColumnName = \"SEQ_COUNT\", pkColumnValue = \"SEQ_GEN\", allocationSize = 50)\n    @Column(name = \"CustomerID\")\n    private Integer CustomerID;\n\n    @Column(name = \"CustomerName\", length = 200, nullable = false, columnDefinition = \"nvarchar(200)\")\n    private String CustomerName;\n\n    @Column(name = \"AccountOpenedDate\", nullable = false)\n    private LocalDateTime AccountOpenedDate;\n\n    @Column(name = \"CreditLimit\", precision = 18, scale = 2, nullable = true)\n    private BigDecimal CreditLimit;\n\n    @Version\n    @Column(name = \"Revision\", nullable = false)\n    private int Revision;\n\n    public Integer getCustomerID() {\n        return CustomerID;\n    }\n\n    public void setCustomerID(Integer value) {\n        this.CustomerID = value;\n    }\n\n    public String getCustomerName() {\n        return CustomerName;\n    }\n\n    public void setCustomerName(String value) {\n        this.CustomerName = value;\n    }\n\n    public LocalDateTime getAccountOpenedDate() {\n        return AccountOpenedDate;\n    }\n\n    public void setAccountOpenedDate(LocalDateTime value) {\n        this.AccountOpenedDate = value;\n    }\n\n    public BigDecimal getCreditLimit() {\n        return CreditLimit;\n    }\n\n    public void setCreditLimit(BigDecimal value) {\n        this.CreditLimit = value;\n    }\n\n    public int getRevision() {\n        return Revision;\n    }\n\n    public void setRevision(int value) {\n        this.Revision = value;\n    }\n}\n"
      },
      "out/query.java": {
        "side": "target",
        "name": "query.java",
        "contentType": 70,
        "content": "public static TypedQuery<Customer> query(EntityManager em, BigDecimal minimumCreditLimit) {\n    return em.createQuery(\"\"\"\n        select c\n        from Customer c\n        where c.CreditLimit > :minimumCreditLimit\n        order by c.CreditLimit desc, c.CustomerName asc\n        \"\"\", Customer.class)\n        .setParameter(\"minimumCreditLimit\", minimumCreditLimit)\n        .setFirstResult(20)\n        .setMaxResults(10);\n}"
      },
      "out/query.jpql": {
        "side": "target",
        "name": "query.jpql",
        "contentType": 80,
        "content": "select c\nfrom Customer c\nwhere c.CreditLimit > :minimumCreditLimit\norder by c.CreditLimit desc, c.CustomerName asc"
      }
    },
    "records": [
      {
        "kind": 5,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Integer supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Nullability (NOT NULL) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type VarChar supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 6,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 5,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states length 200, the catalog column 'CustomerName' has 100. The source outranks the catalog (rule E9, decision 015), so the source value is kept."
      },
      {
        "kind": 5,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Date supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Decimal supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Nullability (NULL) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 4,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "Revision",
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No column of 'Sales.Customers' matches the property; the catalog cannot complete its mapping facts."
      },
      {
        "kind": 6,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states the strategy Auto, the catalog column 'CustomerID' is not an IDENTITY column of 'Sales.Customers'. The source outranks the catalog (rule E9, decision 015), so the source value is kept."
      },
      {
        "kind": 5,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 12,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Unique constraint 'UQ_Sales_Customers_CustomerName' (CustomerName) supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 4,
        "framework": 50,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 10,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The catalog states the foreign key FK_Sales_Customers_BillToCustomerID_Sales_Customers towards 'Customer', but the entity has no navigation property of that type; the relation is not generated."
      },
      {
        "kind": 3,
        "framework": 50,
        "artifact": 60,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source leaves the mechanism to the framework (Auto); EclipseLink resolves it to TABLE on the pinned dialect, and the artifact writes that mechanism out, because AUTO means something else under the other implementation (decisions 076, 077 and 080)."
      },
      {
        "kind": 2,
        "framework": 50,
        "artifact": 60,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 6,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "A date column has no fractional-second part: @Column gives precision to a decimal column and secondPrecision to a time or timestamp one, so the stated value reaches neither and is dropped (decision 079)."
      }
    ]
  },
  "hibernate-to-mybatis": {
    "sourceOrm": 40,
    "targetOrm": 60,
    "runId": "08af681e-4120-42e9-8c2b-3b810b1b653d",
    "toolVersion": "2.0.0",
    "sourceFrameworkVersion": "7.4.5.Final",
    "targetFrameworkVersion": "3.5.19",
    "targetDatabaseDialect": 10,
    "catalogState": 2,
    "catalogReadMilliseconds": 3.0894,
    "files": {
      "in/Customer.java": {
        "side": "source",
        "name": "Customer.java",
        "contentType": 60,
        "content": "package HibernateEntities;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.Version;\nimport java.math.BigDecimal;\nimport java.time.LocalDateTime;\nimport org.hibernate.annotations.Nationalized;\n\n@Entity\n@Table(name = \"Customers\", schema = \"Sales\")\npublic class Customer {\n\n    @Id\n    @GeneratedValue\n    @Column(name = \"CustomerID\")\n    private Integer CustomerID;\n\n    @Nationalized\n    @Column(name = \"CustomerName\", length = 200, nullable = false)\n    private String CustomerName;\n\n    @Column(name = \"AccountOpenedDate\", secondPrecision = 7, nullable = false)\n    private LocalDateTime AccountOpenedDate;\n\n    @Column(name = \"CreditLimit\", precision = 18, scale = 2)\n    private BigDecimal CreditLimit;\n\n    @Version\n    @Column(name = \"Revision\")\n    private int Revision;\n\n    public Integer getCustomerID() {\n        return CustomerID;\n    }\n\n    public void setCustomerID(Integer value) {\n        this.CustomerID = value;\n    }\n\n    public String getCustomerName() {\n        return CustomerName;\n    }\n\n    public void setCustomerName(String value) {\n        this.CustomerName = value;\n    }\n\n    public LocalDateTime getAccountOpenedDate() {\n        return AccountOpenedDate;\n    }\n\n    public void setAccountOpenedDate(LocalDateTime value) {\n        this.AccountOpenedDate = value;\n    }\n\n    public BigDecimal getCreditLimit() {\n        return CreditLimit;\n    }\n\n    public void setCreditLimit(BigDecimal value) {\n        this.CreditLimit = value;\n    }\n\n    public int getRevision() {\n        return Revision;\n    }\n\n    public void setRevision(int value) {\n        this.Revision = value;\n    }\n}"
      },
      "in/CustomerPage.jpql": {
        "side": "source",
        "name": "CustomerPage.jpql",
        "contentType": 80,
        "content": "select c\nfrom Customer c\nwhere c.CreditLimit > :minimumCreditLimit\norder by c.CreditLimit desc, c.CustomerName asc\nlimit 10 offset 20"
      },
      "out/Customer.java": {
        "side": "target",
        "name": "Customer.java",
        "contentType": 60,
        "content": "package HibernateEntities;\n\nimport java.math.BigDecimal;\nimport java.time.LocalDateTime;\n\npublic class Customer {\n\n    private Integer CustomerID;\n\n    private String CustomerName;\n\n    private LocalDateTime AccountOpenedDate;\n\n    private BigDecimal CreditLimit;\n\n    private int Revision;\n\n    public Integer getCustomerID() {\n        return CustomerID;\n    }\n\n    public void setCustomerID(Integer value) {\n        this.CustomerID = value;\n    }\n\n    public String getCustomerName() {\n        return CustomerName;\n    }\n\n    public void setCustomerName(String value) {\n        this.CustomerName = value;\n    }\n\n    public LocalDateTime getAccountOpenedDate() {\n        return AccountOpenedDate;\n    }\n\n    public void setAccountOpenedDate(LocalDateTime value) {\n        this.AccountOpenedDate = value;\n    }\n\n    public BigDecimal getCreditLimit() {\n        return CreditLimit;\n    }\n\n    public void setCreditLimit(BigDecimal value) {\n        this.CreditLimit = value;\n    }\n\n    public int getRevision() {\n        return Revision;\n    }\n\n    public void setRevision(int value) {\n        this.Revision = value;\n    }\n}\n"
      },
      "out/CustomerMapper.xml": {
        "side": "target",
        "name": "CustomerMapper.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\"\n        \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">\n<mapper namespace=\"HibernateEntities.CustomerMapper\">\n    <resultMap id=\"Customer\" type=\"HibernateEntities.Customer\" autoMapping=\"false\">\n        <id column=\"CustomerID\" property=\"CustomerID\" jdbcType=\"INTEGER\" />\n        <result column=\"CustomerName\" property=\"CustomerName\" jdbcType=\"NVARCHAR\" />\n        <result column=\"AccountOpenedDate\" property=\"AccountOpenedDate\" jdbcType=\"DATE\" />\n        <result column=\"CreditLimit\" property=\"CreditLimit\" jdbcType=\"DECIMAL\" />\n        <result column=\"Revision\" property=\"Revision\" />\n    </resultMap>\n</mapper>"
      },
      "out/query.java": {
        "side": "target",
        "name": "query.java",
        "contentType": 70,
        "content": "List<Customer> query(@Param(\"minimumCreditLimit\") BigDecimal minimumCreditLimit);"
      },
      "out/QueryMapper.xml": {
        "side": "target",
        "name": "QueryMapper.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\"\n        \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">\n<mapper namespace=\"HibernateEntities.QueryMapper\">\n    <select id=\"query\" resultType=\"HibernateEntities.Customer\">\n        SELECT *\n        FROM Sales.Customers AS c\n        WHERE c.CreditLimit &gt; #{minimumCreditLimit}\n        ORDER BY c.CreditLimit DESC, c.CustomerName ASC\n        OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY\n    </select>\n</mapper>"
      }
    },
    "records": [
      {
        "kind": 5,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Integer supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type VarChar supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Date supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 5,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Database type Decimal supplied by the database catalog from 'Sales.Customers'."
      },
      {
        "kind": 4,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "Revision",
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No column of 'Sales.Customers' matches the property; the catalog cannot complete its mapping facts."
      },
      {
        "kind": 6,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states the strategy Auto, the catalog column 'CustomerID' is not an IDENTITY column of 'Sales.Customers'. The source outranks the catalog (rule E9, decision 015), so the source value is kept."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 1,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states TableName and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": 2,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states SchemaName and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 5,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states Length and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 6,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states PrecisionAndScale and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 6,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states PrecisionAndScale and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states Nullability and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states Nullability and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "Revision",
        "category": 7,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states Nullability and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states PrimaryKeyStrategy and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 60,
        "artifact": null,
        "entity": "Customer",
        "property": "Revision",
        "category": 11,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states VersionColumn and the target has no way to record it; the fact is dropped from the output."
      }
    ]
  },
  "mybatis-to-dapper": {
    "sourceOrm": 60,
    "targetOrm": 10,
    "runId": "ae3d9215-f301-4205-93b5-941276397363",
    "toolVersion": "2.0.0",
    "sourceFrameworkVersion": "3.5.19",
    "targetFrameworkVersion": "2.1.79",
    "targetDatabaseDialect": 10,
    "catalogState": 1,
    "catalogReadMilliseconds": null,
    "files": {
      "in/Customer.java": {
        "side": "source",
        "name": "Customer.java",
        "contentType": 60,
        "content": "package MyBatisEntities;\n\nimport java.math.BigDecimal;\nimport java.time.LocalDateTime;\nimport java.util.ArrayList;\nimport java.util.List;\n\npublic class Customer {\n\n    private Integer CustomerID;\n    private String CustomerName;\n    private LocalDateTime AccountOpenedDate;\n    private BigDecimal CreditLimit;\n    private List<CustomerTransaction> Transactions = new ArrayList<>();\n\n    public Integer getCustomerID() {\n        return CustomerID;\n    }\n\n    public void setCustomerID(Integer value) {\n        this.CustomerID = value;\n    }\n\n    public String getCustomerName() {\n        return CustomerName;\n    }\n\n    public void setCustomerName(String value) {\n        this.CustomerName = value;\n    }\n\n    public LocalDateTime getAccountOpenedDate() {\n        return AccountOpenedDate;\n    }\n\n    public void setAccountOpenedDate(LocalDateTime value) {\n        this.AccountOpenedDate = value;\n    }\n\n    public BigDecimal getCreditLimit() {\n        return CreditLimit;\n    }\n\n    public void setCreditLimit(BigDecimal value) {\n        this.CreditLimit = value;\n    }\n\n    public List<CustomerTransaction> getTransactions() {\n        return Transactions;\n    }\n\n    public void setTransactions(List<CustomerTransaction> value) {\n        this.Transactions = value;\n    }\n}"
      },
      "in/CustomerMapper.java": {
        "side": "source",
        "name": "CustomerMapper.java",
        "contentType": 70,
        "content": "package MyBatisEntities;\n\nimport java.math.BigDecimal;\nimport java.util.List;\nimport org.apache.ibatis.annotations.Param;\n\npublic interface CustomerMapper {\n\n    List<Customer> findByCreditLimit(@Param(\"creditLimit\") BigDecimal creditLimit);\n\n    List<CustomerTransaction> findTransactions(@Param(\"customerId\") Integer customerId);\n}"
      },
      "in/CustomerMapper.xml": {
        "side": "source",
        "name": "CustomerMapper.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\"\n        \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">\n\n<mapper namespace=\"MyBatisEntities.CustomerMapper\">\n\n    <resultMap id=\"customer\" type=\"Customer\" autoMapping=\"false\">\n        <id     column=\"CustomerID\"        property=\"CustomerID\"/>\n        <result column=\"CustomerName\"      property=\"CustomerName\" jdbcType=\"NVARCHAR\"/>\n        <result column=\"AccountOpenedDate\" property=\"AccountOpenedDate\" jdbcType=\"TIMESTAMP\"/>\n        <result column=\"CreditLimit\"       property=\"CreditLimit\" jdbcType=\"DECIMAL\"/>\n        <collection property=\"Transactions\" ofType=\"CustomerTransaction\"\n                    select=\"findTransactions\" column=\"CustomerID\"/>\n    </resultMap>\n\n    <sql id=\"customerColumns\">\n        c.CustomerID, c.CustomerName, c.AccountOpenedDate, c.CreditLimit\n    </sql>\n\n    <select id=\"findByCreditLimit\" resultMap=\"customer\">\n        SELECT <include refid=\"customerColumns\"/>\n        FROM Sales.Customers AS c\n        <where>\n            c.CreditLimit &gt; #{creditLimit}\n        </where>\n        ORDER BY c.AccountOpenedDate DESC, c.CustomerName ASC\n    </select>\n\n    <select id=\"findTransactions\" resultType=\"CustomerTransaction\">\n        SELECT t.TransactionID, t.Amount\n        FROM Sales.CustomerTransactions AS t\n        WHERE t.CustomerID = #{customerId}\n    </select>\n\n</mapper>"
      },
      "out/Customer.cs": {
        "side": "target",
        "name": "Customer.cs",
        "contentType": 10,
        "content": "namespace MyBatisEntities;\n\npublic class Customer\n{\n    public int? CustomerID { get; set; }\n\n    public string? CustomerName { get; set; }\n\n    public DateTime? AccountOpenedDate { get; set; }\n\n    public decimal? CreditLimit { get; set; }\n\n    public List<CustomerTransaction>? Transactions { get; set; }\n\n}\n"
      },
      "out/query.cs": {
        "side": "target",
        "name": "query.cs",
        "contentType": 20,
        "content": "public static List<Customer> FindByCreditLimit(IDbConnection connection, decimal creditLimit)\n{\n    return connection.Query<Customer>(\n        \"\"\"\n        SELECT c.CustomerID, c.CustomerName, c.AccountOpenedDate, c.CreditLimit\n        FROM Sales.Customers AS c\n        WHERE c.CreditLimit > @creditLimit\n        ORDER BY c.AccountOpenedDate DESC, c.CustomerName ASC\n        \"\"\", new { creditLimit }).ToList();\n}"
      },
      "out/query.sql": {
        "side": "target",
        "name": "query.sql",
        "contentType": 40,
        "content": "SELECT c.CustomerID, c.CustomerName, c.AccountOpenedDate, c.CreditLimit\nFROM Sales.Customers AS c\nWHERE c.CreditLimit > @creditLimit\nORDER BY c.AccountOpenedDate DESC, c.CustomerName ASC"
      },
      "out/query-2.cs": {
        "side": "target",
        "name": "query-2.cs",
        "contentType": 20,
        "content": "public static List<CustomerTransaction> FindTransactions(IDbConnection connection, int customerId)\n{\n    return connection.Query<CustomerTransaction>(\n        \"\"\"\n        SELECT t.TransactionID, t.Amount\n        FROM Sales.CustomerTransactions AS t\n        WHERE t.CustomerID = @customerId\n        \"\"\", new { customerId }).ToList();\n}"
      },
      "out/query-2.sql": {
        "side": "target",
        "name": "query-2.sql",
        "contentType": 40,
        "content": "SELECT t.TransactionID, t.Amount\nFROM Sales.CustomerTransactions AS t\nWHERE t.CustomerID = @customerId"
      }
    },
    "records": [
      {
        "kind": 2,
        "framework": 10,
        "artifact": 60,
        "entity": "Customer",
        "property": "Transactions",
        "category": null,
        "feature": null,
        "unit": "Customer.java",
        "query": null,
        "reason": "The initializer 'new ArrayList<>()' is not a literal both languages spell alike; it was dropped."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": 30,
        "entity": "Customer",
        "property": "Transactions",
        "category": null,
        "feature": null,
        "unit": "CustomerMapper.xml",
        "query": null,
        "reason": "The navigation is filled by the statement 'findTransactions' with 'CustomerID' as its argument rather than by the columns of this result, which says how the value is loaded; the intermediate representation does not carry it and it was dropped. The relation itself is recorded, and its columns are left to a database catalog (F6)."
      },
      {
        "kind": 4,
        "framework": 10,
        "artifact": 30,
        "entity": "Customer",
        "property": null,
        "category": 8,
        "feature": null,
        "unit": "CustomerMapper.xml",
        "query": null,
        "reason": "The source marks CustomerID as the identity of a result row, which is what MyBatis means by <id> - not the primary key of the table, which MyBatis never states; the key was therefore not read and a database catalog can supply it (F6)."
      },
      {
        "kind": 4,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "Transactions",
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "Target entity 'CustomerTransaction' is not part of the conversion, so nothing can be resolved against it - a reference outside the conversion is the database catalog's case (decision 015)."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerID",
        "category": 3,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states ColumnName and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 3,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states ColumnName and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 3,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states ColumnName and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 3,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states ColumnName and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "CustomerName",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states DatabaseType and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "AccountOpenedDate",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states DatabaseType and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": null,
        "entity": "Customer",
        "property": "CreditLimit",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source states DatabaseType and the target has no way to record it; the fact is dropped from the output."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": 30,
        "entity": null,
        "property": null,
        "category": null,
        "feature": null,
        "unit": "CustomerMapper.xml",
        "query": "findByCreditLimit",
        "reason": "The statement states resultMap=\"customer\", which says how its result is mapped; the query representation does not carry it, so it was dropped and the result type is derived from the table."
      },
      {
        "kind": 3,
        "framework": 10,
        "artifact": 20,
        "entity": "Customer",
        "property": null,
        "category": null,
        "feature": 1,
        "unit": "CustomerMapper.xml",
        "query": "findByCreditLimit",
        "reason": "The result type 'Customer' was derived from the table name 'Sales.Customers'; the query does not name it."
      },
      {
        "kind": 2,
        "framework": 10,
        "artifact": 30,
        "entity": null,
        "property": null,
        "category": null,
        "feature": null,
        "unit": "CustomerMapper.xml",
        "query": "findTransactions",
        "reason": "The statement states resultType=\"CustomerTransaction\", which says how its result is mapped; the query representation does not carry it, so it was dropped and the result type is derived from the table."
      },
      {
        "kind": 3,
        "framework": 10,
        "artifact": 20,
        "entity": "CustomerTransaction",
        "property": null,
        "category": null,
        "feature": 1,
        "unit": "CustomerMapper.xml",
        "query": "findTransactions",
        "reason": "The result type 'CustomerTransaction' was derived from the table name 'Sales.CustomerTransactions'; the query does not name it."
      }
    ]
  },
  "order-book": {
    "sourceOrm": 30,
    "targetOrm": 40,
    "runId": "b5d751e9-c2be-423d-b8e3-0bec417c5037",
    "toolVersion": "2.0.0",
    "sourceFrameworkVersion": "10.0.10",
    "targetFrameworkVersion": "7.4.5.Final",
    "targetDatabaseDialect": 10,
    "catalogState": 2,
    "catalogReadMilliseconds": 287.3953,
    "files": {
      "in/Customer.cs": {
        "side": "source",
        "name": "Customer.cs",
        "contentType": 10,
        "content": "using System.ComponentModel.DataAnnotations;\nusing System.ComponentModel.DataAnnotations.Schema;\nusing Microsoft.EntityFrameworkCore;\n\nnamespace OrderBook;\n\n[Table(\"Customers\", Schema = \"Ordering\")]\n[Index(nameof(Email), IsUnique = true, Name = \"UQ_Customers_Email\")]\npublic class Customer\n{\n    [Key]\n    public int CustomerId { get; set; }\n\n    [MaxLength(100)]\n    public required string Name { get; set; }\n\n    [MaxLength(254)]\n    [Unicode(false)]\n    public required string Email { get; set; }\n\n    [Precision(18, 2)]\n    public decimal? CreditLimit { get; set; }\n\n    public DateOnly CustomerSince { get; set; }\n\n    [Timestamp]\n    public byte[] RowVersion { get; set; } = [];\n\n    [NotMapped]\n    public bool IsPreferred { get; set; }\n\n    public List<SalesOrder> Orders { get; set; } = [];\n}"
      },
      "in/SalesOrder.cs": {
        "side": "source",
        "name": "SalesOrder.cs",
        "contentType": 10,
        "content": "using System.ComponentModel.DataAnnotations;\nusing System.ComponentModel.DataAnnotations.Schema;\nusing Microsoft.EntityFrameworkCore;\n\nnamespace OrderBook;\n\n[Table(\"SalesOrders\", Schema = \"Ordering\")]\n[Index(nameof(OrderNumber), IsUnique = true)]\n[Index(nameof(OrderedAt))]\npublic class SalesOrder\n{\n    [Key]\n    public int SalesOrderId { get; set; }\n\n    [MaxLength(20)]\n    [Unicode(false)]\n    public required string OrderNumber { get; set; }\n\n    public int CustomerId { get; set; }\n\n    [ForeignKey(nameof(CustomerId))]\n    public required Customer Customer { get; set; }\n\n    [Precision(3)]\n    public DateTime OrderedAt { get; set; }\n\n    [MaxLength(12)]\n    [Unicode(false)]\n    public required string Status { get; set; }\n\n    [Column(\"ShipToCity\")]\n    [MaxLength(60)]\n    public string? City { get; set; }\n\n    public List<OrderLine> Lines { get; set; } = [];\n}"
      },
      "in/OrderLine.cs": {
        "side": "source",
        "name": "OrderLine.cs",
        "contentType": 10,
        "content": "using System.ComponentModel.DataAnnotations;\nusing System.ComponentModel.DataAnnotations.Schema;\nusing Microsoft.EntityFrameworkCore;\n\nnamespace OrderBook;\n\n[Table(\"OrderLines\", Schema = \"Ordering\")]\n[PrimaryKey(nameof(SalesOrderId), nameof(LineNumber))]\npublic class OrderLine\n{\n    public int SalesOrderId { get; set; }\n\n    public short LineNumber { get; set; }\n\n    [ForeignKey(nameof(SalesOrderId))]\n    public required SalesOrder Order { get; set; }\n\n    public int ProductId { get; set; }\n\n    [ForeignKey(nameof(ProductId))]\n    public required Product Product { get; set; }\n\n    public int Quantity { get; set; }\n\n    [Precision(18, 2)]\n    public decimal UnitPrice { get; set; }\n\n    [Precision(5, 2)]\n    public decimal DiscountPercent { get; set; }\n}"
      },
      "in/Product.cs": {
        "side": "source",
        "name": "Product.cs",
        "contentType": 10,
        "content": "using System.ComponentModel.DataAnnotations;\nusing System.ComponentModel.DataAnnotations.Schema;\nusing Microsoft.EntityFrameworkCore;\n\nnamespace OrderBook;\n\n[Table(\"Products\", Schema = \"Ordering\")]\n[Index(nameof(Sku), IsUnique = true)]\npublic class Product\n{\n    [Key]\n    [DatabaseGenerated(DatabaseGeneratedOption.None)]\n    public int ProductId { get; set; }\n\n    [MaxLength(32)]\n    [Unicode(false)]\n    public required string Sku { get; set; }\n\n    [MaxLength(200)]\n    public required string Name { get; set; }\n\n    [Precision(18, 2)]\n    public decimal ListPrice { get; set; }\n\n    public bool IsDiscontinued { get; set; }\n\n    public List<OrderLine> Lines { get; set; } = [];\n}"
      },
      "in/OpenOrders.cs": {
        "side": "source",
        "name": "OpenOrders.cs",
        "contentType": 20,
        "content": "public List<SalesOrder> OpenOrders(int customerId, DateTime placedAfter, int skip, int take)\n{\n    return ctx.SalesOrders\n        .Where(o => o.CustomerId == customerId\n                 && o.OrderedAt >= placedAfter\n                 && new[] { \"New\", \"Paid\", \"Packed\" }.Contains(o.Status))\n        .OrderByDescending(o => o.OrderedAt)\n        .ThenBy(o => o.OrderNumber)\n        .Skip(skip)\n        .Take(take)\n        .ToList();\n}"
      },
      "in/BestSellers.cs": {
        "side": "source",
        "name": "BestSellers.cs",
        "contentType": 20,
        "content": "public void BestSellers(int minimumQuantity)\n{\n    var rows = ctx.OrderLines\n        .Where(l => l.DiscountPercent < 50)\n        .GroupBy(l => l.ProductId)\n        .Where(g => g.Sum(l => l.Quantity) >= minimumQuantity)\n        .OrderBy(g => g.Key)\n        .Select(g => new\n        {\n            ProductId = g.Key,\n            Quantity = g.Sum(l => l.Quantity),\n            Lines = g.Count(),\n            HighestPrice = g.Max(l => l.UnitPrice),\n        })\n        .ToList();\n}"
      },
      "in/DormantCustomers.cs": {
        "side": "source",
        "name": "DormantCustomers.cs",
        "contentType": 20,
        "content": "public List<Customer> DormantCustomers(DateTime since)\n{\n    return ctx.Customers\n        .Where(c => (c.CreditLimit == null || c.CreditLimit > 0)\n                 && !ctx.SalesOrders.Any(o => o.CustomerId == c.CustomerId && o.OrderedAt >= since))\n        .OrderBy(c => c.Name)\n        .ToList();\n}"
      },
      "in/OrdersWithProducts.cs": {
        "side": "source",
        "name": "OrdersWithProducts.cs",
        "contentType": 20,
        "content": "public List<SalesOrder> OrdersWithProducts(List<int> productIds)\n{\n    return ctx.SalesOrders\n        .Where(o => ctx.OrderLines\n            .Where(l => productIds.Contains(l.ProductId))\n            .Select(l => l.SalesOrderId)\n            .Contains(o.SalesOrderId))\n        .OrderBy(o => o.SalesOrderId)\n        .ToList();\n}"
      },
      "in/PricedAboveAverage.cs": {
        "side": "source",
        "name": "PricedAboveAverage.cs",
        "contentType": 20,
        "content": "public List<Product> PricedAboveAverage()\n{\n    return ctx.Products\n        .Where(p => p.IsDiscontinued == false\n                 && p.ListPrice > ctx.Products\n                        .Where(x => x.IsDiscontinued == false)\n                        .Average(x => x.ListPrice))\n        .OrderByDescending(p => p.ListPrice)\n        .ToList();\n}"
      },
      "in/MailingList.cs": {
        "side": "source",
        "name": "MailingList.cs",
        "contentType": 20,
        "content": "public void MailingList(decimal minimumCreditLimit, DateOnly customerBefore)\n{\n    var addresses = ctx.Customers\n        .Where(c => c.CreditLimit >= minimumCreditLimit)\n        .Select(c => new { Email = c.Email })\n        .Union(ctx.Customers\n            .Where(c => c.CustomerSince < customerBefore)\n            .Select(c => new { Email = c.Email }))\n        .ToList();\n}"
      },
      "out/Customer.java": {
        "side": "target",
        "name": "Customer.java",
        "contentType": 60,
        "content": "package OrderBook;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.GenerationType;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.OneToMany;\nimport jakarta.persistence.SequenceGenerator;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.Transient;\nimport jakarta.persistence.UniqueConstraint;\nimport jakarta.persistence.Version;\nimport java.math.BigDecimal;\nimport java.time.LocalDate;\nimport java.util.ArrayList;\nimport java.util.List;\n\n@Entity\n@Table(name = \"Customers\", schema = \"Ordering\", uniqueConstraints = { @UniqueConstraint(name = \"UQ_Customers_Email\", columnNames = { \"Email\" }) })\npublic class Customer {\n\n    @Id\n    @GeneratedValue(strategy = GenerationType.SEQUENCE, generator = \"Customer_CustomerId_gen\")\n    @SequenceGenerator(name = \"Customer_CustomerId_gen\", sequenceName = \"Customer_SEQ\", allocationSize = 50)\n    @Column(name = \"CustomerId\")\n    private Integer CustomerId;\n\n    @Column(name = \"Name\", length = 100, nullable = false)\n    private String Name;\n\n    @Column(name = \"Email\", length = 254, nullable = false)\n    private String Email;\n\n    @Column(name = \"CreditLimit\", precision = 18, scale = 2, nullable = true)\n    private BigDecimal CreditLimit;\n\n    @Column(name = \"CustomerSince\", nullable = false)\n    private LocalDate CustomerSince;\n\n    @Version\n    @Column(name = \"RowVersion\", nullable = false)\n    private byte[] RowVersion;\n\n    @Transient\n    private boolean IsPreferred;\n\n    @OneToMany(mappedBy = \"Customer\")\n    private List<SalesOrder> Orders = new ArrayList<>();\n\n    public Integer getCustomerId() {\n        return CustomerId;\n    }\n\n    public void setCustomerId(Integer value) {\n        this.CustomerId = value;\n    }\n\n    public String getName() {\n        return Name;\n    }\n\n    public void setName(String value) {\n        this.Name = value;\n    }\n\n    public String getEmail() {\n        return Email;\n    }\n\n    public void setEmail(String value) {\n        this.Email = value;\n    }\n\n    public BigDecimal getCreditLimit() {\n        return CreditLimit;\n    }\n\n    public void setCreditLimit(BigDecimal value) {\n        this.CreditLimit = value;\n    }\n\n    public LocalDate getCustomerSince() {\n        return CustomerSince;\n    }\n\n    public void setCustomerSince(LocalDate value) {\n        this.CustomerSince = value;\n    }\n\n    public byte[] getRowVersion() {\n        return RowVersion;\n    }\n\n    public void setRowVersion(byte[] value) {\n        this.RowVersion = value;\n    }\n\n    public boolean isIsPreferred() {\n        return IsPreferred;\n    }\n\n    public void setIsPreferred(boolean value) {\n        this.IsPreferred = value;\n    }\n\n    public List<SalesOrder> getOrders() {\n        return Orders;\n    }\n\n    public void setOrders(List<SalesOrder> value) {\n        this.Orders = value;\n    }\n}\n"
      },
      "out/SalesOrder.java": {
        "side": "target",
        "name": "SalesOrder.java",
        "contentType": 60,
        "content": "package OrderBook;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.GenerationType;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.JoinColumn;\nimport jakarta.persistence.ManyToOne;\nimport jakarta.persistence.OneToMany;\nimport jakarta.persistence.SequenceGenerator;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.UniqueConstraint;\nimport java.time.LocalDateTime;\nimport java.util.ArrayList;\nimport java.util.List;\n\n@Entity\n@Table(name = \"SalesOrders\", schema = \"Ordering\", uniqueConstraints = { @UniqueConstraint(columnNames = { \"OrderNumber\" }) })\npublic class SalesOrder {\n\n    @Id\n    @GeneratedValue(strategy = GenerationType.SEQUENCE, generator = \"SalesOrder_SalesOrderId_gen\")\n    @SequenceGenerator(name = \"SalesOrder_SalesOrderId_gen\", sequenceName = \"SalesOrder_SEQ\", allocationSize = 50)\n    @Column(name = \"SalesOrderId\")\n    private Integer SalesOrderId;\n\n    @Column(name = \"OrderNumber\", length = 20, nullable = false)\n    private String OrderNumber;\n\n    @Column(name = \"CustomerId\", nullable = false, insertable = false, updatable = false)\n    private int CustomerId;\n\n    @Column(name = \"OrderedAt\", secondPrecision = 3, nullable = false)\n    private LocalDateTime OrderedAt;\n\n    @Column(name = \"Status\", length = 12, nullable = false)\n    private String Status;\n\n    @Column(name = \"ShipToCity\", length = 60, nullable = true)\n    private String City;\n\n    @ManyToOne(optional = false)\n    @JoinColumn(name = \"CustomerId\", referencedColumnName = \"CustomerId\", nullable = false)\n    private Customer Customer;\n\n    @OneToMany(mappedBy = \"Order\")\n    private List<OrderLine> Lines = new ArrayList<>();\n\n    public Integer getSalesOrderId() {\n        return SalesOrderId;\n    }\n\n    public void setSalesOrderId(Integer value) {\n        this.SalesOrderId = value;\n    }\n\n    public String getOrderNumber() {\n        return OrderNumber;\n    }\n\n    public void setOrderNumber(String value) {\n        this.OrderNumber = value;\n    }\n\n    public int getCustomerId() {\n        return CustomerId;\n    }\n\n    public void setCustomerId(int value) {\n        this.CustomerId = value;\n    }\n\n    public LocalDateTime getOrderedAt() {\n        return OrderedAt;\n    }\n\n    public void setOrderedAt(LocalDateTime value) {\n        this.OrderedAt = value;\n    }\n\n    public String getStatus() {\n        return Status;\n    }\n\n    public void setStatus(String value) {\n        this.Status = value;\n    }\n\n    public String getCity() {\n        return City;\n    }\n\n    public void setCity(String value) {\n        this.City = value;\n    }\n\n    public Customer getCustomer() {\n        return Customer;\n    }\n\n    public void setCustomer(Customer value) {\n        this.Customer = value;\n    }\n\n    public List<OrderLine> getLines() {\n        return Lines;\n    }\n\n    public void setLines(List<OrderLine> value) {\n        this.Lines = value;\n    }\n}\n"
      },
      "out/OrderLine.java": {
        "side": "target",
        "name": "OrderLine.java",
        "contentType": 60,
        "content": "package OrderBook;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.IdClass;\nimport jakarta.persistence.JoinColumn;\nimport jakarta.persistence.ManyToOne;\nimport jakarta.persistence.Table;\nimport java.io.Serializable;\nimport java.math.BigDecimal;\nimport java.util.Objects;\n\n@Entity\n@Table(name = \"OrderLines\", schema = \"Ordering\")\n@IdClass(OrderLine.OrderLineId.class)\npublic class OrderLine {\n\n    @Id\n    @Column(name = \"SalesOrderId\")\n    private Integer SalesOrderId;\n\n    @Id\n    @Column(name = \"LineNumber\")\n    private Short LineNumber;\n\n    @Column(name = \"ProductId\", nullable = false, insertable = false, updatable = false)\n    private int ProductId;\n\n    @Column(name = \"Quantity\", nullable = false)\n    private int Quantity;\n\n    @Column(name = \"UnitPrice\", precision = 18, scale = 2, nullable = false)\n    private BigDecimal UnitPrice;\n\n    @Column(name = \"DiscountPercent\", precision = 5, scale = 2, nullable = false)\n    private BigDecimal DiscountPercent;\n\n    @ManyToOne(optional = false)\n    @JoinColumn(name = \"SalesOrderId\", referencedColumnName = \"SalesOrderId\", nullable = false, insertable = false, updatable = false)\n    private SalesOrder Order;\n\n    @ManyToOne(optional = false)\n    @JoinColumn(name = \"ProductId\", referencedColumnName = \"ProductId\", nullable = false)\n    private Product Product;\n\n    public static class OrderLineId implements Serializable {\n        private Integer SalesOrderId;\n        private Short LineNumber;\n\n        public OrderLineId() {\n        }\n\n        public OrderLineId(Integer SalesOrderId, Short LineNumber) {\n            this.SalesOrderId = SalesOrderId;\n            this.LineNumber = LineNumber;\n        }\n\n        @Override\n        public boolean equals(Object obj) {\n            return obj instanceof OrderLineId other\n                && Objects.equals(SalesOrderId, other.SalesOrderId)\n                && Objects.equals(LineNumber, other.LineNumber);\n        }\n\n        @Override\n        public int hashCode() {\n            return Objects.hash(SalesOrderId, LineNumber);\n        }\n    }\n\n    public Integer getSalesOrderId() {\n        return SalesOrderId;\n    }\n\n    public void setSalesOrderId(Integer value) {\n        this.SalesOrderId = value;\n    }\n\n    public Short getLineNumber() {\n        return LineNumber;\n    }\n\n    public void setLineNumber(Short value) {\n        this.LineNumber = value;\n    }\n\n    public int getProductId() {\n        return ProductId;\n    }\n\n    public void setProductId(int value) {\n        this.ProductId = value;\n    }\n\n    public int getQuantity() {\n        return Quantity;\n    }\n\n    public void setQuantity(int value) {\n        this.Quantity = value;\n    }\n\n    public BigDecimal getUnitPrice() {\n        return UnitPrice;\n    }\n\n    public void setUnitPrice(BigDecimal value) {\n        this.UnitPrice = value;\n    }\n\n    public BigDecimal getDiscountPercent() {\n        return DiscountPercent;\n    }\n\n    public void setDiscountPercent(BigDecimal value) {\n        this.DiscountPercent = value;\n    }\n\n    public SalesOrder getOrder() {\n        return Order;\n    }\n\n    public void setOrder(SalesOrder value) {\n        this.Order = value;\n    }\n\n    public Product getProduct() {\n        return Product;\n    }\n\n    public void setProduct(Product value) {\n        this.Product = value;\n    }\n}\n"
      },
      "out/Product.java": {
        "side": "target",
        "name": "Product.java",
        "contentType": 60,
        "content": "package OrderBook;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.OneToMany;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.UniqueConstraint;\nimport java.math.BigDecimal;\nimport java.util.ArrayList;\nimport java.util.List;\n\n@Entity\n@Table(name = \"Products\", schema = \"Ordering\", uniqueConstraints = { @UniqueConstraint(columnNames = { \"Sku\" }) })\npublic class Product {\n\n    @Id\n    @Column(name = \"ProductId\")\n    private Integer ProductId;\n\n    @Column(name = \"Sku\", length = 32, nullable = false)\n    private String Sku;\n\n    @Column(name = \"Name\", length = 200, nullable = false)\n    private String Name;\n\n    @Column(name = \"ListPrice\", precision = 18, scale = 2, nullable = false)\n    private BigDecimal ListPrice;\n\n    @Column(name = \"IsDiscontinued\", nullable = false)\n    private boolean IsDiscontinued;\n\n    @OneToMany(mappedBy = \"Product\")\n    private List<OrderLine> Lines = new ArrayList<>();\n\n    public Integer getProductId() {\n        return ProductId;\n    }\n\n    public void setProductId(Integer value) {\n        this.ProductId = value;\n    }\n\n    public String getSku() {\n        return Sku;\n    }\n\n    public void setSku(String value) {\n        this.Sku = value;\n    }\n\n    public String getName() {\n        return Name;\n    }\n\n    public void setName(String value) {\n        this.Name = value;\n    }\n\n    public BigDecimal getListPrice() {\n        return ListPrice;\n    }\n\n    public void setListPrice(BigDecimal value) {\n        this.ListPrice = value;\n    }\n\n    public boolean isIsDiscontinued() {\n        return IsDiscontinued;\n    }\n\n    public void setIsDiscontinued(boolean value) {\n        this.IsDiscontinued = value;\n    }\n\n    public List<OrderLine> getLines() {\n        return Lines;\n    }\n\n    public void setLines(List<OrderLine> value) {\n        this.Lines = value;\n    }\n}\n"
      },
      "out/query.java": {
        "side": "target",
        "name": "query.java",
        "contentType": 70,
        "content": "public static TypedQuery<SalesOrder> query(EntityManager em, int customerId, LocalDateTime placedAfter, int skip, int take) {\n    return em.createQuery(\"\"\"\n        select o\n        from SalesOrder o\n        where o.CustomerId = :customerId and o.OrderedAt >= :placedAfter and o.Status in ('New', 'Paid', 'Packed')\n        order by o.OrderedAt desc, o.OrderNumber asc\n        \"\"\", SalesOrder.class)\n        .setParameter(\"customerId\", customerId)\n        .setParameter(\"placedAfter\", placedAfter)\n        .setFirstResult(skip)\n        .setMaxResults(take);\n}"
      },
      "out/query.jpql": {
        "side": "target",
        "name": "query.jpql",
        "contentType": 80,
        "content": "select o\nfrom SalesOrder o\nwhere o.CustomerId = :customerId and o.OrderedAt >= :placedAfter and o.Status in ('New', 'Paid', 'Packed')\norder by o.OrderedAt desc, o.OrderNumber asc"
      },
      "out/query-2.java": {
        "side": "target",
        "name": "query-2.java",
        "contentType": 70,
        "content": "public static Query query(EntityManager em, int minimumQuantity) {\n    return em.createQuery(\"\"\"\n        select l.ProductId as ProductId, sum(l.Quantity) as Quantity, count(l) as Lines, max(l.UnitPrice) as HighestPrice\n        from OrderLine l\n        where l.DiscountPercent < 50\n        group by l.ProductId\n        having sum(l.Quantity) >= :minimumQuantity\n        order by l.ProductId asc\n        \"\"\")\n        .setParameter(\"minimumQuantity\", minimumQuantity);\n}"
      },
      "out/query-2.jpql": {
        "side": "target",
        "name": "query-2.jpql",
        "contentType": 80,
        "content": "select l.ProductId as ProductId, sum(l.Quantity) as Quantity, count(l) as Lines, max(l.UnitPrice) as HighestPrice\nfrom OrderLine l\nwhere l.DiscountPercent < 50\ngroup by l.ProductId\nhaving sum(l.Quantity) >= :minimumQuantity\norder by l.ProductId asc"
      },
      "out/query-3.java": {
        "side": "target",
        "name": "query-3.java",
        "contentType": 70,
        "content": "public static TypedQuery<Customer> query(EntityManager em, LocalDateTime since) {\n    return em.createQuery(\"\"\"\n        select c\n        from Customer c\n        where (c.CreditLimit is null or c.CreditLimit > 0) and not (exists (select o from SalesOrder o where o.CustomerId = c.CustomerId and o.OrderedAt >= :since))\n        order by c.Name asc\n        \"\"\", Customer.class)\n        .setParameter(\"since\", since);\n}"
      },
      "out/query-3.jpql": {
        "side": "target",
        "name": "query-3.jpql",
        "contentType": 80,
        "content": "select c\nfrom Customer c\nwhere (c.CreditLimit is null or c.CreditLimit > 0) and not (exists (select o from SalesOrder o where o.CustomerId = c.CustomerId and o.OrderedAt >= :since))\norder by c.Name asc"
      },
      "out/query-4.java": {
        "side": "target",
        "name": "query-4.java",
        "contentType": 70,
        "content": "public static TypedQuery<SalesOrder> query(EntityManager em, Collection<Integer> productIds) {\n    return em.createQuery(\"\"\"\n        select o\n        from SalesOrder o\n        where o.SalesOrderId in (select l.SalesOrderId as SalesOrderId from OrderLine l where l.ProductId in :productIds)\n        order by o.SalesOrderId asc\n        \"\"\", SalesOrder.class)\n        .setParameter(\"productIds\", productIds);\n}"
      },
      "out/query-4.jpql": {
        "side": "target",
        "name": "query-4.jpql",
        "contentType": 80,
        "content": "select o\nfrom SalesOrder o\nwhere o.SalesOrderId in (select l.SalesOrderId as SalesOrderId from OrderLine l where l.ProductId in :productIds)\norder by o.SalesOrderId asc"
      },
      "out/query-5.java": {
        "side": "target",
        "name": "query-5.java",
        "contentType": 70,
        "content": "public static TypedQuery<Product> query(EntityManager em) {\n    return em.createQuery(\"\"\"\n        select p\n        from Product p\n        where p.IsDiscontinued = false and p.ListPrice > (select avg(x.ListPrice) from Product x where x.IsDiscontinued = false)\n        order by p.ListPrice desc\n        \"\"\", Product.class);\n}"
      },
      "out/query-5.jpql": {
        "side": "target",
        "name": "query-5.jpql",
        "contentType": 80,
        "content": "select p\nfrom Product p\nwhere p.IsDiscontinued = false and p.ListPrice > (select avg(x.ListPrice) from Product x where x.IsDiscontinued = false)\norder by p.ListPrice desc"
      },
      "out/query-6.java": {
        "side": "target",
        "name": "query-6.java",
        "contentType": 70,
        "content": "public static Query query(EntityManager em, BigDecimal minimumCreditLimit, LocalDate customerBefore) {\n    return em.createQuery(\"\"\"\n        select c.Email as Email from Customer c where c.CreditLimit >= :minimumCreditLimit\n        union\n        select c.Email as Email from Customer c where c.CustomerSince < :customerBefore\n        \"\"\")\n        .setParameter(\"minimumCreditLimit\", minimumCreditLimit)\n        .setParameter(\"customerBefore\", customerBefore);\n}"
      },
      "out/query-6.jpql": {
        "side": "target",
        "name": "query-6.jpql",
        "contentType": 80,
        "content": "select c.Email as Email from Customer c where c.CreditLimit >= :minimumCreditLimit\nunion\nselect c.Email as Email from Customer c where c.CustomerSince < :customerBefore"
      }
    },
    "records": [
      {
        "kind": 2,
        "framework": 30,
        "artifact": 10,
        "entity": "SalesOrder",
        "property": null,
        "category": null,
        "feature": null,
        "unit": "SalesOrder.cs",
        "query": null,
        "reason": "The index over (OrderedAt) is not unique, so it states no mapping fact the intermediate representation carries, and was dropped (decision 055)."
      },
      {
        "kind": 4,
        "framework": 40,
        "artifact": null,
        "entity": "Customer",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 4,
        "framework": 40,
        "artifact": null,
        "entity": "SalesOrder",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 4,
        "framework": 40,
        "artifact": null,
        "entity": "OrderLine",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 4,
        "framework": 40,
        "artifact": null,
        "entity": "Product",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 3,
        "framework": 40,
        "artifact": 60,
        "entity": "Customer",
        "property": "CustomerId",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source leaves the mechanism to the framework (Auto); Hibernate resolves it to SEQUENCE on the pinned dialect, and the artifact writes that mechanism out, because AUTO means something else under the other implementation (decisions 076, 077 and 080)."
      },
      {
        "kind": 2,
        "framework": 40,
        "artifact": 60,
        "entity": "Customer",
        "property": "RowVersion",
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The initializer '[]' is not a literal both languages spell alike; it was dropped."
      },
      {
        "kind": 3,
        "framework": 40,
        "artifact": 60,
        "entity": "SalesOrder",
        "property": "SalesOrderId",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source leaves the mechanism to the framework (Auto); Hibernate resolves it to SEQUENCE on the pinned dialect, and the artifact writes that mechanism out, because AUTO means something else under the other implementation (decisions 076, 077 and 080)."
      },
      {
        "kind": 3,
        "framework": 40,
        "artifact": 60,
        "entity": "OrderLine",
        "property": null,
        "category": 8,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The source recorded no key class, so the key class of the composite key is named 'OrderLineId' by convention (decision 077)."
      }
    ]
  },
  "lending-library": {
    "sourceOrm": 40,
    "targetOrm": 20,
    "runId": "fe559394-26ae-4a17-b1a6-8444baa4cf05",
    "toolVersion": "2.0.0",
    "sourceFrameworkVersion": "7.4.5.Final",
    "targetFrameworkVersion": "5.7.0",
    "targetDatabaseDialect": 10,
    "catalogState": 2,
    "catalogReadMilliseconds": 109.3194,
    "files": {
      "in/Book.java": {
        "side": "source",
        "name": "Book.java",
        "contentType": 60,
        "content": "package Library;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.GenerationType;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.JoinColumn;\nimport jakarta.persistence.JoinTable;\nimport jakarta.persistence.ManyToMany;\nimport jakarta.persistence.OneToMany;\nimport jakarta.persistence.SequenceGenerator;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.Transient;\nimport jakarta.persistence.UniqueConstraint;\nimport java.util.ArrayList;\nimport java.util.HashSet;\nimport java.util.List;\nimport java.util.Set;\nimport org.hibernate.annotations.Nationalized;\n\n@Entity\n@Table(name = \"Books\", schema = \"Lending\",\n       uniqueConstraints = @UniqueConstraint(name = \"UQ_Books_Isbn\", columnNames = {\"Isbn\"}))\npublic class Book {\n\n    @Id\n    @GeneratedValue(strategy = GenerationType.SEQUENCE, generator = \"book_numbers\")\n    @SequenceGenerator(name = \"book_numbers\", sequenceName = \"BookNumbers\", schema = \"Lending\", allocationSize = 20)\n    @Column(name = \"BookId\")\n    private Long BookId;\n\n    @Column(name = \"Isbn\", length = 13, nullable = false)\n    private String Isbn;\n\n    @Nationalized\n    @Column(name = \"Title\", length = 300, nullable = false)\n    private String Title;\n\n    @Column(name = \"PublishedYear\")\n    private short PublishedYear;\n\n    @Column(name = \"PageCount\")\n    private Integer PageCount;\n\n    @ManyToMany\n    @JoinTable(name = \"BookAuthors\", schema = \"Lending\",\n               joinColumns = @JoinColumn(name = \"BookId\"),\n               inverseJoinColumns = @JoinColumn(name = \"AuthorId\"))\n    private Set<Author> Authors = new HashSet<>();\n\n    @OneToMany(mappedBy = \"Book\")\n    private List<BookCopy> Copies = new ArrayList<>();\n\n    @Transient\n    private String DisplayTitle;\n\n    public Long getBookId() { return BookId; }\n    public void setBookId(Long value) { this.BookId = value; }\n    public String getIsbn() { return Isbn; }\n    public void setIsbn(String value) { this.Isbn = value; }\n    public String getTitle() { return Title; }\n    public void setTitle(String value) { this.Title = value; }\n    public short getPublishedYear() { return PublishedYear; }\n    public void setPublishedYear(short value) { this.PublishedYear = value; }\n    public Integer getPageCount() { return PageCount; }\n    public void setPageCount(Integer value) { this.PageCount = value; }\n    public Set<Author> getAuthors() { return Authors; }\n    public void setAuthors(Set<Author> value) { this.Authors = value; }\n    public List<BookCopy> getCopies() { return Copies; }\n    public void setCopies(List<BookCopy> value) { this.Copies = value; }\n    public String getDisplayTitle() { return DisplayTitle; }\n    public void setDisplayTitle(String value) { this.DisplayTitle = value; }\n}"
      },
      "in/Author.java": {
        "side": "source",
        "name": "Author.java",
        "contentType": 60,
        "content": "package Library;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.GenerationType;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.ManyToMany;\nimport jakarta.persistence.Table;\nimport java.util.HashSet;\nimport java.util.Set;\nimport org.hibernate.annotations.Nationalized;\n\n@Entity\n@Table(name = \"Authors\", schema = \"Lending\")\npublic class Author {\n\n    @Id\n    @GeneratedValue(strategy = GenerationType.IDENTITY)\n    @Column(name = \"AuthorId\")\n    private Integer AuthorId;\n\n    @Nationalized\n    @Column(name = \"FullName\", length = 200, nullable = false)\n    private String FullName;\n\n    @Column(name = \"Country\", length = 2, columnDefinition = \"char(2)\")\n    private String Country;\n\n    @ManyToMany(mappedBy = \"Authors\")\n    private Set<Book> Books = new HashSet<>();\n\n    public Integer getAuthorId() { return AuthorId; }\n    public void setAuthorId(Integer value) { this.AuthorId = value; }\n    public String getFullName() { return FullName; }\n    public void setFullName(String value) { this.FullName = value; }\n    public String getCountry() { return Country; }\n    public void setCountry(String value) { this.Country = value; }\n    public Set<Book> getBooks() { return Books; }\n    public void setBooks(Set<Book> value) { this.Books = value; }\n}"
      },
      "in/BookCopy.java": {
        "side": "source",
        "name": "BookCopy.java",
        "contentType": 60,
        "content": "package Library;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.IdClass;\nimport jakarta.persistence.JoinColumn;\nimport jakarta.persistence.ManyToOne;\nimport jakarta.persistence.Table;\nimport java.io.Serializable;\nimport java.time.LocalDate;\nimport java.util.Objects;\n\n@Entity\n@IdClass(BookCopy.BookCopyId.class)\n@Table(name = \"BookCopies\", schema = \"Lending\")\npublic class BookCopy {\n\n    @Id\n    @Column(name = \"BookId\")\n    private Long BookId;\n\n    @Id\n    @Column(name = \"CopyNumber\")\n    private Short CopyNumber;\n\n    @ManyToOne(optional = false)\n    @JoinColumn(name = \"BookId\", referencedColumnName = \"BookId\", insertable = false, updatable = false)\n    private Book Book;\n\n    @Column(name = \"AcquiredOn\", nullable = false)\n    private LocalDate AcquiredOn;\n\n    @Column(name = \"ShelfMark\", length = 20)\n    private String ShelfMark;\n\n    public Long getBookId() { return BookId; }\n    public void setBookId(Long value) { this.BookId = value; }\n    public Short getCopyNumber() { return CopyNumber; }\n    public void setCopyNumber(Short value) { this.CopyNumber = value; }\n    public Book getBook() { return Book; }\n    public void setBook(Book value) { this.Book = value; }\n    public LocalDate getAcquiredOn() { return AcquiredOn; }\n    public void setAcquiredOn(LocalDate value) { this.AcquiredOn = value; }\n    public String getShelfMark() { return ShelfMark; }\n    public void setShelfMark(String value) { this.ShelfMark = value; }\n\n    public static class BookCopyId implements Serializable {\n        private Long BookId;\n        private Short CopyNumber;\n\n        @Override\n        public boolean equals(Object other) {\n            return other instanceof BookCopyId that\n                && Objects.equals(BookId, that.BookId)\n                && Objects.equals(CopyNumber, that.CopyNumber);\n        }\n\n        @Override\n        public int hashCode() {\n            return Objects.hash(BookId, CopyNumber);\n        }\n    }\n}"
      },
      "in/Loan.java": {
        "side": "source",
        "name": "Loan.java",
        "contentType": 60,
        "content": "package Library;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.GenerationType;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.JoinColumn;\nimport jakarta.persistence.JoinColumns;\nimport jakarta.persistence.ManyToOne;\nimport jakarta.persistence.Table;\nimport jakarta.persistence.Version;\nimport java.time.LocalDate;\nimport java.time.LocalDateTime;\n\n@Entity\n@Table(name = \"Loans\", schema = \"Lending\")\npublic class Loan {\n\n    @Id\n    @GeneratedValue(strategy = GenerationType.IDENTITY)\n    @Column(name = \"LoanId\")\n    private Long LoanId;\n\n    @Column(name = \"BookId\", nullable = false)\n    private Long BookId;\n\n    @Column(name = \"CopyNumber\", nullable = false)\n    private Short CopyNumber;\n\n    @ManyToOne(optional = false)\n    @JoinColumns({\n        @JoinColumn(name = \"BookId\", referencedColumnName = \"BookId\", insertable = false, updatable = false),\n        @JoinColumn(name = \"CopyNumber\", referencedColumnName = \"CopyNumber\", insertable = false, updatable = false)\n    })\n    private BookCopy Copy;\n\n    @Column(name = \"MemberId\", nullable = false)\n    private Integer MemberId;\n\n    @ManyToOne(optional = false)\n    @JoinColumn(name = \"MemberId\", referencedColumnName = \"MemberId\", insertable = false, updatable = false)\n    private Member Member;\n\n    @Column(name = \"LoanedAt\", secondPrecision = 3, nullable = false)\n    private LocalDateTime LoanedAt;\n\n    @Column(name = \"DueOn\", nullable = false)\n    private LocalDate DueOn;\n\n    @Column(name = \"ReturnedAt\", secondPrecision = 3)\n    private LocalDateTime ReturnedAt;\n\n    @Version\n    @Column(name = \"Revision\")\n    private int Revision;\n\n    public Long getLoanId() { return LoanId; }\n    public void setLoanId(Long value) { this.LoanId = value; }\n    public Long getBookId() { return BookId; }\n    public void setBookId(Long value) { this.BookId = value; }\n    public Short getCopyNumber() { return CopyNumber; }\n    public void setCopyNumber(Short value) { this.CopyNumber = value; }\n    public BookCopy getCopy() { return Copy; }\n    public void setCopy(BookCopy value) { this.Copy = value; }\n    public Integer getMemberId() { return MemberId; }\n    public void setMemberId(Integer value) { this.MemberId = value; }\n    public Member getMember() { return Member; }\n    public void setMember(Member value) { this.Member = value; }\n    public LocalDateTime getLoanedAt() { return LoanedAt; }\n    public void setLoanedAt(LocalDateTime value) { this.LoanedAt = value; }\n    public LocalDate getDueOn() { return DueOn; }\n    public void setDueOn(LocalDate value) { this.DueOn = value; }\n    public LocalDateTime getReturnedAt() { return ReturnedAt; }\n    public void setReturnedAt(LocalDateTime value) { this.ReturnedAt = value; }\n    public int getRevision() { return Revision; }\n    public void setRevision(int value) { this.Revision = value; }\n}"
      },
      "in/Member.java": {
        "side": "source",
        "name": "Member.java",
        "contentType": 60,
        "content": "package Library;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.GeneratedValue;\nimport jakarta.persistence.GenerationType;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.OneToMany;\nimport jakarta.persistence.Table;\nimport java.time.LocalDate;\nimport java.util.ArrayList;\nimport java.util.List;\nimport org.hibernate.annotations.Nationalized;\n\n@Entity\n@Table(name = \"Members\", schema = \"Lending\")\npublic class Member {\n\n    @Id\n    @GeneratedValue(strategy = GenerationType.IDENTITY)\n    @Column(name = \"MemberId\")\n    private Integer MemberId;\n\n    @Nationalized\n    @Column(name = \"GivenName\", length = 100, nullable = false)\n    private String GivenName;\n\n    @Nationalized\n    @Column(name = \"Surname\", length = 100, nullable = false)\n    private String Surname;\n\n    @Column(name = \"Email\", length = 254, nullable = false, unique = true)\n    private String Email;\n\n    @Column(name = \"JoinedOn\", nullable = false)\n    private LocalDate JoinedOn;\n\n    @OneToMany(mappedBy = \"Member\")\n    private List<Loan> Loans = new ArrayList<>();\n\n    public Integer getMemberId() { return MemberId; }\n    public void setMemberId(Integer value) { this.MemberId = value; }\n    public String getGivenName() { return GivenName; }\n    public void setGivenName(String value) { this.GivenName = value; }\n    public String getSurname() { return Surname; }\n    public void setSurname(String value) { this.Surname = value; }\n    public String getEmail() { return Email; }\n    public void setEmail(String value) { this.Email = value; }\n    public LocalDate getJoinedOn() { return JoinedOn; }\n    public void setJoinedOn(LocalDate value) { this.JoinedOn = value; }\n    public List<Loan> getLoans() { return Loans; }\n    public void setLoans(List<Loan> value) { this.Loans = value; }\n}"
      },
      "in/OverdueLoans.jpql": {
        "side": "source",
        "name": "OverdueLoans.jpql",
        "contentType": 80,
        "content": "select m.Surname as Surname, m.Email as Email, l.DueOn as DueOn\nfrom Loan l\njoin Member m on m.MemberId = l.MemberId\nwhere l.ReturnedAt is null and l.DueOn < :today\norder by l.DueOn asc, m.Surname asc"
      },
      "in/ActiveMembers.jpql": {
        "side": "source",
        "name": "ActiveMembers.jpql",
        "contentType": 80,
        "content": "select l.MemberId as MemberId, count(l) as Loans\nfrom Loan l\nwhere l.LoanedAt >= :since\ngroup by l.MemberId\nhaving count(l) >= :minimumLoans\norder by l.MemberId asc"
      },
      "in/NeverBorrowed.jpql": {
        "side": "source",
        "name": "NeverBorrowed.jpql",
        "contentType": 80,
        "content": "select b\nfrom Book b\nwhere not exists (select l from Loan l where l.BookId = b.BookId)\n  and b.PageCount > (select avg(x.PageCount) from Book x)\norder by b.Title asc"
      },
      "in/CopiesAcquired.jpql": {
        "side": "source",
        "name": "CopiesAcquired.jpql",
        "contentType": 80,
        "content": "select c\nfrom BookCopy c\nwhere c.BookId in :bookIds and c.AcquiredOn between :fromDate and :toDate\norder by c.BookId asc, c.CopyNumber asc\nlimit :take offset :skip"
      },
      "in/AuthorsFromRegion.jpql": {
        "side": "source",
        "name": "AuthorsFromRegion.jpql",
        "contentType": 80,
        "content": "select distinct a.FullName as FullName, a.Country as Country\nfrom Author a\nwhere a.Country in ('CZ', 'SK', 'PL')\norder by a.FullName asc"
      },
      "out/Book.cs": {
        "side": "target",
        "name": "Book.cs",
        "contentType": 10,
        "content": "namespace Library;\n\npublic class Book\n{\n    public virtual long BookId { get; set; }\n\n    public virtual string Isbn { get; set; }\n\n    public virtual string Title { get; set; }\n\n    public virtual short PublishedYear { get; set; }\n\n    public virtual int? PageCount { get; set; }\n\n    public virtual string? DisplayTitle { get; set; }\n\n    public virtual ISet<BookAuthor> Authors { get; set; }\n\n    public virtual IList<BookCopy> Copies { get; set; }\n\n}\n"
      },
      "out/Book.hbm.xml": {
        "side": "target",
        "name": "Book.hbm.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"Library\">\n    <class name=\"Book\" table=\"Books\" schema=\"Lending\">\n        <id name=\"BookId\" column=\"BookId\" type=\"Int64\">\n            <generator class=\"sequence\">\n                <param name=\"sequence\">Lending.BookNumbers</param>\n            </generator>\n        </id>\n        <property name=\"Isbn\" column=\"Isbn\" not-null=\"true\" length=\"13\" unique=\"true\" />\n        <property name=\"Title\" column=\"Title\" not-null=\"true\" length=\"300\" />\n        <property name=\"PublishedYear\" column=\"PublishedYear\" not-null=\"true\" />\n        <property name=\"PageCount\" column=\"PageCount\" />\n        <set name=\"Authors\" inverse=\"true\">\n            <key column=\"BookId\" />\n            <one-to-many class=\"BookAuthor\" />\n        </set>\n        <bag name=\"Copies\" inverse=\"true\">\n            <key column=\"BookId\" />\n            <one-to-many class=\"BookCopy\" />\n        </bag>\n    </class>\n</hibernate-mapping>"
      },
      "out/Author.cs": {
        "side": "target",
        "name": "Author.cs",
        "contentType": 10,
        "content": "namespace Library;\n\npublic class Author\n{\n    public virtual int AuthorId { get; set; }\n\n    public virtual string FullName { get; set; }\n\n    public virtual string? Country { get; set; }\n\n    public virtual ISet<BookAuthor> Books { get; set; }\n\n}\n"
      },
      "out/Author.hbm.xml": {
        "side": "target",
        "name": "Author.hbm.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"Library\">\n    <class name=\"Author\" table=\"Authors\" schema=\"Lending\">\n        <id name=\"AuthorId\" column=\"AuthorId\" type=\"Int32\">\n            <generator class=\"identity\" />\n        </id>\n        <property name=\"FullName\" column=\"FullName\" not-null=\"true\" length=\"200\" />\n        <property name=\"Country\" type=\"AnsiString\">\n            <column name=\"Country\" length=\"2\" sql-type=\"char(2)\" />\n        </property>\n        <set name=\"Books\" inverse=\"true\">\n            <key column=\"AuthorId\" />\n            <one-to-many class=\"BookAuthor\" />\n        </set>\n    </class>\n</hibernate-mapping>"
      },
      "out/BookCopy.cs": {
        "side": "target",
        "name": "BookCopy.cs",
        "contentType": 10,
        "content": "using System;\n\nnamespace Library;\n\n[Serializable]\npublic class BookCopy\n{\n    public virtual long BookId { get; set; }\n\n    public virtual short CopyNumber { get; set; }\n\n    public virtual DateOnly AcquiredOn { get; set; }\n\n    public virtual string? ShelfMark { get; set; }\n\n    public virtual Book Book { get; set; }\n\n    public override bool Equals(object? obj)\n    {\n        if (ReferenceEquals(this, obj))\n        {\n            return true;\n        }\n\n        if (obj is not BookCopy other)\n        {\n            return false;\n        }\n\n        return Equals(BookId, other.BookId)\n            && Equals(CopyNumber, other.CopyNumber);\n    }\n\n    public override int GetHashCode()\n    {\n        return HashCode.Combine(BookId, CopyNumber);\n    }\n\n}\n"
      },
      "out/BookCopy.hbm.xml": {
        "side": "target",
        "name": "BookCopy.hbm.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"Library\">\n    <class name=\"BookCopy\" table=\"BookCopies\" schema=\"Lending\">\n        <composite-id>\n            <key-property name=\"BookId\" column=\"BookId\" type=\"Int64\" />\n            <key-property name=\"CopyNumber\" column=\"CopyNumber\" type=\"Int16\" />\n        </composite-id>\n        <property name=\"AcquiredOn\" column=\"AcquiredOn\" not-null=\"true\" />\n        <property name=\"ShelfMark\" column=\"ShelfMark\" length=\"20\" />\n        <many-to-one name=\"Book\" class=\"Book\" column=\"BookId\" insert=\"false\" update=\"false\" />\n    </class>\n</hibernate-mapping>"
      },
      "out/Loan.cs": {
        "side": "target",
        "name": "Loan.cs",
        "contentType": 10,
        "content": "namespace Library;\n\npublic class Loan\n{\n    public virtual long LoanId { get; set; }\n\n    public virtual int Revision { get; set; }\n\n    public virtual long BookId { get; set; }\n\n    public virtual short CopyNumber { get; set; }\n\n    public virtual int MemberId { get; set; }\n\n    public virtual DateTime LoanedAt { get; set; }\n\n    public virtual DateOnly DueOn { get; set; }\n\n    public virtual DateTime? ReturnedAt { get; set; }\n\n    public virtual BookCopy Copy { get; set; }\n\n    public virtual Member Member { get; set; }\n\n}\n"
      },
      "out/Loan.hbm.xml": {
        "side": "target",
        "name": "Loan.hbm.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"Library\">\n    <class name=\"Loan\" table=\"Loans\" schema=\"Lending\">\n        <id name=\"LoanId\" column=\"LoanId\" type=\"Int64\">\n            <generator class=\"identity\" />\n        </id>\n        <version name=\"Revision\">\n            <column name=\"Revision\" not-null=\"true\" />\n        </version>\n        <property name=\"BookId\" insert=\"false\" update=\"false\" column=\"BookId\" not-null=\"true\" />\n        <property name=\"CopyNumber\" insert=\"false\" update=\"false\" column=\"CopyNumber\" not-null=\"true\" />\n        <property name=\"MemberId\" insert=\"false\" update=\"false\" column=\"MemberId\" not-null=\"true\" />\n        <property name=\"LoanedAt\" column=\"LoanedAt\" not-null=\"true\" precision=\"3\" />\n        <property name=\"DueOn\" column=\"DueOn\" not-null=\"true\" />\n        <property name=\"ReturnedAt\" column=\"ReturnedAt\" precision=\"3\" />\n        <many-to-one name=\"Copy\" class=\"BookCopy\">\n            <column name=\"BookId\" />\n            <column name=\"CopyNumber\" />\n        </many-to-one>\n        <many-to-one name=\"Member\" class=\"Member\" column=\"MemberId\" />\n    </class>\n</hibernate-mapping>"
      },
      "out/Member.cs": {
        "side": "target",
        "name": "Member.cs",
        "contentType": 10,
        "content": "namespace Library;\n\npublic class Member\n{\n    public virtual int MemberId { get; set; }\n\n    public virtual string GivenName { get; set; }\n\n    public virtual string Surname { get; set; }\n\n    public virtual string Email { get; set; }\n\n    public virtual DateOnly JoinedOn { get; set; }\n\n    public virtual IList<Loan> Loans { get; set; }\n\n}\n"
      },
      "out/Member.hbm.xml": {
        "side": "target",
        "name": "Member.hbm.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"Library\">\n    <class name=\"Member\" table=\"Members\" schema=\"Lending\">\n        <id name=\"MemberId\" column=\"MemberId\" type=\"Int32\">\n            <generator class=\"identity\" />\n        </id>\n        <property name=\"GivenName\" column=\"GivenName\" not-null=\"true\" length=\"100\" />\n        <property name=\"Surname\" column=\"Surname\" not-null=\"true\" length=\"100\" />\n        <property name=\"Email\" column=\"Email\" not-null=\"true\" length=\"254\" unique=\"true\" />\n        <property name=\"JoinedOn\" column=\"JoinedOn\" not-null=\"true\" />\n        <bag name=\"Loans\" inverse=\"true\">\n            <key column=\"MemberId\" />\n            <one-to-many class=\"Loan\" />\n        </bag>\n    </class>\n</hibernate-mapping>"
      },
      "out/BookAuthor.cs": {
        "side": "target",
        "name": "BookAuthor.cs",
        "contentType": 10,
        "content": "using System;\n\nnamespace Library;\n\n[Serializable]\npublic class BookAuthor\n{\n    public virtual long BookId { get; set; }\n\n    public virtual int AuthorId { get; set; }\n\n    public virtual Book Book { get; set; }\n\n    public virtual Author Author { get; set; }\n\n    public override bool Equals(object? obj)\n    {\n        if (ReferenceEquals(this, obj))\n        {\n            return true;\n        }\n\n        if (obj is not BookAuthor other)\n        {\n            return false;\n        }\n\n        return Equals(BookId, other.BookId)\n            && Equals(AuthorId, other.AuthorId);\n    }\n\n    public override int GetHashCode()\n    {\n        return HashCode.Combine(BookId, AuthorId);\n    }\n\n}\n"
      },
      "out/BookAuthor.hbm.xml": {
        "side": "target",
        "name": "BookAuthor.hbm.xml",
        "contentType": 30,
        "content": "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"Library\">\n    <class name=\"BookAuthor\" table=\"BookAuthors\" schema=\"Lending\">\n        <composite-id>\n            <key-property name=\"BookId\" column=\"BookId\" type=\"Int64\" />\n            <key-property name=\"AuthorId\" column=\"AuthorId\" type=\"Int32\" />\n        </composite-id>\n        <many-to-one name=\"Book\" class=\"Book\" column=\"BookId\" insert=\"false\" update=\"false\" />\n        <many-to-one name=\"Author\" class=\"Author\" column=\"AuthorId\" insert=\"false\" update=\"false\" />\n    </class>\n</hibernate-mapping>"
      },
      "out/query.cs": {
        "side": "target",
        "name": "query.cs",
        "contentType": 20,
        "content": "public static IQuery Query(ISession session, DateOnly today)\n{\n    return session.CreateQuery(\n        \"\"\"\n        select m.Surname as Surname, m.Email as Email, l.DueOn as DueOn\n        from Loan l\n            inner join Member m with m.MemberId = l.MemberId\n        where l.ReturnedAt is null and l.DueOn < :today\n        order by l.DueOn asc, m.Surname asc\n        \"\"\")\n        .SetParameter(\"today\", today);\n}"
      },
      "out/query.hql": {
        "side": "target",
        "name": "query.hql",
        "contentType": 50,
        "content": "select m.Surname as Surname, m.Email as Email, l.DueOn as DueOn\nfrom Loan l\n    inner join Member m with m.MemberId = l.MemberId\nwhere l.ReturnedAt is null and l.DueOn < :today\norder by l.DueOn asc, m.Surname asc"
      },
      "out/query-2.cs": {
        "side": "target",
        "name": "query-2.cs",
        "contentType": 20,
        "content": "public static IQuery Query(ISession session, DateTime since, long minimumLoans)\n{\n    return session.CreateQuery(\n        \"\"\"\n        select l.MemberId as MemberId, count(*) as Loans\n        from Loan l\n        where l.LoanedAt >= :since\n        group by l.MemberId\n        having count(*) >= :minimumLoans\n        order by l.MemberId asc\n        \"\"\")\n        .SetParameter(\"since\", since)\n        .SetParameter(\"minimumLoans\", minimumLoans);\n}"
      },
      "out/query-2.hql": {
        "side": "target",
        "name": "query-2.hql",
        "contentType": 50,
        "content": "select l.MemberId as MemberId, count(*) as Loans\nfrom Loan l\nwhere l.LoanedAt >= :since\ngroup by l.MemberId\nhaving count(*) >= :minimumLoans\norder by l.MemberId asc"
      },
      "out/query-3.cs": {
        "side": "target",
        "name": "query-3.cs",
        "contentType": 20,
        "content": "public static IQuery Query(ISession session)\n{\n    return session.CreateQuery(\n        \"\"\"\n        from Book b\n        where not (exists (from Loan l where l.BookId = b.BookId)) and b.PageCount > (select avg(x.PageCount) from Book x)\n        order by b.Title asc\n        \"\"\");\n}"
      },
      "out/query-3.hql": {
        "side": "target",
        "name": "query-3.hql",
        "contentType": 50,
        "content": "from Book b\nwhere not (exists (from Loan l where l.BookId = b.BookId)) and b.PageCount > (select avg(x.PageCount) from Book x)\norder by b.Title asc"
      },
      "out/query-4.cs": {
        "side": "target",
        "name": "query-4.cs",
        "contentType": 20,
        "content": "public static IQuery Query(ISession session, IEnumerable<long> bookIds, DateOnly fromDate, DateOnly toDate, int skip, int take)\n{\n    return session.CreateQuery(\n        \"\"\"\n        from BookCopy c\n        where c.BookId in (:bookIds) and c.AcquiredOn >= :fromDate and c.AcquiredOn <= :toDate\n        order by c.BookId asc, c.CopyNumber asc\n        \"\"\")\n        .SetParameterList(\"bookIds\", bookIds)\n        .SetParameter(\"fromDate\", fromDate)\n        .SetParameter(\"toDate\", toDate)\n        .SetFirstResult(skip)\n        .SetMaxResults(take);\n}"
      },
      "out/query-4.hql": {
        "side": "target",
        "name": "query-4.hql",
        "contentType": 50,
        "content": "from BookCopy c\nwhere c.BookId in (:bookIds) and c.AcquiredOn >= :fromDate and c.AcquiredOn <= :toDate\norder by c.BookId asc, c.CopyNumber asc"
      },
      "out/query-5.cs": {
        "side": "target",
        "name": "query-5.cs",
        "contentType": 20,
        "content": "public static IQuery Query(ISession session)\n{\n    return session.CreateQuery(\n        \"\"\"\n        select distinct a.FullName as FullName, a.Country as Country\n        from Author a\n        where a.Country in ('CZ', 'SK', 'PL')\n        order by a.FullName asc\n        \"\"\");\n}"
      },
      "out/query-5.hql": {
        "side": "target",
        "name": "query-5.hql",
        "contentType": 50,
        "content": "select distinct a.FullName as FullName, a.Country as Country\nfrom Author a\nwhere a.Country in ('CZ', 'SK', 'PL')\norder by a.FullName asc"
      }
    },
    "records": [
      {
        "kind": 2,
        "framework": 20,
        "artifact": 60,
        "entity": "Book",
        "property": "Authors",
        "category": null,
        "feature": null,
        "unit": "Book.java",
        "query": null,
        "reason": "The initializer 'new HashSet<>()' is not a literal both languages spell alike; it was dropped."
      },
      {
        "kind": 2,
        "framework": 20,
        "artifact": 60,
        "entity": "Book",
        "property": "Copies",
        "category": null,
        "feature": null,
        "unit": "Book.java",
        "query": null,
        "reason": "The initializer 'new ArrayList<>()' is not a literal both languages spell alike; it was dropped."
      },
      {
        "kind": 2,
        "framework": 20,
        "artifact": 60,
        "entity": "Author",
        "property": "Books",
        "category": null,
        "feature": null,
        "unit": "Author.java",
        "query": null,
        "reason": "The initializer 'new HashSet<>()' is not a literal both languages spell alike; it was dropped."
      },
      {
        "kind": 2,
        "framework": 20,
        "artifact": 60,
        "entity": "Member",
        "property": "Loans",
        "category": null,
        "feature": null,
        "unit": "Member.java",
        "query": null,
        "reason": "The initializer 'new ArrayList<>()' is not a literal both languages spell alike; it was dropped."
      },
      {
        "kind": 4,
        "framework": 20,
        "artifact": null,
        "entity": "Book",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 4,
        "framework": 20,
        "artifact": null,
        "entity": "Author",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 4,
        "framework": 20,
        "artifact": null,
        "entity": "BookCopy",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 4,
        "framework": 20,
        "artifact": null,
        "entity": "Loan",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 4,
        "framework": 20,
        "artifact": null,
        "entity": "Member",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No table matching the entity was found in the catalog; its mapping facts cannot be completed."
      },
      {
        "kind": 3,
        "framework": 20,
        "artifact": null,
        "entity": "BookAuthor",
        "property": null,
        "category": null,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The many-to-many between 'Book' and 'Author' is generated as the explicit junction entity 'BookAuthor' with two many-to-one relations, and both collections now hold it (decision 005). The class name derives from the table 'BookAuthors', which is the tool's convention, not a fact of the source."
      },
      {
        "kind": 2,
        "framework": 20,
        "artifact": 30,
        "entity": "Book",
        "property": "BookId",
        "category": 9,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "The generator parameter 'BlockSize' ('20') has no counterpart on NHibernate's 'sequence' generator and is dropped (decision 020)."
      },
      {
        "kind": 3,
        "framework": 20,
        "artifact": 30,
        "entity": "Book",
        "property": "Copies",
        "category": 10,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No key columns are known for the collection towards 'BookCopy'; the owner's key column 'BookId' is written, which is the tool's fallback, not a fact of the source (decision 012)."
      },
      {
        "kind": 3,
        "framework": 20,
        "artifact": 30,
        "entity": "Author",
        "property": "Country",
        "category": 4,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "NHibernate 5.7.0 registers no fixed-length string type ('AnsiStringFixedLength' is not in TypeFactory), so 'AnsiString' is written and the claim changes from fixed-length to variable-length character data (decision 019). The column carries the claim instead, as sql-type=\"char(2)\" - the spelling of SqlServer2022, which the artifact thereby names and the source did not (decision 086)."
      },
      {
        "kind": 3,
        "framework": 20,
        "artifact": 30,
        "entity": "Author",
        "property": "Books",
        "category": 10,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No key columns are known for the collection towards 'BookAuthor'; the owner's key column 'AuthorId' is written, which is the tool's fallback, not a fact of the source (decision 012)."
      },
      {
        "kind": 3,
        "framework": 20,
        "artifact": 30,
        "entity": "Member",
        "property": "Loans",
        "category": 10,
        "feature": null,
        "unit": null,
        "query": null,
        "reason": "No key columns are known for the collection towards 'Loan'; the owner's key column 'MemberId' is written, which is the tool's fallback, not a fact of the source (decision 012)."
      },
      {
        "kind": 3,
        "framework": 20,
        "artifact": 80,
        "entity": null,
        "property": null,
        "category": null,
        "feature": 2,
        "unit": "CopiesAcquired.jpql",
        "query": null,
        "reason": "A between predicate was rewritten as a pair of comparisons (rule Q14)."
      }
    ]
  }
};
