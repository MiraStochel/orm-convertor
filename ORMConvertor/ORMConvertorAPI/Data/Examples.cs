using Model;
using ORMConvertorAPI.Dtos;
using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// The examples of the explanatory page (decision 099), in the order the page shows them:
/// two inside .NET, two inside Java, and three across the ecosystems, the last two of them
/// over a whole domain rather than a single class. The small examples are built from the
/// samples the translation screen offers; the larger ones have classes of their own.
///
/// The list is the page's content, not a decision: an example is added, replaced or changed
/// here, beside its section in examples.html, and needs no decision file as long as the floor
/// <c>ExampleCatalogTest</c> holds - at least seven examples, every boundary between the
/// ecosystems crossed, every framework taking part.
/// </summary>
public static class Examples
{
    public static List<ExampleDefinition> GetExamples =>
    [
        // Inside .NET: the source states a lot, then the source states little.
        new("efcore-to-nhibernate", ORMEnum.EFCore, ORMEnum.NHibernate,
        [
            Unit("Customer.cs", ConversionContentType.CSharpEntity, CustomerSampleEFCore.Entity),
            Unit("CustomerQuery.cs", ConversionContentType.CSharpQuery, CustomerSampleEFCore.Query),
        ]),
        new("dapper-to-efcore", ORMEnum.Dapper, ORMEnum.EFCore,
        [
            Unit("Customer.cs", ConversionContentType.CSharpEntity, CustomerSampleDapper.Entity),
            Unit("CustomerQuery.sql", ConversionContentType.SqlQuery, CustomerSampleDapper.Query),
        ]),

        // Inside Java, one input towards two targets: the sibling implementation of the
        // specification, and the SQL mapper.
        new("hibernate-to-eclipselink", ORMEnum.Hibernate, ORMEnum.EclipseLink, HibernateVendorUnits()),
        new("hibernate-to-mybatis", ORMEnum.Hibernate, ORMEnum.MyBatis, HibernateVendorUnits()),

        // Across the ecosystems: two SQL-first frameworks, then the two larger domains, one
        // in each direction of F10.
        new("mybatis-to-dapper", ORMEnum.MyBatis, ORMEnum.Dapper,
        [
            Unit("Customer.java", ConversionContentType.JavaEntity, CustomerSampleMyBatis.Entity),
            Unit("CustomerMapper.java", ConversionContentType.JavaQuery, CustomerSampleMyBatis.MapperInterface),
            Unit("CustomerMapper.xml", ConversionContentType.XML, CustomerSampleMyBatis.XmlMapper),
        ]),
        new("order-book", ORMEnum.EFCore, ORMEnum.Hibernate,
        [
            Unit("Customer.cs", ConversionContentType.CSharpEntity, OrderBookSampleEFCore.Customer),
            Unit("SalesOrder.cs", ConversionContentType.CSharpEntity, OrderBookSampleEFCore.SalesOrder),
            Unit("OrderLine.cs", ConversionContentType.CSharpEntity, OrderBookSampleEFCore.OrderLine),
            Unit("Product.cs", ConversionContentType.CSharpEntity, OrderBookSampleEFCore.Product),
            Unit("OpenOrders.cs", ConversionContentType.CSharpQuery, OrderBookSampleEFCore.OpenOrdersQuery),
            Unit("BestSellers.cs", ConversionContentType.CSharpQuery, OrderBookSampleEFCore.BestSellersQuery),
            Unit("DormantCustomers.cs", ConversionContentType.CSharpQuery, OrderBookSampleEFCore.DormantCustomersQuery),
            Unit("OrdersWithProducts.cs", ConversionContentType.CSharpQuery, OrderBookSampleEFCore.OrdersWithProductsQuery),
            Unit("PricedAboveAverage.cs", ConversionContentType.CSharpQuery, OrderBookSampleEFCore.PricedAboveAverageQuery),
            Unit("MailingList.cs", ConversionContentType.CSharpQuery, OrderBookSampleEFCore.MailingListQuery),
        ]),
        new("lending-library", ORMEnum.Hibernate, ORMEnum.NHibernate,
        [
            Unit("Book.java", ConversionContentType.JavaEntity, LendingLibrarySampleHibernate.Book),
            Unit("Author.java", ConversionContentType.JavaEntity, LendingLibrarySampleHibernate.Author),
            Unit("BookCopy.java", ConversionContentType.JavaEntity, LendingLibrarySampleHibernate.BookCopy),
            Unit("Loan.java", ConversionContentType.JavaEntity, LendingLibrarySampleHibernate.Loan),
            Unit("Member.java", ConversionContentType.JavaEntity, LendingLibrarySampleHibernate.Member),
            Unit("OverdueLoans.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.OverdueLoansQuery),
            Unit("ActiveMembers.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.ActiveMembersQuery),
            Unit("NeverBorrowed.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.NeverBorrowedQuery),
            Unit("CopiesAcquired.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.CopiesAcquiredQuery),
            Unit("AuthorsFromRegion.jpql", ConversionContentType.JpqlQuery, LendingLibrarySampleHibernate.AuthorsFromRegionQuery),
        ]),
    ];

    private static List<ConversionSource> HibernateVendorUnits() =>
    [
        Unit("Customer.java", ConversionContentType.JavaEntity, CustomerVendorSampleHibernate.Entity),
        Unit("CustomerPage.jpql", ConversionContentType.JpqlQuery, CustomerVendorSampleHibernate.PageQuery),
    ];

    private static ConversionSource Unit(string name, ConversionContentType contentType, string content)
        => new() { Name = name, ContentType = contentType, Content = content };
}
