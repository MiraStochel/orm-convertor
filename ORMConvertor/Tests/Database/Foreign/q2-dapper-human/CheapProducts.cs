public static List<DifferentialProduct> CheapProducts(IDbConnection connection)
{
    const string sql = @"
        SELECT ProductId, ProductName, Sku, UnitPrice, Weight, IsDiscontinued
        FROM {{schema}}.DifferentialProducts
        WHERE UnitPrice < 100";

    return connection.Query<DifferentialProduct>(sql).ToList();
}
