using Model;
using ORMConvertorAPI.Dtos;
using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// The examples, in the order the explanatory page shows them (decision 099) and the
/// translation screen lists them: two inside .NET, two inside Java, and three across the
/// ecosystems, the last two of them over a whole domain rather than a single class; then
/// three about queries, over the same two domains, each with its queries in one class as a
/// project keeps them; then four over the marketplace, one source framework each, that
/// together show what the tool reads and writes - fluent configuration, mapping documents
/// with named queries, a tour of the query vocabulary, and one long query; then two
/// applications, one in each ecosystem, whose files are mostly code around the mapping and
/// the queries, so that what the tool takes out of them is the example (decision 111); and
/// last five excerpts of real open-source projects, one per source framework but NHibernate,
/// vendored under SampleData/ThirdParty with their licences (decision 138). The small
/// examples are built from the samples the translation screen offers; the larger ones have
/// classes of their own.
///
/// The list is the pages' content, not a decision: an example is added, replaced or changed
/// here, beside its section in examples.html, and needs no decision file as long as the floor
/// <c>ExampleCatalogTest</c> holds - at least seven examples, every boundary between the
/// ecosystems crossed, every framework taking part.
/// </summary>
public static class Examples
{
    public static List<ExampleDefinition> GetExamples =>
    [
        // Inside .NET: the source states a lot, then the source states little.
        new("efcore-to-nhibernate", "Attributes become an XML mapping", ORMEnum.EFCore, ORMEnum.NHibernate,
        [
            Unit("Customer.cs", ConversionContentType.CSharp, CustomerSampleEFCore.Entity),
            Unit("CustomerQuery.cs", ConversionContentType.CSharp, CustomerSampleEFCore.Query),
        ]),
        new("dapper-to-efcore", "A source that states little", ORMEnum.Dapper, ORMEnum.EFCore,
        [
            Unit("Customer.cs", ConversionContentType.CSharp, CustomerSampleDapper.Entity),
            Unit("CustomerQuery.sql", ConversionContentType.SqlQuery, CustomerSampleDapper.Query),
        ]),

        // Inside Java, one input towards two targets: the sibling implementation of the
        // specification, and the SQL mapper.
        new("hibernate-to-eclipselink", "One specification, two sets of defaults", ORMEnum.Hibernate, ORMEnum.EclipseLink, HibernateVendorUnits()),
        new("hibernate-to-mybatis", "From an ORM to a SQL mapper", ORMEnum.Hibernate, ORMEnum.MyBatis, HibernateVendorUnits()),

        // Across the ecosystems: two SQL-first frameworks, then the two larger domains, one
        // in each direction of F10.
        new("mybatis-to-dapper", "SQL stays SQL, the parameter keeps its type", ORMEnum.MyBatis, ORMEnum.Dapper,
        [
            Unit("Customer.java", ConversionContentType.Java, CustomerSampleMyBatis.Entity),
            Unit("CustomerMapper.java", ConversionContentType.Java, CustomerSampleMyBatis.MapperInterface),
            Unit("CustomerMapper.xml", ConversionContentType.XML, CustomerSampleMyBatis.XmlMapper),
        ]),
        new("order-book", "Order book: four entities, six LINQ queries", ORMEnum.EFCore, ORMEnum.Hibernate,
        [
            Unit("Customer.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.Customer),
            Unit("SalesOrder.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.SalesOrder),
            Unit("OrderLine.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.OrderLine),
            Unit("Product.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.Product),
            Unit("OpenOrders.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.OpenOrdersQuery),
            Unit("BestSellers.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.BestSellersQuery),
            Unit("DormantCustomers.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.DormantCustomersQuery),
            Unit("OrdersWithProducts.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.OrdersWithProductsQuery),
            Unit("PricedAboveAverage.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.PricedAboveAverageQuery),
            Unit("MailingList.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.MailingListQuery),
        ]),
        new("lending-library", "Lending library: five entities, five JPQL queries", ORMEnum.Hibernate, ORMEnum.NHibernate,
        [
            Unit("Book.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Book),
            Unit("Author.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Author),
            Unit("BookCopy.java", ConversionContentType.Java, LendingLibrarySampleHibernate.BookCopy),
            Unit("Loan.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Loan),
            Unit("Member.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Member),
            Unit("OverdueLoans.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.OverdueLoansQuery),
            Unit("ActiveMembers.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.ActiveMembersQuery),
            Unit("NeverBorrowed.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.NeverBorrowedQuery),
            Unit("CopiesAcquired.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.CopiesAcquiredQuery),
            Unit("AuthorsFromRegion.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.AuthorsFromRegionQuery),
        ]),

        // Queries: one set of reports towards two targets, so the difference between the
        // outputs is what each query language can say (decision 113), then a repository whose
        // queries reach EF Core in five different ways.
        new("lending-reports-efcore", "Reports: three in LINQ, two in native SQL", ORMEnum.Hibernate, ORMEnum.EFCore, LendingReportUnits()),
        new("lending-reports-nhibernate", "Reports: the same five, all in native SQL", ORMEnum.Hibernate, ORMEnum.NHibernate, LendingReportUnits()),
        new("order-repository", "Order repository: five ways code hands a query over", ORMEnum.EFCore, ORMEnum.EclipseLink,
        [
            Unit("Customer.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.Customer),
            Unit("SalesOrder.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.SalesOrder),
            Unit("OrderLine.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.OrderLine),
            Unit("Product.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.Product),
            Unit("OrderRepository.cs", ConversionContentType.CSharp, OrderBookSampleEFCore.Repository),
        ]),

        // The marketplace: one domain from four source frameworks. Mapping first - fluent
        // configuration, then mapping documents with named queries -, then the queries: a tour
        // of the vocabulary and one long query, both towards the target whose query language
        // is furthest from the source's.
        new("marketplace-fluent", "Marketplace: fluent configuration of nine entities", ORMEnum.EFCore, ORMEnum.NHibernate,
        [
            Unit("Customers.cs", ConversionContentType.CSharp, MarketplaceSampleEFCore.Customers),
            Unit("Catalog.cs", ConversionContentType.CSharp, MarketplaceSampleEFCore.Catalog),
            Unit("Orders.cs", ConversionContentType.CSharp, MarketplaceSampleEFCore.Orders),
            Unit("MarketplaceContext.cs", ConversionContentType.CSharp, MarketplaceSampleEFCore.Context),
            Unit("OrderLineConfiguration.cs", ConversionContentType.CSharp, MarketplaceSampleEFCore.OrderLineConfiguration),
            Unit("MarketplaceQueries.cs", ConversionContentType.CSharp, MarketplaceSampleEFCore.Queries),
        ]),
        new("marketplace-mappings", "Marketplace: mapping documents and named queries", ORMEnum.NHibernate, ORMEnum.MyBatis,
        [
            Unit("Entities.cs", ConversionContentType.CSharp, MarketplaceSampleNHibernate.Entities),
            Unit("Region.hbm.xml", ConversionContentType.XML, MarketplaceSampleNHibernate.RegionMapping),
            Unit("Customer.hbm.xml", ConversionContentType.XML, MarketplaceSampleNHibernate.CustomerMapping),
            Unit("Tag.hbm.xml", ConversionContentType.XML, MarketplaceSampleNHibernate.TagMapping),
            Unit("Product.hbm.xml", ConversionContentType.XML, MarketplaceSampleNHibernate.ProductMapping),
            Unit("SalesOrder.hbm.xml", ConversionContentType.XML, MarketplaceSampleNHibernate.SalesOrderMapping),
            Unit("OrderLine.hbm.xml", ConversionContentType.XML, MarketplaceSampleNHibernate.OrderLineMapping),
            Unit("Queries.hbm.xml", ConversionContentType.XML, MarketplaceSampleNHibernate.NamedQueries),
            Unit("BestSellers.hql", ConversionContentType.HqlQuery, MarketplaceSampleNHibernate.BestSellersQuery),
            Unit("OrderQueries.cs", ConversionContentType.CSharp, MarketplaceSampleNHibernate.OrderQueries),
        ]),
        new("marketplace-query-tour", "Marketplace: the query vocabulary in fourteen reports", ORMEnum.Hibernate, ORMEnum.EFCore,
            [.. MarketplaceJpaEntityUnits(), Unit("MarketplaceReports.java", ConversionContentType.Java, MarketplaceSampleJpa.Reports)]),
        new("marketplace-long-query", "Marketplace: one long query with seven joins and fourteen subqueries", ORMEnum.EclipseLink, ORMEnum.EFCore,
            [.. MarketplaceJpaEntityUnits(), Unit("SalesDashboard.java", ConversionContentType.Java, MarketplaceSampleJpa.Dashboard)]),

        // Application code: files as a project has them, in which the mapping and the queries
        // are a small part of the text. What the tool takes out of them is the example.
        new("helpdesk-application", "Helpdesk: entities and queries taken out of an ASP.NET Core application", ORMEnum.EFCore, ORMEnum.Dapper,
        [
            Unit("Domain.cs", ConversionContentType.CSharp, HelpdeskSampleEFCore.Domain),
            Unit("HelpdeskContext.cs", ConversionContentType.CSharp, HelpdeskSampleEFCore.Context),
            Unit("TicketService.cs", ConversionContentType.CSharp, HelpdeskSampleEFCore.Service),
            Unit("TicketsController.cs", ConversionContentType.CSharp, HelpdeskSampleEFCore.Controller),
            Unit("BacklogReportJob.cs", ConversionContentType.CSharp, HelpdeskSampleEFCore.ReportJob),
        ]),
        new("clinic-application", "Clinic: entities and queries taken out of a Spring Boot application", ORMEnum.Hibernate, ORMEnum.EFCore,
        [
            Unit("Patient.java", ConversionContentType.Java, ClinicSampleHibernate.Patient),
            Unit("Doctor.java", ConversionContentType.Java, ClinicSampleHibernate.Doctor),
            Unit("Appointment.java", ConversionContentType.Java, ClinicSampleHibernate.Appointment),
            Unit("SchedulingService.java", ConversionContentType.Java, ClinicSampleHibernate.SchedulingService),
            Unit("ClinicReports.java", ConversionContentType.Java, ClinicSampleHibernate.Reports),
        ]),

        // Real projects: excerpts of open-source code at one commit, under their own licence,
        // changed only where the tool reads a construct in another form or does not translate
        // it at all; every file says so in its opening note, and the folder's README lists the
        // changes (decision 138).
        new("keycloak-identity", "Keycloak: users, roles and groups in 21 entities and 47 named queries", ORMEnum.Hibernate, ORMEnum.NHibernate,
            ThirdParty("keycloak", "entities", "")),
        new("ambari-alerts", "Apache Ambari: alerts in six entities and 35 named queries", ORMEnum.EclipseLink, ORMEnum.EFCore,
            ThirdParty("ambari", "entities", "")),
        new("chinook-dapper", "Chinook: a Dapper data layer of eleven repositories", ORMEnum.Dapper, ORMEnum.EFCore,
            ThirdParty("chinook", "Entities", "DataDapper")),
        new("chinook-efcore", "Chinook: the same data layer on EF Core", ORMEnum.EFCore, ORMEnum.Hibernate,
            ThirdParty("chinook", "Entities", "DataEF")),
        new("jpetstore-mybatis", "JPetStore: the MyBatis reference shop", ORMEnum.MyBatis, ORMEnum.Dapper,
            ThirdParty("jpetstore", "domain", "mapper")),
    ];

    /// <summary>
    /// The code files of the given folders of a vendored project (decision 138), folder by
    /// folder in the order given and by name within one, one unit each, named by the file and in
    /// the language its extension names; the licence and the README stay out.
    /// </summary>
    private static List<ConversionSource> ThirdParty(string project, params string[] folders) =>
    [
        .. folders
            .SelectMany(folder => ThirdPartySources.Files(project, folder))
            .Where(path => LanguageOf(path) is not null)
            .Select(path => Unit(Path.GetFileName(path), LanguageOf(path)!.Value, ThirdPartySources.Read(path))),
    ];

    private static ConversionContentType? LanguageOf(string path) => Path.GetExtension(path) switch
    {
        ".cs" => ConversionContentType.CSharp,
        ".java" => ConversionContentType.Java,
        ".xml" => ConversionContentType.XML,
        _ => null,
    };

    private static List<ConversionSource> LendingReportUnits() =>
    [
        Unit("Book.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Book),
        Unit("Author.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Author),
        Unit("BookCopy.java", ConversionContentType.Java, LendingLibrarySampleHibernate.BookCopy),
        Unit("Loan.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Loan),
        Unit("Member.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Member),
        Unit("Genre.java", ConversionContentType.Java, LendingLibrarySampleHibernate.Genre),
        Unit("LoanReports.java", ConversionContentType.Java, LendingLibrarySampleHibernate.LoanReports),
    ];

    private static List<ConversionSource> HibernateVendorUnits() =>
    [
        Unit("Customer.java", ConversionContentType.Java, CustomerVendorSampleHibernate.Entity),
        Unit("CustomerPage.jpql", ConversionContentType.JpqlQuery, CustomerVendorSampleHibernate.PageQuery),
    ];

    private static List<ConversionSource> MarketplaceJpaEntityUnits() =>
    [
        Unit("Region.java", ConversionContentType.Java, MarketplaceSampleJpa.Region),
        Unit("Customer.java", ConversionContentType.Java, MarketplaceSampleJpa.Customer),
        Unit("Category.java", ConversionContentType.Java, MarketplaceSampleJpa.Category),
        Unit("Supplier.java", ConversionContentType.Java, MarketplaceSampleJpa.Supplier),
        Unit("Product.java", ConversionContentType.Java, MarketplaceSampleJpa.Product),
        Unit("Tag.java", ConversionContentType.Java, MarketplaceSampleJpa.Tag),
        Unit("SalesOrder.java", ConversionContentType.Java, MarketplaceSampleJpa.SalesOrder),
        Unit("OrderLine.java", ConversionContentType.Java, MarketplaceSampleJpa.OrderLine),
        Unit("Refund.java", ConversionContentType.Java, MarketplaceSampleJpa.Refund),
        Unit("Review.java", ConversionContentType.Java, MarketplaceSampleJpa.Review),
    ];

    private static ConversionSource Unit(string name, ConversionContentType contentType, string content)
        => new() { Name = name, ContentType = contentType, Content = content };
}
