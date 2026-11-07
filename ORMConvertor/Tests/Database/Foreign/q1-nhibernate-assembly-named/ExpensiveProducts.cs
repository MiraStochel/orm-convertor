public static IQuery ExpensiveProducts(ISession session)
{
    return session.CreateQuery(
        "from DifferentialProduct p where p.UnitPrice > 100 order by p.ProductName asc");
}
